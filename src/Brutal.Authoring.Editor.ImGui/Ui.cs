using System.Buffers;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Wrappers delegate once to ImGui, record semantics and consume queued accessibility requests through the existing return/buffer/commit paths.
/// Follow the ordinary ImGui Begin/End rules; native keyboard and mouse behavior is retained.</summary>
public static partial class Ui
{
    [ThreadStatic] private static UiTree? tree;
    [ThreadStatic] private static Stack<int>? parents;
    [ThreadStatic] private static Stack<string?>? disabled;
    [ThreadStatic] private static char[]? scratch;
    private static char[] Scratch => scratch ??= new char[256];
    /// <summary>The recorder owned by the current UI thread.</summary>
    public static UiTree Tree => tree ??= new();
    private static Stack<int> Parents => parents ??= new();
    private static Stack<string?> Disabled => disabled ??= new();
    private static int Parent => Parents.TryPeek(out var n) ? n : -1;
    [ThreadStatic] private static ImGuiContextPtr recordedContext;
    [ThreadStatic] private static int recordedFrame;
    internal static void BeginFrame()
    {
        BeginActionFrame();
        Tree.Begin(); Parents.Clear(); Disabled.Clear();
        recordedContext = Im.GetCurrentContext(); recordedFrame = Im.GetFrameCount();
    }
    private static void EnsureFrame()
    {
        // Embedders may use native NewFrame directly. Do not change their drawing/input contract,
        // and never publish an unfinished frame: EndFrame remains the only publication hook.
        if (!Tree.Active || recordedFrame != Im.GetFrameCount() || !recordedContext.Equals(Im.GetCurrentContext())) BeginFrame();
    }
    internal static void CompleteFrame() { Tree.Complete(); CompleteActionFrame(); }
    /// <summary>Copy the last completed UI frame without exposing pooled storage.</summary>
    public static UiSnapshot Snapshot() => Tree.Snapshot();
    /// <summary>Copy the last completed frame for one viewport; zero is the main application viewport.</summary>
    public static UiSnapshot Snapshot(uint viewportId) => Tree.Snapshot().ForViewport(viewportId);

    private static uint CurrentViewportId
    {
        get
        {
            var id = Im.GetWindowViewport().ID.Value;
            return id == Im.GetMainViewport().ID.Value ? 0 : id;
        }
    }


    private static string Label(ImString label, bool hideId = true, bool value = false)
    {
        EnsureFrame();
        var bytes = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            if (label.TryFormat(bytes.AsSpan(), out var count, default, null))
            {
                Span<char> chars = stackalloc char[1024];
                var length = System.Text.Encoding.UTF8.GetChars(bytes.AsSpan(0, count), chars);
                return Tree.ReuseText(hideId ? Visible(chars[..length]) : chars[..length], value);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
        var text = label.ToString();
        return Tree.ReuseText(hideId ? Visible(text.AsSpan()) : text.AsSpan(), value);
    }
    private static ReadOnlySpan<char> Visible(ReadOnlySpan<char> label)
    { var hidden = label.IndexOf("##".AsSpan()); return hidden < 0 ? label : label[..hidden]; }
    private static string Value(ImInputString input)
    {
        input.EvaluateLength();
        return Tree.ReuseText(input.ToString().AsSpan(), value: true);
    }
    private static UiBounds LastBounds()
    { var a = Im.GetItemRectMin(); var b = Im.GetItemRectMax(); return new(a.X, a.Y, b.X - a.X, b.Y - a.Y); }
    private static bool IsDisabled() => (Im.GetCurrentContext().LastItemData.ItemFlags & (ImGuiItemFlags)ImGuiItemFlagsPrivate.Disabled) != 0;
    private static int Item(UiRole role, string label, uint id, UiActions actions = UiActions.None,
        bool selected = false, bool? expanded = null, bool? check = null, string? value = null, bool enabled = true, uint? viewportId = null)
    {
        EnsureFrame();
        var matches = id == Im.GetItemID().Value;
        enabled &= !IsDisabled() && ModalAllowsCurrentWindow() && Im.Internal.IsWindowContentHoverable(Im.Internal.GetCurrentWindow(), 0);
        var reason = enabled ? null : Disabled.TryPeek(out var why) && why is not null ? why : "disabled";
        var index = Tree.Record(new((((ulong)role + 1) << 32) | id, role, label,
            matches ? LastBounds() : default, enabled, reason, matches && Im.IsItemFocused(),
            matches && Im.IsItemHovered(), selected, expanded, check, value, Parent, enabled ? actions : UiActions.None, ViewportId: viewportId ?? CurrentViewportId));
        if (reason is not null) Tree.Update(index, Tree.At(index) with { Tooltip = reason });
        if (matches && reason is not null) Tooltips.Show(reason);
        return index;
    }
    private static float TextWrapWidth(bool forceWrap = false)
    {
        var wrap = Im.Internal.GetCurrentWindow().DC.TextWrapPos;
        if (forceWrap && wrap < 0) wrap = 0;
        return wrap < 0 ? -1 : Im.Internal.CalcWrapWidthForPos(Im.GetCursorScreenPos(), wrap);
    }
    private static void RecordTextEvidence(int index, float wrapWidth = -1, bool button = false)
    {
        if (Im.Internal.GetCurrentWindow().SkipItems) return;
        var node = Tree.At(index);
        var size = Im.CalcTextSize(node.Label, false, wrapWidth);
        var a = Im.GetWindowDrawList().GetClipRectMin();
        var b = Im.GetWindowDrawList().GetClipRectMax();
        var origin = new float2(node.Bounds.X, node.Bounds.Y);
        if (button)
        {
            var style = Im.GetStyle();
            var padding = style.FramePadding;
            // SmallButton temporarily sets vertical frame padding to zero.
            if (node.Bounds.Height < size.Y + padding.Y * 2) padding.Y = 0;
            var innerMin = origin + padding;
            var innerMax = origin + new float2(node.Bounds.Width, node.Bounds.Height) - padding;
            origin = innerMin + new float2(MathF.Max(0, innerMax.X - innerMin.X - size.X) * style.ButtonTextAlign.X,
                MathF.Max(0, innerMax.Y - innerMin.Y - size.Y) * style.ButtonTextAlign.Y);
            a = new float2(MathF.Max(a.X, innerMin.X), MathF.Max(a.Y, innerMin.Y));
            b = new float2(MathF.Min(b.X, innerMax.X), MathF.Min(b.Y, innerMax.Y));
        }
        var clip = new UiBounds(a.X, a.Y, MathF.Max(0, b.X - a.X), MathF.Max(0, b.Y - a.Y));
        var full = new UiBounds(origin.X, origin.Y, size.X, size.Y);
        var truncated = node.Bounds.IsEmpty || clip.IsEmpty || origin.X < a.X || origin.Y < a.Y
            || origin.X + size.X > b.X || origin.Y + size.Y > b.Y;
        Tree.Update(index, node with { TextEvidence = new(clip, full, wrapWidth, truncated) });
        if (truncated)
        {
            var description = node.Enabled ? node.Label : node.Label + "\n" + node.DisabledReason;
            Tree.Update(index, Tree.At(index) with { Tooltip = description });
            Tooltips.Show(description);
        }
    }
    /// <summary>Delegate Button to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Button(ImString label, in float2? size = null)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.Button, id, label, UiActions.Focus | UiActions.Invoke); var result = Im.Button(label, size); result |= ApplyAction(request); var node = Item(UiRole.Button, Label(label), id, UiActions.Invoke | UiActions.Focus); RecordTextEvidence(node, button: true); return result; }
    /// <summary>Delegate SmallButton to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool SmallButton(ImString label)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.Button, id, label, UiActions.Focus | UiActions.Invoke); var result = Im.SmallButton(label); result |= ApplyAction(request); var node = Item(UiRole.Button, Label(label), id, UiActions.Invoke | UiActions.Focus); RecordTextEvidence(node, button: true); return result; }
    /// <summary>Delegate Selectable to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Selectable(ImString label, bool selected = false, ImGuiSelectableFlags flags = 0, in float2? size = null)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.ListItem, id, label, UiActions.Focus | UiActions.Select, (flags & ImGuiSelectableFlags.Disabled) == 0); var result = Im.Selectable(label, selected, flags, size); result |= ApplyAction(request); Item(UiRole.ListItem, Label(label), id, UiActions.Select | UiActions.Focus, selected); return result; }
    /// <summary>Delegate Selectable to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Selectable(ImString label, ref bool selected, ImGuiSelectableFlags flags = 0, in float2? size = null)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.ListItem, id, label, UiActions.Focus | UiActions.Select, (flags & ImGuiSelectableFlags.Disabled) == 0); var result = Im.Selectable(label, ref selected, flags, size); var synthetic = ApplyAction(request); if (synthetic && !result) selected = !selected; result |= synthetic; Item(UiRole.ListItem, Label(label), id, UiActions.Select | UiActions.Focus, selected); return result; }
    /// <summary>Delegate InputText to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool InputText(ImString label, ImInputString input, ImGuiInputTextFlags flags = 0,
        ImGuiInputTextCallback? callback = null, nint userData = default)
    {
        var id = Im.GetID(label).Value;
        var oldValue = (flags & ImGuiInputTextFlags.Password) != 0 ? "[password]" : Value(input);
        var allowed = (flags & ImGuiInputTextFlags.ReadOnly) != 0 ? UiActions.Focus : UiActions.SetValue | UiActions.Focus;
        var request = BeforeAction(UiRole.TextField, id, label, allowed, value: oldValue);
        if (request is not null)
        {
            if (!Im.IsRectVisible(new float2(Im.CalcItemWidth(), Im.GetFrameHeight())) || System.Text.Encoding.UTF8.GetByteCount(request.Value!) >= input.Capacity) { RejectAction(request); request = null; }
            else
            {
                if (Im.GetCurrentContext().ActiveId.Value == id) Im.Internal.ClearActiveID();
                input.SetValue(request.Value.AsSpan());
            }
        }
        var result = Im.InputText(label, input, flags, callback, userData);
        if (ApplyAction(request)) { FinishTextAction(); result = true; }
        var value = (flags & ImGuiInputTextFlags.Password) != 0 ? "[password]" : Value(input);
        Item(UiRole.TextField, Label(label), id, (flags & ImGuiInputTextFlags.ReadOnly) != 0 ? UiActions.Focus : UiActions.SetValue | UiActions.Focus, value: value);
        return result;
    }
    /// <summary>Draws a native multiline text field and records the same text, focus, value-action and read-only semantics as InputText.</summary>
    public static bool InputTextMultiline(ImString label, ImInputString input, in float2 size, ImGuiInputTextFlags flags = 0,
        ImGuiInputTextCallback? callback = null, nint userData = default)
    {
        var id = Im.GetID(label).Value;
        var oldValue = (flags & ImGuiInputTextFlags.Password) != 0 ? "[password]" : Value(input);
        var allowed = (flags & ImGuiInputTextFlags.ReadOnly) != 0 ? UiActions.Focus : UiActions.SetValue | UiActions.Focus;
        var request = BeforeAction(UiRole.TextField, id, label, allowed, value: oldValue);
        if (request is not null)
        {
            var visibleSize = new float2(size.X > 0 ? size.X : Im.CalcItemWidth(), size.Y > 0 ? size.Y : Im.GetTextLineHeight() * 8);
            if (!Im.IsRectVisible(visibleSize) || System.Text.Encoding.UTF8.GetByteCount(request.Value!) >= input.Capacity) { RejectAction(request); request = null; }
            else
            {
                if (Im.GetCurrentContext().ActiveId.Value == id) Im.Internal.ClearActiveID();
                input.SetValue(request.Value.AsSpan());
            }
        }
        var result = Im.InputTextMultiline(label, input, size, flags, callback, userData);
        if (ApplyAction(request)) { FinishTextAction(); result = true; }
        var value = (flags & ImGuiInputTextFlags.Password) != 0 ? "[password]" : Value(input);
        Item(UiRole.TextField, Label(label), id, allowed, value: value);
        return result;
    }
    /// <summary>Delegate Checkbox to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Checkbox(ImString label, ref bool value)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.Checkbox, id, label, UiActions.Focus | UiActions.Toggle); var result = Im.Checkbox(label, ref value); var synthetic = ApplyAction(request); if (synthetic && !result) value = !value; result |= synthetic; Item(UiRole.Checkbox, Label(label), id, UiActions.Toggle | UiActions.Focus, check: value); return result; }
    /// <summary>Delegate MenuItem to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool MenuItem(ImString label, ImString shortcut = default, bool selected = false, bool enabled = true, string? disabledReason = null)
    {
        var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.MenuItem, id, label, UiActions.Focus | UiActions.Invoke, enabled); var result = Im.MenuItem(label, shortcut, selected, enabled); var synthetic = ApplyAction(request); if (synthetic && !result && (Im.Internal.GetCurrentWindow().Flags & ImGuiWindowFlags.Popup) != 0) Im.CloseCurrentPopup(); result |= synthetic;
        var node = Item(UiRole.MenuItem, Label(label), id, UiActions.Invoke | UiActions.Focus, check: selected, enabled: enabled);
        if (!enabled && disabledReason is not null)
        {
            Tree.Update(node, Tree.At(node) with { DisabledReason = disabledReason, Tooltip = disabledReason });
            Tooltips.Show(disabledReason);
        }
        return result;
    }
    /// <summary>Delegate MenuItem to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool MenuItem(ImString label, ImString shortcut, ref bool selected, bool enabled = true)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.MenuItem, id, label, UiActions.Focus | UiActions.Invoke, enabled); var result = Im.MenuItem(label, shortcut, ref selected, enabled); var synthetic = ApplyAction(request); if (synthetic && !result) { selected = !selected; if ((Im.Internal.GetCurrentWindow().Flags & ImGuiWindowFlags.Popup) != 0) Im.CloseCurrentPopup(); } result |= synthetic; Item(UiRole.MenuItem, Label(label), id, UiActions.Invoke | UiActions.Focus, check: selected, enabled: enabled); return result; }
    /// <summary>Delegate TextLink to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool TextLink(ImString label)
    { var id = Im.GetID(label).Value; var request = BeforeAction(UiRole.Link, id, label, UiActions.Focus | UiActions.Invoke); var result = Im.TextLink(label); result |= ApplyAction(request); var node = Item(UiRole.Link, Label(label), id, UiActions.Invoke | UiActions.Focus); RecordTextEvidence(node); return result; }
    /// <summary>Begin an unchanged native disabled scope and retain its explanatory reason.</summary>
    public static void BeginDisabled(string reason) => BeginDisabled(true, reason);
    /// <summary>Begin an unchanged native disabled scope and retain its explanatory reason.</summary>
    public static void BeginDisabled(bool isDisabled = true, string? reason = null)
    {
        EnsureFrame();
        Im.BeginDisabled(isDisabled);
        Disabled.Push(isDisabled ? reason ?? "disabled" : Disabled.TryPeek(out var inherited) ? inherited : null);
    }
    /// <summary>End the matching native disabled scope.</summary>
    public static void EndDisabled() { Im.EndDisabled(); Disabled.Pop(); }

    private static int Group(UiRole role, string label, uint nativeId, UiBounds bounds, bool aggregate = false,
        string? semanticId = null, bool positional = false)
    {
        var kind = semanticId is not null ? UiIdKind.Semantic : positional ? UiIdKind.Positional : UiIdKind.ImGui;
        var id = kind == UiIdKind.ImGui ? (((ulong)role + 1) << 32) | nativeId : Synthetic(role, semanticId);
        return Tree.Record(new(id, role, label, bounds, true, null, false, false, false, null, null, null, Parent,
            UiActions.None, kind, ViewportId: CurrentViewportId), aggregate);
    }
    private static ulong Synthetic(UiRole role, string? semanticId)
    {
        // Stable only with an explicit semantic key. Position is deliberately marked in snapshots.
        ulong hash = 14695981039346656037UL;
        var parent = Parent < 0 ? 0UL : Tree.At(Parent).Id;
        hash = (hash ^ parent) * 1099511628211UL; hash = (hash ^ (uint)role) * 1099511628211UL;
        if (semanticId is not null) foreach (var c in semanticId) hash = (hash ^ c) * 1099511628211UL;
        else hash = (hash ^ (uint)Tree.Count) * 1099511628211UL;
        return hash;
    }
    private static UiBounds WindowBounds()
    { var p = Im.GetWindowPos(); var s = Im.GetWindowSize(); return new(p.X, p.Y, s.X, s.Y); }
    private static void Window(UiRole role, ImString label)
    { Parents.Push(Group(role, Label(label), Im.Internal.GetCurrentWindow().ID.Value, WindowBounds())); }
    private static void FinishPopupItem(int index)
    {
        // EndCombo/EndMenu restore the parent window's last-item state. Capture the header, not the popup window title.
        var node = Tree.At(index);
        if (Im.GetItemID().Value == (uint)node.Id)
            Tree.Update(index, node with { Bounds = LastBounds(), Focused = Im.IsItemFocused(), Hovered = Im.IsItemHovered() });
    }
    /// <summary>Delegate Begin to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Begin(ImString name, ImGuiWindowFlags flags = 0)
    { var result = Im.Begin(name, flags); Window(UiRole.Window, name); return result; }
    /// <summary>Delegate Begin to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool Begin(ImString name, ref bool open, ImGuiWindowFlags flags = 0)
    { var result = Im.Begin(name, ref open, flags); Window(UiRole.Window, name); return result; }
    /// <summary>Delegate End to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void End() { Im.End(); Parents.Pop(); }
    /// <summary>Delegate BeginPopupModal to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginPopupModal(ImString name, ImGuiWindowFlags flags = 0)
    { var result = Im.BeginPopupModal(name, flags); if (result) Window(UiRole.Dialog, name); return result; }
    /// <summary>Delegate BeginPopupModal to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginPopupModal(ImString name, ref bool open, ImGuiWindowFlags flags = 0)
    { var result = Im.BeginPopupModal(name, ref open, flags); if (result) Window(UiRole.Dialog, name); return result; }
    /// <summary>Delegate EndPopup to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndPopup() { Im.EndPopup(); Parents.Pop(); }
    /// <summary>Delegate BeginCombo to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginCombo(ImString label, ImString preview, ImGuiComboFlags flags = 0)
    {
        var id = Im.GetID(label).Value; var viewport = CurrentViewportId; var result = Im.BeginCombo(label, preview, flags);
        var previewValue = Label(preview, false, true);
        var node = Item(UiRole.Combo, Label(label), id, UiActions.Expand | UiActions.Focus, expanded: result, value: previewValue, viewportId: viewport);
        if (result) Parents.Push(node); return result;
    }
    /// <summary>Delegate EndCombo to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndCombo() { var node = Parents.Pop(); Im.EndCombo(); FinishPopupItem(node); }
    /// <summary>Delegate BeginMenu to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginMenu(ImString label, bool enabled = true)
    {
        var id = Im.GetID(label).Value; var viewport = CurrentViewportId;
        var visible = Label(label); var window = Im.Internal.GetCurrentWindow();
        // BeginMenuEx paints with the parent's cursor/baseline and draw clip, even when it opens a popup.
        // Native source: imgui_widgets.cpp, v1.92.2-docking-brutal, BeginMenuEx.
        var submitted = !window.SkipItems && !Im.GetCurrentContext().MenusIdSubmittedThisFrame.Span.Contains(id);
        var origin = window.DC.CursorPos;
        origin.Y += window.DC.CurrLineTextBaseOffset;
        if (window.DC.LayoutType == ImGuiLayoutType.Horizontal) origin.X += MathF.Truncate(Im.GetStyle().ItemSpacing.X * 0.5f);
        var size = submitted ? Im.CalcTextSize(visible) : default;
        var list = Im.GetWindowDrawList(); var a = list.GetClipRectMin(); var b = list.GetClipRectMax();
        var result = Im.BeginMenu(label, enabled);
        var node = Item(UiRole.Menu, visible, id, UiActions.Expand | UiActions.Focus, expanded: result, enabled: enabled, viewportId: viewport);
        if (submitted)
        {
            // Vertical menus update their label-column offset during the call.
            origin.X += window.DC.MenuColumns.OffsetLabel;
            var full = new UiBounds(origin.X, origin.Y, size.X, size.Y);
            var clip = new UiBounds(a.X, a.Y, MathF.Max(0, b.X - a.X), MathF.Max(0, b.Y - a.Y));
            var clipped = clip.IsEmpty || origin.X < a.X || origin.Y < a.Y
                || origin.X + size.X > b.X || origin.Y + size.Y > b.Y;
            Tree.Update(node, Tree.At(node) with { TextEvidence = new(clip, full, -1, clipped) });
        }
        if (result) Parents.Push(node); return result;
    }
    /// <summary>Delegate EndMenu to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndMenu() { var node = Parents.Pop(); Im.EndMenu(); FinishPopupItem(node); }
    /// <summary>Delegate BeginMainMenuBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginMainMenuBar()
    { var result = Im.BeginMainMenuBar(); if (result) Window(UiRole.MenuBar, "Menu bar"u8); return result; }
    /// <summary>Delegate EndMainMenuBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndMainMenuBar() { Im.EndMainMenuBar(); Parents.Pop(); }
    /// <summary>Delegate BeginMenuBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginMenuBar()
    {
        var id = Im.GetID("##ui.menuBar"u8).Value; var result = Im.BeginMenuBar();
        if (result) Parents.Push(Group(UiRole.MenuBar, "Menu bar", id, default, true)); return result;
    }
    /// <summary>Delegate EndMenuBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndMenuBar() { Im.EndMenuBar(); Parents.Pop(); }
    /// <summary>Draw text unchanged and record its visible content; semanticId avoids positional identity.</summary>
    public static void Text(ImString text, string? semanticId = null)
    { var wrap = TextWrapWidth(false); Im.Text(text); var node = Group(UiRole.Text, Label(text, false), 0, LastBounds(), semanticId: semanticId, positional: true); RecordTextEvidence(node, wrap); }
    /// <summary>Draw wrapped text unchanged and record its visible content.</summary>
    public static void TextWrapped(ImString text, string? semanticId = null)
    { var wrap = TextWrapWidth(true); Im.TextWrapped(text); var node = Group(UiRole.Text, Label(text, false), 0, LastBounds(), semanticId: semanticId, positional: true); RecordTextEvidence(node, wrap); }
    /// <summary>Draw text unchanged with the semantic status role.</summary>
    public static void Status(ImString text, string? semanticId = null)
    { var wrap = TextWrapWidth(false); Im.Text(text); var node = Group(UiRole.Status, Label(text, false), 0, LastBounds(), semanticId: semanticId, positional: true); RecordTextEvidence(node, wrap); }
    /// <summary>Delegate TreeNodeEx to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool TreeNodeEx(ImString label, ImGuiTreeNodeFlags flags = 0)
    {
        var id = Im.GetID(label).Value; var result = Im.TreeNodeEx(label, flags);
        var node = Item(UiRole.TreeItem, Label(label), id, UiActions.Expand | UiActions.Focus,
            selected: (flags & ImGuiTreeNodeFlags.Selected) != 0, expanded: result);
        if (result && (flags & ImGuiTreeNodeFlags.NoTreePushOnOpen) == 0) Parents.Push(node);
        return result;
    }
    /// <summary>Delegate TreePop to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void TreePop() { Im.TreePop(); Parents.Pop(); }
    /// <summary>Delegate BeginTabBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginTabBar(ImString id, ImGuiTabBarFlags flags = 0) => Im.BeginTabBar(id, flags);
    /// <summary>Delegate EndTabBar to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndTabBar() => Im.EndTabBar();
    /// <summary>Delegate BeginTabItem to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginTabItem(ImString label, ImGuiTabItemFlags flags = 0)
    {
        var result = Im.BeginTabItem(label, flags); var id = Im.GetItemID().Value;
        var node = Item(UiRole.Tab, Label(label), id, UiActions.Select | UiActions.Focus, selected: result);
        if (result) Parents.Push(node); return result;
    }
    /// <summary>Delegate BeginTabItem to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginTabItem(ImString label, ref bool open, ImGuiTabItemFlags flags = 0)
    {
        var result = Im.BeginTabItem(label, ref open, flags); var id = Im.GetItemID().Value;
        var node = Item(UiRole.Tab, Label(label), id, UiActions.Select | UiActions.Focus, selected: result);
        if (result) Parents.Push(node); return result;
    }
    /// <summary>Delegate EndTabItem to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndTabItem() { Im.EndTabItem(); Parents.Pop(); }
    /// <summary>Delegate BeginTable to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool BeginTable(ImString id, int columns, ImGuiTableFlags flags = 0, in float2? outerSize = null, float innerWidth = 0)
    {
        var nativeId = Im.GetID(id).Value; var result = Im.BeginTable(id, columns, flags, outerSize, innerWidth);
        if (result) Parents.Push(Group(UiRole.Table, Label(id), nativeId, default, true)); return result;
    }
    private static void CloseCell()
    { if (Parent >= 0 && Tree.At(Parent).Role == UiRole.Cell) Parents.Pop(); }
    private static void CloseRow()
    { CloseCell(); if (Parent >= 0 && Tree.At(Parent).Role == UiRole.Row) Parents.Pop(); }
    private static void Row(string? semanticId = null)
    { Parents.Push(Group(UiRole.Row, "", 0, default, true, semanticId, true)); }
    private static void Cell(string? semanticId = null)
    { Parents.Push(Group(UiRole.Cell, "", 0, default, true, semanticId, true)); }
    /// <summary>Delegate TableNextRow to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void TableNextRow(ImGuiTableRowFlags flags = 0, float minHeight = 0, string? semanticId = null)
    { CloseRow(); Im.TableNextRow(flags, minHeight); Row(semanticId); }
    /// <summary>Delegate TableNextColumn to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool TableNextColumn(string? semanticId = null)
    {
        CloseCell(); var before = Im.TableGetRowIndex(); var result = Im.TableNextColumn();
        if (Im.TableGetRowIndex() != before || Parent < 0 || Tree.At(Parent).Role != UiRole.Row)
        { CloseRow(); Row(); }
        Cell(semanticId); return result;
    }
    /// <summary>Delegate TableSetColumnIndex to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static bool TableSetColumnIndex(int column, string? semanticId = null)
    {
        CloseCell(); var result = Im.TableSetColumnIndex(column);
        if (Parent < 0 || Tree.At(Parent).Role != UiRole.Row) Row();
        Cell(semanticId); return result;
    }
    /// <summary>Delegate EndTable to ImGui unchanged and record the corresponding semantic item or scope.</summary>
    public static void EndTable() { CloseRow(); Im.EndTable(); Parents.Pop(); }

    /// <summary>Draw disabled text unchanged and record its content.</summary>
    public static void TextDisabled(ImString text, string? semanticId = null)
    { var wrap = TextWrapWidth(false); Im.TextDisabled(text); var node = Group(UiRole.Text, Label(text, false), 0, LastBounds(), semanticId: semanticId, positional: true); RecordTextEvidence(node, wrap); }
    /// <summary>Delegate a numeric input unchanged and record its current value.</summary>
    public static bool InputDouble(ImString label, ref double value, double step = 0, double stepFast = 0, ImString format = default, ImGuiInputTextFlags flags = 0)
    {
        var id = Im.GetID(label).Value;
        var allowed = (flags & ImGuiInputTextFlags.ReadOnly) != 0 ? UiActions.Focus : UiActions.Focus | UiActions.SetValue;
        var request = BeforeAction(UiRole.TextField, id, label, allowed, value: value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request is not null)
        {
            if (!Im.IsRectVisible(new float2(Im.CalcItemWidth(), Im.GetFrameHeight())) || !double.TryParse(request.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            { RejectAction(request); request = null; }
            else { if (Im.GetCurrentContext().ActiveId.Value == id) Im.Internal.ClearActiveID(); value = number; }
        }
        var result = Im.InputDouble(label, ref value, step, stepFast, format, flags);
        if (ApplyAction(request)) { FinishTextAction(); result = true; }
        Item(UiRole.TextField, Label(label), id, (flags & ImGuiInputTextFlags.ReadOnly) != 0 ? UiActions.Focus : UiActions.SetValue | UiActions.Focus,
            value: value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return result;
    }
    /// <summary>Delegate the existing URL-link action unchanged and record its link node.</summary>
    public static void TextLinkOpenURL(ImString label, ImString url = default)
    { var id = Im.GetID(label).Value; BeforeAction(UiRole.Link, id, label, UiActions.Focus); Im.TextLinkOpenURL(label, url); var node = Item(UiRole.Link, Label(label), id, UiActions.Focus); RecordTextEvidence(node); }

    /// <summary>Records an existing invisible canvas item without issuing input or drawing calls.</summary>
    public static int RecordCanvas(string label, bool enabled = true, string? reason = null)
    {
        var node = Item(UiRole.Canvas, label, Im.GetItemID().Value, UiActions.None, enabled: enabled);
        if (!enabled && reason is not null) Tree.Update(node, Tree.At(node) with { DisabledReason = reason });
        return node;
    }
    /// <summary>Records a custom-drawn selectable item, clipped to its canvas; never changes native state.</summary>
    public static void RecordCanvasItem(int canvas, string key, string label, UiBounds bounds, bool selected)
    {
        var parent = Tree.At(canvas); var clip = parent.Bounds;
        var x = MathF.Max(bounds.X, clip.X); var y = MathF.Max(bounds.Y, clip.Y);
        var right = MathF.Min(bounds.X + bounds.Width, clip.X + clip.Width);
        var bottom = MathF.Min(bounds.Y + bounds.Height, clip.Y + clip.Height);
        if (right <= x || bottom <= y) return;
        ulong id = (14695981039346656037UL ^ parent.Id) * 1099511628211UL;
        foreach (var c in key) id = (id ^ c) * 1099511628211UL;
        Tree.Record(new(id, UiRole.ListItem, label, new(x, y, right-x, bottom-y), parent.Enabled,
            parent.DisabledReason, false, false, selected, null, null, null, canvas,
            UiActions.None, UiIdKind.Semantic, ViewportId: parent.ViewportId));
    }

    /// <summary>Annotates the last recorded item with one uppercase access letter, without dispatching input.</summary>
    public static void AccessKeyLast(string letter)
    {
        if (letter.Length != 1 || letter[0] is < 'A' or > 'Z') throw new ArgumentException("Use one uppercase A-Z access letter.", nameof(letter));
        if (Tree.Count == 0) throw new InvalidOperationException("Record an item before annotating its access key.");
        var index = Tree.Count - 1;
        Tree.Update(index, Tree.At(index) with { AccessKey = letter });
    }

    /// <summary>Names a recorded native item without changing its native label, identity, layout or state.</summary>
    public static void NameItem(ImString nativeLabel, string label)
    {
        var id = Im.GetID(nativeLabel).Value;
        for (var i = Tree.Count - 1; i >= 0; i--)
            if (Tree.At(i).IdKind == UiIdKind.ImGui && (uint)Tree.At(i).Id == id)
            { Tree.Update(i, Tree.At(i) with { Label = label, TextEvidence = null, AccessKey = null }); return; }
    }

    /// <summary>Delegate a child window unchanged; EndChild is required even when false.</summary>
    public static bool BeginChild(ImString id, in float2? size = null, ImGuiChildFlags childFlags = 0, ImGuiWindowFlags windowFlags = 0)
    { var result = Im.BeginChild(id, size, childFlags, windowFlags); Window(UiRole.Window, id); return result; }
    /// <summary>End the matching child window and semantic scope.</summary>
    public static void EndChild() { Im.EndChild(); Parents.Pop(); }
    /// <summary>Delegate a popup unchanged and record its actual native window when open.</summary>
    public static bool BeginPopup(ImString id, ImGuiWindowFlags flags = 0)
    { var result = Im.BeginPopup(id, flags); if (result) Window(UiRole.Dialog, id); return result; }
}
