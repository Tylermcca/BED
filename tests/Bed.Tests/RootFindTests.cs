using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RootFindTests : IDisposable
{
    private readonly string temporary=Directory.CreateTempSubdirectory("root-find-").FullName;
    private readonly HeadlessHost host=new(900,700){Drawing=false};
    private string Root=>Path.Combine(temporary,"project");
    private string Nested=>Path.Combine(Root,"data","game");
    private string Schemas=>Path.Combine(Root,"schemas");
    private string Selected=>Path.Combine(Root,"configuration");
    public RootFindTests()
    {
        Directory.CreateDirectory(Nested);Directory.CreateDirectory(Schemas);Directory.CreateDirectory(Selected);Directory.CreateDirectory(Path.Combine(Root,"bin"));
        File.WriteAllText(Path.Combine(Nested,"item.json"),"{}");File.WriteAllText(Path.Combine(Schemas,"item.schema.json"),"{}");
        File.WriteAllText(Path.Combine(Root,"bin","client.exe"),"inert executable declaration; never launched");
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
    }
    public void Dispose(){host.Dispose();Directory.Delete(temporary,true);}
    private sealed class Adapter(string data,string schemas,string root):IProjectAdapter
    {
        public string Name=>"Nested fixture";
        public ProjectPaths? Answer;public int Finds;public ProjectPaths? PlayPaths;
        public bool Recognises(ProjectPaths paths)=>true;
        public ProjectPaths? FindProject(string folder){Finds++;return Answer;}
        public DataProject Open(ProjectPaths paths)=>DataProject.Open(data,schemas);
        public PlaySettings? DefaultPlay(ProjectPaths paths){PlayPaths=paths;return new("bin/client.exe",workingFolder:root);}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    private ProjectLauncher Launcher(Adapter adapter)=>new(new RecentProjects(Path.Combine(temporary,"local","recent.json"))){Loaded=[adapter]};
    private void AssertLaunch(ProjectLauncher launcher,Adapter adapter)
    {
        Assert.Equal(Nested,launcher.Workspace!.Project.DataFolder);
        Assert.Equal(Root,adapter.PlayPaths!.Root);
        Assert.True(launcher.Workspace.PlaySettings!.TryLaunch(out var launch,out var why),why);
        Assert.Equal(Path.Combine(Root,"bin","client.exe"),launch!.Run.Executable);
        Assert.Equal(Root,launch.Run.WorkingDirectory);Assert.Equal(Root,launch.ProjectRoot);
        Assert.False(PlaySettingsResolver.NeedsExternalApproval(launch));
        Assert.False(File.Exists(Path.Combine(Root,".bed","project.json")));
    }
    [Fact]
    public void Standard_folder_survives_automatic_adapter_redirect_to_nested_data_and_resolves_Play_under_the_chosen_root()
    {
        var adapter=new Adapter(Nested,Schemas,Root);using var launcher=Launcher(adapter);
        Assert.True(launcher.OpenFolder(Root),launcher.Said);
        Assert.Equal(0,adapter.Finds);Assert.Null(launcher.Current!.Folder);AssertLaunch(launcher,adapter);
        var recent=Assert.Single(launcher.Recent.Items);Assert.Null(recent.Folder);Assert.Equal(Root,recent.Root);
        Assert.True(launcher.Close());Assert.True(launcher.OpenRecent(recent with {AdapterPath=null}),launcher.Said);AssertLaunch(launcher,adapter);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Provider_root_is_resolved_against_selected_folder_and_preserved_through_Play_and_recent_open(bool relative)
    {
        const string map="{\"game\":\"Fixture\",\"labels\":[],\"references\":[]}";
        var rootMap=Path.Combine(Root,"reference_map.json");File.WriteAllText(rootMap,map);File.WriteAllText(Path.Combine(Root,"provider.json"),map);
        var adapter=new Adapter(Nested,Schemas,Root){Answer=new("../data/game","../schemas","../provider.json"){Root=relative?"..":Root}};
        using var launcher=Launcher(adapter);Assert.True(launcher.OpenFolder(Selected),launcher.Said);
        Assert.Equal(Root,launcher.Current!.Root);Assert.Equal(Selected,launcher.Current.Folder);Assert.Equal(rootMap,launcher.Current.Map);AssertLaunch(launcher,adapter);
        var recent=Assert.Single(launcher.Recent.Items);Assert.Equal(Root,recent.Root);Assert.Equal(Selected,recent.Folder);
        Assert.True(launcher.Close());Assert.True(launcher.OpenRecent(recent with {AdapterPath=null}),launcher.Said);AssertLaunch(launcher,adapter);
    }
    [Theory]
    [InlineData("missing")] [InlineData("sibling")] [InlineData("empty")]
    public void Invalid_provider_root_refuses_before_opening_or_remembering_the_project(string mode)
    {
        var sibling=Root+"-sibling";Directory.CreateDirectory(sibling);
        var declared=mode=="missing"?"missing":mode=="sibling"?sibling:" ";
        var adapter=new Adapter(Nested,Schemas,Root){Answer=new("../data/game","../schemas"){Root=declared}};
        using var launcher=Launcher(adapter);Assert.False(launcher.OpenFolder(Selected));
        Assert.Null(launcher.Workspace);Assert.Empty(launcher.Recent.Items);Assert.Null(adapter.PlayPaths);
        Assert.Contains(mode=="missing"?"root that doesn't exist":mode=="sibling"?"root that doesn't contain its data folder":"empty project root",launcher.Said);
    }
}
