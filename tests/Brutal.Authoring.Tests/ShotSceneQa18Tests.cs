using Brutal.Authoring.Editor;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using BedProgram=Brutal.Authoring.Launcher.Program;
namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ShotSceneQa18Tests
{
    private sealed class BoundScene:IProjectAdapter
    {
        public string Name=>"Typed fixture";
        public bool Recognises(ProjectPaths paths)=>File.Exists(Path.Combine(paths.Data,"level.json"));
        public DataProject Open(ProjectPaths paths)
        {var project=DataProject.Open(paths.Data,paths.Schemas);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out _));return project;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    [Fact]
    public void Normal_startup_lists_typed_scene_without_opening_it_and_explicit_open_reports_its_real_reason()
    {
        var temporary=Directory.CreateTempSubdirectory("shot-invalid-qa18-").FullName;
        try
        {
            var root=Directory.CreateDirectory(Path.Combine(temporary,"project")).FullName;var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var path=Path.Combine(data,"level.json");File.WriteAllText(path,"{\"kind\":\"wrong-kind\",\"version\":1,\"objects\":[]}");File.WriteAllText(Path.Combine(data,"other.json"),"{}");
            foreach(var name in new[]{"level","other"})File.WriteAllText(Path.Combine(schemas,name+".schema.json"),"{\"type\":\"object\"}");
            var original=File.ReadAllBytes(path);using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(temporary,"local","recent.json"))){Loaded=[new BoundScene()]};
            Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;
            Assert.DoesNotContain("that can't open",launcher.Said);Assert.True(workspace.Documents.IsScene("level.json"));
            Assert.False(workspace.Documents.TryGetOpenScene("level.json",out _));Assert.Null(workspace.Project.Find("level.json")!.Session);
            Assert.Equal("other.json",workspace.Active!.Name);Assert.NotNull(workspace.Window);Assert.Null(workspace.SceneWindow);
            Assert.False(workspace.Show("level.json"));Assert.Contains("can't be opened as scene",workspace.Said);
            Assert.Equal("other.json",workspace.Active!.Name);Assert.Null(workspace.Project.Find("level.json")!.Session);Assert.Equal(original,File.ReadAllBytes(path));
        }
        finally{Directory.Delete(temporary,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Actual_shot_opens_minimal_and_full_sample_through_the_typed_route_and_draws_the_level(bool fullSample)
    {
        var temporary=Directory.CreateTempSubdirectory("shot-scene-qa18-").FullName;
        try
        {
            var root=Directory.CreateDirectory(Path.Combine(temporary,"project")).FullName;var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
            foreach(var relative in new[]{"reference_map.json","schemas/scene.schema.json"}.Concat(fullSample?new[]{"data/level.json"}.Concat(Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(sample,path))):[]))
            {var target=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(sample,relative),target);}
            if(!fullSample){Directory.CreateDirectory(Path.Combine(root,"data"));File.WriteAllText(Path.Combine(root,"data","level.json"),new SceneFormat().Write(new Scene([SceneObject.At("plain_box",null,System.Numerics.Vector3.Zero)])));}
            var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
            var shot=Path.Combine(temporary,"level.png");var output=new StringWriter();var errors=new StringWriter();
            // Always supply the real --shot output path, never a visible-window invocation.
            var code=BedProgram.Run([root,"--shot",shot,"--settings",Path.Combine(temporary,"local")],output,errors,Path.Combine(temporary,"unused settings"));
            Assert.Equal(0,code);Assert.Empty(errors.ToString());Assert.DoesNotContain("that can't open",output.ToString());
            var tree=Ui.Snapshot();Assert.Contains(tree.Nodes,n=>n.Role==UiRole.Canvas&&n.Label=="Viewport");
            Assert.Contains(tree.Nodes,n=>n.Role==UiRole.Window&&n.Label=="Inspector");Assert.Contains(tree.Nodes,n=>n.Role==UiRole.Window&&n.Label=="Outliner");
            if(fullSample)Assert.Contains(tree.Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="textured_crate");else Assert.Contains(tree.Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="plain_box");
            var png=File.ReadAllBytes(shot);Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png.Take(8));Assert.True(png.Length>1000);
            foreach(var (path,bytes) in before)Assert.Equal(bytes,File.ReadAllBytes(path));Assert.False(File.Exists(Path.Combine(root,".bed","project.json")));
            Assert.True(File.Exists(Path.Combine(temporary,"local","recent.json")));Assert.False(Directory.Exists(Path.Combine(temporary,"unused settings")));
        }
        finally{Directory.Delete(temporary,true);}
    }
}
