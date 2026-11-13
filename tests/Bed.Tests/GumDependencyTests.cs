using Bed;
using Bed.Data;
using Bed.Editor.Gum;
using Xunit;

namespace Bed.Tests;

// GUM-FILES, on the original and INVENTED fixtures in Fixtures/Gum (see GumCatalogTests). The checking itself is Gum's own
// (Gum.ProjectServices); these tests pin what BED makes of it: which screen a finding belongs to, the plain words, and
// Gum's code and help page on each.
public sealed class GumDependencyTests
{
    private static readonly HashSet<GumTextRoute> FontCache = [GumTextRoute.FontCache];
    private static readonly HashSet<GumTextRoute> Skia = [GumTextRoute.Skia];
    private static readonly HashSet<GumTextRoute> CantTell = [];

    private static GumProject Project(string folder, string projectFile) => GumCatalog.Read(Path.Combine(GumFixtures.Folder(folder), projectFile));

    private static CheckReport Check(string folder, string projectFile, string screen, IReadOnlySet<GumTextRoute>? routes = null)
    {
        var project = Project(folder, projectFile);
        return GumDependencyCheck.Check(project, project.Screens.Single(s => s.Name == screen), routes);
    }

    private static string[] Messages(CheckReport report, Severity severity) =>
        report.Findings.Where(f => f.Problem.Severity == severity).Select(f => f.Problem.Message).ToArray();

    [Fact]
    public void The_original_project_reports_its_missing_fonts_the_same_way_in_xml_and_json()
    {
        var json = GumDependencyCheck.CheckAll(Project("Original", "Original.gumj"), CantTell);
        var xml = GumDependencyCheck.CheckAll(Project("OriginalXml", "Original.gumx"), CantTell);
        var preview = json.Screens["Preview"];
        Assert.True(preview.Complete);
        var error = Assert.Single(Messages(preview, Severity.Error));
        Assert.Contains("screen Preview uses the font file Fonts/PressStart2P-Regular.ttf", error);
        // The built-in Text's own default font belongs to the project as a whole, not to one screen.
        Assert.Contains(Messages(json.Project, Severity.Error), m => m.Contains("font file Fonts/LiberationSans-Regular.ttf") && m.Contains("built-in"));
        Assert.Equal(preview.Findings.Select(f => (f.Problem.Severity, f.Problem.Message, f.Next)), xml.Screens["Preview"].Findings.Select(f => (f.Problem.Severity, f.Problem.Message, f.Next)));
        Assert.Equal(json.Project.Findings.Select(f => f.Problem.Message), xml.Project.Findings.Select(f => f.Problem.Message));
    }

    [Fact]
    public void Missing_files_name_the_part_that_uses_them_and_carry_Gums_code_and_help_page()
    {
        var report = Check("Deps", "Deps.gumj", "Menu");
        Assert.True(report.Complete);
        var errors = report.Findings.Where(f => f.Problem.Severity == Severity.Error).ToArray();
        Assert.Equal(2, errors.Length);
        var picture = Assert.Single(errors, f => f.Problem.Message.Contains("picture Images/pressed.png"));
        Assert.Contains("the part Pic in the component Button", picture.Problem.Message);
        Assert.Single(errors, f => f.Problem.Message.Contains("font file Fonts/pressed.fnt") && f.Problem.Message.Contains("the part Label in the component Button"));
        Assert.All(errors, f => Assert.Equal("GUM0006", f.Code));
        Assert.All(errors, f => Assert.StartsWith("https://docs.flatredball.com/gum/", f.Help));
        // dot.png (reached through Button -> Panel) is present; Text's own custom font is off, so it isn't asked for.
        Assert.DoesNotContain(errors, f => f.Problem.Message.Contains("dot.png") || f.Problem.Message.Contains("unused.fnt"));
        Assert.Empty(Messages(report, Severity.Warning));
    }

    [Fact]
    public void A_missing_built_in_type_is_a_project_finding_from_Gums_checker_naming_its_file()
    {
        var all = GumDependencyCheck.CheckAll(Project("Deps", "Deps.gumj"), CantTell);
        var missing = Assert.Single(all.Project.Findings, f => f.Code == "GUM0004");
        Assert.Equal(Severity.Error, missing.Problem.Severity);
        Assert.Contains("Gum's built-in NineSlice", missing.Problem.Message);
        Assert.Contains("Standards/NineSlice.gutj", missing.Problem.Message);
        Assert.Equal("https://docs.flatredball.com/gum/gum-tool/project-files#missing-source-files-gum0004", missing.Help);
        Assert.Empty(all.Screens["Broken"].Findings);
    }

    [Fact]
    public void A_loop_between_components_is_said_once_with_Gums_code()
    {
        var report = Check("Deps", "Deps.gumj", "Looped");
        var loop = Assert.Single(report.Findings);
        Assert.Equal("GUM0018", loop.Code);
        Assert.Contains("LoopA, which uses LoopB, which uses LoopA", loop.Problem.Message);
        Assert.Contains("screen Looped", loop.Problem.Message);
    }

    [Fact]
    public void A_part_that_does_not_say_what_it_is_is_a_problem()
    {
        var report = Check("Deps", "Deps.gumj", "Dynamic");
        Assert.True(report.Complete);
        Assert.Contains("part, Slot, that doesn't say what it's made from", Assert.Single(Messages(report, Severity.Error)));
    }

    [Fact]
    public void Each_screen_is_checked_only_for_what_it_reaches_with_the_other_screens_left_out()
    {
        // Gum's reachability, with every other screen excluded: Other's component and its missing picture aren't Pages'.
        var all = GumDependencyCheck.CheckAll(Project("Files", "Files.gumj"), CantTell);
        Assert.DoesNotContain(all.Screens["Pages"].Findings, f => f.Problem.Message.Contains("other.png") || f.Problem.Message.Contains("OtherPart"));
        Assert.Contains(all.Screens["Other"].Findings, f => f.Problem.Message.Contains("picture Images/other.png") && f.Problem.Message.Contains("the part Pic in the component OtherPart"));
        Assert.DoesNotContain(all.Screens["Other"].Findings, f => f.Problem.Message.Contains("walk") || f.Problem.Message.Contains("paged"));
        Assert.DoesNotContain(all.Project.Findings, f => f.Problem.Message.Contains("other.png"));
        // Deps: Menu doesn't reach the looped components, Looped does.
        Assert.DoesNotContain(Check("Deps", "Deps.gumj", "Menu").Findings, f => f.Problem.Message.Contains("Loop"));
    }

    [Fact]
    public void A_screen_based_on_another_gets_the_findings_of_the_screen_it_is_based_on()
    {
        var all = GumDependencyCheck.CheckAll(Project("Files", "Files.gumj"), CantTell);
        var child = Messages(all.Screens["Child"], Severity.Error);
        Assert.Contains(child, m => m.Contains("Fonts/paged_0.png"));
        Assert.Contains(child, m => m.Contains("Anim/walk1.png"));
        Assert.All(child, m => Assert.Contains("screen Child", m));
        Assert.DoesNotContain(all.Screens["Pages"].Findings, f => f.Problem.Message.Contains("Child"));
    }

    [Fact]
    public void A_base_type_loop_is_said_once_for_the_project_and_an_unused_components_missing_picture_is_the_projects()
    {
        var all = GumDependencyCheck.CheckAll(Project("Files", "Files.gumj"), CantTell);
        var loop = Assert.Single(all.Project.Findings, f => f.Code == "GUM0014");
        Assert.Contains("CycleA", loop.Problem.Message);
        Assert.Contains("CycleB", loop.Problem.Message);
        var spare = Assert.Single(all.Project.Findings, f => f.Problem.Message.Contains("Images/spare.png"));
        Assert.Contains("Spare", spare.Problem.Message);
        Assert.DoesNotContain(all.Screens.Values.SelectMany(r => r.Findings), f => f.Problem.Message.Contains("spare.png") || f.Code == "GUM0014");
    }

    [Fact]
    public void FontCache_letters_files_are_only_followed_for_a_game_that_draws_ready_made_letters()
    {
        var copy = GumFixtures.Copy("Deps");
        try
        {
            File.Delete(Path.Combine(copy, "FontCache", "Font18Arial_0.png"));
            var project = GumCatalog.Read(Path.Combine(copy, "Deps.gumj"));
            bool Mentioned(IReadOnlySet<GumTextRoute> routes)
            {
                var all = GumDependencyCheck.CheckAll(project, routes);
                return all.Screens.Values.Append(all.Project).SelectMany(r => r.Findings).Any(f => f.Problem.Message.Contains("Font18Arial_0.png"));
            }
            Assert.True(Mentioned(FontCache));
            Assert.False(Mentioned(Skia));
            Assert.False(Mentioned(CantTell));
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Pictures_inside_font_and_animation_files_and_translation_files_are_checked()
    {
        var all = GumDependencyCheck.CheckAll(Project("Files", "Files.gumj"), CantTell);
        var pages = Messages(all.Screens["Pages"], Severity.Error);
        Assert.Contains(pages, m => m.Contains("font file Fonts/paged.fnt needs the picture Fonts/paged_0.png"));
        Assert.Contains(pages, m => m.Contains("animation file Anim/walk.achx needs the picture Anim/walk1.png"));
        Assert.Contains(Messages(all.Project, Severity.Error), m => m.Contains("translation file Strings/Words.csv"));
    }

    [Fact]
    public void A_game_that_draws_ready_made_letters_is_warned_about_a_missing_letters_file_and_told_how_to_make_it()
    {
        var dynamic = Check("Deps", "Deps.gumj", "Dynamic", FontCache);
        var warning = Assert.Single(dynamic.Findings, f => f.Problem.Severity == Severity.Warning);
        Assert.Contains("font Verdana at size 18", warning.Problem.Message);
        Assert.Contains("FontCache/Font18Verdana.fnt", warning.Problem.Message);
        Assert.Contains("gumcli fonts", warning.Next);
        Assert.DoesNotContain(dynamic.Findings, f => f.Problem.Message.Contains("Arial"));
        Assert.Empty(Messages(Check("Deps", "Deps.gumj", "Menu", FontCache), Severity.Warning));
    }

    [Fact]
    public void A_skia_game_never_hears_about_ready_made_letters_but_is_warned_about_fnt_fonts()
    {
        Assert.Empty(Messages(Check("Deps", "Deps.gumj", "Dynamic", Skia), Severity.Warning));
        var warning = Assert.Single(Messages(Check("Files", "Files.gumj", "Pages", Skia), Severity.Warning));
        Assert.Contains("font file Fonts/paged.fnt", warning);
        Assert.Contains("Skia can't draw a .fnt font", warning);
        Assert.Empty(Messages(Check("Files", "Files.gumj", "Pages", FontCache), Severity.Warning));
    }

    [Fact]
    public void When_the_games_text_route_cant_be_told_BED_says_so_and_makes_no_font_findings()
    {
        var all = GumDependencyCheck.CheckAll(Project("Deps", "Deps.gumj"), CantTell);
        Assert.Contains(all.Project.Findings, f => f.Problem.Severity == Severity.Info && f.Problem.Message.Contains("can't tell how your game draws the text"));
        Assert.Empty(Messages(all.Screens["Dynamic"], Severity.Warning));
        Assert.DoesNotContain(GumDependencyCheck.CheckAll(Project("Deps", "Deps.gumj"), Skia).Project.Findings, f => f.Problem.Message.Contains("can't tell"));
    }

    [Fact]
    public void A_file_name_differing_only_in_capitals_is_Gums_case_warning()
    {
        var copy = GumFixtures.Copy("Files");
        try
        {
            Directory.CreateDirectory(Path.Combine(copy, "Images"));
            File.WriteAllBytes(Path.Combine(copy, "Images", "Other.png"), []);
            var project = GumCatalog.Read(Path.Combine(copy, "Files.gumj"));
            var other = GumDependencyCheck.Check(project, project.Screens.Single(s => s.Name == "Other"), CantTell);
            var mismatch = Assert.Single(other.Findings, f => f.Code == "GUM0008");
            Assert.Contains("Images/other.png", mismatch.Problem.Message);
            Assert.Contains("Images/Other.png", mismatch.Problem.Message);
            Assert.Contains("capital letters", mismatch.Next);
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Gums_load_errors_are_kept_in_plain_words()
    {
        var copy = GumFixtures.Copy("Deps");
        try
        {
            File.WriteAllText(Path.Combine(copy, "Components", "Panel.gucj"), "{ \"Name\": \"Panel\", \"States\": [ ");
            File.WriteAllText(Path.Combine(copy, "Screens", "Looped.gusj"), "<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> other\n");
            var project = GumCatalog.Read(Path.Combine(copy, "Deps.gumj"));
            Assert.Contains(project.Findings, f => f.Problem.Message.Contains("The component Panel has a file that isn't laid out the way Gum saves one"));
            Assert.Contains(project.Findings, f => f.Problem.Message.Contains("unfinished merge marks"));
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Checks_can_run_side_by_side_without_mixing_projects()
    {
        // Gum's library keeps one shared project lookup; BED's lock keeps parallel checks apart.
        var results = Enumerable.Range(0, 8).AsParallel().Select(i => i % 2 == 0
            ? Check("Deps", "Deps.gumj", "Menu").Summary
            : Check("Original", "Original.gumj", "Preview").Summary).ToArray();
        Assert.All(results.Where((_, i) => i % 2 == 0), s => Assert.Contains("screen Menu", s));
        Assert.All(results.Where((_, i) => i % 2 == 1), s => Assert.Contains("screen Preview", s));
        Assert.Equal(1, results.Distinct().Count(s => s.Contains("Menu")));
    }

    [Fact]
    public void A_value_set_by_hand_against_a_linked_value_in_another_component_is_reported_as_gumcli_check_does()
    {
        // The link reads another component (Button's Width = Styles' Width), which only Gum's expression reader works out;
        // gumcli check turns it on, and so does BED. Start without it, so this doesn't pass because something else did.
        GumLibrary.Run(() => { global::GumRuntime.ElementSaveExtensions.CustomEvaluateExpression = null; return 0; });
        var report = Check("Linked", "Linked.gumj", "Menu");
        Assert.True(report.Complete);
        var finding = Assert.Single(report.Findings, f => f.Code == "GUM0002");
        Assert.Equal(Severity.Warning, finding.Problem.Severity);
        Assert.Equal("The screen Menu uses the component Button, and the component Button sets Width to 255 by hand, but one of its linked values would set it to 100.",
            finding.Problem.Message);
        Assert.Equal("Open it in Gum and keep either the value or the link, not both.", finding.Next);
        Assert.StartsWith("https://docs.flatredball.com/gum/", finding.Help);
    }

    [Fact]
    public void Messages_are_plain_full_sentences()
    {
        string[] banned = ["element", "instance", "variable", "reference", "dependency", "resolve", "parse", "invalid", "null", "XML", "JSON", "exception", "serializ"];
        foreach (var (folder, file) in new[] { ("Deps", "Deps.gumj"), ("Files", "Files.gumj"), ("Original", "Original.gumj"), ("Linked", "Linked.gumj") })
            foreach (var routes in new[] { CantTell, FontCache, Skia })
            {
                var project = Project(folder, file);
                var all = GumDependencyCheck.CheckAll(project, routes);
                foreach (var (screen, report) in all.Screens)
                    foreach (var finding in report.Findings)
                    {
                        Assert.Contains("screen " + screen, finding.Problem.Message);
                        Plain(finding);
                    }
                foreach (var finding in all.Project.Findings.Concat(project.Findings)) Plain(finding);
            }

        void Plain(CheckFinding finding)
        {
            Assert.EndsWith(".", finding.Problem.Message);
            Assert.EndsWith(".", finding.Next);
            foreach (var word in banned) Assert.DoesNotContain(word, finding.Problem.Message + " " + finding.Next, StringComparison.OrdinalIgnoreCase);
            if (finding.Code is not null) Assert.Matches("^GUM00[0-9][0-9]$", finding.Code);
        }
    }
}
