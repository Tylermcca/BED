using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Checkers;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures and real-input screen scenarios. BORROWED: A059's accepted pixel/navigation
// utilities (my own prior work) and WCAG 2.2 oracles. No native host, external game or OS input.
// W3C sources: https://www.w3.org/WAI/WCAG22/Understanding/{contrast-minimum,non-text-contrast,
// keyboard,focus-visible,resize-text,pause-stop-hide}.html
// Text >=4.5:1 at normal size (>=3:1 for text >=24px); meaningful graphics/focus >=3:1.
// All controls tested here are active; the initially disabled Take back is first enabled by a move.
// The 2.5-second splash is below SC 2.2.2's >5-second threshold; skip-after-one is A067's explicit contract.
[Collection(ImGuiCollection.Name)]
public sealed class LauncherAccessibilityTests
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

    private sealed record GlyphPattern(string Text,int Width,int Height,int InkTop,(int X,int Y)[] Ink,(int X,int Y)[] Paper);
    private sealed record GlyphMatch(Box Rect,uint Ink,uint Paper);
    private static GlyphPattern Pattern(string text,float scale)
    {
        int referenceWidth=Math.Max(800,(int)Math.Ceiling(text.Length*13*scale+40));
        using var h=new HeadlessHost(referenceWidth,180) { Drawing=false };
        ImGui.GetStyle().FontScaleMain=scale;Box box=new(0,0,0,0);
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(referenceWidth,180),ImGuiCond.Always);
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
        return new(text,maxX-minX+1,maxY-minY+1,minY,yes.ToArray(),no.Where((_,i)=>i%Math.Max(1,no.Count/24)==0).ToArray());
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

    private const string LauncherWindow="BED###launcher";
    private const string BoardWindow="Board###a067.board";
    private const string PlayWindow="Checkers###game";
    private const string CreditMore="Built with BRUTAL, a game development framework by Ahwoo Limited. See NOTICE.txt beside this program.";
    private static readonly Dictionary<(string,float),GlyphPattern> Patterns=[];
    private static GlyphPattern Glyph(string text,float scale)
    {
        if(!Patterns.TryGetValue((text,scale),out var p))Patterns[(text,scale)]=p=Pattern(text,scale);
        return p;
    }
    // Preserve internal spaces and every path separator; only layout whitespace may move to a line break.
    private static string[] WrappedParts(string caption)=>System.Text.RegularExpressions.Regex.Matches(caption,@"[^\s\\.]+|\s+|[\\.]")
        .Select(m=>m.Value).ToArray();
    private static IEnumerable<string> WrappedCandidates(string caption)
    {
        var parts=WrappedParts(caption);
        for(int first=0;first<parts.Length;first++)
        {
            if(string.IsNullOrWhiteSpace(parts[first]))continue;
            for(int count=1;count<=parts.Length-first;count++)
            {
                var segment=string.Concat(parts.Skip(first).Take(count)).TrimEnd();
                if(segment.Count(char.IsLetterOrDigit)>=2)yield return segment;
            }
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"a067-"+Guid.NewGuid().ToString("N"));
        public string ProjectRoot=>Path.Combine(Root,"Beacon workshop");
        public string Data=>Path.Combine(ProjectRoot,"data");
        public string Schemas=>Path.Combine(ProjectRoot,"schemas");
        public ProjectPaths Paths=>new(Data,Schemas);
        public Fixture(bool position=false)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Data,position?"position.json":"nodes.json"),position?new GameFormat().Write(Game.Opening()):"""{"nodes":[{"id":"beacon","name":"Amber beacon"}]}""");
            File.WriteAllText(Path.Combine(Schemas,position?"position.schema.json":"nodes.schema.json"),"""{"type":"object"}""");
        }
        public RecentProjects Recent()=>new(Path.Combine(Root,"settings","recent.json"));
        public ProjectLauncher Launcher(bool sample=false,bool recent=false)
        {
            var list=Recent();if(recent)list.Add(Paths);return new(list,sample?Paths:null,StartWindowTestProjects.Creator(Root),Path.Combine(Root,"created"));
        }
        public string Disk()=>string.Join("|",Directory.EnumerateFiles(ProjectRoot,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p=>p+":"+Convert.ToHexString(File.ReadAllBytes(p))+":"+File.GetLastWriteTimeUtc(p).Ticks));
        public void Dispose()
        {
            var path=Path.GetFullPath(Root);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(path).StartsWith("a067-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected cleanup boundary.");
            if(Directory.Exists(path))Directory.Delete(path,true);
        }
    }
    private sealed class Ui : IDisposable
    {
        public HeadlessHost Host {get;}
        public Action Draw {get;}
        public float Scale {get;}
        private readonly Dictionary<string,GlyphPattern> glyphs;
        public Ui(Action draw,float scale,IEnumerable<string> captions,int width=1600,int height=900,bool wrappedRecentPath=false)
        {
            Scale=scale;
            var extra=new List<string>{"Preferences","Beacon workshop","Remove from list","Open project..."};
            // Only wrapped-path oracles consume these unique-path substring references.
            // Build them before the test host, as HeadlessHost permits only one context at a time.
            if(wrappedRecentPath && draw.Target is ProjectLauncher launcher)
                foreach(var project in launcher.Recent.Items)extra.AddRange(WrappedCandidates(project.Folder??project.Data));
            glyphs=captions.Concat(extra).Distinct().ToDictionary(x=>x,x=>Glyph(x=="Open the sample project"?"Try the example":x=="Schema folder"?"Rules folder (schema)":x=="Remove"?"Remove from list":x,scale),StringComparer.Ordinal);
            Host=new HeadlessHost(width,height){Drawing=false};Draw=draw;
            try
            {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;
            Host.MoveMouse(Draw,-100,-100);Host.Frames(Draw,5);
            if(draw.Target is ProjectLauncher && captions.Any(x=>x is "Data folder" or "Schema folder" or "Link map (optional)" or "Open" or "Browse...")){var opener=Find("Open project...",LauncherWindow);Assert.NotNull(opener);Host.Click(Draw,(opener.Rect.X0+opener.Rect.X1)/2,(opener.Rect.Y0+opener.Rect.Y1)/2);Host.Frames(Draw,2);}
            if(draw.Target is ProjectLauncher && captions.Contains("Larger")){Reach("Preferences",window:LauncherWindow);Key(ImGuiKey.Enter);}
            if(draw.Target is ProjectLauncher && captions.Contains("Remove"))
            {
                Reach("Beacon workshop",wide:true,window:LauncherWindow);Key(ImGuiKey.F10,shift:true);Host.Frames(Draw,2);recentMenuWindow=ObserveFocus().Window;
            }
            }
            catch { Host.Dispose(); throw; }
        }
        private string? recentMenuWindow;
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false)
        {
            if(recentMenuWindow is not null && ObserveFocus().Window==recentMenuWindow && key==ImGuiKey.Tab)key=shift?ImGuiKey.UpArrow:ImGuiKey.DownArrow;
            Host.Press(Draw,key,ctrl:ctrl,shift:shift);
        }
        public void Render(){Host.Drawing=true;Host.Frame(Draw);Host.Drawing=false;}
        public Box Clip(string window)
        {
            var w=ImGui.Internal.FindWindowByName(window);Assert.False(w.IsNull(),"Window was not drawn: "+window);
            var b=w.InnerClipRect;return new(b.Min.X,b.Min.Y,b.Max.X,b.Max.Y);
        }
        public GlyphMatch? Find(string caption,string window)
        {Render();return FindGlyphs(Host,Clip(window),glyphs[caption]);}
        public Focus Reach(string caption,bool wide=false,string? window=null)
        {
            if(caption=="Remove" && recentMenuWindow is not null)window=recentMenuWindow;
            wide |= caption is "Open the sample project" or "Open project...";
            var seen=new HashSet<string>();var entered=new HashSet<uint>();var rendered=new HashSet<string>();bool backwards=false;
            for(int i=0;i<70;i++)
            {
                var f=ObserveFocus();
                if(f.Id!=0&&(window is null||f.Window.StartsWith(window,StringComparison.Ordinal)))
                {
                    var identity=$"{f.Window}/{f.Id}/{f.Rect}/{f.Clip}";seen.Add(identity);
                    // A child navigation container can contain a caption without being that row.
                    var rowHeight=13*Scale+ImGui.GetStyle().ItemSpacing.Y+ImGui.GetStyle().FramePadding.Y*2+8;
                    if(f.Rect.Height>rowHeight && !f.Window.Contains("/##recents",StringComparison.Ordinal))
                    {
                        if((caption=="Beacon workshop"||caption.StartsWith('[')||caption.EndsWith(".json",StringComparison.Ordinal))&&entered.Add(f.Id)){Key(ImGuiKey.Enter);continue;}
                        backwards=false;Key(Draw.Target is ProjectLauncher?ImGuiKey.Tab:ImGuiKey.DownArrow);continue;
                    }
                    var pattern=glyphs[caption];
                    if(rendered.Add(identity)&&(wide||f.Rect.Width<=pattern.Width+ImGui.GetStyle().FramePadding.X*2+26*Scale))
                    {
                        Render();
                        if(FindGlyphs(Host,f.Rect.Clip(f.Clip),pattern) is not null)return f;
                    }
                    if(f.Window.Contains("/##entries",StringComparison.Ordinal))
                    {
                        // ImGui list rows use arrows; Escape returns to the parent chooser buttons.
                        if(caption.StartsWith('[')||caption.EndsWith(".json",StringComparison.Ordinal))Key(ImGuiKey.DownArrow);
                        else {Key(ImGuiKey.Escape);backwards=true;}
                        continue;
                    }
                }
                Key(ImGuiKey.Tab,shift:backwards);
            }
            var visible = "";
            if(Draw.Target is ProjectLauncher)
            {
                Host.Frame(()=>{ImGui.LogToClipboard();Draw();ImGui.LogFinish();});visible=ImGui.GetClipboardText().ToString()??"";
            }
            Assert.Fail($"Keyboard Tab traversal did not reach '{caption}' at {Scale}x. Visited {string.Join("; ",seen)}. Visible: {visible}");
            return ObserveFocus();
        }
        public Focus State(string caption,string state,string window,bool wide=false)
        {
            var f=Reach(caption,wide,window);
            if(state=="idle")Key(caption=="Remove"?ImGuiKey.UpArrow:ImGuiKey.Tab);
            if(state is "hover" or "pressed")
            {
                Host.MoveMouse(Draw,f.Rect.X0+Math.Min(8,f.Rect.Width/2),f.Rect.Y0+f.Rect.Height/2);Host.Frames(Draw,2);
                if(state=="pressed"){ImGui.GetIO().AddMouseButtonEvent(0,true);Host.Frames(Draw,2);Assert.True(ImGui.GetIO().MouseDown[0]);}
            }
            else Host.MoveMouse(Draw,-100,-100);
            Render();return f;
        }
        public void TextAA(string caption,string state,string window,bool wide=false)
        {
            var f=State(caption,state,window,wide);var g=FindGlyphs(Host,f.Rect.Clip(f.Clip),glyphs[caption]);
            if(g is null)Host.Save(Path.Combine(Path.GetTempPath(),$"a439-{caption.Replace(' ', '-')}-{state}.png"));
            Assert.True(g is not null,$"Complete '{caption}' glyphs absent/clipped in {state}, {Scale}x, {f.Rect}/{f.Clip}.");
            var ratio=Contrast(g!.Ink,g.Paper);var required=13*Scale>=24?3d:4.5d;
            Assert.True(ratio>=required,$"{caption}/{state}/{Scale}x: actual ink {Rgb(g.Ink)}, adjacent fill {Rgb(g.Paper)}, {ratio:R}:1 < {required}:1.");
        }
        public void StaticTextAA(string caption,string window)
        {
            var found=Find(caption,window);Assert.True(found is not null,$"Meaningful '{caption}' text absent or clipped at {Scale}x in {Clip(window)}.");
            var required=13*Scale>=24?3d:4.5d;var ratio=Contrast(found!.Ink,found.Paper);
            Assert.True(ratio>=required,$"'{caption}' {Scale}x actual {Rgb(found.Ink)} / {Rgb(found.Paper)} = {ratio:R}:1 < {required}:1.");
        }
        // A067 revision 2: reflow is allowed; exact consecutive caption segments must
        // render as complete lines in reading order, within the window, with AA contrast.
        public void WrappedStaticTextAA(string caption,string window,Box? bounds=null)
        {
            Render();var clip=Clip(window);if(bounds is not null)clip=clip.Clip(bounds);var words=WrappedParts(caption);
            GlyphMatch? previous=null;GlyphPattern? previousPattern=null;int first=0;
            // Outside Draw, ImGui restores the default font; this headless font is 13px before the window's text scale.
            var lineHeight=13*Scale;var required=13*Scale>=24?3d:4.5d;
            while(first<words.Length)
            {
                if(string.IsNullOrWhiteSpace(words[first])){first++;continue;}
                GlyphMatch? found=null;GlyphPattern? matched=null;int next=first;
                // Match entire candidate lines. A word rendered in isolation can rasterize
                // differently after fractional advances at 1.5x; line starts reset that phase.
                for(int count=words.Length-first;count>0;count--)
                {
                    var segment=string.Concat(words.Skip(first).Take(count)).TrimEnd();if(!glyphs.TryGetValue(segment,out var pattern))continue;
                    var search=clip;
                    if(previous is not null)
                    {
                        var top=previous.Rect.Y0-previousPattern!.InkTop+lineHeight+pattern.InkTop;
                        search=new Box(clip.X0,top-1,clip.X1,top+pattern.Height+1).Clip(clip);
                    }
                    found=FindGlyphs(Host,search,pattern);
                    if(found is null)continue;
                    Assert.True(found.Rect.X0<=clip.X0+8*Scale,
                        $"Credit line '{segment}' is not at the line's start at {Scale}x.");
                    matched=pattern;next=first+count;break;
                }
                if(found is null)Host.Save(Path.Combine(Path.GetTempPath(),$"a067-credit-{Scale}.png"));
                Assert.True(found is not null,$"Complete credit continuation starting '{words[first]}' is absent, clipped or out of order at {Scale}x.");
                Within(found!.Rect,clip,"Credit line");
                var ratio=Contrast(found.Ink,found.Paper);
                Assert.True(ratio>=required,$"Credit line {Scale}x actual {Rgb(found.Ink)} / {Rgb(found.Paper)} = {ratio:R}:1 < {required}:1.");
                previous=found;previousPattern=matched;first=next;
            }
            Assert.Equal(words.Length,first);
        }
        public Focus ReachWrapped(string anchor,string caption,string window)
        {
            var entered=new HashSet<uint>();
            for(int i=0;i<70;i++)
            {
                var f=ObserveFocus();
                if(f.Id!=0&&f.Window.StartsWith(window,StringComparison.Ordinal))
                {
                    Render();
                    if(FindGlyphs(Host,f.Rect.Clip(f.Clip),glyphs[anchor])is not null)
                    {
                        if(!f.Window.Contains("/##recents",StringComparison.Ordinal) && entered.Add(f.Id)){Key(ImGuiKey.Enter);continue;}
                        Within(f.Rect,f.Clip,"Wrapped recent entry");
                        var parts=caption.Split(new[]{"   "},2,StringSplitOptions.None);
                        if(parts.Length==2 && Draw.Target is ProjectLauncher)
                        {
                            var folder=parts[1];var tokens=WrappedParts(folder);GlyphMatch? first=null;
                            for(int count=tokens.Length;count>0 && first is null;count--)
                            {
                                var segment=string.Concat(tokens.Take(count)).TrimEnd();
                                if(glyphs.TryGetValue(segment,out var pattern))first=FindGlyphs(Host,Clip(window),pattern);
                            }
                            Assert.NotNull(first);
                            WrappedStaticTextAA(folder,window,new Box(first.Rect.X0-2*Scale,f.Rect.Y0,f.Clip.X1,f.Clip.Y1));
                            var name=FindGlyphs(Host,f.Rect.Clip(f.Clip),glyphs[anchor]);Assert.NotNull(name);
                            Assert.True(Contrast(name.Ink,name.Paper)>=(13*Scale>=24?3d:4.5d));
                        }
                        else WrappedStaticTextAA(caption,window,f.Rect.Clip(f.Clip));
                        return f;
                    }
                }
                Key(ImGuiKey.Tab);
            }
            Assert.Fail($"Keyboard traversal did not reach the complete wrapped recent entry at {Scale}x.");
            return ObserveFocus();
        }
        public void VisibleFocus(string caption,string window,bool wide=false)
        {
            var target=Reach(caption,wide,window);Key(ImGuiKey.Tab);Render();var before=Host.Pixels.ToArray();
            // Revisit this exact widget, not another Browse with the same caption.
            for(int i=0;i<70&&ObserveFocus().Id!=target.Id;i++)Key(ImGuiKey.Tab);
            var again=ObserveFocus();Assert.Equal(target.Id,again.Id);Render();
            Assert.NotNull(FindGlyphs(Host,again.Rect.Clip(again.Clip),glyphs[caption]));Within(again.Rect,again.Clip,caption);
            var box=again.Rect.Inset(-4).Clip(new(0,0,Host.Width,Host.Height));int changed=0,edge=0;var cue=new Dictionary<uint,int>();
            for(int y=Math.Max(0,(int)box.Y0);y<Math.Min(Host.Height,(int)box.Y1);y++)
                for(int x=Math.Max(0,(int)box.X0);x<Math.Min(Host.Width,(int)box.X1);x++)
                {
                    var after=Host.Pixels[y*Host.Width+x];if(after==before[y*Host.Width+x])continue;changed++;
                    if(x<again.Rect.X0+2||x>=again.Rect.X1-2||y<again.Rect.Y0+2||y>=again.Rect.Y1-2)
                    {
                        edge++;var color=after&0xffffff;cue[color]=cue.GetValueOrDefault(color)+1;
                    }
                }
            Assert.True(changed>=8&&edge>=4,$"'{caption}' {Scale}x keyboard focus has {changed} changed pixels, {edge} at boundary.");
            // Test each surrounding edge: one may abut another control or the window border.
            var q=again.Rect;Box[] regions=[new(q.X0-8,q.Y0+3,q.X0-6,q.Y1-3),new(q.X1+6,q.Y0+3,q.X1+8,q.Y1-3),
                new(q.X0+3,q.Y0-8,q.X1-3,q.Y0-6),new(q.X0+3,q.Y1+6,q.X1-3,q.Y1+8)];
            var adjacent=regions.Select(b=>b.Clip(new(0,0,Host.Width,Host.Height))).Where(b=>b.Width>=1&&b.Height>=1)
                .Select(b=>Histogram(Host,b).MaxBy(p=>p.Value).Key).ToArray();
            var ratio=cue.Where(p=>p.Value>=4).SelectMany(p=>adjacent.Select(c=>Contrast(p.Key,c))).DefaultIfEmpty(0).Max();
            Assert.True(ratio>=3,$"'{caption}' focus indicator has no sustained >=3:1 cue against surrounding {string.Join(",",adjacent.Select(Rgb))}; best {ratio:R}:1.");
            // SC 1.4.11 compares simultaneous adjacent colours, not before/after state colours.
        }
        public void Dispose()=>Host.Dispose();
    }
    private static string Repo()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)
        {
            if(File.Exists(Path.Combine(d.FullName,"Brutal.Authoring.slnx")))return d.FullName;
            if(File.Exists(Path.Combine(d.FullName,"repo","Brutal.Authoring.slnx")))return Path.Combine(d.FullName,"repo");
        }
        throw new InvalidOperationException("No test overlay repository found.");
    }
    private static string AdapterDll()
    {
        var repo=Repo();var roots=new List<string>{Path.Combine(repo,"samples","CheckersAdapter","bin"),Path.Combine(repo,"artifacts","bin","CheckersAdapter")};
        if(Path.GetFileName(repo)=="repo"&&Path.GetFileName(Path.GetDirectoryName(repo))==".gate")
            roots.Add(Path.Combine(Path.GetDirectoryName(repo)!,"artifacts","bin","CheckersAdapter"));
        return roots.Where(Directory.Exists).SelectMany(r=>Directory.EnumerateFiles(r,"CheckersAdapter.dll",SearchOption.AllDirectories))
            .Where(p=>!p.Contains(Path.DirectorySeparatorChar+"ref"+Path.DirectorySeparatorChar)).OrderByDescending(File.GetLastWriteTimeUtc).First();
    }
    private sealed class BoardRig : IDisposable
    {
        public Fixture Fixture {get;}=new(position:true);
        public DataWorkspace Workspace {get;}
        public Panel Panel {get;}
        private readonly object view;
        private readonly PropertyInfo launch;
        private readonly object? oldLaunch;
        public int Launches;
        public BoardRig()
        {
            var loaded=Adapters.Load([AdapterDll()]);Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
            launch=adapter.GetType().GetProperty("Launch")!;oldLaunch=launch.GetValue(null);
            launch.SetValue(null,new Func<ProcessStartInfo,bool>(_=>{Launches++;return true;}));
            var l=new ProjectLauncher(Fixture.Recent()){Adapter=adapter};Assert.True(l.Open(Fixture.Paths),l.Said);Workspace=l.Workspace!;
            Panel=Assert.Single(Workspace.Window!.Editor.Panels,p=>p.Id=="checkers.board");view=Panel.Draw.Target!;
        }
        public Game Current=>Position.Read(Workspace.Active!.Session!.Current.Root.GetRawText());
        public (float2 Origin,float Cell) LastBoard=>((float2,float))view.GetType().GetProperty("LastBoard")!.GetValue(view)!;
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(20,20),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(Panel.Width,Panel.Height),ImGuiCond.Always);
            if(ImGui.Begin(BoardWindow,ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoSavedSettings))Panel.Draw();ImGui.End();
        }
        public void Dispose(){launch.SetValue(null,oldLaunch);Fixture.Dispose();}
    }

    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_KeyboardSampleOpensItsActualWorkspace(float scale)
    {
        using var f=new Fixture();var l=f.Launcher(sample:true);using var ui=new Ui(l.Draw,scale,["Open the sample project"]);
        var control=ui.Reach("Open the sample project",window:LauncherWindow);Within(control.Rect,control.Clip,"Sample button");ui.Key(ImGuiKey.Enter);
        Assert.NotNull(l.Workspace);Assert.Equal(Path.Combine(f.Root,"created","Example","data"),l.Current!.Data);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_KeyboardTypesProjectFolderAndOpensWithoutMouse(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,scale,["Browse...","Open"]);
        ui.Reach("Browse...",window:LauncherWindow);ui.Key(ImGuiKey.Tab,shift:true);
        ui.Key(ImGuiKey.A,ctrl:true);ui.Host.Type(ui.Draw,f.ProjectRoot);
        ui.Reach("Open",window:LauncherWindow);ui.Key(ImGuiKey.Enter);Assert.NotNull(l.Workspace);Assert.Equal(f.Paths,l.Current);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_KeyboardBrowseOpensTheRealFolderPicker(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,scale,["Browse...","Choose this folder","Cancel"]);
        ui.Reach("Browse...",window:LauncherWindow);ui.Key(ImGuiKey.Tab,shift:true);
        ui.Key(ImGuiKey.A,ctrl:true);ui.Host.Type(ui.Draw,f.Root);ui.Key(ImGuiKey.Tab);ui.Key(ImGuiKey.Enter);
        var choose=ui.Reach("Choose this folder",window:"Choose a folder");Within(choose.Rect,choose.Clip,"Choose this folder");
        var choice=ui.Find("Choose this folder","Choose a folder###picker");if(choice is null)ui.Host.Save(Path.Combine(Path.GetTempPath(),$"a439-folder-choice-{scale}.png"));Assert.NotNull(choice);
        ui.Reach("Cancel",window:"Choose a folder");ui.Key(ImGuiKey.Enter);Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_KeyboardRecentEntryReopensItsNamedProject(float scale)
    {
        using var f=new Fixture();var l=f.Launcher(recent:true);var caption="Beacon workshop   "+f.Data;
        using var ui=new Ui(l.Draw,scale,WrappedCandidates(caption),wrappedRecentPath:true);
        ui.ReachWrapped("Beacon workshop",caption,LauncherWindow);ui.Key(ImGuiKey.Enter);Assert.NotNull(l.Workspace);Assert.Equal(f.Paths,l.Current);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_KeyboardRemoveChangesOnlyTheRecentList(float scale)
    {
        using var f=new Fixture();var l=f.Launcher(recent:true);var bytes=f.Disk();using var ui=new Ui(l.Draw,scale,["Remove"]);
        ui.Reach("Remove",window:LauncherWindow);ui.Key(ImGuiKey.Enter);Assert.Empty(l.Recent.Items);Assert.Equal(bytes,f.Disk());Assert.Null(l.Workspace);
    }
    [Fact] public void Start_KeyboardLargerAndSmallerReachTwoHundredPercentAndBack()
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,1,["Larger"]);
        ui.Reach("Larger",window:LauncherWindow);for(int i=0;i<4;i++)ui.Key(ImGuiKey.Enter);Assert.Equal(2f,EditorWindow<DataDocument>.TextScale);
        ui.Key(ImGuiKey.Tab,shift:true);ui.Key(ImGuiKey.Enter);ui.Key(ImGuiKey.Enter);Assert.Equal(1.5f,EditorWindow<DataDocument>.TextScale);
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Start_OpenButtonTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,1,["Open"]);ui.TextAA("Open",state,LauncherWindow);
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Start_BrowseButtonTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,1,["Browse..."]);ui.TextAA("Browse...",state,LauncherWindow);
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Start_RemoveButtonTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();var l=f.Launcher(recent:true);using var ui=new Ui(l.Draw,1,["Remove"]);ui.TextAA("Remove",state,LauncherWindow);
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Start_RecentEntryTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();var l=f.Launcher(recent:true);using var ui=new Ui(l.Draw,1,["Beacon workshop"]);ui.TextAA("Beacon workshop",state,LauncherWindow,wide:true);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_CopyrightRemainsLegibleAndCompleteAtEveryTextSize(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,scale,[BrutalCredit.Copyright]);ui.StaticTextAA(BrutalCredit.Copyright,LauncherWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_FrameworkCreditAndNoticeHintRemainCompleteAtEveryTextSize(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,scale,[CreditMore]);ui.StaticTextAA(CreditMore,LauncherWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_AllPathLabelsRemainCompleteAtEveryTextSize(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();string[] labels=["Data folder","Schema folder","Link map (optional)"];
        using var ui=new Ui(l.Draw,scale,labels);foreach(var label in labels)ui.StaticTextAA(label,LauncherWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_OpenAndBrowseControlsFitAtEveryTextSize(float scale)
    {
        using var f=new Fixture();var l=f.Launcher();using var ui=new Ui(l.Draw,scale,["Open","Browse..."]);
        foreach(var caption in new[]{"Open","Browse..."}){var item=ui.Reach(caption,window:LauncherWindow);Within(item.Rect,item.Clip,caption);ui.TextAA(caption,"focus",LauncherWindow);}
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_RecentProjectNameAndItsDisambiguatingPathAreNotClipped(float scale)
    {
        using var f=new Fixture();var l=f.Launcher(recent:true);
        var caption="Beacon workshop   "+f.Data;using var ui=new Ui(l.Draw,scale,WrappedCandidates(caption),wrappedRecentPath:true);
        ui.ReachWrapped("Beacon workshop",caption,LauncherWindow);
    }
    [Theory] [InlineData("Open")] [InlineData("Browse...")] [InlineData("Remove")] [InlineData("Open the sample project")] [InlineData("Beacon workshop")]
    public void Start_KeyboardFocusHasAnActualVisiblePixelCue(string caption)
    {
        using var f=new Fixture();var l=f.Launcher(sample:true,recent:true);using var ui=new Ui(l.Draw,1,[caption]);
        ui.VisibleFocus(caption,LauncherWindow,wide:caption=="Beacon workshop");
    }
    private static (FolderPicker Picker,Action Draw,Func<string?> Chosen) Picker(Fixture f,bool file=false,string? start=null)
    {
        var picker=new FolderPicker();picker.Start(start??f.Root,file);string? chosen=null;
        void Draw(){if(picker.Draw(file) is { } x)chosen=x;}
        return(picker,Draw,()=>chosen);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Picker_KeyboardEntersFolderAndChoosesIt(float scale)
    {
        using var f=new Fixture();var folder=Path.Combine(f.Root,"Harbor");Directory.CreateDirectory(folder);var p=Picker(f);
        using var ui=new Ui(p.Draw,scale,["[Harbor]","Choose this folder"]);
        ui.Reach("[Harbor]",wide:true);ui.Key(ImGuiKey.Enter);Assert.Equal(folder,p.Picker.At);
        var choose=ui.Reach("Choose this folder");Within(choose.Rect,choose.Clip,"Choose");ui.Key(ImGuiKey.Enter);Assert.Equal(folder,p.Chosen());
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Picker_KeyboardChoosesAJsonFile(float scale)
    {
        using var f=new Fixture();var file=Path.Combine(f.Root,"invented-map.json");File.WriteAllText(file,"{}");var p=Picker(f,file:true);
        using var ui=new Ui(p.Draw,scale,["invented-map.json"]);ui.Reach("invented-map.json",wide:true);ui.Key(ImGuiKey.Enter);Assert.Equal(file,p.Chosen());
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Picker_KeyboardCancelReturnsNoPath(float scale)
    {
        using var f=new Fixture();var p=Picker(f);using var ui=new Ui(p.Draw,scale,["Cancel"]);ui.Reach("Cancel");ui.Key(ImGuiKey.Enter);Assert.Equal("",p.Chosen());Assert.Equal(f.Root,p.Picker.At);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Picker_KeyboardUpMovesToTheParent(float scale)
    {
        using var f=new Fixture();var child=Path.Combine(f.Root,"Harbor");Directory.CreateDirectory(child);var p=Picker(f,start:child);
        using var ui=new Ui(p.Draw,scale,["Up"]);ui.Reach("Up");ui.Key(ImGuiKey.Enter);Assert.Equal(f.Root,p.Picker.At);
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Picker_ChooseButtonTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();var p=Picker(f);using var ui=new Ui(p.Draw,1,["Choose this folder"]);ui.TextAA("Choose this folder",state,"Choose a folder");
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Picker_FolderEntryHasMeasuredAAContrastInEveryState(string state)
    {
        using var f=new Fixture();Directory.CreateDirectory(Path.Combine(f.Root,"Harbor"));var p=Picker(f);
        using var ui=new Ui(p.Draw,1,["[Harbor]"]);ui.TextAA("[Harbor]",state,"Choose a folder",wide:true);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Picker_ChooseAndCancelFitTheirDefaultWindowAtEveryTextSize(float scale)
    {
        using var f=new Fixture();var p=Picker(f);using var ui=new Ui(p.Draw,scale,["Choose this folder","Cancel"]);
        foreach(var caption in new[]{"Choose this folder","Cancel"}){var item=ui.Reach(caption);Within(item.Rect,item.Clip,caption);ui.TextAA(caption,"focus","Choose a folder");}
    }
    [Theory] [InlineData("Choose this folder")] [InlineData("Cancel")] [InlineData("[Harbor]")]
    public void Picker_KeyboardFocusHasAnActualVisiblePixelCue(string caption)
    {
        using var f=new Fixture();Directory.CreateDirectory(Path.Combine(f.Root,"Harbor"));var p=Picker(f);using var ui=new Ui(p.Draw,1,[caption]);
        ui.VisibleFocus(caption,"Choose a folder###picker",wide:caption=="[Harbor]");
    }

    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Board_KeyboardSwitchChangesTheTurnThroughTheActualButton(float scale)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,scale,["Dark to move: switch"]);
        var disk=r.Fixture.Disk();ui.Reach("Dark to move: switch",window:BoardWindow);ui.Key(ImGuiKey.Enter);
        Assert.Equal(Side.Light,r.Current.ToMove);Assert.Single(r.Workspace.Active!.Session!.Journal);Assert.Equal(disk,r.Fixture.Disk());
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Board_KeyboardPlayUsesOnlyTheInterceptedLaunchHook(float scale)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,scale,["Play in its own window"]);
        ui.Reach("Play in its own window",window:BoardWindow);ui.Key(ImGuiKey.Enter);Assert.Equal(1,r.Launches);Assert.False(r.Workspace.Active!.Dirty);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Board_DarkSquaresCanBeChangedByKeyboardWithoutMouse(float scale)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,scale,[]);
        var before=string.Join("/",r.Current.Rows);var board=r.LastBoard;var area=new Box(board.Origin.X,board.Origin.Y,board.Origin.X+board.Cell*8,board.Origin.Y+board.Cell*8);
        var seen=new HashSet<uint>();
        for(int i=0;i<80;i++)
        {
            ui.Key(ImGuiKey.Tab);var focused=ObserveFocus();if(focused.Id==0)continue;seen.Add(focused.Id);
            var x=(focused.Rect.X0+focused.Rect.X1)/2;var y=(focused.Rect.Y0+focused.Rect.Y1)/2;
            if(x<area.X0||x>=area.X1||y<area.Y0||y>=area.Y1)continue;
            ui.Key(ImGuiKey.Enter);ui.Key(ImGuiKey.Space);
            if(string.Join("/",r.Current.Rows)!=before){Assert.True(r.Workspace.Active!.Dirty);return;}
            ui.Key(ImGuiKey.RightArrow);ui.Key(ImGuiKey.DownArrow);ui.Key(ImGuiKey.Enter);
            if(string.Join("/",r.Current.Rows)!=before){Assert.True(r.Workspace.Active!.Dirty);return;}
        }
        Assert.Fail($"No keyboard square action changed the board at {scale}x; visited {seen.Count} focus ids. Board area {area}. This panel's mouse square action needs a keyboard equivalent.");
    }
    [Theory] [InlineData("Play in its own window")] [InlineData("Dark to move: switch")]
    public void Board_KeyboardButtonFocusHasAnActualVisiblePixelCue(string caption)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,1,[caption]);ui.VisibleFocus(caption,BoardWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Board_BothButtonLabelsRemainCompleteAtEveryTextSize(float scale)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,scale,["Play in its own window","Dark to move: switch"]);
        foreach(var caption in new[]{"Play in its own window","Dark to move: switch"}){var item=ui.Reach(caption,window:BoardWindow);Within(item.Rect,item.Clip,caption);ui.TextAA(caption,"focus",BoardWindow);}
    }
    [Theory] [InlineData("idle")] [InlineData("hover")] [InlineData("pressed")] [InlineData("focus")]
    public void Board_PlayButtonTextHasMeasuredAAContrastInEveryState(string state)
    {
        using var r=new BoardRig();using var ui=new Ui(r.Draw,1,["Play in its own window"]);ui.TextAA("Play in its own window",state,BoardWindow);
    }
    private static void SetPiece(BoardRig r,char piece)
    {
        var row=piece is 'd' or 'D'?5:2;var col=piece is 'd' or 'D'?0:1;if(r.Current.At(row,col)==piece)return;var chars=r.Current.Rows[row].ToCharArray();chars[col]=piece;
        Assert.True(r.Workspace.Active!.Session!.Execute(new SetValue("/board/"+row,JsonSerializer.SerializeToElement(new string(chars)))));
    }
    private static uint At(HeadlessHost h,float x,float y)=>h.Pixels[Math.Clamp((int)y,0,h.Height-1)*h.Width+Math.Clamp((int)x,0,h.Width-1)]&0xffffff;
    private static void PieceAA(HeadlessHost host,(float2 Origin,float Cell) board,int row,int col,string name)
    {
        float cx=board.Origin.X+(col+.5f)*board.Cell,cy=board.Origin.Y+(row+.5f)*board.Cell;
        int visible=0;double best=0;
        for(int a=0;a<72;a++)
        {
            var angle=2*Math.PI*a/72;var outside=At(host,cx+(float)Math.Cos(angle)*board.Cell*.46f,cy+(float)Math.Sin(angle)*board.Cell*.46f);double local=0;
            for(float radius=.32f;radius<=.42f;radius+=.01f)local=Math.Max(local,Contrast(outside,At(host,cx+(float)Math.Cos(angle)*board.Cell*radius,cy+(float)Math.Sin(angle)*board.Cell*radius)));
            if(local>=3)visible++;best=Math.Max(best,local);
        }
        Assert.True(visible>=54,$"{name}: only {visible}/72 silhouette rays have an actual >=3:1 piece/outline against its square; maximum {best:R}:1. A crown inside is not the piece's outer boundary.");
    }
    [Theory] [InlineData('d',1f)] [InlineData('D',1f)] [InlineData('l',1f)] [InlineData('L',1f)]
    [InlineData('d',1.5f)] [InlineData('D',1.5f)] [InlineData('l',1.5f)] [InlineData('L',1.5f)]
    [InlineData('d',2f)] [InlineData('D',2f)] [InlineData('l',2f)] [InlineData('L',2f)]
    public void Board_PieceSilhouetteHasThreeToOneAgainstItsActualSquare(char piece,float scale)
    {
        using var r=new BoardRig();SetPiece(r,piece);using var ui=new Ui(r.Draw,scale,[]);ui.Render();
        PieceAA(ui.Host,r.LastBoard,piece is 'd' or 'D'?5:2,piece is 'd' or 'D'?0:1,Game.Describe(piece)+"/"+scale);
    }
    [Theory] [InlineData('D')] [InlineData('L')]
    public void Board_KingsCrownIsDistinguishableFromThePieceBody(char king)
    {
        using var r=new BoardRig();SetPiece(r,king);using var ui=new Ui(r.Draw,1,[]);ui.Render();var b=r.LastBoard;
        var row=king=='D'?5:2;var col=king=='D'?0:1;float cx=b.Origin.X+(col+.5f)*b.Cell,cy=b.Origin.Y+(row+.5f)*b.Cell;
        var body=At(ui.Host,cx,cy);var crown=new Dictionary<uint,int>();
        for(int angle=0;angle<72;angle++)for(float radius=.17f;radius<=.23f;radius+=.01f)
        {var a=Math.PI*2*angle/72;var c=At(ui.Host,cx+(float)Math.Cos(a)*radius*b.Cell,cy+(float)Math.Sin(a)*radius*b.Cell);crown[c]=crown.GetValueOrDefault(c)+1;}
        var candidates=crown.Where(p=>p.Value>=16&&Contrast(p.Key,body)>=3).ToArray();
        Assert.True(candidates.Length>0,$"{Game.Describe(king)} crown has no sustained >=3:1 mark against actual body {Rgb(body)}; maximum {crown.Keys.Max(c=>Contrast(c,body)):R}:1.");
    }
    private sealed class PlayRig : IDisposable
    {
        public CheckersPlay.Play Play {get;}=new(Game.Opening());
        public Ui Ui {get;}
        public PlayRig(float scale,params string[] captions)=>Ui=new Ui(Play.Draw,scale,captions,720,800);
        public void MoveWithKeys(){Ui.Reach("Computer move",window:PlayWindow);Ui.Key(ImGuiKey.Enter);Assert.Equal(Side.Light,Play.Match.Current.ToMove);}
        public void Dispose()=>Ui.Dispose();
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_KeyboardComputerButtonPlaysAnActualMove(float scale)
    {
        using var r=new PlayRig(scale,"Computer move");var before=string.Join("/",r.Play.Match.Current.Rows);r.MoveWithKeys();Assert.NotEqual(before,string.Join("/",r.Play.Match.Current.Rows));
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_KeyboardTakeBackUndoesTheMove(float scale)
    {
        using var r=new PlayRig(scale,"Computer move","Take back");var before=string.Join("/",r.Play.Match.Current.Rows);r.MoveWithKeys();
        r.Ui.Reach("Take back",window:PlayWindow);r.Ui.Key(ImGuiKey.Enter);Assert.Equal(before,string.Join("/",r.Play.Match.Current.Rows));Assert.Equal(Side.Dark,r.Play.Match.Current.ToMove);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_KeyboardNewGameResetsTheMatch(float scale)
    {
        using var r=new PlayRig(scale,"Computer move","New game");var match=r.Play.Match;r.MoveWithKeys();r.Ui.Reach("New game",window:PlayWindow);r.Ui.Key(ImGuiKey.Enter);
        Assert.NotSame(match,r.Play.Match);Assert.Equal(string.Join("/",Game.Opening().Rows),string.Join("/",r.Play.Match.Current.Rows));
    }
    [Theory] [InlineData("New game","idle")] [InlineData("New game","hover")] [InlineData("New game","pressed")] [InlineData("New game","focus")]
    [InlineData("Computer move","idle")] [InlineData("Computer move","hover")] [InlineData("Computer move","pressed")] [InlineData("Computer move","focus")]
    [InlineData("Take back","idle")] [InlineData("Take back","hover")] [InlineData("Take back","pressed")] [InlineData("Take back","focus")]
    public void Play_EnabledButtonTextHasMeasuredAAContrastInEveryState(string caption,string state)
    {
        using var r=new PlayRig(1,"Computer move",caption);if(caption=="Take back")r.MoveWithKeys();r.Ui.TextAA(caption,state,PlayWindow);
    }
    [Theory] [InlineData("New game")] [InlineData("Computer move")] [InlineData("Take back")]
    public void Play_KeyboardFocusHasAnActualVisiblePixelCue(string caption)
    {
        using var r=new PlayRig(1,"Computer move",caption);if(caption=="Take back")r.MoveWithKeys();r.Ui.VisibleFocus(caption,PlayWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_AllEnabledButtonsFitAtEveryTextSize(float scale)
    {
        using var r=new PlayRig(scale,"New game","Computer move","Take back");r.MoveWithKeys();
        foreach(var caption in new[]{"New game","Computer move","Take back"}){var item=r.Ui.Reach(caption,window:PlayWindow);Within(item.Rect,item.Clip,caption);}
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_CopyrightIsCompleteAndLegibleAtEveryTextSize(float scale)
    {
        var words=BrutalCredit.Copyright.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var lines=Enumerable.Range(0,words.Length).SelectMany(first=>Enumerable.Range(1,words.Length-first)
            .Select(count=>string.Join(' ',words.Skip(first).Take(count)))).ToArray();
        using var r=new PlayRig(scale,lines);r.Ui.WrappedStaticTextAA(BrutalCredit.Copyright,PlayWindow);
    }
    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Play_StatusIsCompleteAndLegibleAtEveryTextSize(float scale)
    {
        using var r=new PlayRig(scale,"Move 1: Dark to play.");r.Ui.StaticTextAA("Move 1: Dark to play.",PlayWindow);
    }
    [Theory] [InlineData(ImGuiKey.Escape)] [InlineData(ImGuiKey.Enter)] [InlineData(ImGuiKey.Space)]
    public void Splash_KeyboardSkipWorksAfterOneSecond(ImGuiKey key)
    {
        using var host=new HeadlessHost(720,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetIO().DeltaTime=.1f;
        var splash=new BrutalSplash();void Draw()=>splash.Draw();host.Frames(Draw,13);Assert.False(splash.Done);
        host.Press(Draw,key);Assert.True(splash.Done);
    }
    [Theory] [InlineData(ImGuiKey.Escape)] [InlineData(ImGuiKey.Enter)] [InlineData(ImGuiKey.Space)]
    public void Splash_EarlyKeyCannotDismissTheRequiredFirstSecond(ImGuiKey key)
    {
        using var host=new HeadlessHost(720,800){Drawing=false};ImGui.GetIO().DeltaTime=.05f;var splash=new BrutalSplash();void Draw()=>splash.Draw();
        host.Frames(Draw,2);host.Press(Draw,key);Assert.False(splash.Done);
    }
    [Fact] public void Splash_EndsByItsDocumentedTwoAndAHalfSecondsWithoutInput()
    {
        using var host=new HeadlessHost(720,800){Drawing=false};ImGui.GetIO().DeltaTime=.1f;var splash=new BrutalSplash();bool drawing=false;void Draw()=>drawing=splash.Draw();
        host.Frames(Draw,23);Assert.False(splash.Done);Assert.True(drawing);host.Frames(Draw,5);Assert.True(splash.Done);Assert.False(drawing);
    }
    [Theory] [InlineData(320,240)] [InlineData(720,800)] [InlineData(1600,900)]
    public void Splash_UnalteredLogoIsVisibleCenteredAndInsideTheViewport(int width,int height)
    {
        using var host=new HeadlessHost(width,height){Drawing=false};ImGui.GetIO().DeltaTime=.1f;var splash=new BrutalSplash();void Draw()=>splash.Draw();host.Frames(Draw,13);host.Drawing=true;host.Frame(Draw);
        var background=host.Pixels.GroupBy(p=>p).MaxBy(g=>g.Count())!.Key;var points=new List<(int X,int Y)>();
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(host.Pixels[y*width+x]!=background)points.Add((x,y));
        Assert.True(points.Count>200,"Logo has no meaningful visible area.");int x0=points.Min(p=>p.X),x1=points.Max(p=>p.X),y0=points.Min(p=>p.Y),y1=points.Max(p=>p.Y);
        Assert.InRange((x0+x1)/2d,width*.4,width*.6);Assert.InRange((y0+y1)/2d,height*.4,height*.6);
        Assert.True(x0>0&&y0>0&&x1<width-1&&y1<height-1,"Logo is clipped.");Assert.False(splash.Done);
        // No edits, recolouring, contrast threshold or colour assertions on the licensed logo.
    }
}
