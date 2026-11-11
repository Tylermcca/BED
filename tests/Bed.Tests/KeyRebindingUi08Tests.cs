using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class KeyRebindingUi08Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("key-rebind-ui08-").FullName;
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public ProjectLauncher? Launcher;
        public string Project=>Path.Combine(Root,"project");
        public string Local=>Path.Combine(Root,"local");
        public string Preferences=>Path.Combine(Local,"shortcuts.json");
        public EditorWindow<DataDocument> Window=>Launcher!.Workspace!.Window!;
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Project,"data"));Directory.CreateDirectory(Path.Combine(Project,"schemas"));Directory.CreateDirectory(Local);
            File.WriteAllText(Path.Combine(Project,"data","item.json"),"{}");File.WriteAllText(Path.Combine(Project,"schemas","item.schema.json"),"{}");
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        }
        public void Open(){Launcher=new(new RecentProjects(Path.Combine(Local,"recent.json"))){ProjectPlayHost=new ProjectPlayBridge()};Assert.True(Launcher.OpenFolder(Project),Launcher.Said);Host.Frames(Launcher.Draw,3);}
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false,bool alt=false){Host.Press(Launcher!.Draw,key,ctrl,shift,alt);Host.Frames(Launcher.Draw,2);}
        private static string Name(ImGuiWindowPtr w)=>w.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)w.Name)??"";
        public void Reach(Func<UiNode,bool> match)
        {
            var target=Assert.Single(Ui.Snapshot().Nodes,n=>match(n));Assert.True(target.Enabled,target.DisabledReason);
            if(target.Role!=UiRole.ListItem&&Name(ImGui.GetCurrentContext().NavWindow).Contains('/'))Key(ImGuiKey.Escape);
            if(!Name(ImGui.GetCurrentContext().NavWindow).Contains("panel.bed.keys",StringComparison.Ordinal))
            {
                var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
                try{for(var i=0;i<25;i++){Key(ImGuiKey.Tab);if(Name(ImGui.GetCurrentContext().NavWindowingTarget).Contains("panel.bed.keys",StringComparison.Ordinal))break;}}
                finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Launcher!.Draw,2);}
            }
            for(var i=0;i<80;i++)
            {
                target=Assert.Single(Ui.Snapshot().Nodes,n=>match(n));if(target.Focused)return;
                var navigation=ImGui.GetCurrentContext();var w=navigation.NavWindow;var rectangle=w.IsNull()?default:w.NavRectRel[(int)navigation.NavLayer];
                if(target.Role==UiRole.ListItem&&rectangle.Max.Y-rectangle.Min.Y>80)Key(ImGuiKey.Enter);else Key(ImGuiKey.Tab);
            }
            Assert.Fail("Keyboard could not reach "+target.Label+"\n"+Ui.Snapshot().Text);
        }
        public void Button(string label){Reach(n=>n.Role==UiRole.Button&&n.Label==label);Key(ImGuiKey.Enter);}
        public void Select(string id)
        {
            var label=Window.PaletteEntries("").FirstOrDefault(e=>e.Id==id).Label??id;
            var text=(Window.Shortcuts.For(id)?.ToString()??"Unbound")+"  "+label;
            Reach(n=>n.Role==UiRole.ListItem&&n.Label==text);Key(ImGuiKey.Enter);
        }
        public void Capture(string id,ImGuiKey key,bool ctrl=false,bool alt=false){Select(id);Button("Rebind selected shortcut");Key(key,ctrl:ctrl,alt:alt);}
        public void Dispose(){Launcher?.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Keyboard_capture_conflict_reset_and_reload_use_only_local_preferences_without_running_captured_Play()
    {
        using var r=new Rig();r.Open();r.Key(ImGuiKey.F1);Assert.True(r.Window.Editor.IsOpen(KeysPanel.Id));
        var revision=r.Window.Editor.Session.Revision;r.Capture("editor.save",ImGuiKey.F5);
        Assert.Equal("F5",r.Window.Shortcuts.For("editor.save")!.Value.ToString());Assert.Null(r.Window.Shortcuts.Action(Chord.Parse("F5")));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("runs neither action",StringComparison.Ordinal)&&n.Label.Contains("F5",StringComparison.Ordinal));
        Assert.False(r.Launcher!.Workspace!.PlaySettings!.Open);Assert.Null(r.Launcher.Workspace.ProjectPlay);
        r.Button("Reset to default");Assert.Equal("Ctrl+S",r.Window.Shortcuts.For("editor.save")!.Value.ToString());
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("runs neither action",StringComparison.Ordinal));
        r.Capture("editor.save",ImGuiKey.K,ctrl:true,alt:true);
        Assert.Equal("Ctrl+Alt+K",r.Window.Shortcuts.For("editor.save")!.Value.ToString());
        Assert.Equal(revision,r.Window.Editor.Session.Revision);Assert.False(r.Window.Editor.Session.Dirty);
        Assert.Equal("{}",File.ReadAllText(Path.Combine(r.Project,"data","item.json")));Assert.False(Directory.Exists(Path.Combine(r.Project,".bed")));
        var bytes=File.ReadAllBytes(r.Preferences);r.Launcher.Dispose();r.Open();
        Assert.Equal("Ctrl+Alt+K",r.Window.Shortcuts.For("editor.save")!.Value.ToString());Assert.Equal(bytes,File.ReadAllBytes(r.Preferences));
        Assert.Equal("Ctrl+Alt+K",Assert.Single(r.Window.PaletteEntries(""),e=>e.Id=="editor.save").Shortcut);
    }
    [Fact]
    public void Escape_cancels_capture_and_keeps_the_exact_preferences_and_document()
    {
        using var r=new Rig();var keys=new Shortcuts();keys.Set("editor.save","Ctrl+Alt+K");File.WriteAllText(r.Preferences,keys.WriteOverrides());
        var before=File.ReadAllBytes(r.Preferences);r.Open();r.Key(ImGuiKey.F1);r.Select("editor.save");r.Button("Rebind selected shortcut");r.Key(ImGuiKey.Escape);
        Assert.Equal("Ctrl+Alt+K",r.Window.Shortcuts.For("editor.save")!.Value.ToString());Assert.Equal(before,File.ReadAllBytes(r.Preferences));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Shortcut change cancelled.");Assert.False(r.Window.Editor.Session.Dirty);
    }
    [Fact]
    public void Removed_binding_is_visible_and_can_be_reset_by_keyboard()
    {
        using var r=new Rig();var keys=new Shortcuts();keys.Set("editor.save",null);File.WriteAllText(r.Preferences,keys.WriteOverrides());
        r.Open();Assert.Null(r.Window.Shortcuts.For("editor.save"));r.Key(ImGuiKey.F1);r.Select("editor.save");r.Button("Reset to default");
        Assert.Equal("Ctrl+S",r.Window.Shortcuts.For("editor.save")!.Value.ToString());
        var restored=new Shortcuts();Assert.Empty(restored.ReadOverrides(File.ReadAllText(r.Preferences)));Assert.Equal("Ctrl+S",restored.For("editor.save")!.Value.ToString());
    }
    [Theory][InlineData("malformed")][InlineData("entry")]
    public void Bad_preferences_report_plain_text_preserve_bytes_and_keep_valid_bindings(string mode)
    {
        using var r=new Rig();var text=mode=="malformed"?"{bad":"{\"kind\":\"editor.shortcuts\",\"version\":1,\"shortcuts\":{\"editor.save\":\"Ctrl+Alt+K\",\"editor.undo\":\"NoSuchKey\"}}";
        File.WriteAllText(r.Preferences,text);r.Open();r.Key(ImGuiKey.F1);Assert.Equal(text,File.ReadAllText(r.Preferences));
        Assert.Equal(mode=="malformed"?"Ctrl+S":"Ctrl+Alt+K",r.Window.Shortcuts.For("editor.save")!.Value.ToString());Assert.Equal("Ctrl+Z",r.Window.Shortcuts.For("editor.undo")!.Value.ToString());
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&(n.Label.Contains("Could not load shortcut preferences",StringComparison.Ordinal)||n.Label.Contains("skipped",StringComparison.Ordinal)));
    }
    [Fact]
    public void Switching_projects_cancels_old_window_capture_without_rebinding_the_next_project()
    {
        using var r=new Rig();r.Open();r.Key(ImGuiKey.F1);r.Select("editor.save");r.Button("Rebind selected shortcut");
        var other=Path.Combine(r.Root,"other");Directory.CreateDirectory(Path.Combine(other,"data"));Directory.CreateDirectory(Path.Combine(other,"schemas"));
        File.WriteAllText(Path.Combine(other,"data","item.json"),"{}");File.WriteAllText(Path.Combine(other,"schemas","item.schema.json"),"{}");
        Assert.True(r.Launcher!.OpenFolder(other));r.Host.Frames(r.Launcher.Draw,3);r.Key(ImGuiKey.F1);
        Assert.True(r.Window.Editor.IsOpen(KeysPanel.Id));Assert.Equal("Ctrl+S",r.Window.Shortcuts.For("editor.save")!.Value.ToString());Assert.False(File.Exists(r.Preferences));
    }
    [Fact]
    public void Unwritable_preferences_keep_session_binding_and_show_plain_save_failure()
    {
        using var r=new Rig();Directory.CreateDirectory(r.Preferences);r.Open();r.Key(ImGuiKey.F1);r.Capture("editor.save",ImGuiKey.K,ctrl:true,alt:true);
        Assert.Equal("Ctrl+Alt+K",r.Window.Shortcuts.For("editor.save")!.Value.ToString());
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("could not save shortcut preferences",StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(r.Local,"*.tmp"));Assert.False(r.Window.Editor.Session.Dirty);
    }
}
