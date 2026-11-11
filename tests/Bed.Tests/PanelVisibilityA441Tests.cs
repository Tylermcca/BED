using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class PanelVisibilityA441Tests
{
    sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("panel-visibility-").FullName;
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly DataWorkspace Workspace;
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Path.Combine(Root,"data","sample.json"),"{\"value\":1}");
            File.WriteAllText(Path.Combine(Root,"schemas","sample.schema.json"),"{\"type\":\"object\"}");
            Workspace=new(DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")));
            Assert.True(Workspace.Show("sample.json"));EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact] public void Hidden_panel_keeps_registered_commands_and_null_predicate_keeps_old_behavior()
    {
        using var r=new Rig();var w=r.Workspace.Window!;bool show=false;var calls=0;
        w.Editor.Add(new Panel("fixture.hidden","Conditional",()=>Ui.Text("Conditional body"u8),Commands:[new("run","Conditional command",()=>{calls++;return true;})],ShowWhen:()=>show));
        w.Editor.Add(new Panel("fixture.normal","Normal",()=>{}));
        Assert.Contains(w.PaletteEntries(""),e=>e.Id==EditorWindow<DataDocument>.PanelAction+"fixture.hidden");
        Assert.All(w.UiActions().Where(a=>a.Id.Contains("fixture.hidden",StringComparison.Ordinal)),a=>Assert.False(a.Present(UiActionContext.Palette).Visible));
        Assert.Contains(w.PaletteEntries(""),e=>e.Id==EditorWindow<DataDocument>.PanelAction+"fixture.normal");
        r.Host.Frames(r.Workspace.Draw,3);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Conditional body");
        Assert.True(w.Run(EditorWindow<DataDocument>.PanelCommandAction+"fixture.hidden/run"));Assert.Equal(1,calls);
        Assert.Contains(w.Editor.Panels,p=>p.Id=="fixture.hidden");
        show=true;r.Host.Frames(r.Workspace.Draw,2);
        Assert.Contains(w.PaletteEntries(""),e=>e.Label=="Conditional command");
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Conditional body");
    }
    [Fact] public void Hidden_registration_is_not_a_visible_or_keyboard_palette_choice()
    {
        using var r=new Rig();var w=r.Workspace.Window!;
        w.Editor.Add(new Panel("fixture.hidden","Conditional",()=>{},OpenAtStart:false,Commands:[new("run","Conditional command",()=>throw new InvalidOperationException("Hidden command was selected"))],ShowWhen:()=>false));
        r.Host.Frames(r.Workspace.Draw,3);Assert.True(w.Run("editor.palette"));r.Host.Frames(r.Workspace.Draw,3);
        Assert.True(w.PaletteOpen);
        Assert.Contains(w.PaletteEntries(""),e=>e.Id==EditorWindow<DataDocument>.PanelCommandAction+"fixture.hidden/run");
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.Contains("Conditional",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem);
        r.Host.Press(r.Workspace.Draw,ImGuiKey.Escape);Assert.False(w.PaletteOpen);
    }
    [Fact] public void Pending_notice_opens_review_and_rejected_last_suggestion_stays_visible_until_closed()
    {
        using var r=new Rig();var w=r.Workspace.Window!;var panel=Assert.Single(w.Editor.Panels,p=>p.Id=="project.proposals");
        Assert.False(panel.ShowWhen!());Assert.False(Assert.Single(w.UiActions(),a=>a.Id==EditorWindow<DataDocument>.PanelAction+panel.Id).Present(UiActionContext.Palette).Visible);
        var basis=r.Workspace.Proposals.List("sample.json");var ids=new List<string>();
        for(var i=0;i<3;i++)
        {
            var p=r.Workspace.Proposals.Submit("sample.json",basis["session"]!.GetValue<string>(),basis["revision"]!.GetValue<long>(),[new("set","/value",JsonSerializer.SerializeToElement(i+2))],[]);
            Assert.Null(p["error"]);ids.Add(p["id"]!.GetValue<string>());
        }
        Assert.True(panel.ShowWhen());r.Host.Frames(r.Workspace.Draw,3);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="3 suggested changes waiting. Review");
        r.Host.Click(r.Workspace.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);
        Assert.True(w.Editor.IsOpen(panel.Id));
        foreach(var id in ids) Assert.Null(r.Workspace.Proposals.Reject(id,r.Workspace.Proposals.Show(id)["digest"]!.GetValue<string>())["error"]);
        Assert.True(panel.ShowWhen());r.Host.Frames(r.Workspace.Draw,2);
        Assert.Contains(w.PaletteEntries(""),e=>e.Id==EditorWindow<DataDocument>.PanelAction+panel.Id);
        Assert.True(w.Editor.SetOpen(panel.Id,false));Assert.False(panel.ShowWhen());
        Assert.False(Assert.Single(w.UiActions(),a=>a.Id==EditorWindow<DataDocument>.PanelAction+panel.Id).Present(UiActionContext.Palette).Visible);
        Assert.Equal("{\"value\":1}",File.ReadAllText(Path.Combine(r.Root,"data","sample.json")));
        Assert.Equal(0,r.Workspace.Active!.Session!.History.UndoCount);
    }
}
