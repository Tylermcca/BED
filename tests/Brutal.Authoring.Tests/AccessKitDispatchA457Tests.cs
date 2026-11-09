using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.App;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AccessKitDispatchA457Tests
{
    private static void Begin() { ImGui.SetNextWindowPos(new float2(20, 20)); ImGui.SetNextWindowSize(new float2(500, 300)); Ui.Begin("Dispatch proof"u8); }
    private static ulong Id(UiRole role) => Ui.Snapshot().Nodes.Single(n => n.Role == role).Id;
    [Fact]
    public void Invoke_runs_the_existing_return_handler_once_on_the_next_frame_and_not_during_queueing()
    {
        using var host = new HeadlessHost { Drawing = false }; var calls = 0;
        void Draw() { Begin(); if (Ui.Button("Do it"u8)) calls++; Ui.End(); }
        host.Frames(Draw, 3);
        var ticket = Ui.RequestAction(Id(UiRole.Button), UiActions.Invoke);
        Assert.Equal(UiActionOutcome.Pending, ticket.Outcome); Assert.Equal(0, calls);
        var duplicate = Ui.RequestAction(Id(UiRole.Button), UiActions.Invoke);
        Assert.Equal(UiActionOutcome.NotAvailable, duplicate.Outcome);
        host.Frame(Draw); Assert.Equal(UiActionOutcome.Applied, ticket.Outcome); Assert.Equal(1, calls);
        host.Frames(Draw, 2); Assert.Equal(1, calls);
    }
    [Fact]
    public void Focus_uses_native_keyboard_focus_and_toggle_uses_the_checkbox_return_path()
    {
        using var host = new HeadlessHost { Drawing = false }; var value = false; var calls = 0;
        void Draw() { Begin(); if (Ui.Checkbox("Choice"u8, ref value)) calls++; Ui.End(); }
        host.Frames(Draw, 3); var id = Id(UiRole.Checkbox);
        var focus = Ui.RequestAction(id, UiActions.Focus); host.Frames(Draw, 2);
        Assert.Equal(UiActionOutcome.Applied, focus.Outcome);
        Assert.True(Ui.Snapshot().Nodes.Single(n => n.Id == id).Focused);
        var toggle = Ui.RequestAction(id, UiActions.Toggle); host.Frame(Draw);
        Assert.True(value); Assert.Equal(1, calls); Assert.Equal(UiActionOutcome.Applied, toggle.Outcome);
        host.Frame(Draw); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selectable_uses_its_existing_selection_return_path_and_respects_its_disabled_flag(bool byReference)
    {
        using var host = new HeadlessHost { Drawing = false }; var selected = false; var disabled = false; var calls = 0;
        void Draw()
        {
            Begin(); var flags = disabled ? ImGuiSelectableFlags.Disabled : 0;
            if (byReference) { if (Ui.Selectable("Row"u8, ref selected, flags)) calls++; }
            else if (Ui.Selectable("Row"u8, selected, flags)) { selected = true; calls++; }
            Ui.End();
        }
        host.Frames(Draw, 3); var id = Id(UiRole.ListItem);
        var select = Ui.RequestAction(id, UiActions.Select); host.Frame(Draw);
        Assert.True(selected); Assert.Equal(1, calls); Assert.Equal(UiActionOutcome.Applied, select.Outcome);
        var focus = Ui.RequestAction(id, UiActions.Focus); disabled = true; host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, focus.Outcome); Assert.Equal(1, calls);
        Assert.Equal(UiActions.None, Ui.Snapshot().Nodes.Single(n => n.Id == id).Actions);
    }

    [Fact]
    public void New_disabled_state_refuses_a_previously_enabled_request_without_calling_handler()
    {
        using var host = new HeadlessHost { Drawing = false }; var disabled = false; var calls = 0;
        void Draw() { Begin(); Ui.BeginDisabled(disabled, "Unavailable"); if (Ui.Button("Do it"u8)) calls++; Ui.EndDisabled(); Ui.End(); }
        host.Frames(Draw, 3); var ticket = Ui.RequestAction(Id(UiRole.Button), UiActions.Invoke);
        disabled = true; host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, ticket.Outcome); Assert.Equal(0, calls);
        Assert.Equal(UiActions.None, Ui.Snapshot().Nodes.Single(n => n.Role == UiRole.Button).Actions);
    }
    [Fact]
    public void Absent_target_expires_after_two_frames_and_does_not_fire_on_return()
    {
        using var host = new HeadlessHost { Drawing = false }; var show = true; var calls = 0;
        void Draw() { Begin(); if (show && Ui.Button("Do it"u8)) calls++; Ui.End(); }
        host.Frames(Draw, 3); var ticket = Ui.RequestAction(Id(UiRole.Button), UiActions.Invoke);
        show = false; host.Frame(Draw); Assert.Equal(UiActionOutcome.Pending, ticket.Outcome);
        host.Frame(Draw); Assert.Equal(UiActionOutcome.NotAvailable, ticket.Outcome);
        show = true; host.Frame(Draw); Assert.Equal(0, calls); Assert.Equal("not available", ticket.Message);
    }
    [Fact]
    public void SetValue_exposes_native_edited_deactivated_facts_to_the_existing_commit_and_validation_path()
    {
        using var host = new HeadlessHost { Drawing = false }; var input = new ImInputString(64, "old"u8);
        var committed = "old"; var undo = new Stack<string>(); var edited = false; var deactivated = false; var refusals = 0;
        void Draw()
        {
            Begin(); Ui.InputText("Name"u8, input);
            edited = ImGui.IsItemEdited(); deactivated = ImGui.IsItemDeactivatedAfterEdit();
            if (deactivated)
            {
                if (input.ToString() == "invalid") refusals++;
                else { undo.Push(committed); committed = input.ToString(); }
            }
            Ui.End();
        }
        host.Frames(Draw, 3); var id = Id(UiRole.TextField);
        var ticket = Ui.RequestAction(id, UiActions.SetValue, "new"); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.Applied, ticket.Outcome); Assert.True(edited); Assert.True(deactivated);
        Assert.Equal("new", committed); Assert.Single(undo);
        host.Frame(Draw); Assert.Single(undo);
        var refused = Ui.RequestAction(id, UiActions.SetValue, "invalid"); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.Applied, refused.Outcome); Assert.Equal(1, refusals); Assert.Equal("new", committed); Assert.Single(undo);
        committed = undo.Pop(); Assert.Equal("old", committed);
    }
    [Fact]
    public void SetValue_and_the_native_keyboard_route_share_one_commit_and_undo_outcome()
    {
        static (string Value, int UndoCount) Run(bool semantic)
        {
            using var host = new HeadlessHost { Drawing = false }; var input = new ImInputString(64, "old"u8);
            var saved = "old"; var undo = new Stack<string>(); var center = float2.Zero;
            void Draw()
            {
                Begin(); Ui.InputText("Name"u8, input);
                center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                if (ImGui.IsItemDeactivatedAfterEdit()) { undo.Push(saved); saved = input.ToString(); }
                Ui.End();
            }
            host.Frames(Draw, 3);
            if (semantic) { var request = Ui.RequestAction(Id(UiRole.TextField), UiActions.SetValue, "new"); host.Frame(Draw); Assert.Equal(UiActionOutcome.Applied, request.Outcome); }
            else { host.Click(Draw, center.X, center.Y); host.Press(Draw, ImGuiKey.A, ctrl: true); host.Type(Draw, "new"); host.Press(Draw, ImGuiKey.Enter); }
            host.Frame(Draw);
            Assert.Equal("new", saved); Assert.Single(undo); Assert.Equal("old", undo.Peek());
            return (saved, undo.Count);
        }
        Assert.Equal(Run(false), Run(true));
    }

    [Fact]
    public void Oversized_readonly_and_stale_value_requests_do_not_change_the_buffer()
    {
        using var host = new HeadlessHost { Drawing = false }; var input = new ImInputString(8, "old"u8); var readOnly = false;
        void Draw() { Begin(); Ui.InputText("Name"u8, input, readOnly ? ImGuiInputTextFlags.ReadOnly : 0); Ui.End(); }
        host.Frames(Draw, 3); var id = Id(UiRole.TextField);
        var oversized = Ui.RequestAction(id, UiActions.SetValue, "12345678"); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, oversized.Outcome); Assert.Equal("old", input.ToString());
        var stale = Ui.RequestAction(id, UiActions.SetValue, "new"); input.SetValue("other"u8); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, stale.Outcome); Assert.Equal("other", input.ToString());
        var changed = Ui.RequestAction(id, UiActions.SetValue, "new"); readOnly = true; host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, changed.Outcome); Assert.Equal("other", input.ToString());
        Assert.Equal(UiActionOutcome.NotAvailable, Ui.RequestAction(id, UiActions.SetValue, "new").Outcome);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fake_native_request_is_marshaled_to_next_frame_and_dispose_cancels_it(bool close)
    {
        using var host = new HeadlessHost { Drawing = false }; var calls = 0;
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new AccessKitWindowA457Tests.WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Proof", windows, trees);
        void Draw() { Begin(); if (Ui.Button("Do it"u8)) calls++; Ui.End(); }
        host.Frames(Draw, 3);
        bridge.Publish(Ui.Snapshot(), "Proof", new(0, 0, 800, 600), 1, 1);
        windows.Activate();
        var target = trees.Ids.Last(); // root, recorded window, then its Button
        var callbackThread = new Thread(() => windows.Receive!(new(target, AccessKitNativeActionKind.Click)));
        callbackThread.Start(); callbackThread.Join();
        Assert.Equal(0, calls);
        bridge.DrainRequests(); Assert.Equal("pending", bridge.LastActionOutcome);
        if (close) bridge.Dispose();
        host.Frame(Draw);
        Assert.Equal(close ? 0 : 1, calls);
        if (!close) { bridge.DrainRequests(); Assert.Equal("applied", bridge.LastActionOutcome); }
    }

    [Fact]
    public void Numeric_value_and_invalid_numeric_text_use_the_existing_ref_commit_path()
    {
        using var host = new HeadlessHost { Drawing = false }; var number = 1.0; var commits = 0;
        void Draw() { Begin(); Ui.InputDouble("Number"u8, ref number); if (ImGui.IsItemDeactivatedAfterEdit()) commits++; Ui.End(); }
        host.Frames(Draw, 3); var id = Id(UiRole.TextField);
        var valid = Ui.RequestAction(id, UiActions.SetValue, "3.5"); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.Applied, valid.Outcome); Assert.Equal(3.5, number); Assert.Equal(1, commits);
        var invalid = Ui.RequestAction(id, UiActions.SetValue, "NaN"); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, invalid.Outcome); Assert.Equal(3.5, number); Assert.Equal(1, commits);
    }

    [Fact]
    public void Modal_popup_blocks_a_queued_background_action_and_removes_its_advertised_actions()
    {
        using var host = new HeadlessHost { Drawing = false }; var modal = false; var calls = 0;
        void Draw()
        {
            if (modal)
            {
                ImGui.OpenPopup("Blocking modal"u8);
                var open = true;
                if (Ui.BeginPopupModal("Blocking modal"u8, ref open)) { Ui.Text("Wait"u8); Ui.EndPopup(); }
            }
            Begin(); if (Ui.Button("Do it"u8)) calls++; Ui.End();
        }
        host.Frames(Draw, 3);
        var ticket = Ui.RequestAction(Id(UiRole.Button), UiActions.Invoke);
        modal = true; host.Frame(Draw);
        Assert.Equal(UiActionOutcome.NotAvailable, ticket.Outcome); Assert.Equal(0, calls);
        var target = Ui.Snapshot().Nodes.Single(n => n.Role == UiRole.Button);
        Assert.False(target.Enabled); Assert.Equal(UiActions.None, target.Actions);
    }

    [Fact]
    public void Menu_invoke_uses_the_return_path_once_and_closes_the_native_popup()
    {
        using var host = new HeadlessHost { Drawing = false }; var start = true; var calls = 0;
        void Draw()
        {
            Begin();
            if (start) { ImGui.OpenPopup("Commands"u8); start = false; }
            if (Ui.BeginPopup("Commands"u8)) { if (Ui.MenuItem("Run"u8)) calls++; Ui.EndPopup(); }
            Ui.End();
        }
        host.Frames(Draw, 3);
        var ticket = Ui.RequestAction(Id(UiRole.MenuItem), UiActions.Invoke);
        host.Frame(Draw); Assert.Equal(UiActionOutcome.Applied, ticket.Outcome); Assert.Equal(1, calls);
        host.Frame(Draw); Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.MenuItem); Assert.Equal(1, calls);
    }

    [Fact]
    public void Description_is_available_without_hover_and_preserves_full_text_evidence()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw() { Begin(); Ui.Button("Info"u8); Ui.TooltipLast("A complete description"); Ui.End(); }
        host.Frame(Draw); var node = Ui.Snapshot().Nodes.Single(n => n.Role == UiRole.Button);
        Assert.False(node.Hovered); Assert.Equal("A complete description", node.Tooltip); Assert.NotNull(node.TextEvidence);
    }
}
