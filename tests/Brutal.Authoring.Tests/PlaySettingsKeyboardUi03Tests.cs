using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlaySettingsKeyboardUi03Tests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("play-keyboard-ui03-").FullName;
        public readonly HeadlessHost Host=new(1200,900){Drawing=false};
        public readonly PlaySettingsForm Form;
        public Rig()
        {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Form=new(Root,null,Path.Combine(Root,"local")){Open=true};Host.Frames(Form.Draw,4);
        }
        public UiNode Field(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label);
        public string? Focused=>Ui.Snapshot().Nodes.FirstOrDefault(n=>n.Focused&&n.Role is UiRole.TextField or UiRole.Button).Label;
        public void Reach(string label)
        {
            for(var i=0;i<30&&Focused!=label;i++)Host.Press(Form.Draw,ImGuiKey.Tab);
            Assert.Equal(label,Focused);
        }
        public void Replace(string label,string value)
        {
            Reach(label);Host.Press(Form.Draw,ImGuiKey.A,ctrl:true);Host.Type(Form.Draw,value);
        }
        public void SaveWithKeys()
        {
            Reach("Save Play settings");Host.Press(Form.Draw,ImGuiKey.Enter);Host.Frames(Form.Draw,2);
        }
        public void Dispose(){Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Tools_access_keys_open_the_real_project_form_and_tab_enters_its_fields()
    {
        using var rig=new Rig();rig.Form.Open=false;
        Directory.CreateDirectory(Path.Combine(rig.Root,"data"));Directory.CreateDirectory(Path.Combine(rig.Root,"schemas"));
        File.WriteAllText(Path.Combine(rig.Root,"data","item.json"),"{}");
        File.WriteAllText(Path.Combine(rig.Root,"schemas","item.schema.json"),"{}");
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(rig.Root,"local","recent.json")));
        Assert.True(launcher.OpenFolder(rig.Root),launcher.Said);var workspace=launcher.Workspace!;
        Assert.True(workspace.Show("item.json"));rig.Host.Frames(launcher.Draw,4);
        rig.Host.Press(launcher.Draw,ImGuiKey.T,alt:true);
        var menu=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Play settings...");
        Assert.True(menu.Enabled);Assert.Equal("P",menu.AccessKey);
        rig.Host.Press(launcher.Draw,ImGuiKey.P,alt:true);rig.Host.Frames(launcher.Draw,4);
        Assert.True(workspace.PlaySettings!.Open);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Program"&&n.Focused);
        rig.Host.Press(launcher.Draw,ImGuiKey.Tab);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Arguments (JSON list)"&&n.Focused);
        Assert.False(File.Exists(Path.Combine(rig.Root,".bed","project.json")));
        Assert.Equal("{}",File.ReadAllText(Path.Combine(rig.Root,"data","item.json")));
    }
    [Fact]
    public void Opening_focuses_program_and_tab_reaches_all_six_named_fields_and_actions()
    {
        using var rig=new Rig();Assert.Equal("Program",rig.Focused);
        var labels=new[]{"Program","Arguments (JSON list)","Working folder","Build command","Build arguments (JSON list)","Build inputs (JSON list of globs)"};
        foreach(var label in labels)
        {
            rig.Reach(label);var node=rig.Field(label);
            Assert.True(node.Enabled);Assert.True(node.Focused);Assert.False(node.Bounds.IsEmpty);
            Assert.NotNull(node.Value);
        }
        rig.Reach("Save Play settings");rig.Reach("Close settings");
        rig.Host.Press(rig.Form.Draw,ImGuiKey.Enter);Assert.False(rig.Form.Open);
        Assert.False(File.Exists(Path.Combine(rig.Root,".bed","project.json")));
    }
    [Theory]
    [InlineData("Arguments (JSON list)","not a list","Arguments")]
    [InlineData("Build arguments (JSON list)","[1]","Build arguments")]
    [InlineData("Build inputs (JSON list of globs)","[null]","Build inputs")]
    public void Invalid_list_is_named_in_plain_tree_text_and_can_be_fixed_without_a_mouse(string label,string invalid,string name)
    {
        using var rig=new Rig();File.WriteAllText(Path.Combine(rig.Root,"game.exe"),"inert fixture, never run");
        rig.Replace("Program","game.exe");rig.Replace(label,invalid);rig.SaveWithKeys();
        var expected=name+" must be a JSON list of quoted strings. Use [] for no items.";
        Assert.Equal(expected,rig.Form.Status);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==expected);
        Assert.False(File.Exists(Path.Combine(rig.Root,".bed","project.json")));
        rig.Replace(label,"[]");rig.SaveWithKeys();
        Assert.Equal("Play settings saved.",rig.Form.Status);
        Assert.True(File.Exists(Path.Combine(rig.Root,".bed","project.json")));
        Assert.False(File.Exists(Path.Combine(rig.Root,"local","play-approvals.json")));
        Assert.Equal("inert fixture, never run",File.ReadAllText(Path.Combine(rig.Root,"game.exe")));
    }
    [Fact]
    public void Escape_closes_and_reopening_returns_keyboard_focus_to_program()
    {
        using var rig=new Rig();rig.Reach("Working folder");
        rig.Host.Press(rig.Form.Draw,ImGuiKey.Escape);Assert.False(rig.Form.Open);
        rig.Form.Open=true;rig.Host.Frames(rig.Form.Draw,4);Assert.Equal("Program",rig.Focused);
        Assert.False(File.Exists(Path.Combine(rig.Root,".bed","project.json")));
    }
}
