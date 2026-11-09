using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

// Glyph presence remains pixel-proved; native tree evidence independently proves full-label layout containment.
[Collection(ImGuiCollection.Name)]
public sealed class HelpMenuA393Tests
{
    [Theory]
    [InlineData(1024, 2f)]
    [InlineData(1280, 1f)]
    [InlineData(1280, 1.5f)]
    [InlineData(1280, 2f)]
    public void Complete_help_label_is_inside_the_actual_menu_bar(int width, float scale)
    {
        var help = Pattern("Help", scale);
        var root = Directory.CreateTempSubdirectory("help-menu-").FullName;
        try
        {
            using var host = new HeadlessHost(width, 720) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain = scale;
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            File.WriteAllText(Path.Combine(data, "draft.json"), """{"items":[{"signal":30}]}""");
            File.WriteAllText(Path.Combine(schemas, "draft.schema.json"),
                """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"signal":{"type":"number"}}}}}}""");
            var workspace = new DataWorkspace(DataProject.Open(data, schemas));
            Assert.True(workspace.Show("draft.json"), workspace.Said);
            workspace.TableOf("draft.json")!.List = "/items";
            workspace.Window!.Editor.Select("/items/0");
            host.MoveMouse(workspace.Draw, -100, -100);
            host.Frames(workspace.Draw, 5);
            host.Drawing = true;
            host.Frame(workspace.Draw);
            var bar = ImGui.Internal.FindWindowByName("##MainMenuBar"u8);
            var bounds = new Box(bar.Pos.X, bar.Pos.Y, bar.Pos.X + bar.Size.X, bar.Pos.Y + bar.Size.Y);
            var menu = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "Help");
            UiTreeAssertionsA448.FullyShown(menu, "Help");
            var full = menu.TextEvidence!.Value.FullBounds;
            Within(new(full.X, full.Y, full.X + full.Width, full.Y + full.Height), bounds, "complete Help layout");
            Assert.True(full.X + full.Width <= host.Width, "Complete Help layout must stay within the viewport.");
            // TextEvidence is layout/clip evidence, so retain the actual complete-glyph proof as well.
            var match = FindGlyphs(host, bounds, help);
            Assert.NotNull(match);
            Within(match.Rect, bounds, "complete Help label");
            Assert.True(match.Rect.X1 <= host.Width, "Help must stay within the viewport.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record Box(float X0,float Y0,float X1,float Y1)
    {
        public float Width=>X1-X0;
        public float Height=>Y1-Y0;
        public Box Inset(float p)=>new(X0+p,Y0+p,X1-p,Y1-p);
        public Box Clip(Box b)=>new(Math.Max(X0,b.X0),Math.Max(Y0,b.Y0),Math.Min(X1,b.X1),Math.Min(Y1,b.Y1));
        public override string ToString()=>$"({X0},{Y0})-({X1},{Y1})";
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
}
