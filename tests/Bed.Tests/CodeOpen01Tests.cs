using System.Diagnostics;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class CodeOpen01Tests
{
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("code-open01-").FullName;
        internal readonly string Kind=Locations.CodeEditor,Program=Locations.CodeEditorProgram;
        internal readonly Func<ProcessStartInfo,bool> OriginalLaunch=CodeEditorTool.Launch;
        internal readonly Func<IReadOnlyList<CodeEditorPlace>> OriginalPlaces=CodeEditorTool.Places;
        internal string Game=>Path.Combine(Root,"My game");
        internal string Editor=>Path.Combine(Root,"Fake editor.exe");
        internal readonly List<ProcessStartInfo> Opened=[];
        internal Rig()
        {
            Directory.CreateDirectory(Game);File.WriteAllText(Editor,"fake editor: never executed");
            Locations.CodeEditor="vscode";Locations.CodeEditorProgram=Editor;
            CodeEditorTool.Launch=plan=>{Opened.Add(plan);return true;};CodeEditorTool.Places=()=>[new("vscode",Editor),new("rider",Editor),new("visualstudio",Editor)];
        }
        internal string Source(string name="Broken.cs"){var file=Path.GetFullPath(Path.Combine(Game,name));Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,"class Broken { int Value => MissingValue; }\n");return file;}
        public void Dispose(){CodeEditorTool.Launch=OriginalLaunch;CodeEditorTool.Places=OriginalPlaces;Locations.CodeEditor=Kind;Locations.CodeEditorProgram=Program;Directory.Delete(Root,true);}
    }
    [Theory]
    [InlineData("vscode")][InlineData("rider")][InlineData("visualstudio")]
    public void Editor_plans_pass_quoted_paths_as_separate_arguments_and_real_line_numbers(string kind)
    {
        using var r=new Rig();var source=r.Source("src/Spaces and & symbols.cs");
        var error=new ProjectBuildError("src/Spaces and & symbols.cs",12,7,"CS0103","MissingValue","Check spelling");
        var plan=CodeEditorTool.Plan(r.Game,r.Game,kind,r.Editor,error);Assert.False(plan.UseShellExecute);Assert.Equal(r.Editor,plan.FileName);Assert.Equal(r.Game,plan.WorkingDirectory);
        var args=plan.ArgumentList.ToArray();
        if(kind=="vscode")Assert.Equal(new[]{"--reuse-window","--goto",source+":12:7"},args);
        else if(kind=="rider")Assert.Equal(new[]{"--line","12","--column","7",source},args);
        else Assert.Equal(new[]{"/Edit",source,"/Command","Edit.GoTo 12"},args);
        Assert.Empty(r.Opened);
    }
    [Fact]
    public void Unique_solution_is_opened_while_ambiguous_solutions_open_the_folder_and_missing_editor_refuses()
    {
        using var r=new Rig();var solution=Path.Combine(r.Game,"My game.slnx");File.WriteAllText(solution,"<Solution />");
        Assert.Equal(solution,CodeEditorTool.Plan(r.Game,r.Game,"windows","").FileName);
        File.WriteAllText(Path.Combine(r.Game,"Other.sln"),"");Assert.Equal(r.Game,CodeEditorTool.Plan(r.Game,r.Game,"windows","").FileName);
        Assert.Equal(r.Editor,CodeEditorTool.Plan(r.Game,r.Game,"vscode","").FileName);
        Assert.Throws<InvalidOperationException>(()=>CodeEditorTool.Plan(r.Game,r.Game,"vscode",Path.Combine(r.Root,"gone.exe")));
        Assert.Empty(r.Opened);
    }
    [Theory]
    [InlineData("../outside.cs",1)][InlineData("game.exe",1)][InlineData("Broken.cs:stream",1)][InlineData("Broken.cs",0)]
    public void Compiler_output_cannot_open_an_outside_path_executable_stream_or_missing_line(string file,int line)
    {
        using var r=new Rig();r.Source();File.WriteAllText(Path.Combine(r.Root,"outside.cs"),"");File.WriteAllText(Path.Combine(r.Game,"game.exe"),"");
        Assert.False(CodeEditorTool.Open(r.Game,r.Game,new(file,line,1,"","",""),out _));Assert.Empty(r.Opened);
    }
    [Fact]
    public void Preference_choice_and_program_round_trip_without_changing_other_settings_or_project_files()
    {
        using var r=new Rig();var local=Directory.CreateDirectory(Path.Combine(r.Root,"local")).FullName;var settings=Path.Combine(local,"settings.json");
        File.WriteAllText(settings,"{\"other\":42}");
        using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using(var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(local,"recent.json"))))
        {
            host.Frames(launcher.Draw,3);Assert.Equal("{\"other\":42}",File.ReadAllText(settings));
            Locations.CodeEditor="rider";Locations.CodeEditorProgram=r.Editor;host.Frames(launcher.Draw,2);
        }
        using(var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(local,"recent.json"))))
        {host.Frames(launcher.Draw,3);Assert.Equal("rider",Locations.CodeEditor);Assert.Equal(r.Editor,Locations.CodeEditorProgram);}
        var saved=JsonNode.Parse(File.ReadAllText(settings))!;Assert.Equal(42,saved["other"]!.GetValue<int>());Assert.Empty(Directory.GetFiles(r.Game));Assert.Empty(r.Opened);
    }
    [Fact]
    public async Task Planted_compile_error_in_Play_console_opens_its_actual_line_in_the_same_fake_editor()
    {
        using var r=new Rig();var broken=r.Source();File.WriteAllText(Path.Combine(r.Game,"Broken.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var data=Directory.CreateDirectory(Path.Combine(r.Game,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Game,"schemas")).FullName;File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
        var dotnet=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
        var launch=new PlayLaunch(new(Path.Combine(r.Game,"never-run.exe"),r.Game,[]),new(dotnet,r.Game,["build","Broken.csproj","-c","Release","--nologo","-v:q"]),r.Game,[]);
        var approvals=new PlayApprovals(Path.Combine(r.Root,"preferences"));approvals.Approve(launch,true);
        var result=await ProjectBuildCommand.RunAsync(launch,approvals,false,timeout:TimeSpan.FromSeconds(30));Assert.False(result.Success);Assert.Contains(result.Errors,e=>e.Code=="CS0103");
        using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Show("item.json"),workspace.Said);workspace.BindPlaySettings(r.Game,null,Path.Combine(r.Root,"preferences"));
        workspace.AcceptBuildOutput(result,launch);
        using var host=new HeadlessHost(1500,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,4);
        var source=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label.Contains("Broken.cs at line 1")&&n.Role==UiRole.Link);
        host.Click(workspace.Draw,source.Bounds.X+source.Bounds.Width/2,source.Bounds.Y+source.Bounds.Height/2);
        var plan=Assert.Single(r.Opened);Assert.Equal(r.Editor,plan.FileName);Assert.Equal(new[]{"--reuse-window","--goto",broken+":1:29"},plan.ArgumentList.ToArray());
        // The ordinary command opens code through the same editor launch seam.
        host.Press(workspace.Draw,ImGuiKey.T,alt:true);host.Frames(workspace.Draw,2);
        var open=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Open game code");
        host.Click(workspace.Draw,open.Bounds.X+open.Bounds.Width/2,open.Bounds.Y+open.Bounds.Height/2);Assert.Equal(r.Editor,r.Opened[1].FileName);
    }
    [Fact]
    public void Build_results_reach_the_Play_console_when_the_Build_panel_is_hidden()
    {
        using var r=new Rig();var source=r.Source();
        var data=Directory.CreateDirectory(Path.Combine(r.Game,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Game,"schemas")).FullName;
        File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
        var tools=Directory.CreateDirectory(Path.Combine(r.Game,"tools")).FullName;
        foreach(var name in new[]{"ProjectPlayTestGame.exe","ProjectPlayTestGame.dll","ProjectPlayTestGame.deps.json","ProjectPlayTestGame.runtimeconfig.json"})File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame",name),Path.Combine(tools,name));
        var finish=Path.Combine(r.Root,"finish");
        var defaults=new PlaySettings("never-run.exe",build:new("tools/ProjectPlayTestGame.exe",["exit-on-file",finish]));
        using var host=new HeadlessHost(1300,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var workspace=new DataWorkspace(DataProject.Open(data,schemas));workspace.BindPlaySettings(r.Game,defaults,Path.Combine(r.Root,"preferences"));Assert.True(workspace.Show("item.json"),workspace.Said);
        Assert.True(workspace.PlaySettings!.TryLaunch(out var launch,out var why),why);new PlayApprovals(Path.Combine(r.Root,"preferences")).Approve(launch!,true);
        workspace.Window!.Editor.SetOpen(ProjectBuildPanel.Id,true);host.Frames(workspace.Draw,4);
        var build=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Build saved files");
        host.Click(workspace.Draw,build.Bounds.X+build.Bounds.Width/2,build.Bounds.Y+build.Bounds.Height/2);
        workspace.Window.Editor.SetOpen(ProjectBuildPanel.Id,false);File.WriteAllText(finish,"finish only after hiding the panel");
        var deadline=Environment.TickCount64+10000;
        do{host.Frame(workspace.Draw);if(Ui.Snapshot().Nodes.Any(n=>n.Label=="plain stdout"))break;Thread.Sleep(5);}while(Environment.TickCount64<deadline);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="plain stdout");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Build finished with exit code 0"));Assert.Empty(r.Opened);
    }

}
