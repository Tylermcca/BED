
using System.Text.Json;
using System.Runtime.InteropServices;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

// INVENTED fixtures and pixel/geometry oracles. BORROWED targets: A058 and WCAG 2.2.
[Collection(ImGuiCollection.Name)]
public sealed class AccessibilityAcceptanceTests
{
    private sealed record Box(float X0,float Y0,float X1,float Y1)
    {
        public float Width=>X1-X0;
        public float Height=>Y1-Y0;
        public Box Inset(float p)=>new(X0+p,Y0+p,X1-p,Y1-p);
        public Box Clip(Box b)=>new(Math.Max(X0,b.X0),Math.Max(Y0,b.Y0),Math.Min(X1,b.X1),Math.Min(Y1,b.Y1));
        public override string ToString()=>$"({X0},{Y0})-({X1},{Y1})";
    }
    private sealed record Focus(uint Id,string Window,Box Rect,Box Clip,Box Bounds);
    private static Focus ObserveFocus()
    {
        var c=ImGui.GetCurrentContext();
        if(c.NavId.Value==0)return new(0,"",new(0,0,0,0),new(0,0,0,0),new(0,0,0,0));
        var w=c.NavWindow;var r=w.NavRectRel[(int)c.NavLayer];var clip=w.InnerClipRect;var bounds=w.InnerRect;
        // Pinned ImGui 1.92.2: NavRectRel is relative to DC.CursorStartPos, not window.Pos.
        var origin=w.DC.CursorStartPos;
        return new(c.NavId.Value,Marshal.PtrToStringUTF8((IntPtr)w.Name)??"",
            new(origin.X+r.Min.X,origin.Y+r.Min.Y,origin.X+r.Max.X,origin.Y+r.Max.Y),
            new(clip.Min.X,clip.Min.Y,clip.Max.X,clip.Max.Y),new(bounds.Min.X,bounds.Min.Y,bounds.Max.X,bounds.Max.Y));
    }
    private static Box LastBox()
    {
        var a=ImGui.GetItemRectMin();var b=ImGui.GetItemRectMax();
        return new(a.X,a.Y,b.X,b.Y);
    }
    private static double Channel(uint c)
    {
        var x=c/255d;return x<=0.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);
    }
    private static double L(uint c)=>.2126*Channel(c&255)+.7152*Channel(c>>8&255)+.0722*Channel(c>>16&255);
    private static double Contrast(uint a,uint b)=>(Math.Max(L(a),L(b))+.05)/(Math.Min(L(a),L(b))+.05);
    private static string Rgb(uint c)=>$"#{c&255:X2}{c>>8&255:X2}{c>>16&255:X2}";
    private static Dictionary<uint,int> Histogram(HeadlessHost h,Box box)
    {
        var result=new Dictionary<uint,int>();
        int x0=Math.Max(0,(int)Math.Ceiling(box.X0)),y0=Math.Max(0,(int)Math.Ceiling(box.Y0));
        int x1=Math.Min(h.Width,(int)Math.Floor(box.X1)),y1=Math.Min(h.Height,(int)Math.Floor(box.Y1));
        Assert.True(x1>x0 && y1>y0,$"No rendered area in {box}.");
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
        {
            var c=h.Pixels[y*h.Width+x]&0xffffff;
            result[c]=result.GetValueOrDefault(c)+1;
        }
        return result;
    }
    private static long Distance(uint a,uint b)
    {
        long r=(a&255)-(long)(b&255),g=(a>>8&255)-(long)(b>>8&255),q=(a>>16&255)-(long)(b>>16&255);
        return r*r+g*g+q*q;
    }
    // The reference region contains only text on a flat background, so its farthest colour is full-coverage ink.
    // The contrast ratio itself always uses pixels actually found in the target widget.
    private static uint ReferenceInk(HeadlessHost h,Box reference)
    {
        var counts=Histogram(h,reference);
        var back=counts.MaxBy(p=>p.Value).Key;
        var ink=counts.Where(p=>p.Value>=2).MaxBy(p=>Distance(p.Key,back)).Key;
        Assert.True(Distance(ink,back)>1000,"Reference text did not render meaningful ink.");
        return ink;
    }
    private static void PixelTextAA(HeadlessHost h,Box target,Box reference,string description)
    {
        uint ink=ReferenceInk(h,reference);
        var counts=Histogram(h,target.Inset(2));
        Assert.True(counts.GetValueOrDefault(ink)>=6,$"{description}: full-coverage reference ink {Rgb(ink)} is absent.");
        var fill=counts.Where(p=>p.Key!=ink).MaxBy(p=>p.Value).Key;
        var ratio=Contrast(ink,fill);
        Assert.True(ratio>=4.5,$"{description}: actual ink {Rgb(ink)}, fill {Rgb(fill)}, ratio {ratio:R}:1 <4.5; target {target}.");
    }
    private static void Within(Box item,Box clip,string meaning)
    {
        Assert.True(item.X0>=clip.X0-1 && item.Y0>=clip.Y0-1 && item.X1<=clip.X1+1 && item.Y1<=clip.Y1+1,
            $"{meaning} is clipped: item {item}, window inner clip {clip}.");
    }

    private sealed class WidgetRig : IDisposable
    {
        public readonly HeadlessHost Host=new(600,380) { Drawing=false };
        public Box Item=new(0,0,0,0),Reference=new(0,0,0,0),Clip=new(0,0,0,0);
        public uint Id;
        public bool Hovered,Active,Focused;
        public float FrameHeight;
        public bool Checked=>check;
        public bool TabSelected,Expanded;
        public string ViewState
        {
            get
            {
                // Snapshot text as a managed string, rather than formatting ImInputString itself.
                string content="";
                if(kind=="text"){text.EvaluateLength();content=text.ToString();}
                return $"{check}/{selected}/{TabSelected}/{Expanded}/{number}/{content}";
            }
        }
        private readonly string kind;
        private readonly ImInputString text=new(256,"Target MIM"u8);
        private double number=12.5;
        private bool check,selected;
        public WidgetRig(string kind,float scale=1)
        {
            this.kind=kind;
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain=scale;
            Host.MoveMouse(Draw,-100,-100);Host.Frames(Draw,4);Raster();
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(10,10),ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(580,360),ImGuiCond.Always);
            ImGui.Begin("A059 control"u8,ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoSavedSettings);
            ImGui.Button("Focus origin"u8);
            switch(kind)
            {
                case "button":ImGui.Button("Target MIM"u8,new float2(180,36));Capture();break;
                case "small":ImGui.SmallButton("Target MIM"u8);Capture();break;
                case "selectable":
                    if(ImGui.Selectable("Target MIM"u8,selected))selected=!selected;
                    Capture();break;
                case "tree":
                    var tree=ImGui.TreeNode("Target MIM"u8);Expanded=tree;Capture();if(tree)ImGui.TreePop();break;
                case "header":Expanded=ImGui.CollapsingHeader("Target MIM"u8);Capture();break;
                case "tab":
                    if(ImGui.BeginTabBar("A059 tabs"u8))
                    {
                        if(ImGui.BeginTabItem("Origin tab"u8))ImGui.EndTabItem();
                        var tab=ImGui.BeginTabItem("Target MIM"u8);TabSelected=tab;Capture();if(tab)ImGui.EndTabItem();
                        ImGui.EndTabBar();
                    }
                    break;
                case "text":ImGui.InputText("Target MIM"u8,text);Capture();break;
                case "number":ImGui.InputDouble("Target MIM"u8,ref number,0,0);Capture();break;
                case "checkbox":ImGui.Checkbox("Target MIM"u8,ref check);Capture();break;
                case "combo":
                    var combo=ImGui.BeginCombo("Target MIM"u8,"Normal"u8);Capture();
                    if(combo){ImGui.Selectable("Normal"u8,true);ImGui.Selectable("Quiet"u8);ImGui.EndCombo();}
                    break;
                default:throw new InvalidOperationException(kind);
            }
            ImGui.Text("Target MIM"u8);Reference=LastBox();
            var a=ImGui.GetWindowPos();var b=ImGui.GetWindowSize();Clip=new(a.X,a.Y,a.X+b.X,a.Y+b.Y);
            ImGui.End();
        }
        private void Capture()
        {
            Item=LastBox();Id=ImGui.GetItemID().Value;
            Hovered=ImGui.IsItemHovered();Active=ImGui.IsItemActive();Focused=ImGui.IsItemFocused();
            FrameHeight=ImGui.GetFrameHeight();
        }
        public void Raster(){Host.Drawing=true;Host.Frame(Draw);}
        public void State(string state)
        {
            Host.Drawing=false;
            if(state=="idle"){Assert.False(Hovered);Assert.False(Active);Raster();return;}
            if(state=="hover" || state=="pressed")
            {
                Host.MoveMouse(Draw,Item.X0+Math.Min(8,Item.Width/2),Item.Y0+Item.Height/2);
                Host.Frames(Draw,2);
                Assert.True(Hovered,$"{kind}: target was not hovered.");
                if(state=="pressed")
                {
                    ImGui.GetIO().AddMouseButtonEvent(0,true);Host.Frames(Draw,2);
                    Assert.True(ImGui.GetIO().MouseDown[0]);
                    Assert.True(Hovered,$"{kind}: held pointer left the target.");
                    if(kind=="button" || kind=="selectable")Assert.True(Active,$"{kind}: active state was not reached.");
                }
                Raster();return;
            }
            Host.MoveMouse(Draw,-100,-100);
            for(int i=0;i<20 && !Focused;i++)
            {
                Host.Press(Draw,ImGuiKey.Tab);
                if(kind=="tab" && !Focused)Host.Press(Draw,ImGuiKey.RightArrow);
            }
            Host.Frames(Draw,2);Raster();
            Assert.True(Focused,$"{kind}: keyboard focus did not reach actual item {Id} (focus {ObserveFocus()}).");
            Assert.False(Hovered && ImGui.GetIO().MousePos.X>=0,"Keyboard case must have the pointer away.");
        }
        public void Dispose()=>Host.Dispose();
    }
    private static void ControlContrast(string kind,string state)
    {
        using var r=new WidgetRig(kind);r.State(state);PixelTextAA(r.Host,r.Item,r.Reference,$"{kind}/{state}");
    }

    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_ButtonText_HasFourAndAHalfToOneInRenderedState(string state)=>ControlContrast("button",state);
    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_SelectableText_HasFourAndAHalfToOneInRenderedState(string state)=>ControlContrast("selectable",state);
    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_TreeText_HasFourAndAHalfToOneInRenderedState(string state)=>ControlContrast("tree",state);
    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_HeaderText_HasFourAndAHalfToOneInRenderedState(string state)=>ControlContrast("header",state);
    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_TabText_HasFourAndAHalfToOneInRenderedState(string state)=>ControlContrast("tab",state);

    [Theory]
    [InlineData("button")][InlineData("small")][InlineData("text")][InlineData("number")]
    [InlineData("checkbox")][InlineData("combo")][InlineData("selectable")][InlineData("tree")][InlineData("header")][InlineData("tab")]
    public void Focus_EachWidgetKind_HasAnActualVisiblePixelCue(string kind)
    {
        using var r=new WidgetRig(kind);
        if(kind=="tab")
        {
            r.State("pressed");ImGui.GetIO().AddMouseButtonEvent(0,false);
            r.Host.Drawing=false;r.Host.MoveMouse(r.Draw,-100,-100);
            for(int i=0;i<12 && r.Focused;i++)r.Host.Press(r.Draw,ImGuiKey.Tab,shift:true);
            r.Host.Frames(r.Draw,2);r.Raster();Assert.True(r.TabSelected);Assert.False(r.Focused);
        }
        var model=r.ViewState;
        var before=r.Host.Pixels.ToArray();
        r.State("focus");Assert.Equal(model,r.ViewState);Assert.Equal(r.Id,ObserveFocus().Id);
        var box=r.Item.Inset(-4).Clip(new(0,0,r.Host.Width,r.Host.Height));
        int changed=0,edgeChanged=0;
        for(int y=(int)box.Y0;y<(int)box.Y1;y++)for(int x=(int)box.X0;x<(int)box.X1;x++)
        {
            if(before[y*r.Host.Width+x]==r.Host.Pixels[y*r.Host.Width+x])continue;
            changed++;
            if(x<r.Item.X0+2 || x>=r.Item.X1-2 || y<r.Item.Y0+2 || y>=r.Item.Y1-2)edgeChanged++;
        }
        Assert.True(changed>=8 && edgeChanged>=4,$"{kind}: {changed} changed pixels, {edgeChanged} at target boundary; caret/text-only changes do not establish a focus outline.");
    }

    [Theory]
    [InlineData(false,1f)][InlineData(true,1f)][InlineData(false,1.5f)][InlineData(true,1.5f)][InlineData(false,2f)][InlineData(true,2f)]
    public void Contrast_CheckboxIdentification_HasAThreeToOneRenderedBoundary(bool on,float scale)
    {
        using var r=new WidgetRig("checkbox",scale);
        if(on)r.Host.Click(r.Draw,r.Item.X0+8,r.Item.Y0+r.FrameHeight/2);
        r.Host.MoveMouse(r.Draw,-100,-100);r.Host.Frames(r.Draw,2);
        Assert.Equal(on,r.Checked);
        var box=new Box(r.Item.X0,r.Item.Y0,r.Item.X0+r.FrameHeight,r.Item.Y0+r.FrameHeight);
        BoundaryAA(r.Host,box,$"checkbox checked={on}, scale={scale}");
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Contrast_InputFrame_HasAThreeToOneRenderedBoundary(float scale)
    {
        using var r=new WidgetRig("text",scale);
        var height=r.FrameHeight;
        var frame=new Box(r.Item.X0,r.Item.Y0,r.Item.X0+Math.Min(100,r.Item.Width/2),r.Item.Y0+height);
        BoundaryAA(r.Host,frame,$"text frame, scale={scale}",leftOnly:true);
    }
    private static void BoundaryAA(HeadlessHost host,Box frame,string name,bool leftOnly=false)
    {
        var outside=Histogram(host,new(frame.X0-4,frame.Y0+3,frame.X0-2,frame.Y1-3)).MaxBy(p=>p.Value).Key;
        var edge=new Dictionary<uint,int>();
        var region=leftOnly?new Box(frame.X0-1,frame.Y0+3,frame.X0+2,frame.Y1-3):frame.Inset(-1);
        for(int y=Math.Max(0,(int)region.Y0);y<Math.Min(host.Height,(int)region.Y1);y++)
            for(int x=Math.Max(0,(int)region.X0);x<Math.Min(host.Width,(int)region.X1);x++)
            {
                if(!leftOnly && x>frame.X0+1 && x<frame.X1-2 && y>frame.Y0+1 && y<frame.Y1-2)continue;
                var c=host.Pixels[y*host.Width+x]&0xffffff;edge[c]=edge.GetValueOrDefault(c)+1;
            }
        // A boundary needs a run of pixels, not a lone glyph/checkmark/AA outlier.
        var candidates=edge.Where(p=>Contrast(p.Key,outside)>=3 && p.Value>=Math.Max(4,frame.Height/2)).ToArray();
        Assert.True(candidates.Length>0,$"{name}: no sustained >=3:1 boundary against actual outside {Rgb(outside)}.");
    }

    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]

    public void Contrast_InformativeMenuShortcutHint_HasFourAndAHalfToOne(float scale)
    {
        var prefix=Pattern("Ctrl+Shift+P",scale);
        using var r=new WorkspaceRig(scale,NumberSchema,NumberData);
        r.Render();
        var bar=ImGui.Internal.FindWindowByName("##MainMenuBar"u8);
        var box=new Box(bar.Pos.X,bar.Pos.Y,bar.Pos.X+bar.Size.X,bar.Pos.Y+bar.Size.Y);
        var actual=FindGlyphs(r.Host,box,prefix);
        Assert.NotNull(actual); // The real fallback hint must render; no recreated TextDisabled implementation.
        var ratio=Contrast(actual.Ink,actual.Paper);
        Assert.True(ratio>=4.5,$"Actual editor shortcut hint at {scale}x: {Rgb(actual.Ink)} / {Rgb(actual.Paper)}, {ratio:R}:1 <4.5.");
    }

    private const string FullSchema="""
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
    "name":{"type":"string"},"signal":{"type":"number","title":"Signal amount","default":15},
    "enabled":{"type":"boolean"},"phase":{"type":"string","enum":["Quiet","Normal","Bright"]},
    "destination":{"type":"string","x-ref":"/targets"}}}},
    "targets":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}
    """;
    private const string FullData="""
    {"items":[{"name":"Harbor relay","signal":30,"enabled":false,"phase":"Normal","destination":"beacon-a"}],
    "targets":[{"id":"beacon-a","name":"Northern light"}]}
    """;
    private const string ResetSchema="""
    {"type":"object","required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","required":["signal"],
    "properties":{"signal":{"type":"number","title":"Signal amount","default":15}}}}}}
    """;
    private const string NumberSchema="""
    {"type":"object","required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","required":["signal"],
    "properties":{"signal":{"type":"number","title":"Signal amount"}}}}}}
    """;
    private const string NumberData="""{"items":[{"signal":30}]}""";
    private const string ChoiceSchema="""
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object",
    "properties":{"phase":{"type":"string","enum":["Quiet","Normal","Bright"]}}}}}}
    """;
    private const string ChoiceData="""{"items":[{"phase":"Normal"}]}""";
    private const string LinkSchema="""
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object",
    "properties":{"destination":{"type":"string","x-ref":"/targets"}}}},
    "targets":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}
    """;
    private const string LinkData="""{"items":[{"destination":"beacon-a"}],"targets":[{"id":"beacon-a","name":"Northern light"}]}""";
    private sealed class WorkspaceRig : IDisposable
    {
        public readonly HeadlessHost Host=new(1280,720) { Drawing=false };
        public readonly DataProject Project;
        public readonly DataWorkspace Workspace;
        private readonly string root;
        public EditorWindow<DataDocument> Window=>Workspace.Window!;
        public DataTableView Table=>Workspace.TableOf("draft.json")!;
        public Action Draw=>Workspace.Draw;
        public WorkspaceRig(float scale=1,string? schema=null,string? data=null,bool broken=false)
        {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain=scale;
            root=Path.Combine(Path.GetTempPath(),"a059-access-"+Guid.NewGuid().ToString("N"));
            var dir=Path.Combine(root,"data");var schemas=Path.Combine(root,"schemas");
            Directory.CreateDirectory(dir);Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(dir,"draft.json"),data??FullData);
            File.WriteAllText(Path.Combine(schemas,"draft.schema.json"),schema??FullSchema);
            if(broken)
            {
                File.WriteAllText(Path.Combine(dir,"zbroken.json"),"""{"items":false}""");
                File.WriteAllText(Path.Combine(schemas,"zbroken.schema.json"),schema??ResetSchema);
            }
            Project=DataProject.Open(dir,schemas);
            if(broken)
            {
                var refused=Project.Find("zbroken.json")!;
                Assert.False(refused.Open());Assert.Equal(DataFileState.Refused,refused.State);
                Assert.NotNull(refused.Schema);
            }
            Workspace=new DataWorkspace(Project);
            Assert.True(Workspace.Show("draft.json"),Workspace.Said);
            Table.List="/items";Window.Editor.Select("/items/0");
            Host.MoveMouse(Draw,-100,-100);Host.Frames(Draw,5);
            Host.Drawing=false;
        }
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false)=>Host.Press(Draw,key,ctrl,shift);
        public void ReachWindow(string name)
        {
            if(ObserveFocus().Window==name)return;
            for(int n=1;n<=10;n++)
            {
                var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
                for(int k=0;k<n;k++)Key(ImGuiKey.Tab);
                io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Draw);
                if(ObserveFocus().Window==name)return;
            }
            Assert.Fail($"Keyboard window cycling did not reach {name}; current {ObserveFocus()}.");
        }
        public void Render(){Host.Drawing=true;Host.Frame(Draw);}
        public void Palette(string query)
        {
            Key(ImGuiKey.P,ctrl:true,shift:true);Assert.True(Window.PaletteOpen);
            Key(ImGuiKey.A,ctrl:true);Host.Type(Draw,query);Key(ImGuiKey.Enter);
        }
        public Focus ResetWithKeys()
        {
            ReachWindow("Inspector###inspector");
            var session=Project.Find("draft.json")!.Session!;
            Assert.Equal(30,session.Current.At("/items/0/signal")!.Value.GetDouble());
            for(int n=0;n<20;n++)
            {
                var f=ObserveFocus();
                if(f.Window=="Inspector###inspector" && f.Rect.Width>0  && f.Rect.Height>0)
                {
                    Key(ImGuiKey.Enter);
                    if(session.Current.At("/items/0/signal")!.Value.GetDouble()==15)return f;
                }
                Key(ImGuiKey.Tab);
            }
            Assert.Fail($"No keyboard activation reset signal to the schema default; last {ObserveFocus()}.");
            throw new InvalidOperationException();
        }
        public Focus FilesAction(string wanted,GlyphPattern label)
        {
            ReachWindow("Files###panel.project.files");
            var seen=new HashSet<string>();
            Focus? NamedButton()
            {
                var f=ObserveFocus();
                if(f.Window!="Files###panel.project.files" || f.Id==0)return null;
                seen.Add($"{f.Id}/{f.Rect}");
                Render();
                // Identify the real button before activating it; unrelated actions can open forms.
                if(FindGlyphs(Host,f.Rect.Clip(f.Clip),label) is null)return null;
                string previous=Workspace.Said;
                Host.Drawing=false;Key(ImGuiKey.Enter);
                bool done=wanted switch
                {
                    "save"=>Workspace.Said!=previous && Workspace.Said=="Nothing to save.",
                    "refresh"=>Workspace.Said!=previous && Workspace.Said==$"{Project.Files.Count} files.",
                    "repair"=>Project.Find("zbroken.json")?.State==DataFileState.Repairing,
                    _=>throw new InvalidOperationException(wanted)
                };
                Assert.True(done,$"Named Files {wanted} button did not perform its action: {Workspace.Said}.");
                return f;
            }
            for(int n=0;n<35;n++)
            {
                if(NamedButton() is { } found)return found;
                Host.Drawing=false;Key(ImGuiKey.Tab);
            }
            // Lists also support spatial keyboard navigation. Check the row and its right-hand action,
            // without activating a file, New, Rename or Delete while searching for Repair.
            for(int n=0;n<12;n++)
            {
                Host.Drawing=false;Key(ImGuiKey.DownArrow);
                if(NamedButton() is { } row)return row;
                Host.Drawing=false;Key(ImGuiKey.RightArrow);
                if(NamedButton() is { } action)return action;
                Host.Drawing=false;Key(ImGuiKey.LeftArrow);
            }
            var shot=Path.Combine(Path.GetTempPath(),$"a059-files-{wanted}-{ImGui.GetStyle().FontScaleMain.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png");
            Host.Save(shot);
            Assert.Fail($"Image {shot}. Named Files {wanted} button not reached with visible text; said {Workspace.Said}; focus {ObserveFocus()}; visited {string.Join("; ",seen)}.");
            throw new InvalidOperationException();
        }
        private void EnterTableWithKeys()
        {
            ReachWindow("Table###panel.editor.table");
            // Standard navigation enters the scrolling child (accepted A037 keyboard route).
            // Window cycling alone leaves focus in the parent table toolbar.
            Key(ImGuiKey.DownArrow);Key(ImGuiKey.DownArrow);Key(ImGuiKey.Enter);
        }
        public Focus FollowWithKeys()
        {
            Assert.Equal("/targets/0",Table.Target("/items/0","destination"));
            EnterTableWithKeys();
            var seen=new HashSet<string>();
            for(int n=0;n<100;n++)
            {
                var f=ObserveFocus();seen.Add($"{f.Window}/{f.Id}/{f.Rect}/anchor={Table.Anchor}");
                if(f.Window.StartsWith("Table###panel.editor.table",StringComparison.Ordinal) && f.Rect.Width>0
                    && f.Rect.Width<=ImGui.GetFrameHeight()*2+4 && f.Rect.Height>0)
                {
                    Key(ImGuiKey.Enter);
                    if(Window.Editor.Primary=="/targets/0")return f;
                    Key(ImGuiKey.Escape);
                }
                Key(ImGuiKey.Tab);
            }
            Assert.Fail($"No keyboard arrow followed the declared link; focus {ObserveFocus()}, table {Table.Said}; visited {string.Join("; ",seen)}.");
            throw new InvalidOperationException();
        }
        public Focus CellWithKeys(string field)
        {
            Assert.Null(Table.Anchor);
            EnterTableWithKeys();
            for(int n=0;n<100;n++)
            {
                Key(ImGuiKey.Tab);
                if(Table.Anchor is { } a && a.Item=="/items/0" && a.Field==field)return ObserveFocus();
            }
            Assert.Fail($"No keyboard focus reached {field}; anchor {Table.Anchor}; focus {ObserveFocus()}.");
            throw new InvalidOperationException();
        }

        public Focus SortHeaderWithKeys(GlyphPattern label)
        {
            EnterTableWithKeys();
            for(int n=0;n<70;n++)
            {
                var f=ObserveFocus();
                if(f.Window.StartsWith("Table###panel.editor.table",StringComparison.Ordinal) && f.Rect.Width>0 && f.Rect.Height>0)
                {
                    Render();
                    if(FindGlyphs(Host,f.Rect.Clip(f.Clip),label) is not null)
                    {
                        Key(ImGuiKey.Enter);
                        if(Table.Sort is { } sort && sort.Column=="phase")return f;
                        Key(ImGuiKey.Escape);
                    }
                }
                Host.Drawing=false;Key(ImGuiKey.Tab);
            }
            Assert.Fail($"No keyboard header activation sorted phase; current {ObserveFocus()}.");
            throw new InvalidOperationException();
        }

        public void Dispose()
        {
            Host.Dispose();
            var full=Path.GetFullPath(root);
            var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a059-access-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected cleanup boundary.");
            Directory.Delete(full,true);
        }
    }

    [Theory]
    [InlineData("Files###panel.project.files")][InlineData("Outliner###outliner")][InlineData("Inspector###inspector")]
    [InlineData("Table###panel.editor.table")][InlineData("##MainMenuBar")]
    public void Keyboard_EveryStockPane_IsReachableWithoutMouse(string pane)
    {
        using var r=new WorkspaceRig();r.ReachWindow(pane);
        Assert.Equal(pane,ObserveFocus().Window);Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void Keyboard_Palette_OpensSearchAndExecutesRefreshWithoutDirtying()
    {
        using var r=new WorkspaceRig();r.Palette("editor.panel_do.project.files/refresh");
        Assert.Contains("Refresh files",r.Window.Status);Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void Keyboard_SaveAs_IsReachableAndEscapeCancelsWithoutWriting()
    {
        using var r=new WorkspaceRig();var before=File.ReadAllBytes(r.Project.Find("draft.json")!.Path);
        r.Palette("editor.file_save_as");Assert.True(r.Window.SaveAsOpen);
        r.Key(ImGuiKey.Escape);Assert.False(r.Window.SaveAsOpen);
        Assert.Equal(before,File.ReadAllBytes(r.Project.Find("draft.json")!.Path));Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void Keyboard_Palette_CanReopenAClosedFilesPanel()
    {
        using var r=new WorkspaceRig();r.Window.Editor.SetOpen("project.files",false);
        r.Palette("editor.panel.project.files");Assert.True(r.Window.Editor.IsOpen("project.files"));Assert.Empty(r.Project.Dirty);
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Keyboard_InspectorReset_UsesTheSchemaDefaultWithoutMouse(float scale)
    {
        using var r=new WorkspaceRig(scale,ResetSchema,NumberData);
        var original=File.ReadAllBytes(r.Project.Find("draft.json")!.Path);
        _=r.ResetWithKeys();Assert.True(r.Project.Find("draft.json")!.Dirty);
        Assert.Equal(original,File.ReadAllBytes(r.Project.Find("draft.json")!.Path));
        Assert.Single(r.Project.Find("draft.json")!.Session!.Journal);
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Focus_InspectorReset_RemainsEntirelyVisibleAtEachTextSize(float scale)
    {
        using var r=new WorkspaceRig(scale,ResetSchema,NumberData);
        var f=r.ResetWithKeys();Within(f.Rect,f.Clip,$"Inspector reset at {scale}x");
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Keyboard_FilesRepair_OpensTheActualBrokenFileWithoutMouse(float scale)
    {
        var label=Pattern("Repair",scale);
        using var r=new WorkspaceRig(scale,ResetSchema,NumberData,broken:true);
        _=r.FilesAction("repair",label);
        Assert.Equal("zbroken.json",r.Workspace.Active!.Name);
        Assert.True(r.Project.Find("zbroken.json")!.Session!.Repairing);
        Assert.NotEmpty(r.Project.Find("zbroken.json")!.Session!.Problems);
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Size_FilesSaveAllButton_FitsItsActualWindow(float scale)
    {
        var label=Pattern("Save all",scale);
        using var r=new WorkspaceRig(scale,NumberSchema,NumberData);
        var f=r.FilesAction("save",label);Within(f.Rect,f.Bounds,$"Files Save all at {scale}x");
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Size_FilesRefreshButton_FitsItsActualWindow(float scale)
    {
        var label=Pattern("Refresh",scale);
        using var r=new WorkspaceRig(scale,NumberSchema,NumberData);
        var f=r.FilesAction("refresh",label);Within(f.Rect,f.Bounds,$"Files Refresh at {scale}x");
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Size_FilesRepairButton_FitsItsActualWindow(float scale)
    {
        var label=Pattern("Repair",scale);
        using var r=new WorkspaceRig(scale,ResetSchema,NumberData,broken:true);
        var f=r.FilesAction("repair",label);Within(f.Rect,f.Bounds,$"Files Repair at {scale}x");
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Keyboard_TableLinkArrow_RevealsItsDeclaredTargetWithoutMouse(float scale)
    {
        using var r=new WorkspaceRig(scale,LinkSchema,LinkData);
        var f=r.FollowWithKeys();
        Assert.Equal("/targets/0",r.Window.Editor.Primary);Assert.Equal("/targets",r.Table.List);
        Within(f.Rect,f.Clip,$"Follow arrow at {scale}x");Assert.Empty(r.Project.Dirty);
    }



    [Theory]
    [InlineData("idle")][InlineData("hover")][InlineData("pressed")][InlineData("focus")]
    public void Contrast_RealTableColumnHeader_HasFourAndAHalfToOne(string state)
    {
        var label=Pattern("phase",1);
        using var r=new WorkspaceRig(1,ChoiceSchema,ChoiceData);
        var header=r.SortHeaderWithKeys(label);
        if(state=="idle")
        {
            r.Key(ImGuiKey.Tab);
            Assert.NotEqual(header.Id,ObserveFocus().Id);
        }
        else if(state=="hover" || state=="pressed")
        {
            r.Host.MoveMouse(r.Draw,header.Rect.X0+8,header.Rect.Y0+header.Rect.Height/2);
            if(state=="pressed")ImGui.GetIO().AddMouseButtonEvent(0,true);
        }
        else Assert.Equal(header.Id,ObserveFocus().Id);
        r.Render();
        if(state=="hover" || state=="pressed")
            Assert.Equal(header.Id,ImGui.GetCurrentContext().HoveredId.Value);
        if(state=="pressed")
        {
            Assert.True(ImGui.GetIO().MouseDown[0]);
            Assert.Equal(header.Id,ImGui.GetCurrentContext().ActiveId.Value);
        }
        var ink=FindGlyphs(r.Host,header.Rect.Clip(header.Clip),label);
        Assert.NotNull(ink);
        var ratio=Contrast(ink.Ink,ink.Paper);
        Assert.True(ratio>=4.5,$"Actual sortable table header/{state}: {Rgb(ink.Ink)} on {Rgb(ink.Paper)}, {ratio:R}:1.");
        Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void Keyboard_RealTableHeader_EnterRequestsTheNamedSort()
    {
        var label=Pattern("phase",1);
        using var r=new WorkspaceRig(1,ChoiceSchema,ChoiceData);
        _=r.SortHeaderWithKeys(label);
        Assert.Equal(("phase",false),r.Table.Sort);
        Assert.Empty(r.Project.Dirty);
    }

    private sealed record GlyphPattern(string Text,int Width,int Height,(int X,int Y)[] Ink,(int X,int Y)[] Paper);
    private sealed record GlyphMatch(Box Rect,uint Ink,uint Paper);
    private static GlyphPattern Pattern(string text,float scale)
    {
        using var h=new HeadlessHost(800,180) { Drawing=false };
        ImGui.GetStyle().FontScaleMain=scale;Box box=new(0,0,0,0);
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(800,180),ImGuiCond.Always);
            ImGui.Begin("Complete glyph reference"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            ImGui.Text(text);box=LastBox();ImGui.End();
        }
        h.Frames(Draw,4);h.Drawing=true;h.Frame(Draw);var ink=ReferenceInk(h,box);var back=Histogram(h,box).MaxBy(p=>p.Value).Key;
        int left=(int)Math.Floor(box.X0),top=(int)Math.Floor(box.Y0),width=(int)Math.Ceiling(box.Width),height=(int)Math.Ceiling(box.Height);
        // Exclude transparent font-cell padding, but retain every non-background glyph pixel,
        // including antialiasing. A clipped transparent top margin is not lost meaningful content.
        var painted=new List<(int X,int Y)>();
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            if((h.Pixels[(top+y)*h.Width+left+x]&0xffffff)!=back)painted.Add((x,y));
        Assert.NotEmpty(painted);
        int minX=painted.Min(p=>p.X),minY=painted.Min(p=>p.Y);
        int maxX=painted.Max(p=>p.X),maxY=painted.Max(p=>p.Y);
        var yes=new List<(int,int)>();var no=new List<(int,int)>();
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
        {
            var c=h.Pixels[(top+y)*h.Width+left+x]&0xffffff;
            if(c==ink)yes.Add((x-minX,y-minY));
            // Negative-space samples stay inside the glyph bounds: a button's decorative border can
            // cross the transparent bottom/side margin of a descender without hiding any glyph ink.
            else if(c==back && x>minX && x<maxX && y>minY && y<maxY)no.Add((x-minX,y-minY));
        }
        Assert.True(yes.Count>=8 && no.Count>=8,"Reference lacks a meaningful glyph/background pattern.");
        return new(text,maxX-minX+1,maxY-minY+1,yes.ToArray(),no.Where((_,i)=>i%Math.Max(1,no.Count/24)==0).ToArray());
    }
    private static bool Near(uint a,uint b)=>Math.Abs((int)(a&255)-(int)(b&255))<=1
        && Math.Abs((int)(a>>8&255)-(int)(b>>8&255))<=1
        && Math.Abs((int)(a>>16&255)-(int)(b>>16&255))<=1;
    private static GlyphMatch? FindGlyphs(HeadlessHost h,Box clip,GlyphPattern pattern)
    {
        int x0=Math.Max(0,(int)Math.Ceiling(clip.X0)),y0=Math.Max(0,(int)Math.Ceiling(clip.Y0));
        int x1=Math.Min(h.Width,(int)Math.Floor(clip.X1))-pattern.Width;
        int y1=Math.Min(h.Height,(int)Math.Floor(clip.Y1))-pattern.Height;
        var first=pattern.Ink[0];var blank=pattern.Paper[0];
        for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
        {
            uint ink=h.Pixels[(y+first.Y)*h.Width+x+first.X]&0xffffff;
            uint paper=h.Pixels[(y+blank.Y)*h.Width+x+blank.X]&0xffffff;
            if(Distance(ink,paper)<100)continue; // A flat fill cannot masquerade as a word.
            int mismatches=0;
            foreach(var p in pattern.Paper)
                if(!Near(h.Pixels[(y+p.Y)*h.Width+x+p.X]&0xffffff,paper))mismatches++;
            if(mismatches>pattern.Paper.Length*.02)continue;
            mismatches=0;
            foreach(var p in pattern.Ink)
            {
                if(!Near(h.Pixels[(y+p.Y)*h.Width+x+p.X]&0xffffff,ink))mismatches++;
                if(mismatches>pattern.Ink.Length*.02)break;
            }
            if(mismatches<=pattern.Ink.Length*.02)
                return new(new(x,y,x+pattern.Width,y+pattern.Height),ink,paper);
        }
        return null;
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Size_InspectorFieldLabel_DisclosesAllItsMeaningfulGlyphs(float scale)
    {
        var signal=Pattern("Signal",scale);var amount=Pattern("amount",scale);var value=Pattern("30",scale);
        using var r=new WorkspaceRig(scale,NumberSchema,NumberData);
        r.ReachWindow("Inspector###inspector");r.Key(ImGuiKey.Escape);
        var f=ObserveFocus();Assert.Equal("Inspector###inspector",f.Window);r.Render();
        var first=FindGlyphs(r.Host,f.Clip,signal);var second=FindGlyphs(r.Host,f.Clip,amount);
        Assert.True(first is not null,$"Signal label absent or clipped at {scale}x in {f.Clip}.");
        Assert.True(second is not null,$"amount label absent or clipped at {scale}x in {f.Clip}.");
        Assert.True(second.Rect.Y0>first.Rect.Y0+1 || (Math.Abs(second.Rect.Y0-first.Rect.Y0)<=1 && second.Rect.X0>first.Rect.X0),
            "The two label words must remain in reading order; above/wrapped layouts are allowed.");
        Assert.NotNull(FindGlyphs(r.Host,f.Clip,value));
    }
    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Size_TableEnumValue_DisclosesItsCompleteCurrentChoice(float scale)
    {
        var complete=Pattern("Normal",scale);
        using var r=new WorkspaceRig(scale,ChoiceSchema,ChoiceData);
        var f=r.CellWithKeys("phase");r.Render();
        Within(f.Rect,f.Clip,$"choice control at {scale}x");
        Assert.NotNull(FindGlyphs(r.Host,f.Rect.Clip(f.Clip),complete));
    }
}
