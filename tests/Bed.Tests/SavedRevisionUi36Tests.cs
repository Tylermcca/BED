using System.Numerics;
using System.Text;
using System.Text.Json;
using Bed.Data;
using Xunit;
namespace Bed.Tests;

public sealed class SavedRevisionUi36Tests : IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("own-saved-revision-").FullName;
    private string PathOf(string name)=>Path.Combine(root,name);
    private static string RepoFile(string relative)
    {var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;Assert.NotNull(repo);return Path.Combine(repo.FullName,relative);}
    private DocumentSession<Scene> SceneSession(string name="scene.json")
    {
        var path=PathOf(name);File.WriteAllBytes(path,File.ReadAllBytes(RepoFile("samples/SceneStarterProject/data/level.json")));
        return DocumentSession<Scene>.Open(path,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
    }
    private static void NoWrite<T>(DocumentSession<T> session,ICommand<T> command) where T:class
    {
        var path=session.FilePath!;var before=File.ReadAllBytes(path);var expected=session.ExpectedOnDisk;var history=session.History.UndoCount;
        Assert.True(session.Execute(command));Assert.True(session.Dirty);Assert.True(session.Undo());Assert.False(session.Dirty);
        var stamp=new DateTime(2002,3,4,5,6,7,DateTimeKind.Utc);File.SetLastWriteTimeUtc(path,stamp);
        session.Save();Assert.Equal(before,File.ReadAllBytes(path));Assert.Equal(stamp,File.GetLastWriteTimeUtc(path));Assert.Equal(expected,session.ExpectedOnDisk);
        Assert.True(session.Redo());Assert.True(session.Dirty);Assert.Equal(history+1,session.History.UndoCount);
    }
    [Fact]
    public void Sparse_level_undo_to_loaded_revision_is_clean_and_shared_Save_writes_nothing()
    {
        var session=SceneSession();NoWrite(session,new MoveObject("textured_crate",new Vector3(2,0,0)));
    }
    [Theory][InlineData("data")][InlineData("kit")]
    public void Data_and_kit_JSON_undo_to_loaded_revision_preserve_exact_source_and_timestamp(string kind)
    {
        var path=PathOf(kind+".json");File.WriteAllText(path," { \"label\": \"Original\", \"parts\": [] }\r\n",new UTF8Encoding(false));
        var schemas=new JsonSchemas().Add("own","""{"type":"object"}""");
        var session=DocumentSession<DataDocument>.Open(path,new DataFormat(kind),new DataValidator(schemas,"own"),DataKit.Catalog());
        NoWrite(session,new SetValue("/label",JsonSerializer.SerializeToElement("Changed")));
    }
    [Fact]
    public void Scene_v2_undo_to_loaded_revision_preserves_noncanonical_source()
    {
        var path=PathOf("scene2.json");var json=JsonDocument.Parse(File.ReadAllText(RepoFile("tests/Bed.Tests/Fixtures/Scene2/scene-3d.example.json")));
        using(json)File.WriteAllText(path,"  "+json.RootElement.GetRawText()+"\r\n\t",new UTF8Encoding(false));
        var session=DocumentSession<Scene2>.Open(path,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());
        NoWrite(session,Scene2Kit.Command("object.move",JsonSerializer.SerializeToElement(new{id="owner",position=new[]{1,2,3}})));
    }
    [Fact]
    public void Undo_past_an_intervening_save_is_dirty_and_redo_to_that_saved_snapshot_is_clean()
    {
        var session=SceneSession();Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(2,0,0))));session.Save();var saved=File.ReadAllBytes(session.FilePath!);
        Assert.False(session.Dirty);Assert.True(session.Undo());Assert.True(session.Dirty);Assert.Equal(saved,File.ReadAllBytes(session.FilePath!));
        Assert.True(session.Redo());Assert.False(session.Dirty);var stamp=new DateTime(2003,4,5,6,7,8,DateTimeKind.Utc);File.SetLastWriteTimeUtc(session.FilePath!,stamp);session.Save();Assert.Equal(stamp,File.GetLastWriteTimeUtc(session.FilePath!));
        Assert.True(session.Undo());Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(3,0,0))));Assert.True(session.Dirty);Assert.Equal(1,session.History.UndoCount);Assert.False(session.History.CanRedo);
        session.Save();Assert.NotEqual(saved,File.ReadAllBytes(session.FilePath!));Assert.False(session.Dirty);
    }
    [Fact]
    public void Explicit_clean_SaveAs_and_first_save_still_write_the_requested_destination()
    {
        var session=SceneSession();var copy=PathOf("copy.json");session.Save(copy);Assert.Equal(copy,session.FilePath);Assert.True(File.Exists(copy));Assert.False(session.Dirty);
        var fresh=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Scene.Empty);var path=PathOf("new.json");Assert.False(fresh.Dirty);fresh.Save(path);Assert.True(File.Exists(path));Assert.Equal(path,fresh.FilePath);
    }
    [Fact]
    public void Revert_and_external_MarkSaved_update_the_saved_snapshot()
    {
        var session=SceneSession();Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(2,0,0))));var written=session.TextToSave;File.WriteAllText(session.FilePath!,written,new UTF8Encoding(false));session.MarkSaved(written);
        NoWrite(session,new MoveObject("textured_crate",new Vector3(3,0,0)));
        File.WriteAllBytes(session.FilePath!,File.ReadAllBytes(RepoFile("samples/SceneStarterProject/data/level.json")));Assert.True(session.Revert());
        NoWrite(session,new MoveObject("textured_crate",new Vector3(4,0,0)));
    }
    [Fact]
    public void MarkSaved_acknowledges_equivalent_noncanonical_source_as_the_saved_snapshot()
    {
        var session=SceneSession();Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(2,0,0))));
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(RepoFile("samples/SceneStarterProject/data/level.json")))!;
        node["objects"]![2]!["position"]![0]=2;
        var raw="  "+node.ToJsonString()+"\r\n";
        Assert.Equal(session.TextToSave,session.Format.Write(session.Format.Read(raw)));
        File.WriteAllText(session.FilePath!,raw,new UTF8Encoding(false));session.MarkSaved(raw);Assert.False(session.Dirty);
        NoWrite(session,new MoveObject("textured_crate",new Vector3(3,0,0)));
    }
    [Fact]
    public void An_older_prepared_write_marks_its_own_snapshot_and_not_a_newer_draft()
    {
        var session=SceneSession();Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(2,0,0))));var written=session.TextToSave;
        Assert.True(session.Execute(new MoveObject("textured_crate",new Vector3(3,0,0))));
        File.WriteAllText(session.FilePath!,written,new UTF8Encoding(false));session.MarkSaved(written);
        Assert.True(session.Dirty);Assert.Equal(3,session.Current.Find("textured_crate")!.Position.X);
        Assert.True(session.Undo());Assert.False(session.Dirty);var stamp=new DateTime(2004,5,6,7,8,9,DateTimeKind.Utc);File.SetLastWriteTimeUtc(session.FilePath!,stamp);session.Save();Assert.Equal(stamp,File.GetLastWriteTimeUtc(session.FilePath!));
        Assert.True(session.Redo());Assert.True(session.Dirty);Assert.Equal(written,File.ReadAllText(session.FilePath!));
    }
    [Fact]
    public void A_clean_save_still_obeys_admission_and_never_overwrites_outside_changes()
    {
        var session=SceneSession();var bytes=File.ReadAllBytes(session.FilePath!);
        session.FileAccessRefusal=_=>"Denied";Assert.Throws<System.IO.IOException>(()=>session.Save());Assert.Equal(bytes,File.ReadAllBytes(session.FilePath!));
        session.FileAccessRefusal=null;File.WriteAllText(session.FilePath!,"outside text",new UTF8Encoding(false));
        session.Save();Assert.Equal("outside text",File.ReadAllText(session.FilePath!));Assert.Equal(DiskState.ChangedOnDisk,session.CheckDisk());
    }
    public void Dispose(){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
}
