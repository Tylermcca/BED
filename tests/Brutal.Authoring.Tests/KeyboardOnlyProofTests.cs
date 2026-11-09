using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.Text;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures and scenarios; BORROWED: public HeadlessHost input and launcher contracts.
[Collection(ImGuiCollection.Name)]
public sealed class KeyboardOnlyProofTests(ITestOutputHelper output)
{
    const string Original = """{"items":[{"name":"Amber","amount":3,"count":1},{"name":"Birch","amount":4,"count":2}]}""";
    const string Schema = """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","title":"Name"},"amount":{"type":"number","title":"Amount"},"count":{"type":"integer","title":"Count"}},"required":["name","amount","count"]}}}}""";
    sealed class ProbeCheck : ICheck
    {
        public CheckDefinition Definition=>new("keyboard.proof","Keyboard proof check","Self-authored finding",TimeSpan.FromSeconds(5));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot,IProgress<CheckProgress> progress,CancellationToken cancel)
            =>Task.FromResult(new CheckReport(true,[new CheckFinding(Issue.Warning("Inspect Birch"),new("items.json","/items/1","/items/1/name"),"Give it a clear name.")],"One invented warning."));
    }
    sealed record Focus(uint Id, uint Active, string Window, float X, float Y, float W, float H, bool Cursor);
    static Focus FocusNow()
    {
        var c = ImGui.GetCurrentContext();
        var w=c.NavWindow;
        if (w.IsNull()) return new(0,c.ActiveId.Value,"",0,0,0,0,c.NavCursorVisible);
        if (c.NavId.Value == 0) return new(0,c.ActiveId.Value,Marshal.PtrToStringUTF8((IntPtr)w.Name)??"",0,0,0,0,c.NavCursorVisible);
        var r=w.NavRectRel[(int)c.NavLayer]; var o=w.DC.CursorStartPos;
        return new(c.NavId.Value,c.ActiveId.Value,Marshal.PtrToStringUTF8((IntPtr)w.Name)??"",o.X+r.Min.X,o.Y+r.Min.Y,r.Max.X-r.Min.X,r.Max.Y-r.Min.Y,c.NavCursorVisible);
    }
    sealed class Rig : IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"a210-"+Guid.NewGuid().ToString("N"));
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public readonly List<object> Steps=[];
        readonly ITestOutputHelper output;
        readonly string scenario;
        public DataWorkspace Workspace=>Launcher.Workspace!;
        public EditorWindow<DataDocument> Window=>Workspace.Window!;
        public DocumentSession<DataDocument> Session=>Window.Editor.Session;
        public DataTableView Table=>Workspace.TableOf("items.json")!;
        public Action Draw=>Launcher.Draw;
        public Rig(string scenario,ITestOutputHelper output,bool sample=true)
        {
            this.scenario=scenario;this.output=output;
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Path.Combine(Root,"data","items.json"),Original);
            File.WriteAllText(Path.Combine(Root,"schemas","items.schema.json"),Schema);
            Launcher=new(new RecentProjects(Path.Combine(Root,"settings","recent.json")),sample?new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")):null)
                {SetUpWorkspace=w=>{w.AddTextPanel();w.Checks.Add(new ProbeCheck());}};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Draw,5);Record("initial");
        }
        public void Record(string action)
        {
            var f=FocusNow();Steps.Add(new{action,focus=f,primary=Launcher.Workspace?.Window?.Editor.Primary,status=Launcher.Workspace?.Window?.Status,
                navAlive=ImGui.GetCurrentContext().NavIdIsAlive,textFocus=Launcher.Workspace?.TextOf("items.json")?.Code?.HasFocus,textDraft=Launcher.Workspace?.TextOf("items.json")?.HasDraft});
            output.WriteLine(action+" "+JsonSerializer.Serialize(f));
        }
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false,bool alt=false)
        {Host.Press(Draw,key,ctrl,shift,alt);Record((ctrl?"Ctrl+":"")+(shift?"Shift+":"")+(alt?"Alt+":"")+(key==ImGuiKey.Tab?"Tab":key.ToString()));}
        public void Type(string text){Host.Type(Draw,text);Record("Type "+text);}
        public void Tabs(int n){for(int i=0;i<n;i++)Key(ImGuiKey.Tab);}
        public void Open(){Key(ImGuiKey.O,ctrl:true);Type(Root);Tabs(2);Key(ImGuiKey.Enter);Assert.NotNull(Launcher.Workspace);Assert.Equal("items.json",Workspace.Active!.Name);}
        public void InWindow(string name)
        {
            if(FocusNow().Window.Contains(name,StringComparison.Ordinal))return;
            // Hold Ctrl across successive Tab presses: releasing after each chord only swaps the two MRU windows.
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            Record("Hold Ctrl for window cycle");
            try
            {
                for(int i=0;i<14;i++)
                {
                    Key(ImGuiKey.Tab);
                    var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    output.WriteLine("Ctrl held, window target: "+title);
                    Steps.Add(new{windowCycleTarget=title});
                    if(title.Contains(name,StringComparison.Ordinal))break;
                }
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Draw,3);Record("Release Ctrl");}
            Assert.Contains(name,FocusNow().Window,StringComparison.Ordinal);
        }
        public void ReachPoint(string window,Func<Brutal.Numerics.float2?> point)
        {
            InWindow(window);
            for(int i=0;i<35;i++)
            {
                var f=FocusNow();var p=point();
                if(p is {} at&&Math.Abs(f.X-at.X)<4&&Math.Abs(f.Y-at.Y)<4&&f.H<35)return;
                Key(ImGuiKey.Tab);
            }
            Assert.Fail("Tabs never reached observed control in "+window);
        }
        public void PickCell(string item,string field)
        {
            InWindow("###panel.editor.table");
            for(int i=0;i<45;i++)
            {
                var f=FocusNow();
                if(f.H<35&&f.Window.Contains("/",StringComparison.Ordinal)&&Table.Anchor==(item,field))return;
                if(f.H>100)Key(ImGuiKey.Enter);else Key(ImGuiKey.Tab);
            }
            Assert.Fail("No native cell reached "+item+"/"+field+"; anchor="+Table.Anchor);
        }
        public void Palette(string id)
        {
            var window=Window;
            Key(ImGuiKey.P,ctrl:true,shift:true);Assert.True(window.PaletteOpen);Type(id);
            Assert.Contains(window.PaletteEntries(id),e=>e.Id==id && e.Unavailable is null);
            Key(ImGuiKey.Enter);Assert.False(window.PaletteOpen);
        }
        public void VisibleFocus(string meaning)
        {
            var f=FocusNow();Assert.True(f.Id!=0&&f.Cursor,meaning+": no navigation focus");
            Host.Drawing=true;Host.Frame(Draw);Host.Drawing=false;
            var color=ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.NavCursor])&0xffffff;
            int count=0;
            for(int y=Math.Max(0,(int)f.Y-4);y<Math.Min(Host.Height,f.Y+f.H+4);y++)
                for(int x=Math.Max(0,(int)f.X-4);x<Math.Min(Host.Width,f.X+f.W+4);x++)
                    if((Host.Pixels[y*Host.Width+x]&0xffffff)==color)count++;
            output.WriteLine($"{meaning}: {count} rendered navigation-cursor pixels");
            Steps.Add(new{checkpoint=meaning,cursorPixels=count});
            if(count<4&&Environment.GetEnvironmentVariable("A210_EVIDENCE") is {Length:>0} dir)
            {Directory.CreateDirectory(dir);Host.Save(Path.Combine(dir,scenario+"-focus.png"));}
            Assert.True(count>=4,meaning+": focus id exists but no visible navigation-cursor pixels in its rectangle");
        }
        public void Screenshot()
        {
            if(Environment.GetEnvironmentVariable("A210_EVIDENCE") is not {Length:>0} dir)return;
            Directory.CreateDirectory(dir);Host.Drawing=true;Host.Frame(Draw);Host.Drawing=false;Host.Save(Path.Combine(dir,scenario+".png"));
        }
        public void EditCellAndPaste()
        {
            InWindow("###outliner");Key(ImGuiKey.Enter);Assert.Equal("/items",Window.Editor.Primary);
            PickCell("/items/0","amount");
            if(!ImGui.GetIO().WantTextInput)Key(ImGuiKey.Enter);
            Key(ImGuiKey.A,ctrl:true);Type("8");Key(ImGuiKey.Enter);
            Assert.Equal(8,Session.Current.At("/items/0/amount")!.Value.GetDouble());VisibleFocus("table after Enter");
            var before=Session.Current;var journal=Session.Journal.Count;
            ImGui.SetClipboardText("7\t5\n9\t6");
            Key(ImGuiKey.Escape);Key(ImGuiKey.Escape);Key(ImGuiKey.Tab,shift:true);Key(ImGuiKey.Enter);
            Assert.NotNull(Table.Pending);Assert.True(Table.Pending.Ok);Assert.Equal(4,Table.Pending.Cells);
            Assert.Same(before,Session.Current);Key(ImGuiKey.Tab);VisibleFocus("paste Apply");Key(ImGuiKey.Enter);
            Assert.Null(Table.Pending);
            Assert.Equal(7,Session.Current.At("/items/0/amount")!.Value.GetDouble());Assert.Equal(5,Session.Current.At("/items/0/count")!.Value.GetInt32());
            Assert.Equal(9,Session.Current.At("/items/1/amount")!.Value.GetDouble());Assert.Equal(6,Session.Current.At("/items/1/count")!.Value.GetInt32());
            Assert.Equal(journal+1,Session.Journal.Count);
            Key(ImGuiKey.Z,ctrl:true);Assert.Same(before,Session.Current);
            Key(ImGuiKey.Y,ctrl:true);Assert.Equal(9,Session.Current.At("/items/1/amount")!.Value.GetDouble());
        }
        public void Dispose()
        {
            var evidence=Environment.GetEnvironmentVariable("A210_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)){Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,scenario+".json"),JsonSerializer.Serialize(Steps,new JsonSerializerOptions{WriteIndented=true}));}
            Host.Dispose();var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Root).StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(Root).StartsWith("a210-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected owned temp fixture");
            Directory.Delete(Root,true);
        }
    }
    [Fact] public void Start_screen_opens_project_with_native_keys()
    {
        using var r=new Rig("open",output);r.Open();
        r.VisibleFocus("opened project");Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Start_screen_opens_a_typed_project_folder_without_sample_or_recent_shortcut()
    {
        using var r=new Rig("typed-open",output,sample:false);
        r.Key(ImGuiKey.O,ctrl:true);r.Type(r.Root);r.VisibleFocus("typed project path");r.Tabs(2);r.Key(ImGuiKey.Enter);
        Assert.NotNull(r.Launcher.Workspace);Assert.Equal("items.json",r.Workspace.Active!.Name);
        r.VisibleFocus("typed project opened");Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Find_and_edit_inspector_without_pointer()
    {
        using var r=new Rig("inspector",output);r.Open();
        r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("",InspectorPart.Find));
        r.Key(ImGuiKey.Enter);r.Type("Amber");r.Key(ImGuiKey.Enter);Assert.NotEmpty(r.Window.Found);
        r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);Assert.Equal("/items/0",r.Window.Editor.Primary);
        r.VisibleFocus("search result");
        r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("name"));
        if(!ImGui.GetIO().WantTextInput)r.Key(ImGuiKey.Enter);
        r.Key(ImGuiKey.A,ctrl:true);r.Type("Cedar");r.Key(ImGuiKey.Enter);
        Assert.Equal("Cedar",r.Session.Current.At("/items/0/name")!.Value.GetString());
        r.VisibleFocus("inspector after Enter");Assert.Single(r.Session.Journal);
    }
    [Fact] public void Table_cell_and_grid_are_editable_without_pointer()
    {
        using var r=new Rig("table",output);r.Open();
        r.EditCellAndPaste();r.Key(ImGuiKey.S,ctrl:true);Assert.False(r.Session.Dirty);
        var disk=File.ReadAllText(Path.Combine(r.Root,"data","items.json"));using var saved=JsonDocument.Parse(disk);Assert.True(JsonSchemas.Same(saved.RootElement,r.Session.Current.Root));
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/close_project");Assert.Null(r.Launcher.Workspace);r.VisibleFocus("closed project start screen");
    }
    [Fact] public void Paste_apply_keeps_a_visible_control_focused_after_undo_redo_and_save()
    {
        using var r=new Rig("paste-focus",output);r.Open();r.EditCellAndPaste();
        r.Key(ImGuiKey.S,ctrl:true);Assert.False(r.Session.Dirty);r.VisibleFocus("after Save");
    }
    [Fact] public void Text_panel_accepts_keyboard_focus_and_apply()
    {
        using var r=new Rig("text",output);r.Open();
        r.Palette("editor.panel.editor.text");r.InWindow("###panel.editor.text");
        r.Tabs(1);r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.A,ctrl:true);
        r.Type(Original.Replace("Amber","Text edit",StringComparison.Ordinal));r.Key(ImGuiKey.Enter,ctrl:true);
        r.Screenshot();
        Assert.Equal("Text edit",r.Session.Current.At("/items/0/name")!.Value.GetString());
    }
    [Fact] public void Text_widget_is_keyboard_reachable_after_tab_arrow_and_window_cycle_attempts()
    {
        using var r=new Rig("text-entry",output);r.Open();r.Palette("editor.panel.editor.text");
        for(int i=0;i<8&&!r.Workspace.TextOf("items.json")!.Code!.HasFocus;i++){r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);}
        foreach(var key in new[]{ImGuiKey.DownArrow,ImGuiKey.Enter,ImGuiKey.UpArrow,ImGuiKey.Space})r.Key(key);
        r.InWindow("###outliner");r.InWindow("###panel.editor.text");r.Screenshot();
        Assert.True(r.Workspace.TextOf("items.json")!.Code!.HasFocus,"No keyboard route entered the Text code widget; no input recipient or caret.");
    }
    [Fact] public void Check_and_finding_use_real_keyboard_routes()
    {
        using var r=new Rig("checks",output);r.Open();
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.checks/run.keyboard.proof");
        var run=r.Workspace.Checks.Last("keyboard.proof")!;
        Assert.True(SpinWait.SpinUntil(()=>run.IsOver,TimeSpan.FromSeconds(8)),"check did not finish");
        r.Host.Frames(r.Draw,3);Assert.Single(run.Report!.Findings);
        r.InWindow("###panel.project.checks");r.Tabs(2);r.VisibleFocus("finding");r.Key(ImGuiKey.Enter);
        Assert.Equal("/items/1",r.Window.Editor.Primary);r.VisibleFocus("after finding");Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Palette_lists_every_registered_command_and_explains_details_it_cannot_supply()
    {
        using var r=new Rig("palette",output);r.Open();
        r.Key(ImGuiKey.P,ctrl:true,shift:true);Assert.True(r.Window.PaletteOpen);r.VisibleFocus("palette");
        var entries=r.Window.PaletteEntries("");
        foreach(var command in r.Session.Catalog.Commands)Assert.Contains(entries,e=>e.Id==command.Id);
        foreach(var panel in r.Window.Editor.Panels)
        {
            Assert.Contains(entries,e=>e.Id=="editor.panel."+panel.Id);
            foreach(var command in panel.Commands??[])Assert.Contains(entries,e=>e.Id==EditorWindow<DataDocument>.PanelCommandAction+panel.Id+"/"+command.Id);
        }
        foreach(var entry in entries)output.WriteLine($"{entry.Id}: {entry.Unavailable??"runnable"}");
        r.Key(ImGuiKey.Escape);Assert.False(r.Window.PaletteOpen);r.VisibleFocus("after palette Escape");Assert.Empty(r.Session.Journal);
    }
}
