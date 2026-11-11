using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// Source-prepared only. Requires the serial native-context collection. No OS clipboard or URLs.
[Collection(ImGuiCollection.Name)]
public sealed class AiSetupPanelUiA392Tests
{
    private const string PanelWindow = "AI setup proof###a392";
    private const string StartWindow = "BED###launcher";
    private static readonly string[] CopyLabels =
        ["Copy bed-cli command", "Copy Claude Desktop JSON", "Copy claude mcp add"];

    private static string FocusWindow()
    {
        var window = ImGui.GetCurrentContext().NavWindow;
        return window.IsNull() ? "" : Marshal.PtrToStringUTF8((IntPtr)window.Name) ?? "";
    }

    private static void Reach(HeadlessHost host, Action draw, string title, string label)
    {
        var window = ImGui.Internal.FindWindowByName(title);
        Assert.False(window.IsNull(), "Missing window: " + title);
        var wanted = window.GetID(label).Value;
        for (var i = 0; i < 40; i++)
        {
            var context = ImGui.GetCurrentContext();
            if (FocusWindow() == title && context.NavId.Value == wanted) return;
            host.Press(draw, ImGuiKey.Tab);
        }
        Assert.Fail("Tab did not reach " + label + " in " + title);
    }

    private static void FocusFits()
    {
        var context = ImGui.GetCurrentContext();
        Assert.NotEqual(0u, context.NavId.Value);
        var window = context.NavWindow;
        Assert.False(window.IsNull());
        var rect = window.NavRectRel[(int)context.NavLayer];
        var origin = window.DC.CursorStartPos;
        var clip = window.InnerClipRect;
        Assert.True(origin.X + rect.Min.X >= clip.Min.X - 1 && origin.X + rect.Max.X <= clip.Max.X + 1,
            "Focused Copy control overflows horizontally.");
        Assert.True(origin.Y + rect.Min.Y >= clip.Min.Y - 1 && origin.Y + rect.Max.Y <= clip.Max.Y + 1,
            "Focused Copy control is not scrolled into view.");
    }

    private static string Text(HeadlessHost host, Action draw)
    {
        host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? "";
    }

    private sealed class PanelRig : IDisposable
    {
        public HeadlessHost Host { get; } = new(1600, 1000) { Drawing = false };
        public AiSetupPanel Panel { get; }
        public PanelRig(float scale, bool builtIn = false)
        {
            var folder = Path.Combine(Path.GetTempPath(), "Beacon examples", "O'Brien tools");
            var project = new ProjectPaths(Path.Combine(folder, "long descriptive data folder"),
                Path.Combine(folder, "schemas"), Path.Combine(folder, "reference_map.json"));
            Panel = new(folder, project, builtIn ? AiSetupPanel.BuiltInReason : null);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain = scale;
            Host.MoveMouse(Draw, -100, -100);
            Host.Frames(Draw, 5);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(20, 20), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(720, 560), ImGuiCond.Always);
            if (ImGui.Begin(PanelWindow, ImGuiWindowFlags.NoSavedSettings)) Panel.Draw();
            ImGui.End();
        }
        public void Dispose() => Host.Dispose();
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Tab_and_Enter_copy_all_three_exact_examples_to_the_owned_clipboard(float scale)
    {
        using var rig = new PanelRig(scale);
        var snippets = Assert.IsType<AiSetupSnippets>(rig.Panel.Snippets);
        string[] expected = [snippets.Command, snippets.DesktopJson, snippets.ClaudeCode];
        for (var i = 0; i < CopyLabels.Length; i++)
        {
            Reach(rig.Host, rig.Draw, PanelWindow, CopyLabels[i]);
            FocusFits();
            ImGui.SetClipboardText("not yet copied");
            rig.Host.Press(rig.Draw, ImGuiKey.Enter);
            Assert.Equal(expected[i], ImGui.GetClipboardText().ToString());
        }
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Built_in_adapter_submits_exact_reason_and_no_Copy_controls(float scale)
    {
        using var rig = new PanelRig(scale, builtIn: true);
        Assert.Null(rig.Panel.Snippets);
        var text = Text(rig.Host, rig.Draw);
        Assert.Contains("this project's adapter is built into BED and can't be passed to bed-cli", text);
        foreach (var label in CopyLabels) Assert.DoesNotContain(label, text);
        var window = ImGui.Internal.FindWindowByName(PanelWindow);
        var forbidden = CopyLabels.Select(label => window.GetID(label).Value).ToArray();
        ImGui.SetClipboardText("unchanged");
        for (var i = 0; i < 16; i++)
        {
            rig.Host.Press(rig.Draw, ImGuiKey.Tab);
            Assert.DoesNotContain(ImGui.GetCurrentContext().NavId.Value, forbidden);
        }
        Assert.Equal("unchanged", ImGui.GetClipboardText().ToString());
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Start_has_no_AI_route_and_human_help_keeps_keyboard_Escape_and_focus(float scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "a392-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = new HeadlessHost(1600, 1000) { Drawing = false };
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain = scale;
            host.MoveMouse(launcher.Draw, -100, -100);
            host.Frames(launcher.Draw, 5);
            Assert.DoesNotContain("Help: Use with an AI assistant", Text(host, launcher.Draw));
            foreach (var label in CopyLabels) Assert.DoesNotContain(label, Text(host, launcher.Draw));
            Reach(host, launcher.Draw, StartWindow, "Getting started");
            var helpId = ImGui.GetCurrentContext().NavId.Value;
            host.Press(launcher.Draw, ImGuiKey.Enter);
            Reach(host, launcher.Draw, StartWindow, "Close guide");
            Assert.Contains(GettingStartedPanel.Heading, Text(host, launcher.Draw));
            Assert.Null(launcher.Workspace);
            host.Press(launcher.Draw, ImGuiKey.Escape);
            host.Frames(launcher.Draw, 3);
            Assert.DoesNotContain(GettingStartedPanel.Heading, Text(host, launcher.Draw));
            Assert.Equal(StartWindow, FocusWindow());
            Assert.Equal(helpId, ImGui.GetCurrentContext().NavId.Value);
            Assert.Null(launcher.Workspace);
        }
        finally
        {
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a392-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }

    // Native full-label extents and draw clips replace the old glyph search. Exact blocks retain
    // every explicit newline, punctuation and path byte, in order, at all three text scales.
    // Tall surface and forced scrollbar still isolate horizontal wrapping at the real 720px width.
    // This does not prove vertical scrolling, inter-window occlusion or launcher layout.
    [Theory]
    [InlineData(1f, false)] [InlineData(1.5f, false)] [InlineData(2f, false)]
    [InlineData(1f, true)] [InlineData(1.5f, true)] [InlineData(2f, true)]
    public void Every_expected_text_line_is_complete_and_inside_the_panel_width(float scale, bool builtIn)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Beacon examples", "O'Brien tools");
        var panel = new AiSetupPanel(folder, new ProjectPaths(Path.Combine(folder, "data"),
            Path.Combine(folder, "schemas"), Path.Combine(folder, "reference_map.json")),
            builtIn ? AiSetupPanel.BuiltInReason : null);
        var snippets = AiSetupSnippets.Create(folder, new ProjectPaths(Path.Combine(folder, "data"),
            Path.Combine(folder, "schemas"), Path.Combine(folder, "reference_map.json")));
        string[] blocks =
        [
            "BED does not contact an AI provider. Your external assistant may send tool inputs and results to its provider; check its privacy settings before connecting.",
            "These examples do not change any app's configuration. Value edits are drafts until save; file create, rename and delete happen immediately unless previewed. Ask the assistant to preview first.",
            "PowerShell: inspect the project", "Copy bed-cli command", snippets.Command,
            "Claude Desktop configuration entry", "Copy Claude Desktop JSON", snippets.DesktopJson,
            "PowerShell: add to Claude Code", "Copy claude mcp add", snippets.ClaudeCode,
            "For a plain project, --read-only may be placed before describe or mcp. It cannot be combined with --adapter. Adapters are programs: use only ones you trust.",
            "Read the packaged AI guide", panel.GuidePath
        ];
        if (builtIn) blocks = [blocks[0], blocks[1], AiSetupPanel.BuiltInReason, "Read the packaged AI guide", panel.GuidePath];
        using var host = new HeadlessHost(800, 6000) { Drawing = false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        ImGui.GetStyle().FontScaleMain = scale;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(20, 20), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(720, 5600), ImGuiCond.Always);
            if (ImGui.Begin(PanelWindow, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysVerticalScrollbar)) panel.Draw();
            ImGui.End();
        }
        host.Frames(Draw, 5);
        Assert.False(ImGui.Internal.FindWindowByName(PanelWindow).IsNull());
        var nodes = Ui.Snapshot().Nodes;
        var previous = -1;
        var previousBottom = float.NegativeInfinity;
        foreach (var block in blocks)
        {
            var matches = nodes.Select((node, index) => (node, index)).Where(pair => pair.node.Label == block).ToArray();
            var match = Assert.Single(matches);
            Assert.True(match.index > previous, "Expected text blocks must retain their displayed order.");
            // Exact full block preserves explicit newlines, punctuation and path bytes. Native wrapping
            // uses the full label, active font and actual clip, including the forced scrollbar width.
            UiTreeAssertionsA448.FullyShown(match.node, block);
            var bounds = match.node.TextEvidence!.Value.FullBounds;
            Assert.True(bounds.Y >= previousBottom, "Expected text blocks must remain in vertical reading order without overlap.");
            previousBottom = bounds.Y + bounds.Height;
            previous = match.index;
        }
    }

    private static Box WrappedLines(HeadlessHost host, string caption, float scale, Box clip,
        IReadOnlyDictionary<string, GlyphPattern> patterns)
    {
        var words = WrappedParts(caption);
        GlyphMatch? previous = null; GlyphPattern? previousPattern = null;
        var first = 0;
        while (first < words.Length)
        {
            if (string.IsNullOrWhiteSpace(words[first])) { first++; continue; }
            GlyphMatch? found = null; GlyphPattern? matched = null; var next = first;
            for (var count = words.Length - first; count > 0; count--)
            {
                var segment = string.Concat(words.Skip(first).Take(count)).TrimEnd();
                if (!patterns.TryGetValue(segment, out var pattern)) continue;
                var search = clip;
                if (previous is not null)
                {
                    var top = previous.Rect.Y0 - previousPattern!.InkTop + 13 * scale + pattern.InkTop;
                    search = new Box(clip.X0, top - 1, clip.X1, top + pattern.Height + 1).Clip(clip);
                }
                found = FindGlyphs(host, search, pattern);
                if (found is null) continue;
                matched = pattern; next = first + count; break;
            }
            Assert.True(found is not null, $"Complete line starting '{words[first]}' absent/clipped/out of order at {scale}x in '{caption}'.");
            Within(found!.Rect, clip, "AI setup text line");
            previous = found; previousPattern = matched; first = next;
        }
        Assert.Equal(words.Length, first);
        Assert.NotNull(previous);
        return previous!.Rect;
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
                if(segment.Count(char.IsLetterOrDigit)>=2 || (!caption.Any(char.IsLetterOrDigit) && segment == caption.Trim()))yield return segment;
            }
        }
    }
}
