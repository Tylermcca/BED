using System.Text.Json;
using Bed.Agent.Cli;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PaletteReasonsUi28Tests
{
    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host=new(1200,900){Drawing=false};
        public readonly EditorWindow<DataDocument> Window;
        public readonly string Why="Save the source draft and wait for its check to finish before running this command. The existing output stays intact while you correct the source and run its check again.";
        public int Locked,Ready;
        public Rig()
        {
            var schemas=new JsonSchemas().Add("sample", "{\"type\":\"object\"}");var session=DataKit.Session(new DataFormat().Read("{}"),schemas,"sample");
            var editor=new EditorModel<DataDocument>(session,DataEditing.Outline);
            editor.Add(new Panel("fixture.palette","Card controls",()=>{},Commands:[new("locked","Card action locked",()=>{Locked++;return true;},()=>Why),new("ready","Card action ready",()=>{Ready++;return true;})]));
            var keys=new Shortcuts();keys.Set("editor.panel_do.fixture.palette/locked","Ctrl+L");keys.Set("editor.panel_do.fixture.palette/ready","Ctrl+K");
            Window=new(editor,shortcuts:keys);EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Window.Draw,4);
        }
        public void Key(string key)=>UiScript.Run(JsonSerializer.Serialize(new[]{new{key}}),Host,Window.Draw,_=>{});
        public void Search(string text){Key("Ctrl+Shift+P");Key("Ctrl+A");UiScript.Run(JsonSerializer.Serialize(new[]{new{type=text}}),Host,Window.Draw,_=>{});}
        public UiNode Item(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.StartsWith(label,StringComparison.Ordinal));
        public UiNode PopupStatus
        {
            get
            {
                var nodes=Ui.Snapshot().Nodes;
                bool InPopup(UiNode node){for(var parent=node.Parent;parent>=0;parent=nodes[parent].Parent)if(nodes[parent].Role==UiRole.Dialog&&nodes[parent].Label=="palette")return true;return false;}
                return Assert.Single(nodes,n=>n.Role==UiRole.Status&&n.Label==Why&&InPopup(n));
            }
        }
        public void Clean(){Assert.Equal(0,Locked);Assert.Empty(Window.Editor.Session.Journal);Assert.False(Window.Editor.Session.Dirty);Assert.Equal("{}",Window.Editor.Session.Current.Root.GetRawText());}
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void Disabled_result_keeps_reason_shortcut_and_highlight_Enter_announces_without_running_then_Down_runs_available()
    {
        using var r=new Rig();r.Search("Card action");var locked=r.Item("Card action locked");Assert.False(locked.Enabled);Assert.Equal(UiActions.None,locked.Actions);Assert.Contains("Ctrl+L",locked.Label);Assert.Equal(r.Why,locked.DisabledReason);Assert.True(locked.Selected);
        r.Key("Enter");Assert.Equal(r.Why,r.Window.Status);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Dialog&&n.Label=="palette");
        var status=r.PopupStatus;Assert.False(status.Bounds.IsEmpty);Assert.NotNull(status.TextEvidence);Assert.False(status.TextEvidence!.Value.Truncated);r.Clean();
        r.Key("DownArrow");Assert.True(r.Item("Card action ready").Selected);r.Key("Enter");Assert.Equal(1,r.Ready);r.Clean();Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Dialog&&n.Label=="palette");
    }
    [Fact]
    public void Only_disabled_result_remains_reachable_with_repeated_Enter_and_Escape_and_no_match_cannot_execute()
    {
        using var r=new Rig();r.Search("Card action locked");r.Key("Enter");var first=r.PopupStatus;r.Key("Enter");var second=r.PopupStatus;Assert.Equal(first.Id,second.Id);r.Clean();r.Key("Escape");Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Dialog&&n.Label=="palette");
        r.Search("there-is-no-such-command-qa28");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="No matching commands.");r.Key("Enter");Assert.Equal(0,r.Ready);r.Clean();
    }
}
