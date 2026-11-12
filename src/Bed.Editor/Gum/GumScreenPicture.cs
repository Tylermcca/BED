using System.Diagnostics;
using Bed.Editor.Gum;
using Gum.DataTypes;
using Gum.DataTypes.Variables;
using Gum.Expressions;
using Gum.Forms;
using Gum.Localization;
using Gum.Managers;
using Gum.Wireframe;
using GumRuntime;
using RenderingLibrary;
using SkiaSharp;
using ToolsUtilities;
using GumAnimationLoader = Gum.GumAnimationLoader;
using GumHotReloadManager = Gum.GumHotReloadManager;
using GumService = Gum.GumService;
using LoaderManager = RenderingLibrary.Content.LoaderManager;
using HotReloadResult = Gum.HotReloadResult;
using ProjectTextureFilter = Gum.ProjectTextureFilter;
using Renderer = RenderingLibrary.Graphics.Renderer;

namespace Bed.Editor.Gum;

// GUM-PICTURE: a live picture of one Gum screen, drawn by Gum itself in BED's own process. BORROWED: the route of Gum's
// "SkiaSharp general canvas" and "rendering to an image" pages (a raster SKSurface, GumService.Default.Initialize on its
// canvas, the screen added to Gum's root, Clear, Update, Draw, read the pixels), and the careful steps of Gum's own
// SkiaGumSvgExportService (missing pictures don't throw, the project's default canvas size for a screen, Gum's missing-file
// setting put back afterwards). Live updates are Gum's own hot reload (GumHotReloadManager), started with the same three
// steps GumServiceSkiaBase.EnableHotReload gives it (GumServiceSkiaBase.cs 365-377 at Gum commit 927cd9e) and driven with
// Gum's root each frame as GumService.Update does. MIXED: BED starts it itself, not through EnableHotReload, so the step Gum
// runs on a newly read project before applying it (the texture filter) can refuse one with a loop of parts: Gum preview
// 5's reload follows such a loop until the program stops, and refusing makes Gum keep the project it had.
// MIXED: for a project with a missing screen or component file, which Gum's Initialize refuses, the project-loading steps
// of Gum's own Initialize are repeated around Gum's tolerant loader (see Ready). INVENTED: re-initialising after another
// Gum call (the Gum check, the screens list) has changed Gum's shared state.

/// <summary>One look of a Gum screen: a Gum state, in a category or not.</summary>
/// <param name="Category">The state's category, or null for one of the screen's own states.</param>
/// <param name="Name">The state's name.</param>
public sealed record GumLook(string? Category, string Name)
{
    /// <summary>How the look is shown: "Category: Name", or just the name.</summary>
    public string Label => Category is null ? Name : $"{Category}: {Name}";

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>One part of a shown Gum screen (see <see cref="GumScreenPicture.Parts"/>).</summary>
/// <param name="Name">The part's name in Gum.</param>
/// <param name="X">Its left edge in the picture's pixels.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Words">Its text, or the first text inside it; null when it has none.</param>
public sealed record GumPart(string Name, float X, float Y, float Width, float Height, string? Words);

/// <summary>A live picture of one screen of a Gum project, drawn by Gum onto a CPU canvas. Call <see cref="Frame"/> once
/// per editor frame; it updates Gum (so animations play and saved changes load), draws, and keeps the pixels in
/// <see cref="Pixels"/>. All calls run under Gum's one lock. Never writes a file, and never throws for what is in the
/// project: <see cref="Problem"/> says what went wrong in plain words.</summary>
public sealed class GumScreenPicture : IDisposable
{
    /// <summary>The size used when the project gives none.</summary>
    public const int FallbackWidth = 800, FallbackHeight = 600;

    private static GumScreenPicture? owner;   // the picture whose screen is in GumService.Default; guarded by GumLibrary's lock
    private static GumHotReloadManager? reloader;   // the owner's live updates, stopped when Gum is released
    private static string? relativeBefore;   // Gum's working folder before a tolerant load pointed it at the project
    private (int Width, int Height)? fixedSize;
    private float zoom = 1;
    private SKSurface? surface;
    private byte[] pixels = [];
    private long lastUse = -1;
    private bool fallback, reloadAfterChange;
    private string? screen;
    private GumLook? look;
    private GraphicalUiElement? shown;   // the screen as Gum built it, while Drawn
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);   // what a game set, kept across reloads
    private bool disposed;

    /// <summary>A picture of a screen in <paramref name="projectFile"/>. Nothing is read until <see cref="Show"/>.</summary>
    /// <param name="projectFile">The Gum project (<c>.gumx</c> or <c>.gumj</c>).</param>
    /// <param name="width">A size to draw at instead of the project's own screen size.</param>
    /// <param name="height">See <paramref name="width"/>.</param>
    public GumScreenPicture(string projectFile, int? width = null, int? height = null)
    {
        ProjectFile = Path.GetFullPath(projectFile);
        if (width is { } w && height is { } h) fixedSize = (Math.Clamp(w, 1, 8192), Math.Clamp(h, 1, 8192));
    }

    /// <summary>The Gum project file.</summary>
    public string ProjectFile { get; }

    /// <summary>The screen being shown, or null before <see cref="Show"/>.</summary>
    public string? Screen => screen;

    /// <summary>The chosen look of the screen, or null for its default look.</summary>
    public GumLook? Look => look;

    /// <summary>The screen's other looks, in Gum's order: its own states other than Default, then each category's.</summary>
    public IReadOnlyList<GumLook> Looks { get; private set; } = [];

    /// <summary>The picture's width and height in pixels: the project's screen size unless one was given.</summary>
    public int Width { get; private set; }

    /// <summary>See <see cref="Width"/>.</summary>
    public int Height { get; private set; }

    /// <summary>The last drawn picture: <see cref="Width"/> x <see cref="Height"/> pixels, RGBA, 8 bits each, top row
    /// first, fully opaque. Empty until the first <see cref="Frame"/> that drew.</summary>
    public ReadOnlySpan<byte> Pixels => pixels;

    /// <summary>Counts the frames whose picture differed from the one before, so a viewer re-uploads only on a change.</summary>
    public long Version { get; private set; }

    /// <summary>What's wrong with the picture, in plain words: which files are missing, or why nothing shows and what to
    /// try. Empty when all is well.</summary>
    public string Problem { get; private set; } = "";

    /// <summary>Gum's own words for the last failure, for a problem report; never shown as it is.</summary>
    public string? GumError { get; private set; }

    /// <summary>Whether the picture shows the screen (true) or nothing could be drawn.</summary>
    public bool Drawn { get; private set; }

    /// <summary>The background behind the screen, where Gum draws nothing: red, green, blue.</summary>
    public (byte Red, byte Green, byte Blue) Background { get; set; } = (0x30, 0x30, 0x30);

    /// <summary>How long the last <see cref="Frame"/>'s update and draw took, without reading the pixels back.</summary>
    public TimeSpan LastDrawTime { get; private set; }

    /// <summary>How much bigger than designed the screen is drawn: 2 draws everything twice the size, so the screen has
    /// half as much room in the picture's pixels to lay itself out in (Gum's own zoom, as Gum's EnableExpandToWindow
    /// does). A game passes its text size, so a Gum screen follows it like the rest of the game's text.</summary>
    public float Zoom => zoom;

    /// <summary>Draws at <paramref name="width"/> x <paramref name="height"/> pixels and <paramref name="newZoom"/> from the
    /// next frame on. Nothing happens when they are what they were; otherwise the screen is laid out again for its new room.</summary>
    public void Resize(int width, int height, float newZoom = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var size = (Math.Clamp(width, 1, 8192), Math.Clamp(height, 1, 8192));
        newZoom = Math.Clamp(newZoom, 0.25f, 8f);
        if (fixedSize == size && zoom == newZoom) return;
        fixedSize = size; zoom = newZoom;
        if (screen is not null) reloadAfterChange = true;
    }

    /// <summary>Shows <paramref name="screenName"/> in <paramref name="chosenLook"/> (null for its default look). Returns
    /// false, with <see cref="Problem"/> saying why, when the screen can't be shown; the picture then shows nothing.</summary>
    public bool Show(string screenName, GumLook? chosenLook = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        screen = screenName; look = chosenLook;
        return GumLibrary.Run(() => Quietly(() => { Load(); lastUse = GumLibrary.Uses; return Drawn; }));
    }

    /// <summary>Updates Gum by the time since the editor started (so animations play, and a screen saved in Gum reloads
    /// after a short pause), draws, and keeps the pixels. Returns true when the picture changed.</summary>
    /// <param name="totalSeconds">Seconds since the editor started.</param>
    public bool Frame(double totalSeconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (screen is null) return false;
        return GumLibrary.Run(() => Quietly(() =>
        {
            // Gum's state is shared: another Gum call since ours (the check, the screens list) or another picture means
            // Gum no longer holds this screen, so load it again. So does a save Gum's own reload can't patch in.
            if (!ReferenceEquals(owner, this) || GumLibrary.Uses != lastUse + 1 || reloadAfterChange) Load();
            lastUse = GumLibrary.Uses;
            if (Drawn) return Draw(totalSeconds);
            // Nothing to show yet: still let Gum's watcher report a save, so a screen whose file comes back appears.
            if (ReferenceEquals(owner, this) && GumService.Default.IsInitialized)
                Guard(() => { Live(GumService.Default); GumService.Default.Update(totalSeconds); }, "Gum couldn't check the project's folder for saved changes.");
            return false;
        }));
    }

    // Gum's Initialize sets its missing-file setting to throw (Gum issue #6889). Like Gum's own export, BED draws the rest
    // of a screen whose picture is missing, and puts Gum's setting back afterwards.
    private static T Quietly<T>(Func<T> work)
    {
        var before = GraphicalUiElement.MissingFileBehavior;
        try { GraphicalUiElement.MissingFileBehavior = MissingFileBehavior.ConsumeSilently; return work(); }
        finally { GraphicalUiElement.MissingFileBehavior = before; }
    }

    // Runs one Gum step; a failure becomes Problem (plain words) and GumError (Gum's words), never an exception.
    private bool Guard(Action step, string problem)
    {
        try { step(); return true; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (problem.Length > 0) Problem = problem;
            GumError = e.ToString();
            Trace.TraceWarning("Gum screen picture: " + problem + Environment.NewLine + e);
            return false;
        }
    }

    private void Load()
    {
        Drawn = false; shown = null; reloadAfterChange = false; Looks = []; GumError = null; Problem = "";
        var name = Path.GetFileNameWithoutExtension(ProjectFile);
        var service = GumService.Default;
        if (!Guard(() => Release(service), $"BED couldn't take down the last Gum picture, so the screen {screen} can't be shown. Close the panel and open it again."))
            return;
        owner = this;

        var loaded = GumLibrary.Load(ProjectFile);
        if (loaded.Project is not { } project) { Problem = $"BED couldn't show the Gum project {name}, because {GumCatalog.Why(ProjectFile, loaded.ErrorMessage)}. Open it in Gum to check it."; return; }
        if (GumLibrary.Unsupported(project)) { Problem = $"The Gum project {name} was saved by a newer Gum than BED can show. Open it in Gum to see it."; return; }
        var (width, height) = fixedSize ?? (project.DefaultCanvasWidth > 0 && project.DefaultCanvasHeight > 0
            ? (Math.Min(project.DefaultCanvasWidth, 8192), Math.Min(project.DefaultCanvasHeight, 8192)) : (FallbackWidth, FallbackHeight));
        if (surface is null || Width != width || Height != height)
        {
            surface?.Dispose();
            surface = null;
            // Gum's drawing needs Skia's native library; on a computer where it can't load, say so instead of failing every frame.
            if (!Guard(() => surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)),
                    "BED couldn't start Gum's drawing on this computer, so the screen can't be shown here. Open it in Gum instead."))
                return;
            if (surface is null) { Problem = $"BED couldn't make room for a {width} x {height} picture of the screen {screen}."; return; }
            Width = width; Height = height; pixels = new byte[width * height * 4];
        }
        var folder = Path.GetDirectoryName(ProjectFile)!;
        var missing = project.AllElements.Where(e => e.IsSourceFileMissing)
            .Select(e => GumCatalog.ElementFile(folder, project, e.Name)).OfType<string>().Select(f => GumCatalog.Relative(folder, f)).ToList();
        fallback = missing.Count > 0;
        if (!Guard(() =>
            {
                if (!fallback) service.Initialize(surface.Canvas, width, height, ProjectFile);
                else Ready(service, surface.Canvas, width, height, project, folder);
            }, $"Gum couldn't open the project {name} to show the screen {screen}. Open it in Gum to check it."))
        {
            Guard(() => Release(service), "");
            return;
        }
        // Gum's own zoom: the screen lays itself out in the picture's size divided by the zoom, then is drawn that much bigger.
        // Set on every load: taking Gum down doesn't clear it, so a picture at another zoom would otherwise keep it.
        Guard(() => service.EnableExpandToWindow(zoom), "");
        // Linked values written as sums: Gum's own expression reader, turned on after Initialize as Gum asks (taking Gum down
        // clears it, so every load turns it on again). BORROWED: Gum.Expressions, as gumcli does.
        GumExpressionService.Initialize();
        GraphicalUiElement.MissingFileBehavior = MissingFileBehavior.ConsumeSilently;

        // Gum's own watcher (with its own short pause after a save) notices saved files, and its Update applies them. It
        // starts before the screen is looked for, so a screen whose file comes back is noticed too. A folder BED can't
        // watch costs only the live updates.
        var watching = Guard(() =>
        {
            var live = new GumHotReloadManager(applyProjectTextureFilter: Reading, loadAnimationsFromProvider: GumAnimationLoader.LoadAnimationsFromProvider,
                disposeCachedAsset: path => LoaderManager.Self.Dispose(path))
            { LocalizationServiceProvider = () => global::SkiaGum.CustomSetPropertyOnRenderable.LocalizationService };
            live.ReloadFinished += Reloaded;
            live.Start(ProjectFile);
            reloader = live;
        }, "");

        var gumProject = ObjectFinder.Self.GumProjectSave!;
        // Gum's loader keeps an empty stand-in for a listed screen whose file is missing; that is nothing to show.
        var listedFile = GumCatalog.ElementFile(folder, project, screen);
        var screenMissing = listedFile is not null && !File.Exists(listedFile);
        var element = screenMissing ? null : gumProject.Screens.FirstOrDefault(s => s.Name == screen);
        if (element is null)
        {
            Problem = screenMissing
                ? $"The screen {screen}'s file isn't in the Gum project's folder, so there's nothing to show. Put the file back, or open the project in Gum."
                : $"The Gum project {name} has no screen called {screen}. Choose another screen.";
            return;
        }
        // A loop of parts: Gum builds the screen empty, but its live reload would follow the loop until BED stops.
        if (GumDependencyCheck.ScreenLoop(gumProject, folder, element.Name) is { } loop)
        {
            Problem = $"{loop.Problem.Message} BED doesn't draw a screen with a loop like this. {loop.Next}";
            return;
        }
        Looks = LooksOf(element);
        if (look is not null && !Looks.Contains(look)) look = null;
        if (!Guard(() =>
            {
                var built = element.ToGraphicalUiElement(SystemManagers.Default, addToManagers: false);
                built.AddToRoot();
                shown = built;
                if (look is { Category: { } category }) built.ApplyState(category, look.Name);
                else if (look is not null) built.ApplyState(look.Name);
            }, $"Gum couldn't build the screen {screen}. Open it in Gum to check it, then save it again."))
            return;
        Problem = missing.Count == 0 ? "" : (missing.Count == 1 ? $"This file is missing, so the picture leaves it out: {missing[0]}."
            : $"These files are missing, so the picture leaves them out: {string.Join(", ", missing)}.");
        if (!watching) Problem = (Problem + " BED can't watch the project's folder, so the picture won't follow saves in Gum.").Trim();
        Drawn = true;
        foreach (var (path, value) in values) Apply(path, value);
    }

    // Gum's Initialize refuses a project with a missing file (GumServiceSkiaBase.InitializeCore, loadResult.ThrowIfFailed),
    // and has no way to take a project already loaded. So Gum is started with no project, and then the project from Gum's
    // own tolerant loader gets the same steps Initialize gives a project it loads: GumServiceSkiaBase.cs lines 608-632 at
    // Gum commit 927cd9e (the preview-5 build). ApplyAllVariableReferencesBeforeFirstBuild and ApplyProjectTextureFilter
    // aren't public, so their bodies (ElementSaveExtensions.GumRuntime.cs 404-416, GumServiceSkiaBase.cs 385-390) are used.
    private static void Ready(GumService service, SKCanvas canvas, int width, int height, GumProjectSave project, string folder)
    {
        service.Initialize(canvas, width, height);
        if (global::SkiaGum.CustomSetPropertyOnRenderable.LocalizationService is { } localization)
            ProjectLocalizationLoader.Load(project, localization, null, new List<string>());
        ObjectFinder.Self.GumProjectSave = project;
        project.Initialize(tolerateMissingDefaultStates: true);   // Gum passes nothing; a project with a missing file may lack a default look
        project.ApplyAllVariableReferences();
        TextureFilter(project);
        FormsUtilities.RegisterFromFileFormRuntimeDefaults();
        // Gum's Uninitialize puts the working folder back only for a project it loaded itself; Release does it for this one.
        relativeBefore ??= FileManager.RelativeDirectory;
        FileManager.RelativeDirectory = FileManager.GetDirectory(Path.Combine(folder, "x"));
    }

    // Gum's reload runs this on the project it has just read, before applying it, and on the project it keeps when that
    // fails. Refusing (throwing) makes Gum keep the project it had and report a failed reload, which loads the picture afresh.
    private void Reading(GumProjectSave project)
    {
        TextureFilter(project);
        if (Drawn && screen is not null && GumDependencyCheck.ScreenLoop(project, Path.GetDirectoryName(ProjectFile)!, screen) is not null)
            throw new InvalidOperationException($"The saved project has a loop of parts in the screen {screen}.");
    }

    // GumServiceSkiaBase.ApplyProjectTextureFilter (GumServiceSkiaBase.cs 385-390), which isn't public.
    private static void TextureFilter(GumProjectSave project) =>
        Renderer.TextureFilter = ProjectTextureFilter.UsesLinearFiltering(project) ? SKFilterMode.Linear : SKFilterMode.Nearest;

    /// <summary>Sets a value on the shown screen, as a game does while it runs: <c>"TurnText.Text"</c> for a part, or
    /// <c>"NewGameButton.Label.Text"</c> for a part inside a part. The value is kept and set again whenever the screen is
    /// loaded afresh. Returns false when the screen isn't showing now or has no such part; never throws.</summary>
    /// <param name="path">The part's name (parts inside parts joined by dots), a dot, and the variable's name.</param>
    /// <param name="value">The value, of the variable's own type (a string for Text, a float for X).</param>
    public bool Set(string path, object? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        values[path] = value;
        return Mine(() => Quietly(() => Apply(path, value)));
    }

    /// <summary>The shown screen's own parts that are showing, in Gum's drawing order (later ones on top): each one's
    /// name, where it is in the picture's pixels (after <see cref="Zoom"/>) and its words, so a game can make its buttons
    /// reachable by keyboard and screen readers. Parts the screen puts inside its own containers are listed too, after
    /// their container. Empty when nothing is showing. The parts inside a component (a button's label) aren't listed;
    /// their words are the part's.</summary>
    public IReadOnlyList<GumPart> Parts()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Mine<IReadOnlyList<GumPart>>(() =>
        {
            if (shown is null) return [];
            // By name: Gum's live reload puts a fresh copy of the screen's description in place, so the one the parts
            // were made from may no longer be the one the screen holds.
            var screenName = (shown.Tag as ElementSave)?.Name ?? screen;
            var parts = new List<GumPart>();
            void Walk(GraphicalUiElement parent)
            {
                foreach (var p in parent.Children.OfType<GraphicalUiElement>())
                {
                    if (!p.Visible || p.Name is null || p.Tag is not InstanceSave instance || instance.ParentContainer?.Name != screenName) continue;
                    parts.Add(new GumPart(p.Name, p.AbsoluteLeft * zoom, p.AbsoluteTop * zoom, p.AbsoluteWidth * zoom, p.AbsoluteHeight * zoom, WordsOf(p)));
                    Walk(p);
                }
            }
            Walk(shown);
            return parts;
        }) ?? [];
    }

    // A part's own text, or the first text inside it.
    private static string? WordsOf(GraphicalUiElement part)
    {
        if (part.RenderableComponent is RenderingLibrary.Graphics.IText { RawText: { Length: > 0 } words }) return words;
        foreach (var inner in part.Children.OfType<GraphicalUiElement>())
            if (WordsOf(inner) is { } found) return found;
        return null;
    }

    // Runs work only when this picture's screen is still the one in Gum, and keeps it so: this picture's own calls don't
    // make the next Frame load the screen again. Otherwise the default, and the next Frame loads it.
    private T? Mine<T>(Func<T?> work) => GumLibrary.Run(() =>
    {
        if (!ReferenceEquals(owner, this) || GumLibrary.Uses != lastUse + 1 || !Drawn) return default;
        lastUse = GumLibrary.Uses;
        return work();
    });

    private bool Apply(string path, object? value)
    {
        if (shown is null) return false;
        var dot = path.LastIndexOf('.');
        var part = dot < 0 ? shown : shown.GetGraphicalUiElementByName(path[..dot].Split('.'));
        return part is not null && Guard(() => part.SetProperty(path[(dot + 1)..], value), "");
    }

    /// <summary>How many times a save in Gum has reached this picture, so a screen can say it just updated.</summary>
    public int Saves { get; private set; }

    private void Reloaded(HotReloadResult result)
    {
        Saves++;
        // Gum keeps the screen in step itself. When its patch doesn't match a fresh start, or a file was missing (Gum's
        // reload only patches what it already shows, so a returning file needs a fresh load), BED loads it afresh.
        if (fallback || !Drawn || !result.MatchesRestart) reloadAfterChange = true;
    }

    private static IReadOnlyList<GumLook> LooksOf(ElementSave element)
    {
        var looks = new List<GumLook>();
        foreach (var state in element.States)
            if (!string.IsNullOrEmpty(state.Name) && state.Name != "Default") looks.Add(new GumLook(null, state.Name));
        foreach (StateSaveCategory category in element.Categories)
            foreach (var state in category.States)
                if (!string.IsNullOrEmpty(state.Name)) looks.Add(new GumLook(category.Name, state.Name));
        return looks;
    }

    // Gum's live updates for its root, as GumService.Update gives them.
    private static void Live(GumService service)
    {
        if (reloader is not null && service.Root is { } root) reloader.Update([root]);
    }

    private bool Draw(double totalSeconds)
    {
        var service = GumService.Default;
        var started = Stopwatch.GetTimestamp();
        var changed = false;
        Guard(() =>
        {
            Live(service);
            service.Update(totalSeconds);
            surface!.Canvas.Clear(new SKColor(Background.Red, Background.Green, Background.Blue));
            service.Draw(surface.Canvas);
            surface.Flush();
            LastDrawTime = Stopwatch.GetElapsedTime(started);
            using var pixmap = surface.PeekPixels() ?? throw new InvalidOperationException("The Skia surface has no readable pixels.");
            var drawn = pixmap.GetPixelSpan();
            if (drawn.Length != pixels.Length || drawn.SequenceEqual(pixels)) return;
            drawn.CopyTo(pixels);
            Version++;
            changed = true;
        }, $"Gum couldn't draw the screen {screen}. Open it in Gum to check it, then save it again.");
        return changed;
    }

    // Takes down whatever GumService.Default holds (another picture's screen, or this one's), so it can start again.
    private static void Release(GumService service)
    {
        try
        {
            var live = reloader;
            reloader = null;
            live?.Stop();
            if (service.IsInitialized) service.Uninitialize();
        }
        finally
        {
            ObjectFinder.Self.GumProjectSave = null;
            if (relativeBefore is not null) { FileManager.RelativeDirectory = relativeBefore; relativeBefore = null; }
            owner = null;
        }
    }

    /// <summary>Takes this picture's screen out of Gum (its watcher stops) and frees the canvas. Never throws.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        GumLibrary.Run(() =>
        {
            if (ReferenceEquals(owner, this)) Quietly(() => Guard(() => Release(GumService.Default), ""));
            return 0;
        });
        surface?.Dispose();
        surface = null;
    }

    /// <summary>Whether Gum's drawing service holds nothing (no picture's screen), for tests.</summary>
    internal static bool GumIsFree => GumLibrary.Run(() => owner is null && !GumService.Default.IsInitialized);
}
