using Xunit;
using System.Diagnostics;
using Bed.Editor.Gum;

namespace Bed.Tests;

public class GumToolTests
{
    private sealed class Temp : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("gum tool ").FullName;
        public string Make(string relative) { var p = Path.GetFullPath(Path.Combine(Root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "x"); return p; }
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void Plans_a_flat_screen_directly_a_nested_one_through_the_project_and_starts_without_a_shell()
    {
        using var t = new Temp();
        var exe = t.Make("Gum Tool/Gum.exe"); var proj = t.Make("My UI/Game.gumx");
        var flat = new GumScreen("Menu", t.Make("My UI/Screens/Menu.gusx"), false);
        var nested = new GumScreen("Shop/Cart", t.Make("My UI/Screens/Shop/Cart.gusx"), false);
        var project = new GumProject(proj, [flat, nested], []);
        var a = GumTool.Plan(exe, project, flat);
        Assert.Equal(flat.File, a.Argument); Assert.Null(a.Problem);
        var b = GumTool.Plan(exe, project, nested);
        Assert.Equal(proj, b.Argument); Assert.Contains("Shop/Cart", b.Note);
        ProcessStartInfo? seen = null;
        var (ok, message) = GumTool.Start(a, i => { seen = i; return true; });
        Assert.True(ok); Assert.Equal("Gum is opening the screen Menu.", message);
        Assert.False(seen!.UseShellExecute); Assert.Equal([flat.File], seen.ArgumentList); // a path with spaces stays one argument
    }

    [Fact]
    public void A_Gum_that_already_has_the_project_open_is_brought_forward_instead_of_starting_another()
    {
        using var t = new Temp();
        var exe = t.Make("Gum/Gum.exe"); var proj = t.Make("ui/Game.gumj"); var project = new GumProject(proj, [], []);
        var launch = GumTool.Plan(exe, project, null);
        Assert.Equal(proj, launch.Project);
        string? asked = null; var started = false;
        var (ok, message) = GumTool.Start(launch, _ => started = true, p => { asked = p; return true; });
        Assert.True(ok); Assert.False(started); Assert.Equal(proj, asked); Assert.Contains("brought it to the front", message);
        (ok, _) = GumTool.Start(launch, _ => started = true, _ => false);
        Assert.True(ok); Assert.True(started);
        Assert.True(GumTool.SameFile(proj.ToUpperInvariant(), proj));
        Assert.True(GumTool.SameFile("Game", proj));      // what Gum 2026.11.02 shows
        Assert.True(GumTool.SameFile("Game.gumj*", proj));
        Assert.False(GumTool.SameFile("Gum", proj));
        Assert.False(GumTool.SameFile(Path.Combine(t.Root, "ui", "Other.gumj"), proj));
    }

    [Theory]
    [InlineData("Menu", false)]
    [InlineData("Shop/Cart", false)]
    [InlineData("Missing", true)]
    public void An_open_project_names_the_selected_screen_without_starting_another(string name, bool missing)
    {
        using var t = new Temp();
        var exe = t.Make("Gum.exe"); var proj = t.Make("ui/Game.gumj");
        var screen = new GumScreen(name, missing ? Path.Combine(t.Root, "ui/Screens/Missing.gusj") : t.Make($"ui/Screens/{name}.gusj"), missing);
        var started = false;
        var (ok, message) = GumTool.Start(GumTool.Plan(exe, new GumProject(proj, [screen], []), screen),
            _ => started = true, _ => true);
        Assert.True(ok); Assert.False(started);
        Assert.Equal($"Gum already has these screens open, so BED brought it to the front. Pick {name} from the screen list there.", message);
    }

    [Fact]
    public void Gum_is_found_without_being_told_where_it_is()
    {
        using var t = new Temp();
        var tools = t.Make("Tools/Gum/Gum.exe"); var other = t.Make("Other/Gum.exe");
        string[] places = [Path.Combine(t.Root, "Nowhere/Gum.exe"), t.Make("Tools/Gum/readme.txt"), tools, other];
        Assert.Equal(tools, GumTool.Find(null, places));                                  // the first that is there
        Assert.Equal(other, GumTool.Find(other, places));                                 // one already picked wins
        Assert.Equal(tools, GumTool.Find(Path.Combine(t.Root, "moved/Gum.exe"), places)); // a picked one that moved is looked for again
        Assert.Null(GumTool.Find("", [Path.Combine(t.Root, "Nowhere/Gum.exe")]));
        Assert.All(GumTool.UsualPlaces().Take(500), p => Assert.Equal("Gum.exe", Path.GetFileName(p)));
    }

    [Fact]
    public void Missing_tool_or_screen_file_says_what_to_do_and_starts_nothing()
    {
        using var t = new Temp();
        var proj = t.Make("ui/Game.gumx"); var project = new GumProject(proj, [], []);
        Assert.Equal(GumTool.NotFound, GumTool.Plan(null, project, null).Problem);
        Assert.Contains("isn't at", GumTool.Plan(Path.Combine(t.Root, "nope.exe"), project, null).Problem);
        var started = false;
        Assert.False(GumTool.Start(GumTool.Plan(null, project, null), _ => started = true).Started); Assert.False(started);
        var exe = t.Make("Gum.exe");
        var gone = GumTool.Plan(exe, project, new GumScreen("Old", Path.Combine(t.Root, "ui/Screens/Old.gusx"), true));
        Assert.Equal(proj, gone.Argument); Assert.Contains("opening the whole project instead", gone.Note);
    }
}
