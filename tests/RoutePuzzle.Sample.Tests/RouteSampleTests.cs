using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using RoutePuzzleSample;
using RoutePuzzleSample.Authoring;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace RoutePuzzleSample.Tests;

// PREPARED, UNRUN: public APIs only, self-authored fixture, no window or network.
public sealed class RouteSampleTests
{
    private static string Example => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixture", "data", "routes.json"));
    private static string Changed(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(Example)!.AsObject(); change(root); return root.ToJsonString();
    }
    private static JsonObject Place(JsonObject root, int index) => root["places"]![index]!.AsObject();
    private sealed class Fixture : IDisposable
    {
        private readonly string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("ROUTE_SAMPLE_TEST_ROOT") ?? Path.GetTempPath());
        public string Root { get; }
        public string Data => Path.Combine(Root, "data");
        public string Schemas => Path.Combine(Root, "schemas");
        public ProjectPaths Paths => new(Data, Schemas);
        public Fixture(string? data = null)
        {
            Root = Path.Combine(parent, "route-sample-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Data, "routes.json"), data ?? Example);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixture", "schemas", "routes.schema.json"), Path.Combine(Schemas, "routes.schema.json"));
        }
        public void Dispose()
        {
            var absolute = Path.GetFullPath(Root);
            var prefix = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(absolute).StartsWith("route-sample-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside this fixture's fresh child folder.");
            Directory.Delete(absolute, recursive: true);
        }
    }
    private sealed class Progress : IProgress<CheckProgress>
    { public void Report(CheckProgress value) { } }

    [Fact]
    public void Catalog_has_a_four_energy_winning_route_and_no_editor_reference()
    {
        var catalog = RouteCatalog.Read(Example);
        Assert.Equal(4, catalog.MinimumCost()); Assert.Equal(5, catalog.Energy);
        Assert.DoesNotContain(typeof(RouteCatalog).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Brutal.Authoring", StringComparison.Ordinal));
    }

    [Fact]
    public void Winning_and_losing_routes_leave_the_catalog_unchanged()
    {
        var catalog = RouteCatalog.Read(Example); var game = new RouteGame(catalog);
        Assert.False(game.Choose("summit")); Assert.Equal("trailhead", game.Current); Assert.Equal(5, game.Energy);
        Assert.True(game.Choose("brook")); Assert.True(game.Choose("summit"));
        Assert.True(game.Won); Assert.False(game.Lost); Assert.Equal(1, game.Energy);
        Assert.False(game.Choose("brook")); Assert.Equal(new[] { "trailhead", "brook", "summit" }, game.Visited);
        var trap = new RouteGame(catalog); Assert.True(trap.Choose("ridge"));
        Assert.True(trap.Lost); Assert.False(trap.Choose("summit")); Assert.Equal(2, trap.Energy);
        Assert.Equal(5, catalog.Energy); Assert.Equal("trailhead", catalog.Start);
    }

    [Theory]
    [InlineData("duplicate", "/places/1/id")]
    [InlineData("unknown", "/places/0/exits/0/to")]
    [InlineData("budget", "/energy")]
    [InlineData("start", "/start")]
    [InlineData("same", "/goal")]
    [InlineData("goal-exit", "/places/3/exits")]
    [InlineData("repeat-exit", "/places/0/exits/1/to")]
    public void Native_rules_and_adapter_return_the_same_field_and_message(string problem, string at)
    {
        var text = Changed(root =>
        {
            switch (problem)
            {
                case "duplicate": Place(root, 1)["id"] = "trailhead"; break;
                case "unknown": Place(root, 0)["exits"]![0]!["to"] = "missing"; break;
                case "budget": root["energy"] = 1; break;
                case "start": root["start"] = "missing"; break;
                case "same": root["goal"] = "trailhead"; break;
                case "goal-exit": Place(root, 3)["exits"]!.AsArray().Add(new JsonObject { ["to"] = "brook", ["cost"] = 1 }); break;
                case "repeat-exit": Place(root, 0)["exits"]![1]!["to"] = "brook"; break;
            }
        });
        var native = Assert.Throws<RouteProblem>(() => RouteCatalog.Read(text));
        var issue = Assert.Single(new RouteRules().Validate(new DataFormat().Read(text)));
        Assert.Equal(at, native.At); Assert.Equal(native.At, issue.Path); Assert.Equal(native.Message, issue.Message);
    }

    [Fact]
    public void Schema_refuses_an_out_of_range_cost_and_native_refuses_a_dangling_reference()
    {
        using var shape = new Fixture(Changed(root => Place(root, 0)["exits"]![0]!["cost"] = 0));
        Assert.False(DataProject.Open(shape.Data, shape.Schemas).Find("routes.json")!.Open());
        using var links = new Fixture(Changed(root => Place(root, 0)["exits"]![0]!["to"] = "missing"));
        Assert.True(DataProject.Open(links.Data, links.Schemas).Find("routes.json")!.Open());
        var file = new RouteAdapter().Open(links.Paths).Find("routes.json")!;
        Assert.False(file.Open()); Assert.Contains(file.Problems, i => i.Message.Contains("existing place", StringComparison.Ordinal));
    }

    [Fact]
    public void Recognition_selects_only_this_catalog_kind_and_checks_are_registered()
    {
        using var fixture = new Fixture(); var adapter = new RouteAdapter();
        Assert.True(adapter.Recognises(fixture.Paths));
        Assert.Equal("sample.route.check", Assert.Single(adapter.Checks).Definition.Id);
        File.WriteAllText(Path.Combine(fixture.Data, "routes.json"), "{\"kind\":\"other.sample\"}");
        Assert.False(adapter.Recognises(fixture.Paths));
    }

    [Fact]
    public async Task Check_freezes_unsaved_edits_and_reports_native_problems_at_the_field()
    {
        using var fixture = new Fixture(); var project = new RouteAdapter().Open(fixture.Paths);
        var file = project.Find("routes.json")!; Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/energy", JsonSerializer.SerializeToElement(4))));
        var frozen = CheckSnapshot.Capture(project);
        Assert.True(file.Session!.Execute(new SetValue("/energy", JsonSerializer.SerializeToElement(6))));
        var check = new RouteCheck(); var report = await check.RunAsync(frozen, new Progress(), CancellationToken.None);
        Assert.True(report.Complete); Assert.Empty(report.Findings); Assert.Contains("energy 4", report.Summary);
        Assert.Equal(5, RouteCatalog.Read(File.ReadAllText(Path.Combine(fixture.Data, "routes.json"))).Energy);
        File.WriteAllText(Path.Combine(fixture.Data, "routes.json"), Changed(root => root["energy"] = 1));
        var bad = new RouteAdapter().Open(fixture.Paths);
        var failed = await check.RunAsync(CheckSnapshot.Capture(bad), new Progress(), CancellationToken.None);
        Assert.True(failed.Complete); var finding = Assert.Single(failed.Findings);
        var place = Assert.IsType<CheckPlace>(finding.Place);
        Assert.Equal("/energy", place.At); Assert.Equal("routes.json", place.File);
        Assert.NotEmpty(finding.Next);
    }

    [Fact]
    public async Task Check_cancellation_and_missing_catalog_never_claim_a_clean_result()
    {
        using var fixture = new Fixture(); File.Delete(Path.Combine(fixture.Data, "routes.json"));
        var snapshot = CheckSnapshot.Capture(new RouteAdapter().Open(fixture.Paths)); var check = new RouteCheck();
        var report = await check.RunAsync(snapshot, new Progress(), CancellationToken.None);
        Assert.False(report.Complete); Assert.Contains("No routes.json", report.Summary);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.RunAsync(snapshot, new Progress(), cancel.Token));
    }

    [Fact]
    public void Play_uses_unsaved_values_and_Stop_preserves_draft_undo_and_disk()
    {
        using var fixture = new Fixture(); using var host = new HeadlessHost(1000, 700);
        var adapter = new RouteAdapter(); var project = adapter.Open(fixture.Paths);
        using var workspace = new DataWorkspace(project, configure: adapter.Configure);
        Assert.True(workspace.Show("routes.json")); var editor = workspace.Window!.Editor;
        Assert.True(editor.Execute(new SetValue("/energy", JsonSerializer.SerializeToElement(4))));
        var draft = new DataFormat().Write(editor.Session.Current);
        Assert.True(editor.Play("sample.route.play")); var play = Assert.IsType<RoutePlay>(editor.Game);
        Assert.Equal(4, play.Game.Energy); Assert.True(play.Choose("brook")); Assert.True(play.Choose("summit"));
        Assert.True(play.Game.Won); Assert.Equal(0, play.Game.Energy);
        Assert.False(editor.Execute(new SetValue("/energy", JsonSerializer.SerializeToElement(7))));
        Assert.True(editor.Stop()); Assert.True(play.Stopped); Assert.False(play.Choose("brook"));
        play.Dispose(); Assert.Equal("Stopped.", play.Status);
        Assert.Equal(draft, new DataFormat().Write(editor.Session.Current)); Assert.True(editor.Session.Dirty);
        Assert.Equal(5, RouteCatalog.Read(File.ReadAllText(Path.Combine(fixture.Data, "routes.json"))).Energy);
        Assert.True(editor.Session.Undo()); Assert.Equal(5, editor.Session.Current.Root.GetProperty("energy").GetInt32());
    }

    [Fact]
    public void Workspace_disposal_stops_Play_and_play_view_draws_only_inside_the_headless_host()
    {
        using var fixture = new Fixture(); using var host = new HeadlessHost(1000, 700);
        var adapter = new RouteAdapter(); var workspace = new DataWorkspace(adapter.Open(fixture.Paths), configure: adapter.Configure);
        Assert.True(workspace.Show("routes.json")); var editor = workspace.Window!.Editor;
        Assert.True(editor.Play("sample.route.play")); var play = Assert.IsType<RoutePlay>(editor.Game);
        host.Frames(() => { Brutal.ImGuiApi.ImGui.Begin("Route sample"); play.Draw(); Brutal.ImGuiApi.ImGui.End(); }, 2);
        Assert.True(host.Covered(0, 0, 1000, 700) > 100);
        workspace.Dispose(); Assert.True(play.Stopped); Assert.Null(editor.Game);
        Assert.False(play.Choose("brook")); Assert.False(editor.Session.Dirty);
        workspace.Dispose();
    }
}
