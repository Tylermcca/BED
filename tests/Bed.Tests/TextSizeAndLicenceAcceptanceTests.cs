using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using App = Bed.Launcher.Program;

namespace Bed.Tests;

// A070. INVENTED fixtures and independent acceptance oracles; MIXED with the existing headless input contract.
// BORROWED expectations: the named public XML comments, BRUTAL License v1.0 2.1-2.3 / notices 4.0.
// The supplied BRUTAL branding is read in place for the comparison this card explicitly requests; never copied.
// Every invocation of the app below has --shot or --help. No OS window, actual settings or game data is touched.
[Collection(ImGuiCollection.Name)]
public sealed class TextSizeAndLicenceAcceptanceTests : IDisposable
{
    private const string Credit = "BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";
    private readonly string root = Path.Combine(Path.GetTempPath(), "a070-" + Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(root, "trial settings");
    private string Project => Path.Combine(root, "invented project");
    private string Data => Path.Combine(Project, "data", "items.json");
    private static float Scale => EditorWindow<DataDocument>.TextScale;
    private static bool Set(float value) => EditorWindow<DataDocument>.SetTextScale(value);

    public TextSizeAndLicenceAcceptanceTests()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(Project, "data"));
        Directory.CreateDirectory(Path.Combine(Project, "schemas"));
        File.WriteAllText(Data, """{"items":[{"id":"bead","value":3}]}""");
        File.WriteAllText(Path.Combine(Project, "schemas", "items.schema.json"),
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"value":{"type":"integer"}},"required":["id","value"]}}},"required":["items"]}""");
    }

    public void Dispose() => Directory.Delete(root, true);

    private ProjectLauncher Launcher(bool makeSettings = true)
    {
        if (makeSettings) Directory.CreateDirectory(Settings);
        return new(new RecentProjects(Path.Combine(Settings, "recent.json")));
    }

    private static HeadlessHost Host(int width = 1200, int height = 800)
    {
        var host = new HeadlessHost(width, height) { Drawing = false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        return host;
    }

    private EditorWindow<DataDocument> Open(ProjectLauncher launcher, HeadlessHost host)
    {
        host.Frames(launcher.Draw, 2);
        Assert.True(launcher.OpenFolder(Project), launcher.Said);
        host.Frames(launcher.Draw, 3);
        return launcher.Workspace!.Window!;
    }

    private static string Log(HeadlessHost host, Action draw)
    {
        // HeadlessHost owns an in-memory clipboard: this never uses the person's clipboard.
        host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? "";
    }

    private static void StartButton(HeadlessHost host, ProjectLauncher launcher, string caption)
    {
        if (caption is "Larger" or "Smaller" && !Log(host, launcher.Draw).Contains("Text size", StringComparison.Ordinal))
            StartButton(host, launcher, "Preferences");
        var window = ImGui.Internal.FindWindowByName("BED###launcher");
        Assert.False(window.IsNull());
        var wanted = window.GetID(caption).Value;
        for (var i = 0; i < 45; i++)
        {
            if (ImGui.GetCurrentContext().NavId.Value == wanted)
            {
                host.Press(launcher.Draw, ImGuiKey.Space);
                return;
            }
            host.Press(launcher.Draw, ImGuiKey.Tab);
        }
        Assert.Fail("Keyboard never reached the named start-screen button: " + caption);
    }

    private static void Palette(HeadlessHost host, ProjectLauncher launcher, string caption)
    {
        host.Press(launcher.Draw, ImGuiKey.P, ctrl: true, shift: true);
        Assert.True(launcher.Workspace!.Window!.PaletteOpen);
        host.Type(launcher.Draw, caption);
        host.Frames(launcher.Draw, 2);
        host.Press(launcher.Draw, ImGuiKey.Enter);
        Assert.False(launcher.Workspace.Window.PaletteOpen);
    }

    private float SavedScale()
    {
        var path = Path.Combine(Settings, "settings.json");
        Assert.True(File.Exists(path), "Changing text size must create its settings file.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("textScale").GetSingle();
    }

    private static string Repository([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Bed.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Source: MinTextScale "The smallest text size"; MaxTextScale "twice normal"; TextStep "How much Larger and Smaller text change it."
    [Fact] public void Size_ContractBoundsAndStepAreExplicit()
    {
        Assert.Equal(.75f, EditorWindow<DataDocument>.MinTextScale);
        Assert.Equal(2f, EditorWindow<DataDocument>.MaxTextScale);
        Assert.Equal(.25f, EditorWindow<DataDocument>.TextStep);
    }

    // Source: TextScale "no window yet: normal"; SetTextScale "False when it doesn't change."
    [Fact] public void Size_NoContextIsNormalAndCannotChange()
    {
        Assert.True(ImGui.GetCurrentContext().IsNull());
        Assert.Equal(1, Scale);
        Assert.False(Set(1.5f));
        Assert.Equal(1, Scale);
    }

    // Source: SetTextScale "kept between MinTextScale and MaxTextScale."
    [Theory]
    [InlineData(-100f,.75f)][InlineData(0f,.75f)][InlineData(.5f,.75f)]
    [InlineData(2.25f,2f)][InlineData(100f,2f)]
    public void Size_OutOfRangeRequestsClamp(float request, float expected)
    {
        using var host = Host();
        Assert.True(Set(request));
        Assert.Equal(expected, Scale);
        Assert.False(Set(request));
    }

    // Source: TextStep "How much Larger and Smaller text change it."
    [Theory]
    [InlineData(.75f)][InlineData(1f)][InlineData(1.25f)][InlineData(1.5f)][InlineData(1.75f)][InlineData(2f)]
    public void Size_EachSupportedStepIsExactlyRepresentable(float value)
    {
        using var host = Host();
        Set(value);
        Assert.Equal(value, Scale);
        Assert.False(Set(value));
    }

    // Source: SetTextScale rounds a requested size to TextStep before clamping (public method).
    [Theory][InlineData(1.12f,1f)][InlineData(1.38f,1.5f)][InlineData(1.86f,1.75f)]
    public void Size_OffStepRequestsLandOnTheNearestQuarter(float request, float expected)
    {
        using var host = Host(); Set(request); Assert.Equal(expected, Scale);
    }

    // Source: TextScale "shared by every window."
    [Fact] public void Size_OneContextSharesTheSizeAcrossDocumentTypes()
    {
        using var host = Host();
        Assert.True(Set(1.5f));
        Assert.Equal(1.5f, EditorWindow<object>.TextScale);
        Assert.True(EditorWindow<object>.SetTextScale(.75f));
        Assert.Equal(.75f, Scale);
    }

    // Source: TextScale "no window yet: normal."
    [Fact] public void Size_NewContextStartsAtNormalWithoutLeakingPreviousContext()
    {
        using (var first = Host()) { Set(2); Assert.Equal(2, Scale); }
        Assert.Equal(1, Scale);
        using var second = Host(); Assert.Equal(1, Scale);
    }

    // Source: TextStep "How much Larger and Smaller text change it."
    [Fact] public void Actions_LargerVisitsEveryStepAndStopsAtTwo()
    {
        var l = Launcher(); using var h = Host(); var w = Open(l,h);
        foreach (var expected in new[] {1.25f,1.5f,1.75f,2f})
        { Assert.True(w.Run("editor.text_larger")); Assert.Equal(expected, Scale); }
        Assert.False(w.Run("editor.text_larger"));
        Assert.Equal(2,Scale); Assert.Contains("as large as it goes",w.Status);
    }

    // Source: TextStep "How much Larger and Smaller text change it."
    [Fact] public void Actions_SmallerVisitsEveryStepAndStopsAtMinimum()
    {
        var l = Launcher(); using var h = Host(); var w = Open(l,h); Set(2);
        foreach (var expected in new[] {1.75f,1.5f,1.25f,1f,.75f})
        { Assert.True(w.Run("editor.text_smaller")); Assert.Equal(expected,Scale); }
        Assert.False(w.Run("editor.text_smaller"));
        Assert.Equal(.75f,Scale); Assert.Contains("as small as it goes",w.Status);
    }

    // Source: palette action editor.text_reset "Normal text size."
    [Theory][InlineData(.75f)][InlineData(2f)]
    public void Actions_ResetReturnsToNormalAndThenReportsNoChange(float from)
    {
        var l=Launcher(); using var h=Host(); var w=Open(l,h); Set(from);
        Assert.True(w.Run("editor.text_reset")); Assert.Equal(1,Scale);
        Assert.False(w.Run("editor.text_reset")); Assert.Contains("already its normal size",w.Status);
    }

    // Source: Configure "every action is reachable without a pointer."
    [Theory][InlineData("Larger text",1.25f)][InlineData("Smaller text",.75f)]
    public void Keyboard_PaletteChangesSizeThroughActualKeys(string caption,float expected)
    {
        var l=Launcher();using var h=Host();Open(l,h);
        Palette(h,l,caption);Assert.Equal(expected,Scale);Assert.Equal(expected,SavedScale());
    }

    // Source: TextScale "a text-size setting anyone can reach by keyboard, through the palette."
    [Fact] public void Keyboard_PaletteCanRestoreNormalSize()
    {
        var l=Launcher();using var h=Host();Open(l,h);Set(2);
        Palette(h,l,"Normal text size");Assert.Equal(1,Scale);Assert.Equal(1,SavedScale());
    }

    // Source: PaletteEntries exposes unavailable actions with their reason.
    [Fact] public void Actions_PaletteExplainsBothBoundsAndNormalReset()
    {
        var l=Launcher();using var h=Host();var w=Open(l,h);
        Assert.NotNull(Assert.Single(w.PaletteEntries("Normal text size")).Unavailable);
        Set(.75f);
        Assert.NotNull(Assert.Single(w.PaletteEntries("Smaller text")).Unavailable);
        Assert.Null(Assert.Single(w.PaletteEntries("Larger text")).Unavailable);
        Set(2);
        Assert.NotNull(Assert.Single(w.PaletteEntries("Larger text")).Unavailable);
        Assert.Null(Assert.Single(w.PaletteEntries("Smaller text")).Unavailable);
    }

    // Source: EditorWindow "It owns no document state: every edit goes through the model's session."
    [Fact] public void Actions_TextSizeIsViewStateLeavingDraftHistoryAndDiskAlone()
    {
        var l=Launcher();using var h=Host();var w=Open(l,h);
        var before=File.ReadAllBytes(Data);var current=w.Editor.Session.Current;
        w.Run("editor.text_larger");w.Run("editor.text_smaller");w.Run("editor.text_reset");
        Assert.Same(current,w.Editor.Session.Current);Assert.False(w.Editor.Session.Dirty);
        Assert.False(w.Editor.Session.Undo());Assert.False(w.Editor.Session.Redo());
        Assert.Equal(before,File.ReadAllBytes(Data));
    }

    // Source: ProjectLauncher.DrawStart offers "Smaller" and "Larger"; Configure "every action is reachable without a pointer."
    [Theory][InlineData("Smaller",.75f)][InlineData("Larger",1.25f)]
    public void Keyboard_StartButtonsChangeSizeByTabAndSpace(string button,float expected)
    {
        var l=Launcher();using var h=Host();h.Frames(l.Draw,3);
        StartButton(h,l,button);Assert.Equal(expected,Scale);Assert.Equal(expected,SavedScale());
        Assert.Null(l.Workspace);
    }

    // Source: KeepTextSize "Nothing is written until it does [change]."
    [Fact] public void Settings_IdleStartCreatesNoSettingsOrRecentFile()
    {
        var l=Launcher(false);using var h=Host();h.Frames(l.Draw,8);
        Assert.False(File.Exists(l.SettingsFile));Assert.False(File.Exists(l.Recent.File));
        Assert.False(Directory.Exists(Settings));
    }

    // Source: SettingsFile "The settings file beside the recent list: the person's text size."
    [Fact] public void Settings_FileIsBesideTheRecentListEvenForRelativePaths()
    {
        var relative=Path.GetRelativePath(Environment.CurrentDirectory,Path.Combine(Settings,"recent.json"));
        var l=new ProjectLauncher(new RecentProjects(relative));
        Assert.Equal(Path.Combine(Settings,"settings.json"),l.SettingsFile,
            OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
    }

    // Source: KeepTextSize "saves it whenever it changes (by the palette or the start screen's buttons)."
    [Fact] public void Settings_FirstStartButtonChangeCreatesANewSettingsFolder()
    {
        var l=Launcher(false);using var h=Host();h.Frames(l.Draw,3);
        StartButton(h,l,"Larger");Assert.Equal(1.25f,Scale);Assert.Equal(1.25f,SavedScale());
    }

    // Source: KeepTextSize "Applies the remembered text size on the first frame."
    [Theory][InlineData(.75f)][InlineData(1.25f)][InlineData(2f)]
    public void Settings_RememberedSizeAppliesOnFirstFrameWithoutRewriting(float expected)
    {
        Directory.CreateDirectory(Settings);var path=Path.Combine(Settings,"settings.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new {textScale=expected}));
        var before=File.ReadAllBytes(path);var stamp=File.GetLastWriteTimeUtc(path);
        var l=Launcher();using var h=Host();h.Frame(l.Draw);
        Assert.Equal(expected,Scale);h.Frames(l.Draw,4);
        Assert.Equal(before,File.ReadAllBytes(path));Assert.Equal(stamp,File.GetLastWriteTimeUtc(path));
    }

    // Source: SetTextScale "kept between MinTextScale and MaxTextScale."
    [Theory][InlineData(-3f,.75f)][InlineData(9f,2f)]
    public void Settings_OutOfRangeRememberedSizeIsClamped(float value,float expected)
    {
        Directory.CreateDirectory(Settings);
        File.WriteAllText(Path.Combine(Settings,"settings.json"),JsonSerializer.Serialize(new{textScale=value}));
        var l=Launcher();using var h=Host();h.Frame(l.Draw);Assert.Equal(expected,Scale);
    }

    // Source: ProjectLauncher constructor "a bad file is the normal size."
    [Theory][InlineData("{broken")][InlineData("")][InlineData("{}")]
    public void Settings_MalformedOrMissingSizeFallsBackToNormal(string text)
    {
        Directory.CreateDirectory(Settings);File.WriteAllText(Path.Combine(Settings,"settings.json"),text);
        var l=Launcher();using var h=Host();h.Frames(l.Draw,3);Assert.Equal(1,Scale);
    }

    // Source: ProjectLauncher constructor "a bad file is the normal size."
    [Theory][InlineData("""{"textScale":"large"}""")][InlineData("[]")][InlineData("null")]
    public void Settings_WrongJsonShapeAlsoFallsBackToNormal(string text)
    {
        Directory.CreateDirectory(Settings);File.WriteAllText(Path.Combine(Settings,"settings.json"),text);
        var l=Launcher();using var h=Host();h.Frame(l.Draw);Assert.Equal(1,Scale);
    }

    // Source: KeepTextSize "then saves it whenever it changes."
    [Fact] public void Settings_ChangedSizeSurvivesANewLauncherAndContext()
    {
        var l=Launcher();
        using(var h=Host()){h.Frames(l.Draw,3);StartButton(h,l,"Larger");StartButton(h,l,"Larger");Assert.Equal(1.5f,SavedScale());}
        var next=Launcher();using var again=Host();again.Frame(next.Draw);Assert.Equal(1.5f,Scale);
    }

    // Source: KeepTextSize "not remembering is never fatal."
    [Fact] public void Settings_UnwritableDestinationKeepsTheUiRunning()
    {
        var l=Launcher();Directory.CreateDirectory(l.SettingsFile);using var h=Host();
        h.Frames(l.Draw,3);StartButton(h,l,"Larger");Assert.Equal(1.25f,Scale);
        Assert.True(Directory.Exists(l.SettingsFile));Assert.Null(l.Workspace);
    }

    // Source: BrutalCredit.Copyright "The credit line the licence requires, word for word."
    [Fact] public void Credit_IsTheExactRequiredSentence()
    { Assert.Equal(Credit,BrutalCredit.Copyright); }

    // Source: BrutalCredit.Notice "BRUTAL's NOTICE.txt, as shipped beside the program."
    [Fact] public void Credit_EmbeddedNoticeMatchesTheDistributedRootNotice()
    {
        Assert.Equal(File.ReadAllText(Path.Combine(Repository(),"NOTICE.txt")).Replace("\r\n","\n"),
            BrutalCredit.Notice.Replace("\r\n","\n"));
        Assert.Contains(Credit,BrutalCredit.Notice);
    }

    // Source: THIRD-PARTY-NOTICES "on the app's start screen."
    [Theory][InlineData(.75f)][InlineData(1f)][InlineData(2f)]
    public void Credit_StartScreenSubmitsTheWholeSentenceAtEachSize(float scale)
    {
        var l=Launcher();using var h=Host(1600,1000);h.Frames(l.Draw,3);Set(scale);h.Frames(l.Draw,3);
        var text=Log(h,l.Draw);
        Assert.Contains(Credit,text.Replace("\r\n","\n").Replace("\n"," "));
    }

    // Source: THIRD-PARTY-NOTICES "in each program's --help."
    [Theory][InlineData("--help")][InlineData("-h")][InlineData("/?")]
    public void Credit_LauncherHelpHasExactSentenceAndNoticeLocation(string option)
    {
        var output=new StringWriter();var errors=new StringWriter();
        Assert.Equal(0,App.Run([option],output,errors,Settings));
        Assert.Contains(Credit+" See NOTICE.txt.",output.ToString());Assert.Equal("",errors.ToString());
        Assert.False(Directory.Exists(Settings));
    }

    // Source: THIRD-PARTY-NOTICES "in each program's --help"; BRUTAL 2.1 "included in the Credits screen or equivalent."
    [Theory][InlineData("--help")][InlineData("-h")]
    public void Credit_CheckersHelpUsesTheExactRequiredSentence(string option)
    {
        var output=new StringWriter();var errors=new StringWriter();
        Assert.Equal(0,CheckersPlay.Program.Run([option],output,errors));
        Assert.Contains(Credit+" See NOTICE.txt.",output.ToString());Assert.Equal("",errors.ToString());
    }

    // Source: THIRD-PARTY-NOTICES "in each program's --help"; CLI Run "2 bad arguments."
    [Fact] public void Credit_AgentUsageIncludesTheExactSentenceAndNoticeLocation()
    {
        var output=new StringWriter();var errors=new StringWriter();
        Assert.Equal(2,Bed.Agent.Cli.Program.Run([],TextReader.Null,output,errors));
        Assert.Contains(Credit+" See NOTICE.txt.",errors.ToString());Assert.Equal("",output.ToString());
    }

    // Source: BrutalCredit.LogoSize "The logo's size in pixels."
    [Fact] public void Logo_SizeMatchesTheSuppliedPngHeader()
    {
        var png=ReadLogo();Assert.Equal((png.W,png.H),BrutalCredit.LogoSize);
        Assert.Contains(png.Pixels,p=>(p>>24)>0);Assert.Contains(png.Pixels,p=>(p>>24)==0);
    }

    // Source: license 2.2 "The logo shall be shown unaltered except for scaling."
    [Theory][InlineData(1)][InlineData(2)]
    public void Logo_EveryRenderedPixelMatchesTheSourceAtIntegerScale(int scale)
    {
        var png=ReadLogo();var x0=19;var y0=23;
        using var h=Host(png.W*scale+40,png.H*scale+50);h.Drawing=true;h.Background=0xff000000;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0));ImGui.SetNextWindowSize(new float2(h.Width,h.Height));
            ImGui.PushStyleColor(ImGuiCol.WindowBg,new float4(0,0,0,1));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize,0f);
            ImGui.Begin("logo oracle"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoMove);
            BrutalCredit.DrawLogo(new float2(x0,y0),scale);ImGui.End();ImGui.PopStyleVar();ImGui.PopStyleColor();
        }
        h.Frames(Draw,3);int mismatches=0;string first="";
        for(int y=0;y<h.Height;y++)for(int x=0;x<h.Width;x++)
        {
            var inside=x>=x0&&y>=y0&&x<x0+png.W*scale&&y<y0+png.H*scale;
            uint p=inside?png.Pixels[((y-y0)/scale)*png.W+(x-x0)/scale]:0;
            var actual=h.Pixels[y*h.Width+x];
            if(!CompositeMatches(p,actual,1))
            {mismatches++;if(first.Length==0)first=$"({x},{y}): source {p:X8}, rendered {actual:X8}";}
        }
        Assert.True(mismatches==0,$"{mismatches} altered logo/background pixels at {scale}x; first {first}");
    }

    // Source: DrawLogo "at alpha opacity (for a fade)."
    [Fact] public void Logo_ZeroOpacityLeavesTheBlackBackgroundUntouched()
    {
        using var h=Host(900,300);h.Drawing=true;h.Background=0xff000000;
        void Draw(){ImGui.SetNextWindowPos(new float2(0,0));ImGui.SetNextWindowSize(new float2(900,300));
            ImGui.PushStyleColor(ImGuiCol.WindowBg,new float4(0,0,0,1));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize,0f);
            ImGui.Begin("zero logo"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            BrutalCredit.DrawLogo(new float2(40,40),1,0);ImGui.End();ImGui.PopStyleVar();ImGui.PopStyleColor();}
        h.Frames(Draw,3);Assert.All(h.Pixels,p=>Assert.Equal(0xff000000u,p));
    }

    // Source: BrutalSplash "over 2.5 seconds ... after the first second."
    [Fact] public void Splash_ContractTimingAndInitialState()
    {Assert.Equal(2.5,BrutalSplash.Seconds);Assert.Equal(1,BrutalSplash.SkippableAfter);Assert.False(new BrutalSplash().Done);}

    private static void At(HeadlessHost h,BrutalSplash splash,float delta)
    {ImGui.GetIO().DeltaTime=delta;h.Frame(()=>splash.Draw());}

    // Source: BrutalSplash.Draw "Draws a frame of the splash, filling the window."
    [Fact] public void Splash_InitialFrameIsCompletelyBlack()
    {
        using var h=Host(800,400);h.Drawing=true;var s=new BrutalSplash();At(h,s,1f/60);
        Assert.All(h.Pixels,p=>Assert.Equal(0xff000000u,p));Assert.False(s.Done);
    }

    // Source: BrutalSplash "on black, centred, legible."
    [Fact] public void Splash_LogoIsProminentCentredAndSurroundedByBlack()
    {
        using var h=Host(1424,600);h.Drawing=true;var s=new BrutalSplash();At(h,s,1f/60);At(h,s,.5f);
        var white=h.Pixels.Select((p,i)=>(p,i)).Where(v=>(v.p&0xffffff)>0).Select(v=>(X:v.i%h.Width,Y:v.i/h.Width)).ToArray();
        Assert.True(white.Length>5000);Assert.InRange((white.Min(v=>v.X)+white.Max(v=>v.X))/2f,700,724);
        Assert.InRange((white.Min(v=>v.Y)+white.Max(v=>v.Y))/2f,288,312);
        Assert.Equal(0xff000000u,h.Pixels[0]);Assert.Equal(0xff000000u,h.Pixels[^1]);
    }

    // Source: BrutalSplash "skippable by a click or a key after the first second."
    [Theory][InlineData(ImGuiKey.Escape)][InlineData(ImGuiKey.Enter)][InlineData(ImGuiKey.Space)]
    public void Splash_EarlySupportedKeysCannotSkip(ImGuiKey key)
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);
        h.Press(()=>s.Draw(),key);Assert.False(s.Done);
    }

    // Source: BrutalSplash "skippable by a click ... after the first second."
    [Fact] public void Splash_EarlyClickCannotSkip()
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);
        h.Click(()=>s.Draw(),100,100);Assert.False(s.Done);
    }

    // Source: BrutalSplash "skippable by ... a key after the first second."
    [Theory][InlineData(ImGuiKey.Escape)][InlineData(ImGuiKey.Enter)][InlineData(ImGuiKey.Space)]
    public void Splash_SupportedKeysSkipAfterOneSecond(ImGuiKey key)
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);At(h,s,1.01f);
        ImGui.GetIO().DeltaTime=.01f;h.Press(()=>s.Draw(),key);Assert.True(s.Done);
    }

    // Source: BrutalSplash "skippable by a click ... after the first second."
    [Fact] public void Splash_ClickSkipsAfterOneSecond()
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);At(h,s,1.01f);
        ImGui.GetIO().DeltaTime=.01f;h.Click(()=>s.Draw(),100,100);Assert.True(s.Done);
    }

    // Source: BrutalSplash "skippable ... after the first second."
    [Fact] public void Splash_KeyOneMillisecondBeforeTheBoundaryStillCannotSkip()
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);At(h,s,.989f);
        ImGui.GetIO().DeltaTime=.01f;ImGui.GetIO().AddKeyEvent(ImGuiKey.Space,true);h.Frame(()=>s.Draw());
        Assert.False(s.Done);
    }

    // Source: BrutalSplash.Seconds "How long it shows, in seconds."
    [Fact] public void Splash_CompletesNaturallyAfterItsDuration()
    {
        using var h=Host();var s=new BrutalSplash();At(h,s,.01f);At(h,s,2.49f);Assert.False(s.Done);
        At(h,s,.02f);Assert.True(s.Done);
        bool? returned=null;h.Frame(()=>returned=s.Draw());Assert.False(returned);
    }

    // Source: BrutalSplash.Skip "Ends it now (for a headless picture of what comes after)."
    [Fact] public void Splash_ExplicitHeadlessSkipIsImmediateAndPermanent()
    {
        using var h=Host();var s=new BrutalSplash();s.Skip();Assert.True(s.Done);
        bool? result=null;h.Frame(()=>result=s.Draw());Assert.False(result);
        s.Skip();Assert.True(s.Done);
    }

    // Source: ProjectLauncher.Draw "Draws the start screen, or the open project's workspace"; splash before it.
    [Fact] public void Splash_StartScreenWaitsUntilSplashFinishes()
    {
        var l=Launcher();l.Splash=new BrutalSplash();using var h=Host();
        h.Frame(l.Draw);Assert.DoesNotContain("Text size",Log(h,l.Draw));Assert.False(l.Splash.Done);
        l.Splash.Skip();Assert.Contains("Open recent",Log(h,l.Draw));StartButton(h,l,"Preferences");Assert.Contains("Text size",Log(h,l.Draw));Assert.Contains(Credit,Log(h,l.Draw));
    }

    // Source: BRUTAL 2.3 "The NOTICE.txt file shall be distributed alongside all copies of the Product."
    [Theory]
    [InlineData("src/Bed.Launcher/Bed.Launcher.csproj","Bed.Launcher")]
    [InlineData("src/Bed.Agent.Cli/Bed.Agent.Cli.csproj","Bed.Agent.Cli")]
    [InlineData("samples/Checkers.Game/Checkers.Game.csproj","play-checkers")]
    [InlineData("samples/Checkers.App/Checkers.App.csproj","Checkers.App")]
    public void Shipping_EachBrutalProgramBuildHasTheExactNoticeBesideIt(string project,string assembly)
    {
        var folder=OutputFolder(project,assembly);
        var path=Path.Combine(folder,"NOTICE.txt");
        Assert.True(File.Exists(path),project+": NOTICE.txt absent beside "+assembly);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repository(),"NOTICE.txt")),File.ReadAllBytes(path));
    }

    // Source: Notices.targets "every referenced Ahwoo.BRUTAL package's third-party notices ... into licenses/."
    [Theory]
    [InlineData("src/Bed.Launcher/Bed.Launcher.csproj","Bed.Launcher")]
    [InlineData("src/Bed.Agent.Cli/Bed.Agent.Cli.csproj","Bed.Agent.Cli")]
    [InlineData("samples/Checkers.Game/Checkers.Game.csproj","play-checkers")]
    [InlineData("samples/Checkers.App/Checkers.App.csproj","Checkers.App")]
    public void Shipping_EachBrutalProgramCopiesEveryBundledThirdPartyNotice(string project,string assembly)
    {
        var folder=OutputFolder(project,assembly);
        var assets=FindAssets(project);using var json=JsonDocument.Parse(File.ReadAllText(assets));
        var packageFolders=json.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p=>p.Name).ToArray();
        var expected=new HashSet<string>(StringComparer.Ordinal);
        foreach(var package in json.RootElement.GetProperty("libraries").EnumerateObject().Where(p=>p.Name.StartsWith("Ahwoo.Brutal.",StringComparison.OrdinalIgnoreCase)))
        {
            var relative=package.Value.GetProperty("path").GetString()!;
            var source=packageFolders.Select(p=>Path.Combine(p,relative,"LICENSES")).FirstOrDefault(Directory.Exists);
            if(source is null)continue; // Some BRUTAL packages redistribute no third-party components.
            foreach(var notice in Directory.GetFiles(source,"*.txt"))
            {
                expected.Add(Path.GetFileName(notice));var shipped=Path.Combine(folder,"licenses",Path.GetFileName(notice));
                Assert.True(File.Exists(shipped),project+" lacks "+Path.GetFileName(notice)+" from "+package.Name);
                Assert.Equal(File.ReadAllBytes(notice),File.ReadAllBytes(shipped));
            }
        }
        // A program that ships a bundled sample game (the launcher carries the physics sample and its native library) also lists
        // that sample's package notices at its top level, so the release's licence folder covers everything in the zip.
        var bundled=Path.Combine(folder,"samples","PhysicsStarter","licenses");
        if(Directory.Exists(bundled))
            foreach(var notice in Directory.GetFiles(bundled,"BRUTAL-*.txt"))
            {
                expected.Add(Path.GetFileName(notice));var shipped=Path.Combine(folder,"licenses",Path.GetFileName(notice));
                Assert.True(File.Exists(shipped),project+" lacks the bundled sample's "+Path.GetFileName(notice));
                Assert.Equal(File.ReadAllBytes(notice),File.ReadAllBytes(shipped));
            }
        // The CLI has no BRUTAL packages in its resolved dependency graph: zero bundled files is correct there.
        var directory=Path.Combine(folder,"licenses");
        // Package notices can have upstream filenames, not just the old BRUTAL-prefixed names.
        // Exclude only the exact non-package licences staged separately by Notices.targets.
        var shippedNames=Directory.Exists(directory)?Directory.GetFiles(directory,"*.txt").Select(Path.GetFileName).OfType<string>()
            .Where(name=>name is not ("NVorbis-LICENSE.txt" or "DOTNET-RUNTIME-LICENSE.txt" or "DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt")).ToArray():[];
        Assert.Equal(expected.Order(StringComparer.Ordinal),shippedNames.Order(StringComparer.Ordinal));
    }

    // Source: package_release.py REQUIRED "What a release holds ... checked after packing."
    [Fact] public void Shipping_ReleaseRequiredListIncludesNoticeAndDependencyLicences()
    {
        var text=File.ReadAllText(Path.Combine(Repository(),"tools","package_release.py"));
        var list=text.Split("REQUIRED = [",2)[1].Split(']',2)[0];
        foreach(var expected in new[]{"NOTICE.txt","licenses/BRUTAL-IMGUI-THIRD-PARTY-NOTICES.txt","licenses/DOTNET-RUNTIME-LICENSE.txt"})
            Assert.Contains("\""+expected+"\"",list);
    }

    // Source: Notices.targets "for a self-contained publish, the .NET runtime's licence and notices."
    [Fact] public void Shipping_SelfContainedTargetCarriesBothRuntimeLegalFiles()
    {
        var xml=XDocument.Load(Path.Combine(Repository(),"build","Notices.targets"));
        var target=Assert.Single(xml.Descendants("Target"),e=>(string?)e.Attribute("Name")=="IncludeDotnetRuntimeNotices");
        Assert.Contains("SelfContained",(string?)target.Attribute("Condition")??"");
        var published=target.Descendants("ResolvedFileToPublish").Select(e=>(string?)e.Attribute("RelativePath")).ToArray();
        Assert.Contains(@"licenses\DOTNET-RUNTIME-LICENSE.txt",published);
        Assert.Contains(@"licenses\DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt",published);
    }

    // Source: Program.Run "--settings wins over the folder the caller passes."
    [Fact] public void Cli_SettingsOverrideKeepsTheSimulatedPersonalFolderByteIdentical()
    {
        var personal=Path.Combine(root,"simulated personal settings");Directory.CreateDirectory(personal);
        File.WriteAllText(Path.Combine(personal,"recent.json"),"""[{"Data":"private sentinel","Schemas":"private sentinel"}]""");
        File.WriteAllText(Path.Combine(personal,"settings.json"),"""{"textScale":0.75}""");
        var before=Snapshot(personal);var output=new StringWriter();var errors=new StringWriter();
        Assert.Equal(0,App.Run([Project,"--settings",Settings,"--shot",Path.Combine(root,"trial.png")],output,errors,personal));
        Assert.Equal(before,Snapshot(personal));Assert.Equal("",errors.ToString());
        Assert.Single(new RecentProjects(Path.Combine(Settings,"recent.json")).Items);
        Assert.True(File.ReadAllBytes(Path.Combine(root,"trial.png")).AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}));
        Assert.Contains("Opened invented project",output.ToString());
    }

    // Source: --settings "keeps the recent-projects list in that folder instead of your own settings."
    [Fact] public void Cli_StartScreenWithOverrideNeverCreatesTheFallbackFolder()
    {
        var fallback=Path.Combine(root,"do not create");var shot=Path.Combine(root,"start.png");
        Assert.Equal(0,App.Run(["--settings",Settings,"--shot",shot],TextWriter.Null,TextWriter.Null,fallback));
        Assert.False(Directory.Exists(fallback));Assert.True(File.Exists(shot));
    }

    // Source: --settings "for tests and trials"; ProjectLauncher remembers text size beside the recent list.
    [Fact] public void Cli_OverrideLoadsItsOwnTextSizeWithoutChangingOtherSettings()
    {
        Directory.CreateDirectory(Settings);File.WriteAllText(Path.Combine(Settings,"settings.json"),"""{"textScale":1.75}""");
        var before=Snapshot(Settings);var fallback=Path.Combine(root,"unused");var shot=Path.Combine(root,"sized.png");
        Assert.Equal(0,App.Run(["--settings",Settings,"--shot",shot],TextWriter.Null,TextWriter.Null,fallback));
        Assert.Equal(before,Snapshot(Settings));Assert.False(Directory.Exists(fallback));
        var smaller=Path.Combine(root,"small setting");Directory.CreateDirectory(smaller);
        File.WriteAllText(Path.Combine(smaller,"settings.json"),"""{"textScale":0.75}""");
        var smallBefore=Snapshot(smaller);var smallShot=Path.Combine(root,"smaller.png");
        Assert.Equal(0,App.Run(["--settings",smaller,"--shot",smallShot],TextWriter.Null,TextWriter.Null,fallback));
        Assert.Equal(smallBefore,Snapshot(smaller));
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(shot))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(smallShot))));
        Assert.False(Directory.Exists(fallback));
    }

    // Source: Program.Run "Exit codes: ... 2 bad arguments."
    [Fact] public void Cli_MissingSettingsValueIsRejectedWithoutWritingAnything()
    {
        var before=Snapshot(root);var errors=new StringWriter();
        Assert.Equal(2,App.Run(["--settings"],TextWriter.Null,errors,Settings));
        Assert.Contains("usage:",errors.ToString());Assert.Equal(before,Snapshot(root));
    }

    // Source: Program.Run "Exit codes: ... 1 the project couldn't open (with why)."
    [Fact] public void Cli_FailedProjectWithSettingsOverrideDoesNotTouchFallback()
    {
        var fallback=Path.Combine(root,"fallback");Directory.CreateDirectory(fallback);
        File.WriteAllText(Path.Combine(fallback,"sentinel.txt"),"self-authored settings sentinel");var before=Snapshot(fallback);
        var errors=new StringWriter();var shot=Path.Combine(root,"must-not-exist.png");
        Assert.Equal(1,App.Run([root,"--settings",Settings,"--shot",shot],TextWriter.Null,errors,fallback));
        Assert.Equal(before,Snapshot(fallback));Assert.False(File.Exists(shot));
        Assert.Contains("doesn't hold a data folder",errors.ToString());
    }

    // Source: --settings "keeps ... in that folder instead of your own settings."
    [Fact] public void Cli_SeparateTrialFoldersKeepIndependentRecentLists()
    {
        var a=Path.Combine(root,"trial a");var b=Path.Combine(root,"trial b");var fallback=Path.Combine(root,"unused");
        Assert.Equal(0,App.Run([Project,"--settings",a,"--shot",Path.Combine(root,"a.png")],TextWriter.Null,TextWriter.Null,fallback));
        var first=Snapshot(a);
        Assert.Equal(0,App.Run(["--settings",b,"--shot",Path.Combine(root,"b.png")],TextWriter.Null,TextWriter.Null,fallback));
        Assert.Equal(first,Snapshot(a));Assert.False(File.Exists(Path.Combine(b,"recent.json")));Assert.False(Directory.Exists(fallback));
    }

    private static string[] Snapshot(string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p=>Path.GetRelativePath(folder,p)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray() : [];

    private static string GateOrRepository()
    {
        var repo=Repository();var parent=Path.GetDirectoryName(repo)!;
        return Path.GetFileName(parent)==".gate"?parent:repo;
    }

    private static string OutputFolder(string project,string assembly)
    {
        var repo=Repository();var name=Path.GetFileNameWithoutExtension(project);var search=GateOrRepository();
        string[] Outputs() => Directory.GetFiles(search,assembly+".runtimeconfig.json",SearchOption.AllDirectories)
            .Where(p=>!p.Split(Path.DirectorySeparatorChar).Contains("obj") && !p.Contains(Path.DirectorySeparatorChar+"ref"+Path.DirectorySeparatorChar))
            .Where(p=>p.Contains(Path.DirectorySeparatorChar+name+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                ||p.StartsWith(Path.Combine(repo,Path.GetDirectoryName(project.Replace('/',Path.DirectorySeparatorChar))!,"bin"),StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        var files=Outputs();
        if(files.Length==0)
        {
            // dotnet test builds the tests' references, not every executable in the solution. Build the missing
            // existing program in the SAME gate copy, inheriting its isolated SDK/restore/output environment.
            var start=new ProcessStartInfo("dotnet"){WorkingDirectory=repo,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"build",Path.Combine(repo,project.Replace('/',Path.DirectorySeparatorChar)),"--no-restore","--nologo","-v:q","-nodeReuse:false","-p:UseSharedCompilation=false"})
                start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(120000)){process.Kill(entireProcessTree:true);process.WaitForExit();Assert.Fail("Timed out building "+project);}
            // MSBuild worker nodes can inherit the pipes; -nodeReuse:false stops that, and the wait is bounded anyway.
            Assert.True(Task.WaitAll(new Task[]{stdout,stderr},30000),"Build output never closed for "+project);
            Assert.True(process.ExitCode==0,"Program build failed: "+stdout.Result+"\n"+stderr.Result);
            files=Outputs();
        }
        Assert.True(files.Length>0,"No built program output found for "+project);
        return Path.GetDirectoryName(files[0])!;
    }

    private static string FindAssets(string project)
    {
        var name=Path.GetFileNameWithoutExtension(project);
        // The project's own restore first: a search of the whole tree also finds the copies in Codex's outbox gates.
        var own=Path.Combine(GateOrRepository(),Path.GetDirectoryName(project.Replace('/',Path.DirectorySeparatorChar))!,"obj","project.assets.json");
        if(File.Exists(own))return own;
        var candidates=Directory.GetFiles(GateOrRepository(),"project.assets.json",SearchOption.AllDirectories)
            .Where(p=>p.Contains(Path.DirectorySeparatorChar+name+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            .Where(p=>!p.Contains(Path.DirectorySeparatorChar+"codex"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Single(candidates);
        return candidates[0];
    }

    private sealed record Logo(int W,int H,uint[] Pixels);

    // Independent decoder, BORROWED algorithm: PNG standard row predictors. No product Decode/DrawLogo is called here.
    private static Logo ReadLogo()
    {
        var data=File.ReadAllBytes(Path.Combine(Repository(),"src","Bed.Editor.ImGui","Branding","LOGO_BRUTAL_WHT.png"));
        Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},data[..8]);
        int w=0,h=0;using var compressed=new MemoryStream();
        for(int at=8;at<data.Length;)
        {
            int size=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at,4));
            var kind=System.Text.Encoding.ASCII.GetString(data,at+4,4);
            if(kind=="IHDR"){w=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at+8,4));h=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at+12,4));
                Assert.Equal(8,data[at+16]);Assert.Equal(6,data[at+17]);Assert.Equal(0,data[at+20]);}
            if(kind=="IDAT")compressed.Write(data,at+8,size);
            at+=size+12;
        }
        compressed.Position=0;using var z=new ZLibStream(compressed,CompressionMode.Decompress);using var decoded=new MemoryStream();z.CopyTo(decoded);
        var bytes=decoded.ToArray();var stride=w*4;Assert.Equal(h*(stride+1),bytes.Length);
        var rows=new byte[h*stride];var pixels=new uint[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<stride;x++)
        {
            int f=bytes[y*(stride+1)],left=x<4?0:rows[y*stride+x-4],up=y==0?0:rows[(y-1)*stride+x],ul=y==0||x<4?0:rows[(y-1)*stride+x-4];
            int predictor=f switch{0=>0,1=>left,2=>up,3=>(left+up)/2,4=>Predict(left,up,ul),_=>throw new InvalidDataException("Unknown PNG filter")};
            rows[y*stride+x]=unchecked((byte)(bytes[y*(stride+1)+1+x]+predictor));
        }
        for(int i=0;i<pixels.Length;i++)pixels[i]=BinaryPrimitives.ReadUInt32LittleEndian(rows.AsSpan(i*4,4));
        return new(w,h,pixels);
    }

    private static int Predict(int a,int b,int c)
    {
        var estimate=a+b-c;var choices=new[]{(Value:a,Distance:Math.Abs(estimate-a)),(Value:b,Distance:Math.Abs(estimate-b)),(Value:c,Distance:Math.Abs(estimate-c))};
        return choices.OrderBy(v=>v.Distance).First().Value;
    }

    private static bool CompositeMatches(uint source,uint rendered,int tolerance)
    {
        var alpha=(source>>24)/255d;
        for(int shift=0;shift<24;shift+=8)
        {
            var expected=(int)Math.Round(((source>>shift)&255)*alpha,MidpointRounding.AwayFromZero);
            if(Math.Abs(expected-(int)((rendered>>shift)&255))>tolerance)return false;
        }
        return (rendered>>24)==255;
    }
}
