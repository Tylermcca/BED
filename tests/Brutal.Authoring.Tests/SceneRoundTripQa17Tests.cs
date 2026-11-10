using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneRoundTripQa17Tests
{
    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("scene-roundtrip-qa17-").FullName;
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly DataWorkspace Workspace;
        public readonly Scene Original,Destination;
        public readonly Dictionary<string,string> DiskBefore;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Active=>Workspace.Active!.Name;
        public string Disk(string name)=>Path.Combine(root,"data",name);
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var frame=SceneObject.At("frame",null,new(5,1,7)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.25f),Scale=new(1.5f)};
            var parent=SceneObject.At("parent",null,new(1,0,0)) with{Parent="frame",Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.2f),Scale=new(.75f)};
            parent=parent with{Properties=parent.Properties.Add("caption",JsonSerializer.SerializeToElement("Panel \"A\" \u2014 \u6c34\nsecond line")).Add("enabled",JsonSerializer.SerializeToElement(true)).Add("weight",JsonSerializer.SerializeToElement(1.25)),References=parent.References.Add("peer","sibling")};
            var child=SceneObject.At("child",null,new(0,1,0)) with{Parent="parent"};
            var watcher=SceneObject.At("watcher",null,new(10,0,0));watcher=watcher with{References=watcher.References.Add("parent_link","parent").Add("child_link","child"),Properties=watcher.Properties.Add("caption",JsonSerializer.SerializeToElement("Unchanged watcher metadata"))};
            Original=new([frame,parent,child,SceneObject.At("sibling",null,new(-2,0,3)) with{Parent="frame"},watcher]);
            Destination=new([SceneObject.At("sibling",null,Vector3.Zero),SceneObject.At("copy_parent",null,new(-10,0,0))]);
            File.WriteAllText(Disk("a.json"),new SceneFormat().Write(Original));File.WriteAllText(Disk("b.json"),new SceneFormat().Write(Destination));
            DiskBefore=new[]{"a.json","b.json"}.ToDictionary(name=>name,name=>File.ReadAllText(Disk(name)));
            Workspace=new(DataProject.Open(data,schemas));foreach(var name in DiskBefore.Keys)Assert.True(Workspace.Documents.BindScene(name,"scene",out var why),why);
            EditorWindow<Scene>.Configure(ImGui.GetIO());Show("a.json");
            Workspace.SceneViewport!.Camera=new(new Vector3(5,1,7),.2f,.3f,20);
        }
        public void Show(string name){Assert.True(Workspace.Show(name),Workspace.Said);Host.Frames(Workspace.Draw,3);}
        public void Select(params string[] ids){Window.Editor.Select(ids);Host.Frames(Workspace.Draw,2);}
        public void Run(string action){var window=Window;Assert.True(window.Run(action),window.Status);Host.Frames(Workspace.Draw,2);}
        public void Canvas()
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var found=false;
                for(var i=0;i<16;i++)
                {
                    Host.Press(Workspace.Draw,ImGuiKey.Tab);var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    if(title.Contains("panel.editor.viewport",StringComparison.Ordinal)){found=true;break;}
                }
                Assert.True(found,"Native Ctrl+Tab did not reach the viewport window.");
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Workspace.Draw,3);}
            for(var i=0;!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Canvas&&n.Focused)&&i<12;i++)Host.Press(Workspace.Draw,ImGuiKey.Tab);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Focused);
        }
        public void Key(ImGuiKey key){Host.Press(Workspace.Draw,key);Host.Frames(Workspace.Draw,2);}
        public void DeleteAnyway()
        {
            Run("scene.remove");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("watcher: refers to 'parent'",StringComparison.Ordinal));
            for(var i=0;!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Delete anyway"&&n.Focused)&&i<8;i++)Key(ImGuiKey.Tab);
            var choice=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Delete anyway");Assert.True(choice.Enabled);Assert.True(choice.Focused);
            Key(ImGuiKey.Enter);
            Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Delete referenced object?");
        }
        public void RoundTrip(string operation)
        {
            var name=Active;var oldWindow=Window;var old=oldWindow.Editor.Session;var expected=old.Current;var format=new SceneFormat();var canonical=format.Write(expected);
            Assert.Empty(new SceneValidator().Validate(expected));Assert.True(old.Dirty);Assert.NotEqual(DiskBefore[name],canonical);
            Assert.Equal(1,old.History.UndoCount);Assert.True(old.Undo());Assert.Equal(DiskBefore[name],format.Write(old.Current));Assert.True(old.Redo());Assert.Same(expected,old.Current);
            Run("editor.save");Assert.False(old.Dirty);Assert.Equal(canonical,File.ReadAllText(Disk(name)));
            // Use the actual clean Close action, not a retained in-memory scene or a serializer-only read.
            Run("editor.close");Assert.Null(Workspace.SceneWindow);Assert.False(Workspace.Documents.TryGetOpenScene(name,out _));
            Show(name);var reopened=Window.Editor.Session;Assert.NotSame(oldWindow,Window);Assert.NotSame(old,reopened);Assert.NotSame(expected,reopened.Current);
            Assert.Equal(canonical,format.Write(reopened.Current));Assert.Equal(canonical,File.ReadAllText(Disk(name)));Assert.False(reopened.Dirty);Assert.Empty(reopened.Journal);Assert.False(reopened.History.CanUndo);
            Assert.Equal(expected.Objects.Select(o=>o.Id),reopened.Current.Objects.Select(o=>o.Id));Assert.Empty(new SceneValidator().Validate(reopened.Current));
            foreach(var other in DiskBefore.Keys.Where(file=>file!=name))Assert.Equal(DiskBefore[other],File.ReadAllText(Disk(other)));
            Assert.True(canonical.Contains("caption",StringComparison.Ordinal),operation+" lost untouched properties.");
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("duplicate")][InlineData("delete_with_references")][InlineData("copy_paste")][InlineData("group")][InlineData("ungroup")]
    [InlineData("nudge")][InlineData("rotate")][InlineData("scale")]
    public void Operation_saves_closes_and_reopens_the_complete_level_exactly(string operation)
    {
        using var r=new Rig();
        switch(operation)
        {
            case "duplicate":
                r.Select("parent","child");r.Run("scene.duplicate");Assert.Equal(7,r.Window.Editor.Session.Current.Objects.Length);break;
            case "delete_with_references":
                r.Select("parent");r.DeleteAnyway();var deleted=r.Window.Editor.Session.Current;Assert.Null(deleted.Find("parent"));
                Assert.False(deleted.Find("watcher")!.References.ContainsKey("parent_link"));Assert.Equal("child",deleted.Find("watcher")!.References["child_link"]);break;
            case "copy_paste":
                r.Select("parent","child");r.Run("scene.copy");Assert.False(r.Window.Editor.Session.Dirty);Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
                r.Show("b.json");r.Run("scene.paste");Assert.Equal(4,r.Window.Editor.Session.Current.Objects.Length);Assert.Equal(4,r.Window.Editor.Session.Current.Objects.Select(o=>o.Id).Distinct().Count());break;
            case "group":r.Select("parent","sibling");r.Run("scene.group");Assert.Equal(6,r.Window.Editor.Session.Current.Objects.Length);break;
            case "ungroup":r.Select("frame");r.Run("scene.ungroup");Assert.Null(r.Window.Editor.Session.Current.Find("frame"));Assert.Equal(4,r.Window.Editor.Session.Current.Objects.Length);break;
            case "nudge":
                r.Select("sibling");r.Canvas();r.Key(ImGuiKey.G);r.Key(ImGuiKey.RightArrow);
                Assert.NotEqual(r.Original.Find("sibling")!.Position,r.Window.Editor.Session.Current.Find("sibling")!.Position);break;
            case "rotate":
                r.Select("parent");r.Canvas();r.Key(ImGuiKey.R);r.Key(ImGuiKey.Z);r.Key(ImGuiKey.RightArrow);
                Assert.NotEqual(r.Original.Find("parent")!.Rotation,r.Window.Editor.Session.Current.Find("parent")!.Rotation);break;
            case "scale":
                r.Select("parent");r.Canvas();r.Key(ImGuiKey.S);r.Key(ImGuiKey.X);r.Key(ImGuiKey.RightArrow);
                Assert.NotEqual(r.Original.Find("parent")!.Scale,r.Window.Editor.Session.Current.Find("parent")!.Scale);break;
            default:throw new ArgumentOutOfRangeException(nameof(operation));
        }
        r.RoundTrip(operation);
    }
}
