using System.Text;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

/// <summary>The outcome of one queued accessibility request; no request value is included.</summary>
public enum UiActionOutcome
{
    /// <summary>Waiting for the next matching wrapper, at most two frames.</summary>
    Pending,
    /// <summary>The existing wrapper consumed the request.</summary>
    Applied,
    /// <summary>The target, action or value is unavailable or stale.</summary>
    NotAvailable,
}
/// <summary>A UI-thread request outcome. It contains no text-field payload.</summary>
public sealed class UiActionTicket
{
    internal UiActionTicket(ulong id, UiActions action) { NodeId = id; Action = action; }
    /// <summary>The original semantic node ID.</summary>
    public ulong NodeId { get; }
    /// <summary>The requested single action.</summary>
    public UiActions Action { get; }
    /// <summary>Current outcome, updated on the UI thread.</summary>
    public UiActionOutcome Outcome { get; internal set; }
    /// <summary>A fixed, payload-free availability description.</summary>
    public string Message => Outcome switch { UiActionOutcome.Pending => "pending", UiActionOutcome.Applied => "applied", _ => "not available" };
}

public static partial class Ui
{
    private sealed record PendingAction(UiActionTicket Ticket, UiNode Expected, ulong? ParentId, int Frame, string? Value);
    [ThreadStatic] private static List<PendingAction>? pendingActions;
    [ThreadStatic] private static ImGuiContextPtr actionContext;
    [ThreadStatic] private static int actionFrame;
    private static List<PendingAction> Pending => pendingActions ??= [];
    private const UiActions SupportedRequests = UiActions.Focus | UiActions.Invoke | UiActions.Select | UiActions.Toggle | UiActions.SetValue;

    /// <summary>Queues one action against the last completed frame on its UI thread. Only a matching enabled wrapper may consume it next frame; unconsumed requests expire after two frames.</summary>
    public static UiActionTicket RequestAction(ulong nodeId, UiActions action, string? value = null)
    {
        var ticket = new UiActionTicket(nodeId, action);
        var context = Im.GetCurrentContext();
        if (!recordedContext.Equals(context) || context.Equals(default(ImGuiContextPtr)) || action == 0
            || (action & SupportedRequests) != action || ((int)action & ((int)action - 1)) != 0
            || Pending.Count >= 128 || Pending.Any(p => p.Ticket.NodeId == nodeId && p.Ticket.Outcome == UiActionOutcome.Pending)
            || (action == UiActions.SetValue && (value is null || value.Contains('\0') || Encoding.UTF8.GetByteCount(value) > 65536)))
        { ticket.Outcome = UiActionOutcome.NotAvailable; return ticket; }
        var snapshot = Tree.Snapshot();
        var match = snapshot.Nodes.Where(n => n.Id == nodeId).ToArray();
        if (match.Length != 1 || !match[0].Enabled || match[0].Bounds.IsEmpty || match[0].IdKind == UiIdKind.Positional || (match[0].Actions & action) == 0)
        { ticket.Outcome = UiActionOutcome.NotAvailable; return ticket; }
        var expected = match[0];
        ulong? parent = expected.Parent >= 0 ? snapshot.Nodes[expected.Parent].Id : null;
        Pending.Add(new(ticket, expected, parent, Im.GetFrameCount(), value));
        return ticket;
    }
    /// <summary>Cancels one still-pending request on its UI thread, including when its native HWND closes.</summary>
    public static void CancelAction(UiActionTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var request = Pending.FirstOrDefault(p => ReferenceEquals(p.Ticket, ticket));
        if (request is null) return;
        ticket.Outcome = UiActionOutcome.NotAvailable;
        Pending.Remove(request);
    }
    private static void BeginActionFrame()
    {
        var context = Im.GetCurrentContext(); var frame = Im.GetFrameCount();
        if (!context.Equals(actionContext) || frame <= actionFrame)
        {
            foreach (var p in Pending) p.Ticket.Outcome = UiActionOutcome.NotAvailable;
            Pending.Clear();
        }
        actionContext = context; actionFrame = frame;
    }
    private static void CompleteActionFrame()
    {
        var frame = Im.GetFrameCount();
        foreach (var p in Pending)
            if (p.Ticket.Outcome == UiActionOutcome.Pending && frame >= p.Frame + 2) p.Ticket.Outcome = UiActionOutcome.NotAvailable;
        Pending.RemoveAll(p => p.Ticket.Outcome != UiActionOutcome.Pending);
    }
    private static unsafe bool ModalAllowsCurrentWindow()
    {
        var modal = Im.Internal.GetTopMostPopupModal();
        if (modal.Ptr == null) return true;
        var window = Im.Internal.GetCurrentWindow();
        return window.Equals(modal) || Im.Internal.IsWindowChildOf(window, modal, true, true);
    }
    private static PendingAction? BeforeAction(UiRole role, uint id, ImString label, UiActions allowed, bool enabled = true, string? value = null)
    {
        EnsureFrame();
        var identity = (((ulong)role + 1) << 32) | id;
        var request = Pending.FirstOrDefault(p => p.Ticket.NodeId == identity && p.Ticket.Outcome == UiActionOutcome.Pending && Im.GetFrameCount() > p.Frame);
        if (request is null) return null;
        var context = Im.GetCurrentContext();
        ulong? parent = Parent >= 0 ? Tree.At(Parent).Id : null;
        enabled &= (context.CurrentItemFlags & (ImGuiItemFlags)ImGuiItemFlagsPrivate.Disabled) == 0;
        if (!enabled || !ModalAllowsCurrentWindow() || Im.Internal.GetCurrentWindow().SkipItems || !Im.Internal.IsWindowContentHoverable(Im.Internal.GetCurrentWindow(), 0)
            || (allowed & request.Ticket.Action) == 0 || request.Expected.Label != ActionLabel(id, label) || request.ParentId != parent
            || request.Expected.ViewportId != CurrentViewportId
            || (request.Ticket.Action == UiActions.SetValue && request.Expected.Value != value))
        { request.Ticket.Outcome = UiActionOutcome.NotAvailable; return null; }
        if (request.Ticket.Action == UiActions.Focus)
        {
            Im.SetKeyboardFocusHere(); request.Ticket.Outcome = UiActionOutcome.Applied; return null;
        }
        return request;
    }
    private static bool ApplyAction(PendingAction? request, bool compound = false)
    {
        if (request is null) return false;
        if ((!compound && Im.GetItemID().Value != (uint)request.Expected.Id) || IsDisabled() || !Im.IsItemVisible()) { request.Ticket.Outcome = UiActionOutcome.NotAvailable; return false; }
        request.Ticket.Outcome = UiActionOutcome.Applied;
        return true;
    }
    private static void RejectAction(PendingAction? request)
    { if (request is not null) request.Ticket.Outcome = UiActionOutcome.NotAvailable; }
    private static void FinishTextAction()
    {
        // Exactly the native facts used by existing IsItemEdited/IsItemDeactivatedAfterEdit commit paths.
        // Do not mark another field's ActiveId edited, nor write a document from the accessibility layer.
        var context = Im.GetCurrentContext();
        context.LastItemData.StatusFlags |= ImGuiItemStatusFlags.Edited | ImGuiItemStatusFlags.HasDeactivated | ImGuiItemStatusFlags.Deactivated;
        context.DeactivatedItemData.ID = context.LastItemData.ID;
        context.DeactivatedItemData.ElapseFrame = Im.GetFrameCount();
        context.DeactivatedItemData.HasBeenEditedBefore = true;
        context.DeactivatedItemData.IsAlive = true;
    }
    internal static void AnnotateNativeTooltip(string text)
    {
        if (!Tree.Active || Tree.Count == 0) return;
        var index = Tree.Count - 1;
        if ((uint)Tree.At(index).Id == Im.GetItemID().Value) Tree.Update(index, Tree.At(index) with { Tooltip = text });
    }
    /// <summary>Records a full description independently of hover and shows the native delayed tooltip for the last item.</summary>
    public static void TooltipLast(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Tree.Count == 0) throw new InvalidOperationException("Record an item before annotating its tooltip.");
        var index = Tree.Count - 1;
        Tree.Update(index, Tree.At(index) with { Tooltip = text });
        Tooltips.Show(text);
    }
}
