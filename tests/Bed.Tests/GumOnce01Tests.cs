using System.ComponentModel;
using Bed.Editor.Gum;
using Xunit;

namespace Bed.Tests;

public sealed class GumOnce01Tests
{
    private sealed class Rig : IDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("gum-once01-").FullName;
        internal int Starts, Releases, Lookups;
        internal bool Running = true, Window;
        internal GumTool.OwnStarts Tracker;
        internal Rig() => Tracker = new(_ => { Starts++; return new(() => Running, () => Releases++); });
        internal GumTool.Launch Launch(string name = "Game")
        {
            var exe = Path.Combine(Root, "Gum.exe"); File.WriteAllText(exe, "not executable");
            var project = Path.Combine(Root, name + ".gumj"); File.WriteAllText(project, "{}");
            return GumTool.Plan(exe, new GumProject(project, [], []), null);
        }
        internal bool Show(string _) { Lookups++; return Window; }
        public void Dispose()
        {
            Tracker.Dispose();
            if (!Path.GetFullPath(Root).StartsWith(Path.Combine(Path.GetTempPath(), "gum-once01-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected Gum fixture path");
            Directory.Delete(Root, true);
        }
    }

    [Fact]
    public void Newly_started_window_is_fitted_once_when_ready()
    {
        using var r = new Rig();
        var attempted = new List<string>();
        var ready = false;
        using var tracker = new GumTool.OwnStarts(_ => new(() => true, () => { }, project =>
        {
            attempted.Add(project);
            return ready;
        }));
        var launch = r.Launch();
        Assert.True(tracker.Start(launch, r.Show).Started);
        tracker.FitOpenedWindows();
        ready = true;
        tracker.FitOpenedWindows();
        tracker.FitOpenedWindows();
        Assert.Equal(new[] { launch.Project, launch.Project }, attempted);
    }

    [Fact]
    public void Oversized_Gum_window_fits_the_work_area_even_on_a_left_hand_monitor()
    {
        var area = new GumTool.WindowRect(-2560, 0, 0, 1540);
        Assert.Equal(new GumTool.WindowRect(-2560, 87, 0, 1540),
            GumTool.FitWorkArea(new(-2217, 343, 369, 1796), area));
        var alreadyVisible = new GumTool.WindowRect(-1000, 100, -500, 900);
        Assert.Equal(alreadyVisible, GumTool.FitWorkArea(alreadyVisible, area));
    }

    [Fact]
    public void Repeated_presses_and_other_screen_requests_wait_for_the_same_live_process()
    {
        using var r = new Rig(); var launch = r.Launch();
        Assert.Equal((true, launch.Note), r.Tracker.Start(launch, r.Show));
        var anotherScreen = launch with { ScreenName = "Shop" };
        for (var i = 0; i < 5; i++)
        {
            var result = r.Tracker.Start(anotherScreen, r.Show);
            Assert.True(result.Started); Assert.StartsWith("This project was already sent to Gum", result.Message);
            Assert.DoesNotContain("brought it", result.Message);
        }
        Assert.Equal(1, r.Starts); Assert.Equal(6, r.Lookups); Assert.Equal(0, r.Releases);
    }

    [Fact]
    public void Project_keys_are_full_paths_case_insensitive_and_different_projects_can_start()
    {
        using var r = new Rig(); var launch = r.Launch();
        Assert.True(r.Tracker.Start(launch, r.Show).Started);
        var alias = launch with { Project = Path.Combine(r.Root, ".", "GAME.gumj") };
        Assert.StartsWith("This project was already sent to Gum", r.Tracker.Start(alias, r.Show).Message);
        Assert.True(r.Tracker.Start(r.Launch("Other"), r.Show).Started);
        Assert.Equal(2, r.Starts);
    }

    [Fact]
    public void An_exited_process_is_released_and_the_next_press_can_retry()
    {
        using var r = new Rig(); var launch = r.Launch();
        Assert.True(r.Tracker.Start(launch, r.Show).Started);
        r.Running = false;
        Assert.Equal((true, launch.Note), r.Tracker.Start(launch, r.Show));
        Assert.Equal(2, r.Starts); Assert.Equal(1, r.Releases);
    }

    [Fact]
    public void A_matching_window_takes_over_and_keeps_selected_screen_guidance()
    {
        using var r = new Rig(); var launch = r.Launch() with { ScreenName = "Menu" };
        r.Tracker.Start(launch, r.Show); r.Window = true;
        var result = r.Tracker.Start(launch, r.Show);
        Assert.True(result.Started); Assert.Contains("brought it to the front", result.Message);
        Assert.Contains("Pick Menu", result.Message); Assert.Equal(1, r.Starts); Assert.Equal(1, r.Releases);
        r.Tracker.Start(launch, r.Show); Assert.Equal(1, r.Starts);
        // After the visible Gum closes or switches away, the old pending lease must not block a fresh start.
        r.Window = false; Assert.Equal((true, launch.Note), r.Tracker.Start(launch, r.Show)); Assert.Equal(2, r.Starts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_failed_start_does_not_poison_the_project_and_retry_works(bool throws)
    {
        using var r = new Rig(); var launch = r.Launch(); r.Tracker.Dispose();
        r.Tracker = new(_ =>
        {
            r.Starts++;
            if (r.Starts == 1) { if (throws) throw new Win32Exception("fake start failure"); return null; }
            return new(() => true, () => r.Releases++);
        });
        Assert.False(r.Tracker.Start(launch, r.Show).Started);
        Assert.True(r.Tracker.Start(launch, r.Show).Started); Assert.Equal(2, r.Starts);
    }

    [Fact]
    public void Invalid_plan_never_looks_up_or_starts_Gum()
    {
        using var r = new Rig();
        var launch = new GumTool.Launch(null, null, "", "missing project");
        Assert.Equal((false, "missing project"), r.Tracker.Start(launch, r.Show));
        Assert.Equal(0, r.Starts); Assert.Equal(0, r.Lookups);
    }

    [Fact]
    public void A_process_that_closes_during_inspection_is_forgotten_without_killing_it()
    {
        using var r = new Rig(); var launch = r.Launch(); r.Tracker.Dispose();
        r.Tracker = new(_ => { r.Starts++; return new(() => throw new InvalidOperationException("closed meanwhile"), () => r.Releases++); });
        Assert.True(r.Tracker.Start(launch, r.Show).Started);
        Assert.True(r.Tracker.Start(launch, r.Show).Started);
        Assert.Equal(2, r.Starts); Assert.Equal(1, r.Releases);
    }

    [Fact]
    public async Task Concurrent_requests_share_the_single_pending_start()
    {
        using var r = new Rig(); var launch = r.Launch();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => r.Tracker.Start(launch, r.Show))));
        Assert.All(results, result => Assert.True(result.Started));
        Assert.Equal(1, r.Starts); Assert.Equal(8, r.Lookups);
        Assert.Equal(7, results.Count(result => result.Message.StartsWith("This project was already sent to Gum", StringComparison.Ordinal)));
    }
}