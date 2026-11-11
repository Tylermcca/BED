using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ProjectRootPlayTests
{
    private sealed class Session:IProjectPlaySession
    {
        public bool Start(bool dirty,bool build=false){Assert.False(dirty);return true;}
        public bool HasCompleted=>true;
        public int? ExitCode=>null;
        public bool CanBuildAndPlay=>false;
        public string Status=>"Unit observation only; no process.";
        public void RequestStop(){}
        public void Update(){}
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
        public void Draw(){}
        public void Dispose(){}
    }
    private sealed class Host:IProjectPlayHost
    {public PlayLaunch? Launch;public IProjectPlaySession Start(PlayLaunch launch){Launch=launch;return new Session();}}
    private sealed class Assets:IAssetCatalogProvisioner
    {public ProjectPaths? Paths;public ProjectAssetCatalog? Open(ProjectPaths paths,string? folder=null){Paths=paths;return null;}}
    private sealed class Adapter:IProjectAdapter
    {
        public string Name=>"Nested fixture";
        public string? SeenRoot;
        public bool Recognises(ProjectPaths paths)=>true;
        public DataProject Open(ProjectPaths paths)=>DataProject.Open(paths.Data,paths.Schemas);
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
        public PlaySettings DefaultPlay(ProjectPaths paths){SeenRoot=paths.Root;return new("game.exe",["adapter-default"]);}
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Nested_data_uses_explicit_root_then_folder_for_settings_play_build_globs_assets_and_recents(bool explicitRoot)
    {
        var root=Directory.CreateTempSubdirectory("nested-project-root-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data","game")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
            File.WriteAllText(Path.Combine(root,"game.exe"),"inert unit fixture: never executed");File.WriteAllText(Path.Combine(root,"build.exe"),"inert");
            PlaySettingsStore.Save(root,new("game.exe",["from-root"],".",new("./build.exe",[],["src/**/*.cs"])));
            var settings=File.ReadAllText(Path.Combine(root,".bed","project.json"));var adapter=new Adapter();var assets=new Assets();var host=new Host();
            var recentPath=Path.Combine(root,"local","recent.json");
            using var ui=new HeadlessHost{Drawing=false};
            using var launcher=new ProjectLauncher(new(recentPath)){Loaded=[adapter],ProjectPlayHost=host,AssetCatalogProvisioner=assets};
            var paths=new ProjectPaths(data,schemas){Root=explicitRoot?root:null,Folder=explicitRoot?Path.Combine(root,"data"):root};
            Assert.True(launcher.Open(paths),launcher.Said);Assert.Equal(root,adapter.SeenRoot);Assert.Equal(root,assets.Paths!.Root);
            Assert.True(launcher.Workspace!.Show("item.json"));Assert.True(launcher.Workspace.RequestProjectPlay(),launcher.Workspace.Said);
            var launch=Assert.IsType<PlayLaunch>(host.Launch);Assert.Equal(root,launch.ProjectRoot);Assert.Equal(root,launch.Run.WorkingDirectory);
            Assert.Equal(Path.Combine(root,"game.exe"),launch.Run.Executable);Assert.Equal(new[]{"from-root"},launch.Run.Arguments);
            Assert.Equal(root,launch.Build!.WorkingDirectory);Assert.Equal(Path.Combine(root,"build.exe"),launch.Build.Executable);Assert.Equal(new[]{"src/**/*.cs"},launch.BuildInputs);
            Assert.Equal(settings,File.ReadAllText(Path.Combine(root,".bed","project.json")));Assert.False(Directory.Exists(Path.Combine(root,"data",".bed")));
            var remembered=Assert.Single(new RecentProjects(recentPath).Items);Assert.Equal(explicitRoot?root:null,remembered.Root);Assert.Equal(paths.Folder,remembered.Folder);
            Assert.Equal("{}",File.ReadAllText(Path.Combine(data,"item.json")));Assert.True(launcher.Close(),launcher.Said);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Invalid_explicit_root_is_a_refusal_not_an_unhandled_path_error()
    {
        var root=Directory.CreateTempSubdirectory("invalid-project-root-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
            using var launcher=new ProjectLauncher(new(Path.Combine(root,"recent.json")));Assert.False(launcher.Open(new(data,schemas){Root="\0"}));Assert.Contains("root",launcher.Said,StringComparison.OrdinalIgnoreCase);Assert.Null(launcher.Workspace);
        }
        finally{Directory.Delete(root,true);}
    }
}
