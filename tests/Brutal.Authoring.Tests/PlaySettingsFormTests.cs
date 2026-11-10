using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class PlaySettingsFormTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Directory.CreateTempSubdirectory("play-form-").FullName;
        public string Project=>Path.Combine(Root,"project");public string Local=>Path.Combine(Root,"local");
        public Fixture(){Directory.CreateDirectory(Path.Combine(Project,"data"));Directory.CreateDirectory(Path.Combine(Project,"schemas"));File.WriteAllText(Path.Combine(Project,"data","item.json"),"{}");File.WriteAllText(Path.Combine(Project,"schemas","item.schema.json"),"{}");}
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void Plain_project_tools_menu_opens_settings_without_starting_or_changing_project_data()
    {
        using var f=new Fixture();using var host=new HeadlessHost(1100,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(f.Local,"recent.json")));
        Assert.True(launcher.OpenFolder(f.Project),launcher.Said);var workspace=launcher.Workspace!;Assert.NotNull(workspace.PlaySettings);
        Assert.True(workspace.Show("item.json"));host.Frames(launcher.Draw,3);
        void Click(string label){var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label);host.Click(launcher.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);host.Frames(launcher.Draw,2);}
        Click("Tools");Click("Play settings...");Assert.True(workspace.PlaySettings!.Open);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Save Play settings");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Program");
        Assert.Equal("{}",File.ReadAllText(Path.Combine(f.Project,"data","item.json")));Assert.Empty(workspace.Project.Dirty);
        Assert.False(File.Exists(Path.Combine(f.Project,".bed","project.json")));
    }
    [Fact]
    public void Saving_external_command_does_not_grant_approval_and_form_displays_exact_question()
    {
        using var f=new Fixture();var exe=Path.Combine(f.Root,"external.exe");File.WriteAllText(exe,"inert fixture, never run");
        using var host=new HeadlessHost(1100,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var form=new PlaySettingsForm(f.Project,null,f.Local){Open=true};form.SetDraft(new(exe,["--test"]));Assert.True(form.Save(),form.Status);
        Assert.True(form.TryLaunch(out var launch,out var why),why);Assert.Contains("Needs your OK",form.ApprovalNeeded(launch!,false));
        host.Frames(form.Draw,3);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains(exe));
        Assert.False(File.Exists(Path.Combine(f.Local,"play-approvals.json")));
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Allow this exact saved command");
        host.Click(form.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);host.Frames(form.Draw,2);
        Assert.Null(form.ApprovalNeeded(launch!,false));Assert.True(File.Exists(Path.Combine(f.Local,"play-approvals.json")));
        Assert.Equal("inert fixture, never run",File.ReadAllText(exe));
    }
    [Fact]
    public void Missing_program_without_build_refuses_save_and_does_not_create_settings()
    {
        using var f=new Fixture();var form=new PlaySettingsForm(f.Project,null,f.Local);form.SetDraft(new("missing.exe"));
        Assert.False(form.Save());Assert.Contains("does not exist",form.Status);Assert.False(File.Exists(Path.Combine(f.Project,".bed","project.json")));
    }
}
