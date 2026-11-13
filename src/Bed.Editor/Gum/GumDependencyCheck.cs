using Bed.Data;
using Gum.Bundle;
using Gum.DataTypes;
using Gum.DataTypes.Behaviors;
using Gum.Expressions;
using Gum.Managers;
using Gum.ProjectServices;
using Gum.StateAnimation.SaveClasses;
using RenderingLibrary.Graphics.Fonts;

namespace Bed.Editor.Gum;

// GUM-FILES: checks a Gum project screen by screen with Gum's own library. BORROWED, all from Gum: the loader and its load
// errors (Gum.ProjectServices.ProjectLoader), the checker behind gumcli check with its codes and help pages
// (HeadlessErrorChecker, with the animation and leftover-setting checks and the expression reader gumcli turns on), what one screen reaches
// (GumElementReachability, from one explicit root including its base screens), every file that reached
// set needs, including the pictures inside .fnt files, the frames inside .achx files and the project's translation files
// (GumProjectDependencyWalker, project-wide mode), and the fonts each one needs (FontReferenceCollector, whose FontCache
// file names are Gum's own). INVENTED: splitting findings into the screen that needs them and the project as a whole
// (built-in types, behaviours, translations, components no screen uses), the font findings by the game's text route
// (GumTextRoutes), the plain words (GumWords) and reporting as BED check findings with a "complete" flag.

/// <summary>What <see cref="GumDependencyCheck.CheckAll"/> found: the findings for the project as a whole, and a report
/// for each screen whose file Gum could read, by screen name.</summary>
public sealed record GumProjectCheck(CheckReport Project, IReadOnlyDictionary<string, CheckReport> Screens);

/// <summary>Checks a Gum project with Gum's own checker, screen by screen: what each screen uses (its components, what
/// those are based on, their behaviours) and every picture, font, animation and other file any of them needs, plus the
/// fonts the game's way of drawing text needs.</summary>
public static class GumDependencyCheck
{
    // Gum's own file kinds where Gum's library has them: its screen, component, built-in and behaviour file endings, the
    // font file endings Gum treats as fonts (BmfcSave.FontFileExtensions, plus its .fnt bitmap fonts) and its animation
    // file endings (ElementAnimationsSave; .achx/.achj as the walker reads them). GumCommon has no public list of picture
    // endings (the runtimes' LoaderManager.ValidTextureExtensions isn't in it), so that one is BED's.
    private static readonly HashSet<string> GumFileExtensions = new[]
    {
        GumProjectSave.ScreenExtension, GumProjectSave.ScreenJsonExtension, GumProjectSave.ComponentExtension, GumProjectSave.ComponentJsonExtension,
        GumProjectSave.StandardExtension, GumProjectSave.StandardJsonExtension, BehaviorReference.Extension, BehaviorReference.JsonExtension,
    }.Select(e => "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);
    // Gum's backend-specific raster list excludes SVG/Lottie, which use separate element loaders.
    private static readonly HashSet<string> PictureExtensions = new(
        RenderingLibrary.Content.ContentLoader.SupportedImageExtensions.Select(e => "." + e).Append(".svg"), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FontExtensions = BmfcSave.FontFileExtensions.Append(".fnt").ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AnimationExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".achx", ".achj", "." + ElementAnimationsSave.FileExtension, "." + ElementAnimationsSave.JsonFileExtension };

    /// <summary>Checks <paramref name="screen"/> of <paramref name="project"/> only (not the project as a whole). Missing
    /// files are errors; Gum's own findings keep Gum's severity and code. Anything BED couldn't follow is reported as
    /// worth knowing and makes the report incomplete: an incomplete report never counts as clean. Reads only; never
    /// writes a file and never throws for a bad project.</summary>
    /// <param name="project">The project, as <see cref="GumCatalog.Read"/> returned it.</param>
    /// <param name="screen">One of the project's screens.</param>
    /// <param name="routes">How the game draws text (<see cref="GumTextRoutes.Detect"/>); null or empty when that can't
    /// be told, and then no font-file findings are made.</param>
    public static CheckReport Check(GumProject project, GumScreen screen, IReadOnlySet<GumTextRoute>? routes = null) =>
        GumLibrary.Run(() =>
        {
            try
            {
                var loaded = GumLibrary.Load(project.ProjectFile);
                if (Readable(loaded) is not { } gum) return Unreadable(project);
                return new Session(project, gum, routes ?? new HashSet<GumTextRoute>()).Screen(screen.Name);
            }
            finally { GumLibrary.Release(); }
        });

    /// <summary>Checks the whole project once: the findings that belong to no one screen, then each screen whose file
    /// Gum could read. Reads only; never throws for a bad project.</summary>
    /// <param name="project">The project, as <see cref="GumCatalog.Read"/> returned it.</param>
    /// <param name="routes">How the game draws text (<see cref="GumTextRoutes.Detect"/>); empty when that can't be told,
    /// which the project findings then say.</param>
    public static GumProjectCheck CheckAll(GumProject project, IReadOnlySet<GumTextRoute> routes) => GumLibrary.Run(() =>
    {
        try
        {
            var loaded = GumLibrary.Load(project.ProjectFile);
            return Readable(loaded) is { } gum ? All(project, gum, routes) : new GumProjectCheck(Unreadable(project), new Dictionary<string, CheckReport>());
        }
        finally { GumLibrary.Release(); }
    });

    /// <summary>Reads <paramref name="projectFile"/> once and checks it: what <see cref="GumCatalog.Read"/> lists, and,
    /// when Gum could read the project, what <see cref="CheckAll"/> finds (null when it couldn't). Reads only; never
    /// throws for a bad project.</summary>
    public static (GumProject Project, GumProjectCheck? Check) ReadAndCheck(string projectFile, IReadOnlySet<GumTextRoute> routes)
    {
        var full = Path.GetFullPath(projectFile);
        return GumLibrary.Run(() =>
        {
            try
            {
                var loaded = GumLibrary.Load(full);
                var project = GumCatalog.FromLoaded(full, loaded);
                return (project, Readable(loaded) is { } gum ? All(project, gum, routes) : null);
            }
            finally { GumLibrary.Release(); }
        });
    }

    // Gum's loop findings: based on each other (GUM0014), containing each other (GUM0018), placed inside each other (GUM0024).
    private static readonly HashSet<string> LoopCodes = ["GUM0014", "GUM0018", "GUM0024"];

    /// <summary>The first loop Gum's own checker finds in what <paramref name="screen"/> uses (its parts based on, holding
    /// or placed inside each other), in plain words, or null. Gum can build such a screen but its live reload can't (it
    /// follows the loop until the program stops), so a screen with a loop isn't shown. Call under GumLibrary's lock with
    /// <paramref name="gum"/> as Gum's current project.</summary>
    public static CheckFinding? ScreenLoop(GumProjectSave gum, string folder, string screen)
    {
        var checker = new HeadlessErrorChecker(new DefaultTypeResolver());
        var words = new GumWords(gum, folder, screen);
        foreach (var element in ScreenElements(gum, screen))
        {
            IReadOnlyList<ErrorResult> errors;
            try { errors = checker.GetErrorsFor(element, gum); }
            catch (Exception) { continue; }   // Gum's checker couldn't follow this part, as in the full check
            if (errors.FirstOrDefault(e => e.Code is { } code && LoopCodes.Contains(code)) is { } loop)
                return words.Finding(loop, GumCatalog.ElementFile(folder, gum, element.Name) ?? folder);
        }
        return null;
    }

    /// <summary>The screens, components and behaviours <paramref name="name"/> uses, by Gum's reachability from that screen
    /// alone: every other screen is left out (neither kept nor walked through), except the screens this one is based on,
    /// whose parts it has too. Built-in types are left out.</summary>
    private static List<ElementSave> ScreenElements(GumProjectSave gum, string name)
    {
        var kept = new GumElementReachability().Compute(gum, [name], excludedElementNames: null, screensAreRoots: false).KeptElementNames;
        return gum.AllElements.Where(e => e is not StandardElementSave && kept.Contains(e.Name)).ToList();
    }



    private static GumProjectSave? Readable(ProjectLoadResult loaded) =>
        loaded.Project is { } gum && loaded.ErrorMessage is null && !GumLibrary.Unsupported(gum) ? gum : null;

    private static CheckReport Unreadable(GumProject project) =>
        new(false, [GumWords.Make(Severity.Info, project.ProjectFile, null,
            "BED couldn't read the Gum project, so it couldn't check its screens.", "Open the project in Gum to check it.", null)],
            "BED couldn't check the Gum project.");

    private static GumProjectCheck All(GumProject project, GumProjectSave gum, IReadOnlySet<GumTextRoute> routes)
    {
        var session = new Session(project, gum, routes);
        var screens = new Dictionary<string, CheckReport>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in project.Screens.Where(s => !s.Missing)) screens[screen.Name] = session.Screen(screen.Name);
        return new GumProjectCheck(session.Project(), screens);
    }

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    private static string KindOf(string text)
    {
        var extension = Path.GetExtension(text);
        return PictureExtensions.Contains(extension) ? "picture" : FontExtensions.Contains(extension) ? "font file"
            : AnimationExtensions.Contains(extension) ? "animation file" : "file";
    }

    /// <summary>One loaded project, checked under GumLibrary's lock.</summary>
    private sealed class Session
    {
        private readonly GumProject project;
        private readonly IReadOnlySet<GumTextRoute> routes;
        private readonly GumProjectSave gum;
        private readonly string folder;
        private readonly HeadlessErrorChecker checker;
        private WalkResult? baseline;
        private bool baselineTried;

        public Session(GumProject project, GumProjectSave gum, IReadOnlySet<GumTextRoute> routes)
        {
            this.project = project;
            this.gum = gum;
            this.routes = routes;
            folder = project.Folder;
            // The same checker gumcli check builds, with Gum's expression reader turned on first as gumcli check does, so GUM0002
            // checks linked values written as sums or plain numbers too (CheckCommand.cs). Turning it on again is harmless.
            GumExpressionService.Initialize();
            checker = new HeadlessErrorChecker(new DefaultTypeResolver(), [new AnimationKeyframeErrorSource(new FileElementAnimationsProvider())])
            { ReportsLeftoverInstanceVariables = true };
        }

        /// <summary>The full path of the file Gum reads <paramref name="name"/> from, or the project file.</summary>
        private string FileOf(string? name) => GumCatalog.ElementFile(folder, gum, name) ?? project.ProjectFile;

        /// <summary>Everything one screen needs, without what belongs to the project as a whole.</summary>
        public CheckReport Screen(string name)
        {
            var report = new Report(new GumWords(gum, folder, name));
            if (gum.Screens.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is not { IsSourceFileMissing: false })
            {
                report.Unknown(project.ProjectFile, $"Gum couldn't read the file for the screen {name}, so BED couldn't check it.", "Open the project in Gum to check it.");
                return report.Finish($"the screen {name}");
            }
            var used = ScreenElements(gum, name);

            foreach (var element in used) GumFindings(report, element, dropMissingFiles: true);

            var walk = Walk(report, new HashSet<string>(used.Select(e => e.Name), StringComparer.OrdinalIgnoreCase));
            if (walk is not null && Baseline(report) is { } shared)
            {
                var common = shared.MissingFiles.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var missing in walk.MissingFiles.Where(m => !common.Contains(Key(m)))) MissingFile(report, missing);
                BitmapFonts(report, walk, shared);
            }
            Fonts(report, used, name);
            return report.Finish($"the screen {name}");
        }

        /// <summary>What belongs to no one screen: Gum's project-wide findings, the built-in types and behaviours every
        /// screen shares, translation files, and the components no screen uses.</summary>
        public CheckReport Project()
        {
            var report = new Report(new GumWords(gum, folder, null));
            if (routes.Count == 0)
                report.Add(GumWords.Make(Severity.Info, project.ProjectFile, null,
                    "BED can't tell how your game draws the text in Gum screens, so it didn't check font files. Games on MonoGame, KNI, FNA, raylib or sokol need Gum's ready-made letters in the FontCache folder; BRUTAL games, which draw with Skia, need .ttf or .otf fonts instead.",
                    "Add the game's Gum package (Gum.Brutal.OGL, Gum.MonoGame and the like) to a project file in this folder, or keep the built game in it, so BED can tell.", null), null);
            try
            {
                foreach (var error in checker.GetProjectErrors(gum))
                    report.Add(report.Words.Finding(error, FileOf(error.ElementName)), error);
            }
            catch (Exception)
            {
                report.Unknown(project.ProjectFile, "Gum's own checker stopped part way through the project, so BED couldn't check everything.", "Open the project in Gum and look at its Errors tab.");
            }
            foreach (var standard in gum.StandardElements) GumFindings(report, standard, dropMissingFiles: true);
            if (Baseline(report) is { } shared)
            {
                foreach (var missing in shared.MissingFiles) MissingFile(report, missing);
                BitmapFonts(report, shared, null);
            }
            // Components no screen reaches: Gum's checker still looks at them, as gumcli check does.
            var unused = new GumElementReachability().Compute(gum, []).TrimmedElementNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var element in gum.Components.Where(c => unused.Contains(c.Name))) GumFindings(report, element, dropMissingFiles: false);
            return report.Finish("the Gum project as a whole");
        }

        /// <summary>Gum's checker on one screen, component or built-in type. Gum's missing-file finding (GUM0006) is left
        /// out where the project-wide walk reports the same files, and more (the pictures inside font files, say).</summary>
        private void GumFindings(Report report, ElementSave element, bool dropMissingFiles)
        {
            IReadOnlyList<ErrorResult> errors;
            try { errors = checker.GetErrorsFor(element, gum); }
            catch (Exception)
            {
                // Gum's checker reads saved values of many kinds; one it can't follow means this part wasn't checked.
                report.Unknown(FileOf(element.Name), $"Gum's own checker couldn't finish checking {report.Words.Describe(element.Name)}, so BED couldn't check everything.",
                    "Open the project in Gum and look at its Errors tab.");
                return;
            }
            foreach (var error in errors)
            {
                if (dropMissingFiles && error.Code == "GUM0006") continue;
                report.Add(report.Words.Finding(error, FileOf(element.Name)), error);
            }
        }

        /// <summary>Gum's walk over the kept screens and components. The FontCache folder's letters files (and the pictures
        /// inside them) are only asked for when the game draws Gum's ready-made letters.</summary>
        private WalkResult? Walk(Report report, ISet<string> keptNames)
        {
            var inclusion = GumBundleInclusion.Core | GumBundleInclusion.ExternalFiles;
            if (routes.Contains(GumTextRoute.FontCache)) inclusion |= GumBundleInclusion.FontCache;
            try { return new GumProjectDependencyWalker().Walk(gum, folder, inclusion, keptNames); }
            catch (Exception)
            {
                report.Unknown(project.ProjectFile, "BED couldn't follow every file the Gum project names, so it couldn't check everything.", "Open the project in Gum to check it.");
                return null;
            }
        }

        /// <summary>What every screen shares: the walk with no screen or component kept (built-in types, behaviours, translations).</summary>
        private WalkResult? Baseline(Report report)
        {
            if (!baselineTried) { baselineTried = true; baseline = Walk(report, new HashSet<string>()); }
            return baseline;
        }

        private static string Key(DependencyWarning warning) => warning.ReferencedPath + "|" + warning.ReferencedFromElementName;

        private void MissingFile(Report report, DependencyWarning missing)
        {
            var path = missing.ReferencedPath;
            // Screen, component, built-in and behaviour files: Gum's checker says these (GUM0004, GUM0031).
            if (GumFileExtensions.Contains(Path.GetExtension(path))) return;
            var owner = missing.ReferencedFromElementName;
            var words = report.Words;
            var screen = words.Screen;
            if (owner == path)
            {
                report.Unknown(project.ProjectFile, $"BED couldn't read the {KindOf(path)} {path} to find the pictures it needs, so it couldn't check them.",
                    "Open the file in the program that made it, or make it again.");
                return;
            }
            var kind = KindOf(path);
            var ownerIsFile = owner.Contains('/') || FontExtensions.Contains(Path.GetExtension(owner)) || AnimationExtensions.Contains(Path.GetExtension(owner));
            var described = words.Describe(owner);
            var known = !ownerIsFile && owner != "(project)" && described != owner;
            string message, next = $"Put the file back, or change the {kind} in Gum.";
            if (owner == "(project)")
            {
                message = $"The Gum project names the translation file {path}, but that file isn't in the project's folder.";
                next = "Put the file back, or change the translation files in the Gum project's settings.";
            }
            else if (ownerIsFile)
            {
                message = $"{(screen is null ? "The" : $"The screen {screen} uses the {KindOf(owner)} {owner}, and the")} {KindOf(owner)} {owner} needs the {kind} {path}, but that file isn't in the project's folder.";
                next = $"Put the file back, or make the {KindOf(owner)} {owner} again.";
            }
            else if (!known)
                // Gum names a font file after its font when it can't say which part asked for it.
                message = $"{(screen is null ? "Gum's built-in parts use" : $"The screen {screen} uses")} the {kind} {path}{(kind == "font file" ? $" for the font {owner}" : "")}, but that file isn't in the project's folder.";
            else
                message = $"{(screen is null ? GumWords.Capital(described) + " names" : $"The screen {screen} uses")} the {kind} {path}{(screen is null || described == $"the screen {screen}" ? "" : $" (in {described})")}, but that file isn't in the project's folder.";
            var item = known ? owner : null;
            report.Add(GumWords.Make(Severity.Error, FileOf(item?.Split('.')[0]), item, message, next, "GUM0006"), null, "missing:" + path + "|" + owner);
        }

        /// <summary>For a game that draws with Skia (BRUTAL games do): every .fnt font file the walk found that the shared
        /// walk didn't, because Skia can't draw a .fnt font.</summary>
        private void BitmapFonts(Report report, WalkResult walk, WalkResult? shared)
        {
            if (!routes.Contains(GumTextRoute.Skia)) return;
            static IEnumerable<string> Fnt(WalkResult w) => w.ExternalFiles.Concat(w.MissingFiles.Select(m => m.ReferencedPath))
                .Where(p => string.Equals(Path.GetExtension(p), ".fnt", StringComparison.OrdinalIgnoreCase));
            var common = shared is null ? [] : Fnt(shared).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var screen = report.Words.Screen;
            foreach (var file in Fnt(walk).Distinct(StringComparer.OrdinalIgnoreCase).Where(f => !common.Contains(f)))
                report.Add(GumWords.Make(Severity.Warning, project.ProjectFile, null,
                    $"{(screen is null ? "Gum's built-in parts use" : $"The screen {screen} uses")} the font file {file}. This game draws Gum's text with Skia, as BRUTAL games do, and Skia can't draw a .fnt font, so that text will show in a stand-in font.",
                    "In Gum, choose a .ttf or .otf font file instead.", null), null, "fnt:" + file);
        }

        /// <summary>For a game that draws Gum's ready-made letters (FontCache): the letters file for each font and size
        /// the screen uses, by Gum's own file names.</summary>
        private void Fonts(Report report, IReadOnlyList<ElementSave> used, string screen)
        {
            if (!routes.Contains(GumTextRoute.FontCache)) return;
            var collector = new FontReferenceCollector(instance => ObjectFinder.Self.GetElementSave(instance));
            ObjectFinder.Self.EnableCache();
            try
            {
                foreach (var element in used)
                {
                    Dictionary<string, BmfcSave> fonts;
                    try { fonts = collector.Collect(gum, [element]); }
                    catch (Exception)
                    {
                        report.Unknown(FileOf(element.Name), $"BED couldn't work out every font {report.Words.Describe(element.Name)} uses, so it couldn't check them all.",
                            "Open the screen in Gum to check it.");
                        continue;
                    }
                    var owner = report.Words.Describe(element.Name);
                    var where = owner == $"the screen {screen}" ? "" : $" (in {owner})";
                    foreach (var font in fonts.Values)
                    {
                        if (!string.IsNullOrEmpty(font.FontFile)) continue;   // a font file: the walk checked it
                        var letters = font.FontCacheFileName.Replace('\\', '/');
                        if (File.Exists(Path.Combine(folder, letters))) continue;
                        report.Add(GumWords.Make(Severity.Warning, FileOf(element.Name), element.Name,
                            $"The screen {screen} uses the font {font.FontName} at size {font.FontSize:0.##}{where}, but the letters file Gum makes for it, {letters}, isn't in the project's folder. This game draws Gum's ready-made letters, so that text won't show.",
                            "Ask Gum to make the missing font files: run gumcli fonts on the Gum project, or start Gum with --rebuildfonts and the project.", null), null, "font:" + letters);
                    }
                }
            }
            finally { ObjectFinder.Self.DisableCache(); }
        }
    }

    /// <summary>Findings gathered for one screen or for the project, each said once.</summary>
    private sealed class Report(GumWords words)
    {
        private readonly List<CheckFinding> findings = [];
        private readonly HashSet<string> said = new(StringComparer.OrdinalIgnoreCase);
        private bool complete = true;

        public GumWords Words => words;

        public void Add(CheckFinding finding, ErrorResult? from, string? key = null)
        {
            // A loop is reported by Gum once for each thing on it; BED says it once.
            key ??= from?.Code is "GUM0014" or "GUM0018"
                ? from.Code + ":" + string.Join(",", GumWords.LoopOf(from).Order(StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase))
                : finding.Problem.Message;
            if (said.Add(key)) findings.Add(finding);
        }

        public void Unknown(string file, string message, string next)
        {
            complete = false;
            Add(GumWords.Make(Severity.Info, file, null, message, next, null), null);
        }

        public CheckReport Finish(string what)
        {
            int errors = findings.Count(f => f.Problem.Severity == Severity.Error), warnings = findings.Count(f => f.Problem.Severity == Severity.Warning);
            var summary = errors + warnings == 0
                ? complete ? $"BED found nothing wrong with {what}." : $"BED found nothing wrong with {what}, but it couldn't check everything."
                : $"BED found {Count(errors, "problem")} and {Count(warnings, "warning")} for {what}." + (complete ? "" : " It couldn't check everything, so there may be more.");
            return new CheckReport(complete, findings, summary);
        }
    }
}
