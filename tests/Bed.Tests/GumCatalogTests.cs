using System.Security.Cryptography;
using Bed;
using Bed.Editor.Gum;
using Xunit;

namespace Bed.Tests;

// GUM-LIST. Fixtures in Fixtures/Gum are original: a project made by BED's own research in Gum (Original), its XML twin
// written by hand from Gum's saved format (OriginalXml), and small INVENTED projects (Deps, Newer, Files). The only font
// files are INVENTED stand-ins written by hand: Deps/FontCache/Font18Arial.fnt and Files/Fonts/paged.fnt hold a few lines of
// BMFont text and at most a space; Files/Anim/walk.achx is an INVENTED one-frame animation list. Pictures they name are left
// out on purpose, except Deps' one-pixel dot.png and Font18Arial_0.png.
internal static class GumFixtures
{
    public static string Folder(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Bed.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "tests", "Bed.Tests", "Fixtures", "Gum", name);
    }

    /// <summary>A throwaway copy of a fixture project, for tests that change files. The caller deletes it.</summary>
    public static string Copy(string name)
    {
        var from = Folder(name);
        var to = Directory.CreateTempSubdirectory("bed-gum-copy-").FullName;
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return to;
    }

    public static Dictionary<string, string> Hashes(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
}

public sealed class GumCatalogTests
{
    [Fact]
    public void Xml_and_json_projects_list_the_same_screens()
    {
        var json = GumCatalog.Read(Path.Combine(GumFixtures.Folder("Original"), "Original.gumj"));
        var xml = GumCatalog.Read(Path.Combine(GumFixtures.Folder("OriginalXml"), "Original.gumx"));
        Assert.Empty(json.Findings);
        Assert.Empty(xml.Findings);
        Assert.Equal(["Preview"], json.Screens.Select(s => s.Name));
        Assert.Equal(json.Screens.Select(s => (s.Name, s.Missing)), xml.Screens.Select(s => (s.Name, s.Missing)));
        Assert.Equal(Path.Combine(GumFixtures.Folder("Original"), "Screens", "Preview.gusj"), json.Screens[0].File);
        Assert.Equal(Path.Combine(GumFixtures.Folder("OriginalXml"), "Screens", "Preview.gusx"), xml.Screens[0].File);
    }

    [Fact]
    public void Screens_are_listed_in_Gums_order_and_a_missing_screen_is_marked_and_explained_with_Gums_code()
    {
        // Gum's own loader sorts the project's lists by name, as the Gum tool shows them.
        var project = GumCatalog.Read(Path.Combine(GumFixtures.Folder("Deps"), "Deps.gumj"));
        Assert.Equal(["Broken", "Dynamic", "Gone", "Looped", "Menu"], project.Screens.Select(s => s.Name));
        Assert.Equal([false, false, true, false, false], project.Screens.Select(s => s.Missing));
        var finding = Assert.Single(project.Findings, f => f.Code == "GUM0004");
        Assert.Equal(Severity.Error, finding.Problem.Severity);
        Assert.Contains("screen Gone", finding.Problem.Message);
        Assert.Contains("Screens/Gone.gusj", finding.Problem.Message);
        Assert.False(string.IsNullOrWhiteSpace(finding.Next));
    }

    [Fact]
    public void A_project_from_a_newer_gum_gives_a_plain_message_and_no_guessed_screens()
    {
        var project = GumCatalog.Read(Path.Combine(GumFixtures.Folder("Newer"), "Newer.gumj"));
        Assert.Empty(project.Screens);
        var finding = Assert.Single(project.Findings);
        Assert.Equal(Severity.Error, finding.Problem.Severity);
        Assert.Contains("newer Gum than BED can read", finding.Problem.Message);
        Assert.DoesNotContain("99", finding.Problem.Message);
        Assert.Equal("Open it in Gum to see it.", finding.Next);
    }

    [Fact]
    public void An_unreadable_or_absent_project_file_gives_a_finding_not_an_exception()
    {
        var folder = Directory.CreateTempSubdirectory("bed-gum-").FullName;
        try
        {
            var broken = Path.Combine(folder, "Broken.gumx");
            File.WriteAllText(broken, "<GumProjectSave><Version>2</Vers");
            var bad = GumCatalog.Read(broken);
            Assert.Empty(bad.Screens);
            Assert.Contains("couldn't read the Gum project Broken", Assert.Single(bad.Findings).Problem.Message);
            var absent = GumCatalog.Read(Path.Combine(folder, "Absent.gumj"));
            Assert.Contains("isn't there", Assert.Single(absent.Findings).Problem.Message);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Finds_projects_at_the_top_and_one_folder_down_skipping_build_folders()
    {
        var root = Directory.CreateTempSubdirectory("bed-gum-find-").FullName;
        try
        {
            foreach (var name in new[] { "Top.gumx", "ui/Ui.gumj", "bin/Copy.gumx", "obj/Copy.gumj", ".git/X.gumx", ".bed/Y.gumx", "a/b/Deep.gumx", "ui/notes.txt" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
                File.WriteAllText(Path.Combine(root, name), "");
            }
            var found = GumCatalog.FindProjects(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));
            Assert.Equal(["Top.gumx", "ui/Ui.gumj"], found);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Reading_never_changes_a_file()
    {
        foreach (var (name, file) in new[] { ("Original", "Original.gumj"), ("OriginalXml", "Original.gumx"), ("Deps", "Deps.gumj"), ("Newer", "Newer.gumj"), ("Files", "Files.gumj") })
        {
            var folder = GumFixtures.Folder(name);
            var before = GumFixtures.Hashes(folder);
            var project = GumCatalog.Read(Path.Combine(folder, file));
            GumDependencyCheck.CheckAll(project, new HashSet<GumTextRoute> { GumTextRoute.FontCache, GumTextRoute.Skia });
            Assert.Equal(before, GumFixtures.Hashes(folder));
        }
    }
}
