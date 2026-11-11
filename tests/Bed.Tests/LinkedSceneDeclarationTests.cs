using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class LinkedSceneDeclarationTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("bed-scene-declaration-").FullName;
        private readonly string data,schemas;
        public readonly string File,Saved;
        public readonly DocumentHost Host;
        public Fixture()
        {
            data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File=Path.Combine(data,"invented.json");
            Saved=new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)]));
            System.IO.File.WriteAllText(File,Saved);
            Host=DocumentHost.For(DataProject.Open(data,schemas));
            Assert.True(Host.BindScene("invented.json","scene",out var why),why);
        }
        public SceneDocumentEntry Open()
        {
            Assert.True(Host.OpenScene("invented.json",out var entry,out var why),why);
            return entry!;
        }
        public GameLaunchSpec Spec()=>new(Path.Combine(AppContext.BaseDirectory,"invented.exe"),AppContext.BaseDirectory,[]);
        public void Dispose()
        {
            System.IO.File.Delete(File);Directory.Delete(data);Directory.Delete(schemas);Directory.Delete(root);
        }
    }

    [Fact]
    public void Undeclared_scene_retains_the_existing_refusal_without_mutation()
    {
        using var f=new Fixture();var entry=f.Open();var before=entry.Session.Current;
        Assert.Equal("Use project Play from Tools > Play or the toolbar. Configure the program in Tools > Play settings.",entry.Editor.PlayRefusal!());
        Assert.Same(before,entry.Session.Current);Assert.False(entry.Session.Dirty);
        Assert.Equal(f.Saved,System.IO.File.ReadAllText(f.File));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Declared_project_connects_existing_and_future_scene_guards_without_saving(bool openFirst)
    {
        using var f=new Fixture();var before=openFirst ? f.Open() : null;
        f.Host.DeclareLinkedPlay(f.Spec());var entry=f.Open();
        if(before is not null) Assert.Same(before,entry);
        Assert.Null(entry.Editor.PlayRefusal!());Assert.False(entry.Session.Dirty);
        Assert.Empty(entry.Session.Journal);Assert.Equal(0,entry.Session.History.UndoCount);
        Assert.Equal(f.Saved,entry.Session.ExpectedOnDisk);Assert.Equal(f.Saved,System.IO.File.ReadAllText(f.File));
    }

    [Fact]
    public void Null_declaration_refuses_and_does_not_unlock_an_open_scene()
    {
        using var f=new Fixture();var entry=f.Open();
        Assert.Throws<ArgumentNullException>(()=>f.Host.DeclareLinkedPlay(null!));
        Assert.Equal("Use project Play from Tools > Play or the toolbar. Configure the program in Tools > Play settings.",entry.Editor.PlayRefusal!());
        Assert.Equal(f.Saved,System.IO.File.ReadAllText(f.File));
    }

    [Fact]
    public void Declaration_preserves_an_explicit_custom_scene_play_policy()
    {
        using var f=new Fixture();var entry=f.Open();
        entry.Editor.PlayRefusal=()=>"Invented project policy refuses Play.";
        f.Host.DeclareLinkedPlay(f.Spec());
        Assert.Equal("Invented project policy refuses Play.",entry.Editor.PlayRefusal());
        Assert.Equal(f.Saved,System.IO.File.ReadAllText(f.File));
    }
}
