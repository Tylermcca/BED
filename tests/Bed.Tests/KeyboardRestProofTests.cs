using System.Runtime.InteropServices;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Editor.Text;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// INVENTED fixtures and scenarios; BORROWED: public HeadlessHost input and launcher contracts.
[Collection(ImGuiCollection.Name)]
public sealed class KeyboardRestProofTests(ITestOutputHelper output)
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
        public readonly string Root=Path.Combine(Path.GetTempPath(),"a217-"+Guid.NewGuid().ToString("N"));
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
        public void Reach(string title,string label)
        {
            InWindow(title);
            var w=ImGui.Internal.FindWindowByName(title);
            Assert.False(w.IsNull());var wanted=w.GetID(label).Value;
            for(int i=0;i<32 && FocusNow().Id!=wanted;i++)Key(ImGuiKey.Tab);
            Assert.Equal(wanted,FocusNow().Id);
        }
        public void RenameForm(){Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/rename_file");Host.Frames(Draw,2);}
        public void Record(string action)
        {
            var f=FocusNow();Steps.Add(new{action,focus=f,primary=Launcher.Workspace?.Window?.Editor.Primary,status=Launcher.Workspace?.Window?.Status,workspaceSaid=Launcher.Workspace?.Said,
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
            Key(ImGuiKey.P,ctrl:true,shift:true);Assert.True(window.PaletteOpen);Key(ImGuiKey.A,ctrl:true);Type(id);
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
            if(count<4&&Environment.GetEnvironmentVariable("A217_EVIDENCE") is {Length:>0} dir)
            {Directory.CreateDirectory(dir);Host.Save(Path.Combine(dir,scenario+"-focus.png"));}
            Assert.True(count>=4,meaning+": focus id exists but no visible navigation-cursor pixels in its rectangle");
        }
        public void Screenshot()
        {
            if(Environment.GetEnvironmentVariable("A217_EVIDENCE") is not {Length:>0} dir)return;
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
            var evidence=Environment.GetEnvironmentVariable("A217_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)){Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,scenario+".json"),JsonSerializer.Serialize(Steps,new JsonSerializerOptions{WriteIndented=true}));}
            Host.Dispose(); // Keep only this owned scratch fixture for evidence.
        }
    }

    [Fact] public void Files_can_select_another_file_and_leave_with_visible_focus()
    {
        using var r=new Rig("files",output);
        File.WriteAllText(Path.Combine(r.Root,"data","zeta.json"),Original);
        File.WriteAllText(Path.Combine(r.Root,"schemas","zeta.schema.json"),Schema);
        r.Open();r.Reach("Files###panel.project.files","  zeta.json##file.zeta.json");r.VisibleFocus("second file");r.Key(ImGuiKey.Enter);
        Assert.Equal("zeta.json",r.Workspace.Active!.Name);r.InWindow("###outliner");r.VisibleFocus("leave Files");r.Key(ImGuiKey.Escape);Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Outliner_selects_expands_and_leaves_without_editing()
    {
        using var r=new Rig("outliner",output);r.Open();r.InWindow("###outliner");r.VisibleFocus("outliner");r.Key(ImGuiKey.Enter);
        Assert.Equal("/items",r.Window.Editor.Primary);r.Key(ImGuiKey.RightArrow);r.Key(ImGuiKey.DownArrow);r.Key(ImGuiKey.Enter);
        Assert.Equal("/items/0",r.Window.Editor.Primary);r.VisibleFocus("outliner child");r.Key(ImGuiKey.Escape);r.InWindow("###inspector");Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Search_result_reveals_a_nested_value_and_can_be_left()
    {
        using var r=new Rig("search",output);r.Open();r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("",InspectorPart.Find));r.Key(ImGuiKey.Enter);r.Type("Birch");r.Key(ImGuiKey.Enter);
        Assert.NotEmpty(r.Window.Found);r.Key(ImGuiKey.Tab);r.VisibleFocus("search result");r.Key(ImGuiKey.Enter);Assert.Equal("/items/1",r.Window.Editor.Primary);
        r.Key(ImGuiKey.Escape);r.InWindow("###outliner");r.VisibleFocus("leave search");Assert.Empty(r.Session.Journal);
    }
    [Fact] public void Rename_accepts_Enter_and_leaves_a_visible_focus()
    {
        using var r=new Rig("rename",output);File.WriteAllText(Path.Combine(r.Root,"data","items.json"),Original.Replace("{\"items\"", "{\"$schema\":\"items.schema.json\",\"items\""));r.Open();r.RenameForm();r.VisibleFocus("rename field");r.Key(ImGuiKey.A,ctrl:true);r.Type("renamed.json");r.Key(ImGuiKey.Enter);
        Assert.True(File.Exists(Path.Combine(r.Root,"data","renamed.json")));Assert.False(File.Exists(Path.Combine(r.Root,"data","items.json")));
        r.VisibleFocus("after rename");r.InWindow("###outliner");
    }
    [Fact] public void Rename_Escape_cancels_and_returns_to_a_live_control()
    {
        using var r=new Rig("rename-cancel",output);r.Open();r.RenameForm();r.Key(ImGuiKey.Escape);r.Host.Frames(r.Draw,2);
        Assert.True(File.Exists(Path.Combine(r.Root,"data","items.json")));r.VisibleFocus("rename cancelled");Assert.True(ImGui.GetCurrentContext().NavIdIsAlive,"Cancelled rename left a dead control focused.");
    }
    [Fact] public void Delete_cancel_and_confirm_are_keyboard_reachable()
    {
        using var r=new Rig("delete",output);r.Open();r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/delete_file");r.InWindow("###panel.project.files");r.Key(ImGuiKey.Escape);
        Assert.True(File.Exists(Path.Combine(r.Root,"data","items.json")));
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/delete_file");r.Reach("Files###panel.project.files","Delete it");r.VisibleFocus("delete confirmation");r.Key(ImGuiKey.Enter);
        Assert.False(File.Exists(Path.Combine(r.Root,"data","items.json")));Assert.Contains("items.json",r.Workspace.Project.Deleted);
        r.Reach("Files###panel.project.files","Restore");r.Key(ImGuiKey.Enter);Assert.True(File.Exists(Path.Combine(r.Root,"data","items.json")));r.VisibleFocus("after restore");
    }
    [Fact] public void Unsaved_close_Escape_keeps_the_project_and_dismisses_the_prompt()
    {
        using var r=new Rig("close-escape",output);r.Open();r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("",InspectorPart.Find));r.Key(ImGuiKey.Enter);r.Type("Amber");r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);
        r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("name"));if(!ImGui.GetIO().WantTextInput)r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.A,ctrl:true);r.Type("Changed");r.Key(ImGuiKey.Enter);Assert.True(r.Session.Dirty);
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/close_project");r.InWindow("###launcher.close");r.VisibleFocus("unsaved close");r.Key(ImGuiKey.Escape);r.Host.Frames(r.Draw,3);
        Assert.NotNull(r.Launcher.Workspace);Assert.True(r.Session.Dirty);
        var dialog=ImGui.Internal.FindWindowByName("Save changes?###launcher.close");Assert.True(dialog.IsNull()||!dialog.Active,"Escape should dismiss the close prompt without discarding the draft.");
    }
    [Fact] public void Save_as_Escape_cancels_without_writing()
    {
        using var r=new Rig("saveas",output);r.Open();r.Palette("editor.file_save_as");r.InWindow("###saveas");r.VisibleFocus("save as");r.Key(ImGuiKey.Escape);r.Host.Frames(r.Draw,3);
        var dialog=ImGui.Internal.FindWindowByName("Save as###saveas");Assert.True(dialog.IsNull()||!dialog.Active,"Escape should dismiss Save as.");Assert.Single(Directory.GetFiles(Path.Combine(r.Root,"data")));r.InWindow("###outliner");r.VisibleFocus("leave save as");
    }

    static void DirtyByKeyboard(Rig r)
    {
        r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("",InspectorPart.Find));r.Key(ImGuiKey.Enter);r.Type("Amber");r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);
        r.ReachPoint("###inspector",()=>r.Window.InspectorPlace("name"));if(!ImGui.GetIO().WantTextInput)r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.A,ctrl:true);r.Type("Changed");r.Key(ImGuiKey.Enter);Assert.True(r.Session.Dirty);
    }
    [Fact] public void Unsaved_close_default_Enter_saves_without_Tab_navigation()
    {
        using var r=new Rig("close-default",output);r.Open();DirtyByKeyboard(r);
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/close_project");
        r.VisibleFocus("default Save");r.Key(ImGuiKey.Enter);r.Host.Frames(r.Draw,3);
        Assert.Null(r.Launcher.Workspace);
        Assert.Contains("Changed",File.ReadAllText(Path.Combine(r.Root,"data","items.json")));
    }
    [Theory] [InlineData("Cancel")] [InlineData("Save")] [InlineData("Don't Save")]
    public void Unsaved_close_buttons_accept_Enter(string choice)
    {
        using var r=new Rig("close-"+choice.Replace(" ","-"),output);r.Open();DirtyByKeyboard(r);
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/close_project");r.Reach("Save changes?###launcher.close",choice);r.VisibleFocus("close choice");r.Key(ImGuiKey.Enter);r.Host.Frames(r.Draw,3);
        if(choice=="Cancel"){Assert.NotNull(r.Launcher.Workspace);Assert.True(r.Session.Dirty);}
        else Assert.Null(r.Launcher.Workspace);
        var disk=File.ReadAllText(Path.Combine(r.Root,"data","items.json"));Assert.Equal(choice=="Save",disk.Contains("Changed",StringComparison.Ordinal));
    }
    [Fact] public void New_file_accepts_Enter_and_keeps_visible_focus()
    {
        using var r=new Rig("new",output);r.Open();r.Palette(EditorWindow<DataDocument>.PanelCommandAction+"project.files/new_file");r.VisibleFocus("new filename");r.Type("created.json");r.Key(ImGuiKey.Enter);
        Assert.True(File.Exists(Path.Combine(r.Root,"data","created.json")),r.Workspace.Said);r.VisibleFocus("after new file");r.InWindow("###outliner");
    }
    sealed class TinyMap:IMapSource<DataDocument>
    {
        public MapSnapshot Read(DataDocument d)=>new(new MapFrame("invented",0,"units","x","y"),[new MapMarker(new("invented","plant","one"),"/items/0","One",new MapPoint(0,0),true)],[]);
        public MapEdit<DataDocument>? MoveBy(DataDocument d,MapKey k,MapPoint by)=>null;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Canvas_is_reachable_as_a_keyboard_control(bool viewport)
    {
        using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var session=new DocumentSession<DataDocument>(new DataFormat(),new DataValidator(new JsonSchemas().Add("s",Schema),"s"),DataKit.Catalog(),new DataDocument(JsonDocument.Parse(Original).RootElement.Clone()));
        var editor=new EditorModel<DataDocument>(session,new FlatOutline<DataDocument>(_=>[("/items/0","One")]));var map=new MapCanvas<DataDocument>(editor,new TinyMap());
        var scene=new Scene([SceneObject.At("cube",null,System.Numerics.Vector3.Zero)]);
        var se=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);var view=new ViewportView<Scene>(se,new SceneViewport());
        void Draw(){ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0,0));ImGui.SetNextWindowSize(new Brutal.Numerics.float2(900,700));ImGui.Begin("Canvas proof");ImGui.Button("Before");if(viewport)view.Draw();else map.Draw(450);ImGui.Button("After");ImGui.End();}
        host.Frames(Draw,4);var wanted=ImGui.Internal.FindWindowByName("Canvas proof").GetID(viewport?"##viewport":"##map").Value;var reached=false;
        for(int i=0;i<12;i++){host.Press(Draw,ImGuiKey.Tab);var f=FocusNow();output.WriteLine(JsonSerializer.Serialize(f));if(f.Id==wanted){reached=true;break;}}
        host.Press(Draw,ImGuiKey.Enter);host.Press(Draw,ImGuiKey.Escape);
        Assert.True(reached,(viewport?"3D viewport":"Map canvas")+" is skipped by keyboard Tab; cannot perform its pointer-only main task.");
    }
}


