using Bed.Editor.Gum;
using Gum.ProjectServices;
using Xunit;

namespace Bed.Tests;

// GumMessages is the one place BED reads Gum's English message text. Each test here makes Gum's own loader or checker
// say the real message on a fixture and pins the pattern to it, so a Gum rewording fails here.
public sealed class GumMessagesTests
{
    /// <summary>Everything Gum's loader and checker say about a project, as Gum says it.</summary>
    private static List<ErrorResult> GumSays(string projectFile) => GumLibrary.Run(() =>
    {
        var loaded = GumLibrary.Load(projectFile);
        try
        {
            var said = loaded.LoadErrors.ToList();
            if (loaded.Project is { } project)
            {
                var checker = new HeadlessErrorChecker(new DefaultTypeResolver(), []) { ReportsLeftoverInstanceVariables = true };
                foreach (var element in project.AllElements) said.AddRange(checker.GetErrorsFor(element, project));
                said.AddRange(checker.GetProjectErrors(project));
            }
            return said;
        }
        finally { GumLibrary.Release(); }
    });

    private static string Fixture(string folder, string file) => Path.Combine(GumFixtures.Folder(folder), file);

    [Fact]
    public void A_part_made_from_nothing_is_read_from_Gums_message()
    {
        var said = GumSays(Fixture("Deps", "Deps.gumj"));
        Assert.Contains(said, e => e.Code is null && GumMessages.InvalidPart(e.Message) == ("Slot", ""));
    }

    [Fact]
    public void The_path_of_a_part_loop_is_read_from_Gums_GUM0018_message()
    {
        var loops = GumSays(Fixture("Deps", "Deps.gumj")).Where(e => e.Code == "GUM0018").Select(e => GumMessages.InstanceLoop(e.Message)).ToList();
        Assert.NotEmpty(loops);
        Assert.All(loops, loop =>
        {
            Assert.Contains("LoopA", loop);
            Assert.Contains("LoopB", loop);
            Assert.Equal(loop[0], loop[^1]);
        });
    }

    [Fact]
    public void Who_names_a_missing_file_is_read_from_Gums_GUM0006_message()
    {
        var missing = GumSays(Fixture("Files", "Files.gumj")).Where(e => e.Code == "GUM0006").Select(e => GumMessages.MissingFile(e.Message)).ToList();
        Assert.Contains(missing, m => m.File == "Images/spare.png" && m.Owner is { Length: > 0 });
        Assert.DoesNotContain(missing, m => m.File is null);
    }

    [Fact]
    public void The_spelling_asked_for_is_read_from_Gums_GUM0008_message()
    {
        var copy = GumFixtures.Copy("Files");
        try
        {
            Directory.CreateDirectory(Path.Combine(copy, "Images"));
            File.WriteAllBytes(Path.Combine(copy, "Images", "Other.png"), []);
            var mismatch = Assert.Single(GumSays(Path.Combine(copy, "Files.gumj")), e => e.Code == "GUM0008");
            Assert.Equal("Images/other.png", GumMessages.CaseMismatchAsked(mismatch.Message)?.Replace('\\', '/'));
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Merge_marks_and_damaged_files_are_told_apart_from_Gums_load_errors()
    {
        var copy = GumFixtures.Copy("Deps");
        try
        {
            File.WriteAllText(Path.Combine(copy, "Components", "Panel.gucj"), "{ \"Name\": \"Panel\", \"States\": [ ");
            File.WriteAllText(Path.Combine(copy, "Screens", "Looped.gusj"), "<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> other\n");
            var said = GumSays(Path.Combine(copy, "Deps.gumj"));
            Assert.Single(said, e => e.Code is null && GumMessages.IsConflictMarkers(e.Message));
            Assert.Single(said, e => e.Code is null && GumMessages.IsUnreadableFile(e.Message));
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Text_that_matches_no_pattern_reads_as_nothing()
    {
        Assert.Null(GumMessages.InvalidPart("something else"));
        Assert.Empty(GumMessages.InstanceLoop("something else"));
        Assert.Equal((null, null), GumMessages.MissingFile("something else"));
        Assert.Null(GumMessages.CaseMismatchAsked(null));
        Assert.False(GumMessages.IsConflictMarkers(null));
        Assert.False(GumMessages.IsUnreadableFile("Something"));
    }
}
