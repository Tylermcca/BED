using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

public sealed class SceneHostLifecycleTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("scene-life-").FullName;
    private readonly DataProject project;
    private readonly DocumentHost host;
    private readonly SceneDocumentEntry entry;
    private string PathToScene => Path.Combine(root,"data","room.json");
    public SceneHostLifecycleTests()
    {
        Directory.CreateDirectory(Path.Combine(root,"data")); Directory.CreateDirectory(Path.Combine(root,"schemas"));
        File.WriteAllText(PathToScene,new SceneFormat().Write(Scene.Empty));
        File.WriteAllText(Path.Combine(root,"data","values.json"),"{}");
        File.WriteAllText(Path.Combine(root,"schemas","values.schema.json"),"{}");
        project=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"));
        host=DocumentHost.For(project);
        Assert.True(host.BindScene("room.json","scene",out _));
        Assert.True(host.OpenScene("room.json",out var opened,out _)); entry=opened!;
        Assert.True(entry.Session.Execute(new AddObject(SceneObject.At("item",null,Vector3.Zero))));
    }
    public void Dispose()=>Directory.Delete(root,true);

    [Fact]
    public void Typed_check_sees_authoritative_draft_and_save_keeps_history()
    {
        var calls=0;
        host.SceneSaveCheck=e=>{ Assert.Same(entry,e); Assert.NotNull(e.Session.Current.Find("item"));calls++;return null; };
        Assert.True(host.SaveScene("room.json",out var why),why);
        Assert.Equal(1,calls); Assert.False(entry.Session.Dirty); Assert.True(entry.Session.History.CanUndo);
        Assert.NotNull(new SceneFormat().Read(File.ReadAllText(PathToScene)).Find("item"));
    }

    [Fact]
    public void Legacy_check_is_never_given_a_scene_bound_datafile()
    {
        project.SaveCheck=_=>throw new InvalidOperationException("must not run");
        Assert.False(host.SaveScene("room.json",out var why));Assert.Contains("explicit scene",why);
        Assert.True(entry.Session.Dirty);
        host.SceneSaveCheck=checkedEntry=>null;
        Assert.True(host.SaveScene("room.json",out why),why);
    }

    [Theory]
    [InlineData("deny")][InlineData("throw")][InlineData("edit")][InlineData("disk")][InlineData("delete")]
    public void Refusal_or_changed_inputs_preserves_draft_without_overwriting(string mode)
    {
        var original=File.ReadAllText(PathToScene);
        host.SceneSaveCheck=e=>
        {
            if(mode=="deny")return "not ready";
            if(mode=="throw")throw new InvalidOperationException("check fault");
            if(mode=="edit")Assert.True(e.Session.Execute(new MoveObject("item",Vector3.One)));
            if(mode=="disk")File.WriteAllText(PathToScene,"outside writer");
            if(mode=="delete")File.Delete(PathToScene);
            return null;
        };
        Assert.False(host.SaveScene("room.json",out var why));Assert.NotEmpty(why);
        Assert.True(entry.Session.Dirty); Assert.True(entry.Session.History.CanUndo);
        if(mode=="delete")Assert.False(File.Exists(PathToScene));
        else Assert.Equal(mode=="disk"?"outside writer":original,File.ReadAllText(PathToScene));
    }

    [Fact]
    public void Reentrant_save_and_close_are_refused_while_check_is_running()
    {
        host.SceneSaveCheck=checkedEntry=>
        {
            Assert.False(host.SaveScene("room.json",out _));
            Assert.False(host.CloseScene("room.json",SceneCloseDecision.Discard,out _));
            return null;
        };
        Assert.True(host.SaveScene("room.json",out var why),why);
        Assert.True(host.TryGetOpenScene("room.json",out var same));Assert.Same(entry,same);
    }

    [Fact]
    public void Cancel_failed_save_and_discard_have_distinct_lifetimes_and_reopen_replaces_views()
    {
        using var workspace=new DataWorkspace(project);
        Assert.True(workspace.Show("room.json"));var before=workspace.SceneWindow;
        Assert.False(host.CloseScene("room.json",SceneCloseDecision.Cancel,out _));
        host.SceneSaveCheck=checkedEntry=>"blocked";
        Assert.False(host.CloseScene("room.json",SceneCloseDecision.Save,out _));
        Assert.Same(before,workspace.SceneWindow);
        Assert.True(host.CloseScene("room.json",SceneCloseDecision.Discard,out _));
        Assert.Null(workspace.SceneWindow);Assert.Null(workspace.SceneViewport);
        Assert.False(project.Find("room.json")!.Open());
        Assert.True(workspace.Show("room.json"));Assert.NotSame(before,workspace.SceneWindow);
        Assert.Empty(workspace.SceneWindow!.Editor.Session.Current.Objects);
        Assert.False(workspace.SceneWindow.Editor.Session.History.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Window_save_action_is_final_and_does_not_fall_through(bool success)
    {
        var original=File.ReadAllText(PathToScene);
        var calls=0;
        var window=new EditorWindow<Scene>(entry.Editor)
        {
            SaveRefusal=()=>"Save as is refused.",
            SaveAction=()=>{ calls++; return (success,"host result"); }
        };
        Assert.Equal(success,window.Run("editor.save"));
        Assert.Equal(1,calls); Assert.Equal("host result",window.Status);
        Assert.True(entry.Session.Dirty); Assert.Equal(original,File.ReadAllText(PathToScene));
        var other=Path.Combine(root,"other.json");
        Assert.False(window.SaveAs(other)); Assert.False(File.Exists(other)); Assert.Equal(1,calls);
    }

    [Fact]
    public void Window_save_action_can_route_to_typed_host_once()
    {
        var checks=0;
        host.SceneSaveCheck=e=>{ Assert.Same(entry,e);checks++;return null; };
        var window=new EditorWindow<Scene>(entry.Editor)
        {
            SaveRefusal=()=>"Save as is refused.",
            SaveAction=()=>{ var ok=host.SaveScene("room.json",out var why);return (ok,why); }
        };
        Assert.True(window.Run("editor.save"),window.Status);
        Assert.Equal(1,checks); Assert.False(entry.Session.Dirty);
        Assert.NotNull(new SceneFormat().Read(File.ReadAllText(PathToScene)).Find("item"));
        Assert.True(entry.Session.History.CanUndo);
    }

    [Fact]
    public void Null_window_save_action_keeps_default_save_and_refusal()
    {
        var original=File.ReadAllText(PathToScene);
        var window=new EditorWindow<Scene>(entry.Editor) { SaveRefusal=()=>"blocked" };
        Assert.Null(window.SaveAction); Assert.False(window.Run("editor.save"));
        Assert.Equal("blocked",window.Status); Assert.Equal(original,File.ReadAllText(PathToScene));
        window.SaveRefusal=null;
        Assert.True(window.Run("editor.save")); Assert.False(entry.Session.Dirty);
        Assert.NotNull(new SceneFormat().Read(File.ReadAllText(PathToScene)).Find("item"));
    }

    [Fact]
    public async Task Workspace_refuses_save_and_close_until_real_background_check_completes()
    {
        var file=project.Find("values.json")!;Assert.True(file.Open());
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        project.SaveCheck=f=>{Assert.Same(file,f);entered.SetResult();release.Task.GetAwaiter().GetResult();return null;};
        var typedCalls=0;host.SceneSaveCheck=_=>{typedCalls++;return null;};
        using var workspace=new DataWorkspace(project);Assert.True(workspace.Show("room.json"));
        var original=File.ReadAllText(PathToScene);
        var check=project.AskSaveCheckInBackground(file);
        try
        {
            Assert.True(project.Judging);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(workspace.SceneWindow!.SaveAction);
            Assert.False(workspace.SceneWindow.Run("editor.save"));
            Assert.False(host.SaveScene("room.json",out _));
            Assert.False(host.CloseScene("room.json",SceneCloseDecision.Discard,out _));
            Assert.Equal(0,typedCalls);Assert.True(entry.Session.Dirty);Assert.Equal(original,File.ReadAllText(PathToScene));
        }
        finally { release.TrySetResult();await check.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.False(project.Judging);Assert.True(workspace.SceneWindow!.Run("editor.save"));
        Assert.Equal(1,typedCalls);Assert.False(entry.Session.Dirty);
    }

    [Fact]
    public void Read_only_scene_refuses_save_and_save_close()
    {
        var readOnly=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"),readOnly:true);
        var other=DocumentHost.For(readOnly);
        Assert.True(other.BindScene("room.json","scene",out _));
        Assert.True(other.OpenScene("room.json",out var scene,out _));
        Assert.True(scene!.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.One))));
        Assert.False(other.SaveScene("room.json",out var why));Assert.Contains("read-only",why);
        Assert.False(other.CloseScene("room.json",SceneCloseDecision.Save,out _));
        Assert.True(scene.Session.Dirty);
    }
}

