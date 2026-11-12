using System.Xml;
using System.Xml.Linq;

namespace Bed.Editor.Gum;

// GUM-FONTS: which way a game draws Gum's text, read from the Gum packages its project files reference. BORROWED: the four
// font routes in Gum's own docs (Fonts page): ready-made letters from FontCache (MonoGame, KNI, FNA, raylib without
// KernSmith, and sokol), letters made while the game runs (KernSmith.*Gum), and Skia, which draws .ttf/.otf fonts itself
// and never reads FontCache but can't draw a .fnt font (SkiaGum, which Gum.Brutal.* build on). INVENTED: reading the
// routes from package references, or from the Gum libraries a built game in the folder carries (beside it, or inside a
// single-file program), and saying "can't tell" rather than guessing when neither names a known Gum runtime.

/// <summary>A way a game can draw the text in Gum screens.</summary>
public enum GumTextRoute
{
    /// <summary>Ready-made letter pictures from the Gum project's FontCache folder (MonoGame, KNI, FNA, raylib, sokol).</summary>
    FontCache,

    /// <summary>Letters made while the game runs (KernSmith), so FontCache isn't needed.</summary>
    MadeWhileRunning,

    /// <summary>Skia, which BRUTAL games use through Gum.Brutal: draws .ttf and .otf fonts, never reads FontCache, and
    /// can't draw a .fnt font.</summary>
    Skia,
}

/// <summary>Works out how a game draws Gum's text from the Gum packages its project files reference.</summary>
public static class GumTextRoutes
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".bed" };
    private static readonly string[] FontCacheRuntimes = ["Gum.MonoGame", "Gum.KNI", "Gum.FNA", "Gum.raylib", "Gum.sokol"];

    /// <summary>The routes the game in <paramref name="projectRoot"/> uses, from the <c>PackageReference</c> items
    /// (<c>Include</c> or <c>Update</c>) in its <c>.csproj</c> and <c>.props</c> files, in the folder and its immediate
    /// subfolders. A version listed only in <c>Directory.Packages.props</c> doesn't count: listing a version doesn't
    /// mean the game uses the package. A built game there counts too: the Gum libraries beside it, or named inside a
    /// program built as one file (a packaged game with no code beside it). Empty when nothing names a known Gum runtime:
    /// then BED says it can't tell.</summary>
    public static IReadOnlySet<GumTextRoute> Detect(string projectRoot)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(projectRoot);
        if (Directory.Exists(root))
        {
            Read(root, packages); Built(root, packages);
            foreach (var folder in Safe(() => Directory.GetDirectories(root)))
                if (!Skipped.Contains(Path.GetFileName(folder))) { Read(folder, packages); Built(folder, packages); }
        }
        return FromPackages(packages);
    }

    /// <summary>The routes the named packages give (see <see cref="Detect"/>).</summary>
    public static IReadOnlySet<GumTextRoute> FromPackages(IEnumerable<string> packages)
    {
        var routes = new HashSet<GumTextRoute>();
        var list = packages.ToList();
        bool Has(Func<string, bool> test) => list.Any(test);
        if (Has(p => p.StartsWith("Gum.Brutal.", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Gum.SkiaSharp", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("KniSkia", StringComparison.OrdinalIgnoreCase)))
            routes.Add(GumTextRoute.Skia);
        var kernSmith = Has(p => p.StartsWith("KernSmith.", StringComparison.OrdinalIgnoreCase) && p.EndsWith("Gum", StringComparison.OrdinalIgnoreCase));
        if (Has(p => FontCacheRuntimes.Any(r => p.Equals(r, StringComparison.OrdinalIgnoreCase)) || p.StartsWith("FlatRedBall.GumCore", StringComparison.OrdinalIgnoreCase)))
            routes.Add(kernSmith ? GumTextRoute.MadeWhileRunning : GumTextRoute.FontCache);
        else if (kernSmith) routes.Add(GumTextRoute.MadeWhileRunning);
        return routes;
    }

    // Each Gum runtime's library and the package it comes from (the AssemblyName and PackageId in Gum's own project files).
    private static readonly (string Library, string Package)[] Libraries =
    [
        ("SkiaGum", "Gum.SkiaSharp"), ("BrutalGum", "Gum.Brutal.OGL"), ("KniSkiaGum", "Gum.KniSkia"),
        ("MonoGameGum", "Gum.MonoGame"), ("KniGum", "Gum.KNI"), ("FnaGum", "Gum.FNA"), ("RaylibGum", "Gum.raylib"), ("SokolGum", "Gum.sokol"),
        ("GumCore", "FlatRedBall.GumCore"),
        ("KernSmith.MonoGameGum", "KernSmith.MonoGameGum"), ("KernSmith.KniGum", "KernSmith.KniGum"),
        ("KernSmith.FnaGum", "KernSmith.FnaGum"), ("KernSmith.RaylibGum", "KernSmith.RaylibGum"),
    ];

    /// <summary>The Gum packages a built game carries: a Gum library file (<c>SkiaGum.dll</c>, <c>SkiaGum.Standalone.dll</c>
    /// and the like) beside it, or such a name inside a program built as one file.</summary>
    public static IEnumerable<string> PackagesInBuiltGame(string folder)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Safe(() => Directory.GetFiles(folder)))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (library, package) in Libraries)
                    if (name.Equals(library + ".dll", StringComparison.OrdinalIgnoreCase) || name.StartsWith(library + ".", StringComparison.OrdinalIgnoreCase)) found.Add(package);
            }
            else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or OutOfMemoryException) { continue; }
                foreach (var (library, package) in Libraries)
                    if (Names(bytes, library)) found.Add(package);
            }
        }
        return found;
    }

    // Whether a single-file program's list of the files inside it names the library ("SkiaGum.dll", "SkiaGum.Standalone.dll").
    private static bool Names(ReadOnlySpan<byte> program, string library)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(library + ".");
        for (var at = program.IndexOf(name); at >= 0; )
        {
            var rest = program[(at + name.Length)..];
            var end = rest.IndexOf((byte)0);
            var tail = rest[..Math.Min(end < 0 ? rest.Length : end, 64)];
            var dll = System.Text.Encoding.ASCII.GetBytes("dll");
            // Right after the name: "dll", or more of a dotted name ending in ".dll" (SkiaGum.Standalone.dll).
            if (tail.StartsWith(dll) || (tail.IndexOf(dll) is var d and > 0 && tail[..d].IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789."u8) < 0)) return true;
            var next = rest.IndexOf(name);
            if (next < 0) return false;
            at += name.Length + next;
        }
        return false;
    }

    private static void Built(string folder, HashSet<string> packages)
    {
        foreach (var package in PackagesInBuiltGame(folder)) packages.Add(package);
    }

    private static void Read(string folder, HashSet<string> packages)
    {
        foreach (var file in Safe(() => Directory.GetFiles(folder)))
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".props", StringComparison.OrdinalIgnoreCase)) continue;
            XDocument document;
            try { document = XDocument.Load(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException) { continue; }
            // MSBuild project files may or may not use the old namespace; the local name is what counts.
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
                if (((string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update")) is { Length: > 0 } name)
                    packages.Add(name.Trim());
        }
    }

    private static string[] Safe(Func<string[]> list)
    {
        try { return list(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
