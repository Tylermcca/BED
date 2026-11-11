using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class NumericStepsA11y12Tests
{
    private sealed class Sink : IAccessKitUpdateSink
    {
        public AccessKitProjectedUpdate Last = null!;
        public bool TryApply(AccessKitProjectedUpdate update) { Last = update; return true; }
    }
    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host = new(800, 500) { Drawing = false };
        public double Value = 2, Step = 1, FastStep = 10;
        public bool ReadOnly, Disabled, Offscreen;
        private readonly bool integer;
        public string Label = "Quantity";
        public int Commits, Refusals;
        public bool Edited, Deactivated;
        public readonly Stack<double> Undo = new();
        public Rig(bool integer = false) { this.integer = integer; EditorWindow<DataDocument>.Configure(ImGui.GetIO()); Host.Frames(Draw, 3); }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(20,20)); ImGui.SetNextWindowSize(new float2(650,350));
            Ui.Begin("Numeric action proof"u8);
            if (Offscreen) ImGui.SetCursorPosY(2000);
            Ui.BeginDisabled(Disabled, "Locked");
            var candidate = Value;
            if (Input(ref candidate))
            {
                if (candidate < 0) Refusals++;
                else { Undo.Push(Value); Value = candidate; Commits++; }
            }
            Edited = ImGui.IsItemEdited(); Deactivated = ImGui.IsItemDeactivatedAfterEdit();
            Ui.EndDisabled(); Ui.End();
        }
        private bool Input(ref double candidate)
        {
            var flags=ReadOnly?ImGuiInputTextFlags.ReadOnly:ImGuiInputTextFlags.EnterReturnsTrue;
            if(!integer)return Ui.InputDouble(Label,ref candidate,Step,FastStep,"%g"u8,flags);
            var number=(int)candidate;var result=Ui.InputInt(Label,ref number,(int)Step,(int)FastStep,flags);
            candidate=number;return result;
        }
        public AccessKitProjectedNode Project(string name)
        {
            var sink = new Sink();
            Assert.True(new AccessKitProjection().Submit(Ui.Snapshot(), "Numbers", new(0,0,800,500), 1, 1, sink));
            return Assert.Single(sink.Last.ChangedNodes, n => n.Label == name);
        }
        public UiActionTicket Request(string name, UiActions action, string? value = null)
        {
            var node = Project(name); Assert.True(node.Actions.HasFlag(action));
            return Ui.RequestAction(node.SourceId!.Value, action, value);
        }
        public void Dispose() => Host.Dispose();
    }
    [Fact]
    public void Projected_SetValue_and_step_Invoke_commit_once_and_expose_commit_facts()
    {
        using var r = new Rig();
        var ticket = r.Request("Quantity", UiActions.SetValue, "4.5"); r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied, ticket.Outcome); Assert.Equal(4.5,r.Value);
        Assert.True(r.Edited); Assert.True(r.Deactivated); Assert.Equal(1,r.Commits);
        ticket = r.Request("Increase Quantity", UiActions.Invoke); r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,ticket.Outcome); Assert.Equal(5.5,r.Value);
        Assert.True(r.Edited); Assert.True(r.Deactivated);
        ticket = r.Request("Decrease Quantity",UiActions.Invoke); r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,ticket.Outcome); Assert.Equal(4.5,r.Value);
        r.Host.Frames(r.Draw,2); Assert.Equal(3,r.Commits); Assert.Equal(3,r.Undo.Count);
        r.Value=r.Undo.Pop(); r.Value=r.Undo.Pop(); r.Value=r.Undo.Pop(); Assert.Equal(2,r.Value);
    }
    [Fact]
    public void Projected_button_bounds_are_the_real_mouse_step_targets()
    {
        using var r = new Rig();
        var plus = r.Project("Increase Quantity").Bounds;
        r.Host.Click(r.Draw, plus.X + plus.Width/2, plus.Y + plus.Height/2);
        Assert.Equal(3,r.Value); Assert.Equal(1,r.Commits);
        var minus = r.Project("Decrease Quantity").Bounds;
        r.Host.Click(r.Draw,minus.X+minus.Width/2,minus.Y+minus.Height/2);
        Assert.Equal(2,r.Value); Assert.Equal(2,r.Commits);
    }
    [Theory]
    [InlineData("NaN")][InlineData("Infinity")][InlineData("abc")][InlineData("1e999")]
    public void Invalid_numeric_actions_do_not_edit(string value)
    {
        using var r=new Rig(); var t=r.Request("Quantity",UiActions.SetValue,value); r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome); Assert.Equal(2,r.Value); Assert.Equal(0,r.Commits);
    }
    [Theory]
    [InlineData("readonly")][InlineData("disabled")][InlineData("renamed")][InlineData("clipped")]
    public void Changed_target_guards_refuse_pending_step_and_value(string change)
    {
        foreach(var step in new[]{false,true})
        {
            using var r=new Rig(); var t=r.Request(step?"Increase Quantity":"Quantity",step?UiActions.Invoke:UiActions.SetValue,step?null:"7");
            switch(change){case "readonly":r.ReadOnly=true;break;case "disabled":r.Disabled=true;break;case "renamed":r.Label="Other";break;case "clipped":r.Offscreen=true;break;}
            r.Host.Frames(r.Draw,2); Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome); Assert.Equal(2,r.Value); Assert.Equal(0,r.Commits);
        }
    }
    [Fact]
    public void Native_accessibility_bridge_dispatches_projected_SetValue_and_Click()
    {
        using var r=new Rig();
        var trees=new AccessKitNativeTreeA457Tests.Fake(); var windows=new AccessKitWindowA457Tests.WindowFake(trees);
        using var bridge=new AccessKitWindowBridge(42,"Numbers",windows,trees);
        Assert.True(bridge.Publish(Ui.Snapshot(),"Numbers",new(0,0,800,500),1,1));
        var field=r.Project("Quantity");
        windows.Receive!(new(field.Id,AccessKitNativeActionKind.SetValue,"9"));bridge.DrainRequests();
        Assert.Equal(2,r.Value);r.Host.Frame(r.Draw);bridge.RaisePendingEvents();bridge.DrainRequests();
        Assert.Equal(9,r.Value);Assert.Equal("applied",bridge.LastActionOutcome);Assert.Equal(1,r.Commits);
        bridge.Publish(Ui.Snapshot(),"Numbers",new(0,0,800,500),1,1);
        windows.Receive!(new(r.Project("Increase Quantity").Id,AccessKitNativeActionKind.Click));bridge.DrainRequests();
        r.Host.Frame(r.Draw);bridge.RaisePendingEvents();bridge.DrainRequests();
        Assert.Equal(10,r.Value);Assert.Equal("applied",bridge.LastActionOutcome);Assert.Equal(2,r.Commits);
    }
    [Fact]
    public void Native_typing_and_assistive_value_share_validation_and_undo()
    {
        foreach(var semantic in new[]{false,true})
        {
            using var r=new Rig();
            if(semantic){r.Request("Quantity",UiActions.SetValue,"7");r.Host.Frame(r.Draw);}
            else
            {
                var b=r.Project("Quantity").Bounds;
                r.Host.Click(r.Draw,b.X+b.Width/2,b.Y+b.Height/2);
                r.Host.Press(r.Draw,ImGuiKey.A,ctrl:true);r.Host.Type(r.Draw,"7");r.Host.Press(r.Draw,ImGuiKey.Enter);
            }
            Assert.Equal(7,r.Value);Assert.Equal(1,r.Commits);Assert.Equal(2,Assert.Single(r.Undo));
        }
    }
    [Fact]
    public void Fast_step_uses_native_control_modifier_policy()
    {
        using var r=new Rig();
        var t=r.Request("Increase Quantity",UiActions.Invoke);ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Ctrl,true);r.Host.Frame(r.Draw);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Ctrl,false);r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(12,r.Value);Assert.Equal(1,r.Commits);
    }
    [Fact]
    public void Nonpositive_step_has_no_phantom_buttons_and_value_still_works()
    {
        using var r=new Rig();r.Step=-1;r.Host.Frames(r.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button);
        var t=r.Request("Quantity",UiActions.SetValue,"6");r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(6,r.Value);Assert.Equal(1,r.Commits);
    }
    [Fact]
    public void Overflowing_step_is_refused_without_changing_the_callers_value()
    {
        using var r=new Rig();r.Step=double.MaxValue;r.Value=double.MaxValue;r.Host.Frames(r.Draw,2);
        var t=r.Request("Increase Quantity",UiActions.Invoke);r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome);Assert.Equal(double.MaxValue,r.Value);Assert.Equal(0,r.Commits);
    }
    [Fact]
    public void Focus_and_SetValue_replace_an_active_nested_text_buffer_without_late_commit()
    {
        using var r=new Rig();var focus=r.Request("Quantity",UiActions.Focus);r.Host.Frames(r.Draw,2);
        Assert.Equal(UiActionOutcome.Applied,focus.Outcome);Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField).Focused);
        r.Host.Press(r.Draw,ImGuiKey.A,ctrl:true);r.Host.Type(r.Draw,"99");
        var value=r.Request("Quantity",UiActions.SetValue,"8");r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,value.Outcome);Assert.Equal(8,r.Value);Assert.Equal(1,r.Commits);
        r.Host.Press(r.Draw,ImGuiKey.Enter);r.Host.Frames(r.Draw,2);
        Assert.Equal(8,r.Value);Assert.Equal(1,r.Commits);
    }
    [Fact]
    public void Integer_API_inventory_has_approved_signature_and_defaults()
    {
        var method=typeof(Ui).GetMethod(nameof(Ui.InputInt));Assert.NotNull(method);Assert.True(method.IsPublic);Assert.True(method.IsStatic);
        Assert.Equal(typeof(bool),method.ReturnType);
        Assert.Equal(new[]{typeof(ImString),typeof(int).MakeByRefType(),typeof(int),typeof(int),typeof(ImGuiInputTextFlags)},method.GetParameters().Select(p=>p.ParameterType).ToArray());
        Assert.Equal(1,method.GetParameters()[2].DefaultValue);Assert.Equal(100,method.GetParameters()[3].DefaultValue);
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;Assert.NotNull(root);
        Assert.Contains("Ui.InputInt(ImString, ref int, int step = 1, int stepFast = 100, ImGuiInputTextFlags flags = 0)",File.ReadAllText(Path.Combine(root.FullName,"docs/guide/api_overview.md")));
    }
    [Fact]
    public void Integer_projected_value_steps_and_native_typing_share_the_commit_path()
    {
        using var r=new Rig(true);var t=r.Request("Quantity",UiActions.SetValue,"7");r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(7,r.Value);Assert.True(r.Edited);Assert.True(r.Deactivated);
        t=r.Request("Increase Quantity",UiActions.Invoke);r.Host.Frame(r.Draw);Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(8,r.Value);
        var minus=r.Project("Decrease Quantity").Bounds;r.Host.Click(r.Draw,minus.X+minus.Width/2,minus.Y+minus.Height/2);Assert.Equal(7,r.Value);
        var field=r.Project("Quantity").Bounds;r.Host.Click(r.Draw,field.X+field.Width/2,field.Y+field.Height/2);
        r.Host.Press(r.Draw,ImGuiKey.A,ctrl:true);r.Host.Type(r.Draw,"9");r.Host.Press(r.Draw,ImGuiKey.Enter);
        Assert.Equal(9,r.Value);Assert.Equal(4,r.Commits);Assert.Equal(4,r.Undo.Count);
    }
    [Theory]
    [InlineData("1.5")][InlineData("2147483648")][InlineData("-2147483649")][InlineData("1e3")][InlineData("NaN")]
    public void Integer_invalid_or_out_of_range_values_do_not_edit(string value)
    {
        using var r=new Rig(true);var t=r.Request("Quantity",UiActions.SetValue,value);r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome);Assert.Equal(2,r.Value);Assert.Empty(r.Undo);
    }
    [Fact]
    public void Integer_step_overflow_and_new_readonly_state_refuse_without_wraparound()
    {
        using var r=new Rig(true);r.Value=int.MaxValue;r.Host.Frames(r.Draw,2);
        var t=r.Request("Increase Quantity",UiActions.Invoke);r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome);Assert.Equal(int.MaxValue,r.Value);Assert.Empty(r.Undo);
        t=r.Request("Decrease Quantity",UiActions.Invoke);r.ReadOnly=true;r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.NotAvailable,t.Outcome);Assert.Equal(int.MaxValue,r.Value);Assert.Empty(r.Undo);
        Assert.False(r.Project("Quantity").Actions.HasFlag(UiActions.SetValue));
        Assert.Equal(UiActions.None,r.Project("Increase Quantity").Actions);
    }
    [Fact]
    public void Field_and_step_share_the_callers_refusal_path()
    {
        using var r=new Rig(); var t=r.Request("Quantity",UiActions.SetValue,"-1");r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(1,r.Refusals);Assert.Equal(2,r.Value);Assert.Empty(r.Undo);
        r.Value=0;r.Host.Frames(r.Draw,2);t=r.Request("Decrease Quantity",UiActions.Invoke);r.Host.Frame(r.Draw);
        Assert.Equal(UiActionOutcome.Applied,t.Outcome);Assert.Equal(2,r.Refusals);Assert.Equal(0,r.Value);Assert.Empty(r.Undo);
    }
}
