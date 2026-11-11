using System.Runtime.InteropServices;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReferencesPanelUi22Tests
{
    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("references-ui22-").FullName;
        public readonly HeadlessHost Host=new(1500,1000){Drawing=false};
        public readonly DataWorkspace Workspace;
        private readonly Dictionary<string,byte[]> original;
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"blank.schema.json"),"""{"type":"object","additionalProperties":true}""");
            File.WriteAllText(Path.Combine(data,"items.json"),"""{"$schema":"blank.schema.json","items":[{"id":"amber","caption":"Amber"},{"id":"jade","caption":"Jade"},{"id":"unused","caption":"Unused"}],"local":[{"target":"amber"}]}""");
            File.WriteAllText(Path.Combine(data,"routes.json"),"""{"$schema":"blank.schema.json","routes":[{"caption":"Courier","target":"amber","many":["amber","jade"],"byKey":{"amber":2}}]}""");
            File.WriteAllText(Path.Combine(data,"other.json"),"""{"$schema":"blank.schema.json","other":[{"caption":"Observer","target":"amber"}]}""");
            var map=ReferenceMap.Read("""
                {"game":"Invented relay fixture",
                 "labels":[{"file":"items.json","list":"/items","field":"caption"},{"file":"routes.json","list":"/routes","field":"caption"},{"file":"other.json","list":"/other","field":"caption"}],
                 "references":[
                   {"file":"items.json","at":"/local/*/target","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"Self-authored fixture."},
                   {"file":"routes.json","at":"/routes/*/target","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"Self-authored fixture."},
                   {"file":"routes.json","at":"/routes/*/many","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"Self-authored fixture."},
                   {"file":"routes.json","at":"/routes/*/byKey","keys":true,"target":{"file":"items.json","list":"/items","key":"id"},"evidence":"Self-authored fixture."},
                   {"file":"other.json","at":"/other/*/target","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"Self-authored fixture."}]}
                """);
            original=Directory.GetFiles(data).ToDictionary(p=>p,File.ReadAllBytes);
            Workspace=new(DataProject.Open(data,schemas),map:map);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());Target();
        }
        public void Target(string pointer="/items/0")
        {Assert.True(Workspace.Show("items.json"),Workspace.Said);Workspace.Window!.Editor.Select(pointer);Assert.Equal(pointer,Workspace.Window.Editor.Primary);Host.Frames(Workspace.Draw,3);}
        public void Key(ImGuiKey key){Host.Press(Workspace.Draw,key);Host.Frames(Workspace.Draw,2);}
        public UiNode[] Rows()
        {
            var snapshot=Ui.Snapshot();
            return snapshot.Nodes.Where(n=>n.Role==UiRole.ListItem&&Inside(snapshot,n)).ToArray();
        }
        private static bool Inside(UiSnapshot snapshot,UiNode node)
        {
            for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                if(snapshot.Nodes[parent].Role==UiRole.Window&&snapshot.Nodes[parent].Label=="Used by")return true;
            return false;
        }
        public void FocusReferences()
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var reached=false;
                for(var i=0;i<20;i++)
                {
                    Key(ImGuiKey.Tab);var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var name=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    if(name.Contains("project.references",StringComparison.Ordinal)){reached=true;break;}
                }
                Assert.True(reached,"Native Ctrl+Tab did not reach Used by.");
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Workspace.Draw,3);}
        }
        public void NoNavigationEdits()
        {
            Assert.Empty(Workspace.Project.Dirty);
            foreach(var file in Workspace.Project.Files.Where(f=>f.Session is not null))
            {Assert.Empty(file.Session!.Journal);Assert.False(file.Session.History.CanUndo);}
            DiskUnchanged();
        }
        public void DiskUnchanged(){foreach(var (path,bytes) in original)Assert.Equal(bytes,File.ReadAllBytes(path));}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Every_same_file_cross_file_list_and_key_reference_exposes_its_path_and_jumps_by_native_keyboard()
    {
        using var r=new Rig();
        var expected=new[]{("items.json","/local/0/target"),("routes.json","/routes/0/target"),("routes.json","/routes/0/many/0"),("routes.json","/routes/0/byKey/amber"),("other.json","/other/0/target")};
        Assert.Equal(5,r.Workspace.ReferencedBy().Count);Assert.Equal(5,r.Rows().Length);
        foreach(var (file,path) in expected)
        {
            r.Target();r.FocusReferences();
            var suffix="("+file+" "+path+")";
            UiNode Row()=>Assert.Single(r.Rows(),n=>n.Label.EndsWith(suffix,StringComparison.Ordinal));
            for(var i=0;!Row().Focused&&i<20;i++)r.Key(ImGuiKey.Tab);
            Assert.True(Row().Focused,Row().Label);Assert.True(Row().Enabled);
            r.Key(ImGuiKey.Enter);
            Assert.Equal(file,r.Workspace.Active!.Name);
            var holder=Pointer.Join(Pointer.Split(path).Take(2));
            Assert.Equal(holder,r.Workspace.Window!.Editor.Primary);
            Assert.Contains(file,r.Workspace.Said,StringComparison.Ordinal);
            Assert.Contains(path[holder.Length..],r.Workspace.Said,StringComparison.Ordinal);
            r.NoNavigationEdits();
        }
    }
    [Fact]
    public void Incoming_rows_follow_unsaved_source_edits_and_undo_without_touching_saved_files()
    {
        using var r=new Rig();Assert.Equal(5,r.Rows().Length);
        var source=r.Workspace.Project.Find("routes.json")!;Assert.True(source.Open());
        var original=source.Session!.Current;
        Assert.True(source.Session.Execute(new SetValue("/routes/0/target",JsonSerializer.SerializeToElement("jade"))));
        r.Host.Frames(r.Workspace.Draw,3);
        Assert.Equal(4,r.Rows().Length);Assert.DoesNotContain(r.Rows(),n=>n.Label.Contains("(routes.json /routes/0/target)",StringComparison.Ordinal));
        Assert.Equal("/items/0",r.Workspace.Window!.Editor.Primary);Assert.True(source.Dirty);r.DiskUnchanged();
        Assert.True(source.Session.Undo());r.Host.Frames(r.Workspace.Draw,3);
        Assert.Same(original,source.Session.Current);Assert.Equal(5,r.Rows().Length);Assert.False(source.Dirty);r.DiskUnchanged();
        r.Target("/items/2");Assert.Empty(r.Rows());
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Nothing refers to ",StringComparison.Ordinal));
        r.Workspace.Window!.Editor.Select([]);r.Host.Frames(r.Workspace.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Select an item to see what refers to it.");Assert.Empty(r.Rows());
    }
}
