using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using App = Brutal.Authoring.Launcher.Program;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED three-file workshop, adapters and faults; BORROWED targets: A073 and public XML comments.
// These are boundary tests, not an in-process security sandbox. Never start a process or a native window.
[Collection(ImGuiCollection.Name)]
public sealed class AdapterAcceptanceTests
{
    private sealed class Trial : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("a073-").FullName;
        public string History => Path.Combine(Root, "settings", "recent.json");
        public ProjectPaths Paths { get; }
        public ProjectPaths Previous { get; }
        public Trial()
        {
            Paths = Make("Copper garden");
            Previous = Make("Previous workshop");
            new RecentProjects(History).Add(Previous);
        }
        private ProjectPaths Make(string name)
        {
            var folder = Path.Combine(Root, name);
            var data = Directory.CreateDirectory(Path.Combine(folder, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(folder, "schemas")).FullName;
            File.WriteAllText(Path.Combine(data, "beacons.json"),
                """{"beacons":[{"id":"red","name":"Red lantern","limit":5,"value":2}]}""");
            File.WriteAllText(Path.Combine(schemas, "beacons.schema.json"),
                """{"type":"object","properties":{"beacons":{"type":"array","items":{"type":"object","required":["id","name","limit","value"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"limit":{"type":"integer"},"value":{"type":"integer","minimum":0}}}}},"required":["beacons"]}""");
            File.WriteAllText(Path.Combine(data, "routes.json"), """{"routes":[{"id":"reed","destination":"red"}]}""");
            File.WriteAllText(Path.Combine(schemas, "routes.schema.json"), """{"type":"object","properties":{"routes":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"destination":{"type":"string"}}}}}}""");
            File.WriteAllText(Path.Combine(data, "workshops.json"), """{"workshops":[{"id":"north","name":"North shed"}]}""");
            File.WriteAllText(Path.Combine(schemas, "workshops.schema.json"), """{"type":"object","properties":{"workshops":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}""");
            var map = Path.Combine(folder, "reference_map.json");
            File.WriteAllText(map, """{"game":"A073 invented workshop","labels":[{"file":"beacons.json","list":"/beacons","field":"name"}],"references":[{"file":"routes.json","at":"/routes/*/destination","target":{"file":"beacons.json","list":"/beacons","key":"id"},"evidence":"Invented workshop route."}]}""");
            return new(data, schemas, map);
        }
        public ProjectLauncher Launcher() => new(new RecentProjects(History));
        public string Put(string name, string content = "A073 self-authored non-assembly text.")
        {
            var p = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
            return p;
        }
        public Dictionary<string, byte[]> Files() =>
            Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories)
                .Where(p => !p.StartsWith(Path.Combine(Root, "settings") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p, File.ReadAllBytes);
        public void SameFiles(Dictionary<string, byte[]> before, bool publishedEmptyCache = false)
        {
            var after = Files();
            IEnumerable<string> expected = before.Keys;
            if (publishedEmptyCache)
            {
                var projectRoot = Path.GetDirectoryName(Paths.Data)!;
                var published = Brutal.Authoring.Runtime.ProjectAssets.Open(projectRoot);
                Assert.Empty(published.ArtifactNames);
                var cache = Path.Combine(projectRoot, ".bed", "cache");
                var generation = Path.Combine(cache, "generations", published.Id);
                var cacheFiles = new[] { Path.Combine(cache, "current.json"), Path.Combine(cache, "owner.lock"),
                    Path.Combine(generation, "manifest.json"), Path.Combine(generation, "owner.txt") };
                Assert.Equal(cacheFiles.Order(), Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Order());
                expected = expected.Concat(cacheFiles.Where(p => p.EndsWith(".json", StringComparison.Ordinal)));
            }
            Assert.Equal(expected.Order(), after.Keys.Order());
            foreach (var p in before.Keys) Assert.Equal(before[p], after[p]);
        }
        public void HistorySurvives() =>
            Assert.Contains(new RecentProjects(History).Items, p => p == Previous);
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a073-", StringComparison.Ordinal))
                throw new InvalidOperationException("A073 cleanup outside its own temp folder.");
            // Loading a copied DLL can keep it locked on Windows. Nothing outside this checked trial is removed.
            try { Directory.Delete(full, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Instrumented(string name = "Garden adapter") : IProjectAdapter
    {
        public Func<string>? GetName;
        public Func<ProjectPaths, bool>? Recognise;
        public Func<ProjectPaths, DataProject>? OpenProject;
        public Func<IEnumerable<ICheck>>? GetChecks;
        public Action<EditorModel<DataDocument>, DataTableView>? Setup;
        public int Recognised, Opened, Configured;
        public string Name => GetName?.Invoke() ?? name;
        public bool Recognises(ProjectPaths p) { Recognised++; return Recognise?.Invoke(p) ?? true; }
        public DataProject Open(ProjectPaths p) { Opened++; return OpenProject?.Invoke(p) ?? DataProject.Open(p.Data, p.Schemas); }
        public IEnumerable<ICheck> Checks => GetChecks?.Invoke() ?? [];
        public void Configure(EditorModel<DataDocument> e, DataTableView t) { Configured++; Setup?.Invoke(e, t); }
    }
    private sealed class GardenRule : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) =>
            d.Root.GetProperty("beacons")[0].GetProperty("value").GetInt32() > d.Root.GetProperty("beacons")[0].GetProperty("limit").GetInt32()
                ? [Issue.Error("Beacon value exceeds its local limit; lower value or raise limit.", "/beacons/0", "/beacons/0/value")] : [];
    }
    private sealed class GardenCheck(string id = "garden.audit", bool broken = false) : ICheck
    {
        public CheckDefinition Definition => new(id, "Garden consistency", "Checks the self-authored garden snapshot.", TimeSpan.FromSeconds(3));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            if (broken) throw new InvalidOperationException("A073 check execution failed");
            var draft = snapshot.Text("data/beacons.json")!;
            return Task.FromResult(new CheckReport(true, [], draft.Contains("Red lantern") ? "Saw garden snapshot" : "Saw edited snapshot"));
        }
    }
    private static string TestDll => typeof(AdapterAcceptanceTests).Assembly.Location;
    private static SetValue Value(int n) => new("/beacons/0/value", JsonSerializer.SerializeToElement(n));
    private static string LoadedNames(IReadOnlyList<IProjectAdapter> all) => string.Join(", ", all.Select(a => a.GetType().FullName));
    private static ProjectLauncher With(Trial r, IProjectAdapter a)
    {
        var l = r.Launcher(); l.Adapter = a; return l;
    }
    private static string DrawText(Action draw)
    {
        using var host = new HeadlessHost(1500, 900);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(draw, 3);
        // HeadlessHost installs its own clipboard; this never reads or writes the desktop clipboard.
        host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? "";
    }
    private static string Built(string project, string file)
    {
        var artifacts = Environment.GetEnvironmentVariable("ArtifactsPath");
        if (artifacts is not null && Directory.Exists(Path.Combine(artifacts, "bin", project)))
            return Directory.GetFiles(Path.Combine(artifacts, "bin", project), file, SearchOption.AllDirectories).Order().First();
        for (var d = new DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Brutal.Authoring.slnx")))
            {
                var bin = Path.Combine(d.FullName, "samples", project, "bin");
                if (Directory.Exists(bin)) return Directory.GetFiles(bin, file, SearchOption.AllDirectories).Order().First();
            }
        throw new InvalidOperationException("Build the solution's existing " + project + " project before these tests.");
    }
    private static void Recovery(Trial r, ProjectLauncher l, Dictionary<string, byte[]> before)
    {
        r.SameFiles(before); r.HistorySurvives();
        l.Close(discard: true); l.NoAdapter = true;
        Assert.True(l.Open(r.Paths), l.Said);
        Assert.Null(l.Using);
        Assert.Equal(3, l.Workspace!.Project.Files.Count);
        r.SameFiles(before); r.HistorySurvives();
    }

    // Source: Adapters XML "every public IProjectAdapter with a constructor that takes nothing."
    [Fact] public void Load_SeveralExportedAdaptersInOneAssembly_AllArrive()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems);
        Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073SecondAdapter));
        Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073ConstructorAdapter));
    }
    [Fact] public void Load_NoInput_ReturnsTwoEmptyLists()
    {
        var result = Adapters.Load([]); Assert.Empty(result.Loaded); Assert.Empty(result.Problems);
    }
    [Fact] public void Load_OrdinaryManagedLibrary_ExplainsTheRequiredInterfaceAndConstructor()
    {
        var result = Adapters.Load([typeof(DataDocument).Assembly.Location]);
        Assert.Empty(result.Loaded); Assert.Contains(result.Problems, p => p.Contains("IProjectAdapter") && p.Contains("constructor"));
    }
    [Fact] public void Load_MissingAssembly_IdentifiesItsFile()
    {
        using var r = new Trial(); var path = Path.Combine(r.Root, "AbsentGardenAdapter.dll");
        var result = Adapters.Load([path]);
        Assert.Empty(result.Loaded); Assert.Contains(result.Problems, p => p.Contains("AbsentGardenAdapter.dll") && p.Contains("load"));
    }
    [Fact] public void Load_NonManagedBytes_AreReturnedAsProblems()
    {
        using var r = new Trial(); var path = r.Put("TextGardenAdapter.dll");
        var result = Adapters.Load([path]); Assert.Empty(result.Loaded);
        Assert.Contains(result.Problems, p => p.Contains("TextGardenAdapter.dll"));
    }
    [Fact] public void Load_EmptyPath_IsReportedWithoutAnException()
    {
        (IReadOnlyList<IProjectAdapter> Loaded, IReadOnlyList<string> Problems) result = ([], []);
        var ex = Record.Exception(() => result = Adapters.Load([""]));
        Assert.Null(ex); Assert.Empty(result.Loaded); Assert.NotEmpty(result.Problems);
    }
    [Fact] public void Load_AbstractType_IsNeverInstantiated()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems); Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.DoesNotContain(result.Loaded, a => a.GetType() == typeof(A073AbstractAdapter));
    }
    [Fact] public void Load_InternalType_IsNeverExposed()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems); Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.DoesNotContain(result.Loaded, a => a.GetType() == typeof(A073InternalAdapter));
    }
    [Fact] public void Load_PublicNestedTypeInsideInternalContainer_IsNotAnExport()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems); Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.DoesNotContain(result.Loaded, a => a.GetType() == typeof(A073InternalContainer.NestedAdapter));
    }
    [Fact] public void Load_RequiredArgumentConstructor_IsIgnored()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems); Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.DoesNotContain(result.Loaded, a => a.GetType() == typeof(A073ArgumentAdapter));
    }
    [Fact] public void Load_PrivateEmptyConstructor_IsNotAnEmptyPublicConstructor()
    {
        var result = Adapters.Load([TestDll]);
        Assert.Empty(result.Problems); Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073FirstAdapter));
        Assert.DoesNotContain(result.Loaded, a => a.GetType() == typeof(A073PrivateConstructorAdapter));
    }
    [Fact] public void Load_ThrowingConstructor_IsReportedWithItsOriginalReason()
    {
        A073ConstructorAdapter.Throw = true;
        try
        {
            var result = Adapters.Load([TestDll]);
            Assert.Contains(result.Problems, p => p.Contains("A073 constructor broke"));
        }
        finally { A073ConstructorAdapter.Throw = false; }
    }
    // Source: A073 "every public IProjectAdapter with an empty constructor loads; problems are returned, never thrown."
    [Fact] public void Load_OneThrowingConstructor_DoesNotDiscardOtherAdaptersInTheSameDll()
    {
        A073ConstructorAdapter.Throw = true;
        try
        {
            var result = Adapters.Load([TestDll]);
            Assert.Contains(result.Problems, p => p.Contains("A073 constructor broke"));
            Assert.True(result.Loaded.Any(a => a.GetType() == typeof(A073FirstAdapter)), LoadedNames(result.Loaded));
            Assert.Contains(result.Loaded, a => a.GetType() == typeof(A073SecondAdapter));
        }
        finally { A073ConstructorAdapter.Throw = false; }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Load_BadDllAmongTwoGoodDlls_KeepsBothGoodOnes(bool badFirst)
    {
        using var r = new Trial(); var bad = r.Put("BrokenAdapter.dll");
        var first = Built("StarterAdapter", "StarterAdapter.dll");
        var second = Built("CheckersAdapter", "CheckersAdapter.dll");
        var result = Adapters.Load(badFirst ? [bad, first, second] : [first, bad, second]);
        Assert.Contains(result.Loaded, a => a.Name.StartsWith("Starter"));
        Assert.Contains(result.Loaded, a => a.Name == "Checkers");
        Assert.Single(result.Problems); Assert.Contains("BrokenAdapter.dll", result.Problems[0]);
    }
    [Fact] public void Load_RealStarterSharesTheAppsInterfaceIdentity()
    {
        var result = Adapters.Load([Built("StarterAdapter", "StarterAdapter.dll")]);
        var a = Assert.Single(result.Loaded); Assert.Empty(result.Problems);
        Assert.True(typeof(IProjectAdapter).IsAssignableFrom(a.GetType()));
        using var r = new Trial(); Assert.False(a.Recognises(r.Paths));
    }
    [Fact] public void Load_RealCheckersCanResolveItsRulesWithoutBecomingRequiredForPlainData()
    {
        var result = Adapters.Load([Built("CheckersAdapter", "CheckersAdapter.dll")]);
        using var r = new Trial(); var a = Assert.Single(result.Loaded);
        Assert.Empty(result.Problems); Assert.False(a.Recognises(r.Paths));
        var l = r.Launcher(); l.Loaded = result.Loaded;
        Assert.True(l.Open(r.Paths), l.Said); Assert.Null(l.Using);
    }
    [Fact] public void In_NestedAndTopLevelNamedFiles_AreBothDiscovered()
    {
        using var r = new Trial(); var one = r.Put("adapters/TopAdapter.dll"); var two = r.Put("adapters/deeper/NestedAdapter.dll");
        Assert.Equal(new[] { one, two }.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase), Adapters.In(Path.Combine(r.Root, "adapters")).Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase));
    }
    [Fact] public void In_NonAdapterSuffixAndNonDllFiles_AreIgnored()
    {
        using var r = new Trial(); r.Put("adapters/Adapter.dll.txt"); r.Put("adapters/Other.dll"); r.Put("adapters/Adapter.cs");
        Assert.Empty(Adapters.In(Path.Combine(r.Root, "adapters")));
    }
    [Fact] public void In_DirectoryNamedLikeAnAdapter_IsNotADll()
    {
        using var r = new Trial(); Directory.CreateDirectory(Path.Combine(r.Root, "adapters", "NotAFileAdapter.dll"));
        Assert.Empty(Adapters.In(Path.Combine(r.Root, "adapters")));
    }
    [Fact] public void In_MissingFolder_IsEmpty()
    {
        using var r = new Trial(); Assert.Empty(Adapters.In(Path.Combine(r.Root, "never-created")));
    }
    [Fact] public void In_DiscoversByName_LoadExplainsBadContents()
    {
        using var r = new Trial(); r.Put("adapters/DiscoveredAdapter.dll");
        var result = Adapters.Load(Adapters.In(Path.Combine(r.Root, "adapters")));
        Assert.Empty(result.Loaded); Assert.Contains(result.Problems, p => p.Contains("DiscoveredAdapter.dll"));
    }

    // Source: ProjectLauncher.Adapter "the first loaded adapter that recognises it, or none."
    [Fact] public void Recognition_TwoMatches_FirstOneConfiguresAllFiles_SecondNeverOpens()
    {
        using var r = new Trial(); var first = new Instrumented("First"); var second = new Instrumented("Second");
        var l = r.Launcher(); l.Loaded = [first, second]; Assert.True(l.Open(r.Paths), l.Said);
        foreach (var f in l.Workspace!.Project.Files) Assert.True(l.Workspace.Show(f.Name));
        Assert.Same(first, l.Using); Assert.Equal(3, first.Configured);
        Assert.Equal(0, second.Opened); Assert.Equal(0, second.Configured);
    }
    [Fact] public void Recognition_NoMatch_StillGetsThreeFilesAndCrossFileLinks()
    {
        using var r = new Trial(); var a = new Instrumented { Recognise = _ => false };
        var l = r.Launcher(); l.Loaded = [a]; Assert.True(l.Open(r.Paths), l.Said);
        Assert.Null(l.Using); Assert.Equal(0, a.Opened); Assert.NotNull(l.Workspace!.Map);
        Assert.True(l.Workspace.GoTo(new ReferenceTarget("routes.json", "/routes/0")));
        Assert.True(l.Workspace.GoTo(new ReferenceTarget("beacons.json", "/beacons/0")));
        Assert.True(l.Workspace.Back()); Assert.Equal("routes.json", l.Workspace.Active!.Name);
    }
    // Source: goal 1 "Game adapters ... are optional extras it loads, never a requirement."
    [Fact] public void NoAdapters_ProjectCanEditUndoSaveAndReopenAcrossSeveralFiles()
    {
        using var r = new Trial(); var l = r.Launcher(); Assert.Empty(l.Loaded); Assert.True(l.Open(r.Paths), l.Said);
        var w = l.Workspace!; Assert.True(w.Show("beacons.json")); var f = w.Active!;
        Assert.True(f.Session!.Execute(Value(4))); Assert.True(f.Session.Undo()); Assert.True(f.Session.Redo());
        Assert.True(w.SaveAll()); Assert.True(l.Close()); Assert.True(l.Open(r.Paths));
        Assert.Equal(4, l.Workspace!.Active!.Session!.Current.At("/beacons/0/value")!.Value.GetInt32());
    }
    [Fact] public void ExplicitChoice_DoesNotNeedRecognition_AndCannotCallOtherAdapters()
    {
        using var r = new Trial(); var chosen = new Instrumented { Recognise = _ => throw new Exception("must not ask") };
        var other = new Instrumented { Recognise = _ => throw new Exception("must not ask others") };
        var l = With(r, chosen); l.Loaded = [other]; Assert.True(l.Open(r.Paths), l.Said);
        Assert.Same(chosen, l.Using); Assert.Equal(0, chosen.Recognised); Assert.Equal(0, other.Recognised);
    }
    [Fact] public void PlainChoice_BypassesAllMembersEvenOnAnExplicitBrokenAdapter()
    {
        using var r = new Trial(); var a = new Instrumented
        {
            GetName = () => throw new Exception("Name must not run"), Recognise = _ => throw new Exception("Recognises must not run"),
            OpenProject = _ => throw new Exception("Open must not run"), GetChecks = () => throw new Exception("Checks must not run"),
            Setup = (_, _) => throw new Exception("Configure must not run")
        };
        var l = With(r, a); l.Loaded = [a]; l.NoAdapter = true;
        Assert.True(l.Open(r.Paths), l.Said); Assert.Null(l.Using); Assert.Equal(0, a.Opened);
    }
    [Fact] public void RecognisesThrows_PlainFallbackLeavesFilesAndPreviousHistoryIntact()
    {
        using var r = new Trial(); var before = r.Files(); var a = new Instrumented { Recognise = _ => throw new InvalidOperationException("A073 recognition broke") };
        var l = r.Launcher(); l.Loaded = [a]; Assert.True(l.Open(r.Paths), l.Said);
        Assert.Null(l.Using); Assert.Equal(0, a.Opened); Assert.Equal(3, l.Workspace!.Project.Files.Count);
        r.SameFiles(before); r.HistorySurvives();
    }
    [Fact] public void RecognisesThrows_LaterMatchingAdapterCanStillConfigureAndEdit()
    {
        using var r = new Trial(); var bad = new Instrumented { Recognise = _ => throw new Exception("probe broke") };
        var good = new Instrumented("Later");
        var l = r.Launcher(); l.Loaded = [bad, good]; Assert.True(l.Open(r.Paths), l.Said);
        Assert.Same(good, l.Using); Assert.Equal(0, bad.Configured);
        Assert.True(l.Workspace!.Active!.Session!.Execute(Value(3)));
    }

    // A270: public recognition causes and the already-safe fallback, without loader or interface changes.
    [Theory]
    [InlineData("unreadable", false)] [InlineData("unreadable", true)]
    [InlineData("invalid", false)] [InlineData("invalid", true)]
    [InlineData("unavailable", false)] [InlineData("unavailable", true)]
    [InlineData("unexpected", false)] [InlineData("unexpected", true)]
    public void RecognitionFault_ReportsTheActualCauseWhileKeepingFallback(string cause, bool laterMatch)
    {
        // Synthetic callback faults only: no permission change, malformed import, external file or process.
        Exception failure = cause switch
        {
            "unreadable" => new IOException("A270 metadata unreadable"),
            "invalid" => new FormatException("A270 marker format unknown"),
            "unavailable" => new UnauthorizedAccessException("A270 metadata unavailable"),
            _ => new InvalidOperationException("A270 recognition callback broke"),
        };
        using var r = new Trial(); var before = r.Files();
        var broken = new Instrumented("Broken recogniser") { Recognise = _ => throw failure };
        var later = new Instrumented("Later matching adapter");
        using var l = r.Launcher();
        l.Loaded = laterMatch ? [broken, later] : [broken];

        Assert.True(l.Open(r.Paths), l.Said);
        if (laterMatch) Assert.Same(later, l.Using); else Assert.Null(l.Using);
        Assert.Equal(1, broken.Recognised);
        Assert.Equal(0, broken.Opened);
        Assert.Equal(0, broken.Configured);
        Assert.Equal(laterMatch ? 1 : 0, later.Opened);
        Assert.Equal(3, l.Workspace!.Project.Files.Count);
        Assert.Contains("Broken recogniser", l.Said);
        Assert.Contains(failure.Message, l.Said);
        Assert.Contains("so it wasn't used", l.Said);
        Assert.Contains("Broken recogniser", l.Workspace.Said);
        Assert.Contains(failure.Message, l.Workspace.Said);
        r.SameFiles(before); r.HistorySurvives();
    }

    [Fact]
    public void RecognitionFault_IsVisibleInTheOpenedWorkspace()
    {
        using var r = new Trial(); var before = r.Files();
        var broken = new Instrumented("Visible recogniser")
        { Recognise = _ => throw new InvalidOperationException("A270 visible cause") };
        using var l = r.Launcher(); l.Loaded = [broken];
        Assert.True(l.Open(r.Paths), l.Said);

        // Existing owned offscreen host/clipboard seam; never the desktop clipboard or an OS window.
        var shown = DrawText(l.Draw);
        Assert.Contains("Visible recogniser", shown);
        Assert.Contains("A270 visible cause", shown);
        Assert.Contains("so it wasn't used", shown);
        Assert.Null(l.Using); Assert.Equal(0, broken.Opened);
        r.SameFiles(before); r.HistorySurvives();
    }

    [Fact]
    public void RecognitionFault_AThrowingNameStillIdentifiesTheTypeAndOriginalCause()
    {
        using var r = new Trial(); var before = r.Files();
        var broken = new Instrumented
        {
            GetName = () => throw new InvalidOperationException("A270 name accessor unavailable"),
            Recognise = _ => throw new InvalidOperationException("A270 original recognition cause"),
        };
        using var l = r.Launcher(); l.Loaded = [broken];
        Assert.True(l.Open(r.Paths), l.Said);

        Assert.Null(l.Using); Assert.Equal(0, broken.Opened);
        Assert.Contains(nameof(Instrumented), l.Said);
        Assert.Contains("A270 original recognition cause", l.Said);
        Assert.Contains(nameof(Instrumented), l.Workspace!.Said);
        Assert.Contains("A270 original recognition cause", l.Workspace.Said);
        r.SameFiles(before); r.HistorySurvives();
    }

    [Fact]
    public void RecognitionFault_DoesNotLeakIntoTheNextCleanOpen()
    {
        using var r = new Trial(); var before = r.Files();
        using var l = r.Launcher();
        l.Loaded = [new Instrumented("Previous broken recogniser")
        { Recognise = _ => throw new InvalidOperationException("A270 previous recognition cause") }];
        Assert.True(l.Open(r.Paths), l.Said);
        Assert.Contains("A270 previous recognition cause", l.Said);
        Assert.Contains("A270 previous recognition cause", l.Workspace!.Said);

        l.Loaded = [];
        Assert.True(l.Open(r.Previous), l.Said);
        Assert.Null(l.Using);
        Assert.DoesNotContain("Previous broken recogniser", l.Said);
        Assert.DoesNotContain("A270 previous recognition cause", l.Said);
        Assert.DoesNotContain("A270 previous recognition cause", l.Workspace!.Said);
        r.SameFiles(before); r.HistorySurvives();
    }

    [Fact]
    public void RecognitionDeclined_IsPlainFallbackWithoutAFaultDiagnostic()
    {
        using var r = new Trial(); var before = r.Files();
        var declined = new Instrumented
        {
            Recognise = _ => false,
            GetName = () => throw new InvalidOperationException("A270 unused name must not run"),
        };
        using var l = r.Launcher(); l.Loaded = [declined];
        Assert.True(l.Open(r.Paths), l.Said);

        Assert.Null(l.Using); Assert.Equal(1, declined.Recognised); Assert.Equal(0, declined.Opened);
        Assert.DoesNotContain("couldn't tell whether", l.Said);
        Assert.DoesNotContain("couldn't tell whether", l.Workspace!.Said);
        Assert.DoesNotContain("A270 unused name", l.Said);
        r.SameFiles(before); r.HistorySurvives();
    }

    // Source: A073 "an adapter that throws from each member ... the app, the recent list and the data files survive."
    [Fact] public void NameThrows_DuringOpen_IsContainedAndPlainRecoveryStillWorks()
    {
        using var r = new Trial(); var before = r.Files(); var a = new Instrumented { GetName = () => throw new InvalidOperationException("A073 Name broke") };
        var l = With(r, a); var ex = Record.Exception(() => l.Open(r.Paths)); var diagnostic = l.Said;
        Recovery(r, l, before);
        Assert.True(ex is null, "Name escaped Open: " + ex); Assert.Contains("Name broke", diagnostic);
    }
    [Fact] public void NameThrows_OnTheSelectedStartScreen_IsContained()
    {
        using var r = new Trial(); var before = r.Files(); var a = new Instrumented { GetName = () => throw new InvalidOperationException("A073 Name broke") };
        var l = With(r, a); l.Loaded = [a];
        var shown = "";
        var ex = Record.Exception(() => { shown = DrawText(l.Draw); });
        r.SameFiles(before); r.HistorySurvives();
        Assert.True(ex is null, "Name escaped the start-screen draw: " + ex);
        Assert.Contains("Available game connections", shown);
        Assert.Contains("Instrumented", shown);
        Assert.Contains("couldn't give its name", shown);
        Assert.Contains("A073 Name broke", shown);
    }
    [Theory] [InlineData("unexpected")] [InlineData("argument")]
    public void OpenThrows_ProgrammingFault_IsContainedAndCanBeRetriedPlain(string fault)
    {
        using var r = new Trial(); var before = r.Files();
        var a = new Instrumented { OpenProject = _ => throw (fault == "argument" ? new ArgumentException("A073 Open broke") : new InvalidOperationException("A073 Open broke")) };
        var l = With(r, a); var ex = Record.Exception(() => l.Open(r.Paths)); var diagnostic = l.Said;
        Recovery(r, l, before); Assert.True(ex is null, "Open escaped: " + ex); Assert.Contains("Open broke", diagnostic);
    }
    [Fact] public void OpenThrows_ExpectedReadError_PreservesAnAlreadyOpenCleanWorkspace()
    {
        using var r = new Trial(); var before = r.Files(); var l = r.Launcher(); Assert.True(l.Open(r.Previous));
        var old = l.Workspace;
        l.Adapter = new Instrumented("Read trouble") { OpenProject = _ => throw new IOException("A073 catalog absent; choose plain data.") };
        Assert.False(l.Open(r.Paths)); Assert.Same(old, l.Workspace); Assert.Equal(r.Previous, l.Current);
        Assert.Contains("choose plain data", l.Said); r.SameFiles(before); r.HistorySurvives();
    }
    [Fact] public void ChecksGetterThrows_IsContainedAndPlainRecoveryStillWorks()
    {
        using var r = new Trial(); var before = r.Files();
        var l = With(r, new Instrumented { GetChecks = () => throw new InvalidOperationException("A073 Checks broke") });
        var ex = Record.Exception(() => l.Open(r.Paths)); var diagnostic = l.Said;
        Recovery(r, l, before); Assert.True(ex is null, "Checks getter escaped: " + ex); Assert.Contains("Checks broke", diagnostic);
    }
    private static IEnumerable<ICheck> FailingEnumeration()
    {
        yield return new GardenCheck();
        throw new InvalidOperationException("A073 Checks enumeration broke");
    }
    [Fact] public void ChecksEnumerationThrows_DoesNotRegisterAPartialCollectionOrEscape()
    {
        using var r = new Trial(); var before = r.Files();
        var l = With(r, new Instrumented { GetChecks = FailingEnumeration });
        var ex = Record.Exception(() => l.Open(r.Paths)); var diagnostic = l.Said;
        if (l.Workspace is { } w) Assert.DoesNotContain(w.Checks.Registered, d => d.Id == "garden.audit");
        Recovery(r, l, before); Assert.True(ex is null, "Checks enumeration escaped: " + ex); Assert.Contains("Checks enumeration broke", diagnostic);
    }
    // Source: ProjectLauncher XML "Every refusal says what's wrong and what to do."
    [Fact] public void InvalidAdapterChecks_TheirDiagnosticIsNotOverwrittenBySuccessfulOpen()
    {
        using var r = new Trial(); var l = With(r, new Instrumented("Duplicate checks") { GetChecks = () => [new GardenCheck("links.all")] });
        l.Open(r.Paths);
        var visible = l.Said + "\n" + (l.Workspace is null ? "" : l.Workspace.Said);
        Assert.Contains("checks", visible, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("links.all", visible);
        Assert.DoesNotContain(l.Workspace!.Checks.Registered, d => d.Id == "garden.audit");
    }
    [Fact] public void ConfigureThrows_OnFirstFile_IsContainedAndPlainRecoveryStillWorks()
    {
        using var r = new Trial(); var before = r.Files();
        var l = With(r, new Instrumented { Setup = (_, _) => throw new InvalidOperationException("A073 Configure broke") });
        var ex = Record.Exception(() => l.Open(r.Paths)); var diagnostic = l.Said;
        Recovery(r, l, before); Assert.True(ex is null, "Configure escaped Open: " + ex); Assert.Contains("Configure broke", diagnostic);
    }
    [Fact] public void ConfigureThrows_OnLaterFile_DoesNotLoseFirstFilesDraftOrHistory()
    {
        using var r = new Trial(); var before = r.Files(); var calls = 0;
        var l = With(r, new Instrumented { Setup = (_, _) => { if (++calls > 1) throw new InvalidOperationException("A073 later Configure broke"); } });
        Assert.True(l.Open(r.Paths)); var w = l.Workspace!; var first = w.Active!;
        Assert.True(first.Session!.Execute(Value(4))); var text = first.Session.Current.Root.GetRawText(); var undo = first.Session.History.UndoCount;
        var ex = Record.Exception(() => w.Show("routes.json")); var diagnostic = w.Said;
        Assert.Equal(text, first.Session.Current.Root.GetRawText()); Assert.Equal(undo, first.Session.History.UndoCount);
        Assert.True(first.Dirty); r.SameFiles(before); r.HistorySurvives();
        Assert.True(ex is null, "Later Configure escaped file selection: " + ex); Assert.Contains("Configure broke", diagnostic);
    }

    private static Instrumented RulesAdapter() => new()
    {
        OpenProject = p => DataProject.Open(p.Data, p.Schemas, rules: name => name == "beacons.json" ? new GardenRule() : null)
    };
    // Source: DataProject.Open rules "an edit the game would refuse is refused here, with the game's message".
    [Fact] public void RuleRefusal_LeavesDocumentUndoJournalAndDiskUnchanged()
    {
        using var r = new Trial(); var before = r.Files(); var l = With(r, RulesAdapter()); Assert.True(l.Open(r.Paths));
        var s = l.Workspace!.Active!.Session!; var text = s.Current.Root.GetRawText(); var undo = s.History.UndoCount; var journal = JsonSerializer.Serialize(s.Journal);
        Assert.False(s.Execute(Value(6))); Assert.Contains(s.LastIssues, i => i.Message.Contains("local limit") && i.Path == "/beacons/0/value");
        Assert.Equal(text, s.Current.Root.GetRawText()); Assert.Equal(undo, s.History.UndoCount); Assert.Equal(journal, JsonSerializer.Serialize(s.Journal));
        Assert.False(s.Dirty); r.SameFiles(before);
    }
    [Fact] public void RuleDoesNotLeakToOtherFiles_AndValidChangesStillUndo()
    {
        using var r = new Trial(); var l = With(r, RulesAdapter()); Assert.True(l.Open(r.Paths));
        var s = l.Workspace!.Active!.Session!; Assert.True(s.Execute(Value(5))); Assert.True(s.Undo()); Assert.True(s.Redo());
        Assert.True(l.Workspace.Show("workshops.json"));
        Assert.True(l.Workspace.Active!.Session!.Execute(new SetValue("/workshops/0/name", JsonSerializer.SerializeToElement("West shed"))));
    }
    // Source: IProjectAdapter.Checks "shown in the Checks panel beside the tools' link check."
    [Fact] public void AdapterCheck_IsOfferedBesideLinkCheck_AndItsLabelsActuallyDraw()
    {
        using var r = new Trial(); var l = With(r, new Instrumented { GetChecks = () => [new GardenCheck()] }); Assert.True(l.Open(r.Paths));
        var w = l.Workspace!;
        Assert.Contains(w.ChecksPanel.Commands!, c => c.Id == "run.garden.audit");
        Assert.Contains(w.ChecksPanel.Commands!, c => c.Id == "run.links.all");
        var text = DrawText(() => { ImGui.Begin("A073 checks"); w.ChecksPanel.Draw(); ImGui.End(); });
        Assert.Contains("Garden consistency", text); Assert.Contains("Check every link", text, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task AdapterCheck_RunsOnCurrentDraft_WithoutSavingOrChangingHistory()
    {
        using var r = new Trial(); var before = r.Files();
        var l = With(r, new Instrumented { GetChecks = () => [new GardenCheck()] }); Assert.True(l.Open(r.Paths));
        var w = l.Workspace!; var s = w.Active!.Session!;
        Assert.True(s.Execute(new SetValue("/beacons/0/name", JsonSerializer.SerializeToElement("Blue lantern"))));
        var text = s.Current.Root.GetRawText(); var undo = s.History.UndoCount; var journal = JsonSerializer.Serialize(s.Journal);
        Assert.True(w.RunCheck("garden.audit")); var run = w.Checks.Last("garden.audit")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Saw edited snapshot", run.Report!.Summary); Assert.True(run.Clean(w.Project));
        Assert.Equal(text, s.Current.Root.GetRawText()); Assert.Equal(undo, s.History.UndoCount); Assert.Equal(journal, JsonSerializer.Serialize(s.Journal));
        Assert.True(s.Dirty); r.SameFiles(before);
    }
    [Fact] public async Task AdapterChecksOwnExecutionFault_IsAReportNotAnEditorCrash()
    {
        using var r = new Trial(); var before = r.Files();
        var l = With(r, new Instrumented { GetChecks = () => [new GardenCheck(broken: true)] }); Assert.True(l.Open(r.Paths));
        var w = l.Workspace!; Assert.True(w.RunCheck("garden.audit")); var run = w.Checks.Last("garden.audit")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(CheckState.Failed, run.State); Assert.Contains("A073 check execution failed", run.Problem);
        Assert.True(w.Show("routes.json")); r.SameFiles(before); r.HistorySurvives();
    }
    [Fact] public void Configure_OneEditorPerFile_ReusesItsOwnStateOnReturn()
    {
        using var r = new Trial(); var a = new Instrumented
        {
            Setup = (e, _) => e.Add(new Panel("garden.special", "Garden panel", () => { }))
        };
        var l = With(r, a); Assert.True(l.Open(r.Paths)); var w = l.Workspace!; var first = w.Window;
        foreach (var f in w.Project.Files) { Assert.True(w.Show(f.Name)); Assert.Contains(w.Window!.Editor.Panels, p => p.Id == "garden.special"); }
        Assert.True(w.Show("beacons.json")); Assert.Same(first, w.Window); Assert.Equal(3, a.Configured);
    }

    // Source: A073 "--adapter with a bad DLL, from Program's entry point, headless with --shot."
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Program_BadExplicitDll_DiagnosesItAndKeepsStartOrProjectUsable(bool project)
    {
        using var r = new Trial(); var before = r.Files(); var bad = r.Put("FaultyGardenAdapter.dll");
        var shot = Path.Combine(r.Root, "bad-adapter.png"); var o = new StringWriter(); var err = new StringWriter();
        var args = new List<string> { "--adapter", bad, "--settings", Path.GetDirectoryName(r.History)!, "--shot", shot };
        if (project) args.Insert(0, Path.GetDirectoryName(r.Paths.Data)!);
        var ex = Record.Exception(() => Assert.Equal(0, App.Run(args.ToArray(), o, err, Path.Combine(r.Root, "unused-settings"))));
        r.SameFiles(before, publishedEmptyCache: project); r.HistorySurvives(); Assert.Null(ex);
        Assert.Contains("FaultyGardenAdapter.dll", err.ToString()); Assert.True(new FileInfo(shot).Length > 1000);
        if (project) Assert.Contains("Opened Copper garden", o.ToString());
        Assert.False(Directory.Exists(Path.Combine(r.Root, "unused-settings")));
    }
    [Fact] public void Program_TwoBadExplicitDlls_ReturnsBothReasonsAndStillRenders()
    {
        using var r = new Trial(); var bad = r.Put("BadOneAdapter.dll"); var absent = Path.Combine(r.Root, "BadTwoAdapter.dll");
        var err = new StringWriter(); var shot = Path.Combine(r.Root, "two-bad.png");
        Assert.Equal(0, App.Run(["--adapter", bad, "--adapter", absent, "--settings", Path.GetDirectoryName(r.History)!, "--shot", shot], TextWriter.Null, err, Path.Combine(r.Root, "unused")));
        Assert.Contains("BadOneAdapter.dll", err.ToString()); Assert.Contains("BadTwoAdapter.dll", err.ToString());
        Assert.True(new FileInfo(shot).Length > 1000); r.HistorySurvives();
    }
    [Fact] public void Program_EmptyAdapterPath_IsRecoverableWhileOpeningPlainProject()
    {
        using var r = new Trial(); var err = new StringWriter(); var shot = Path.Combine(r.Root, "empty-path.png");
        Assert.Equal(0, App.Run([Path.GetDirectoryName(r.Paths.Data)!, "--adapter", "", "--settings", Path.GetDirectoryName(r.History)!, "--shot", shot], TextWriter.Null, err, Path.Combine(r.Root, "unused")));
        Assert.NotEmpty(err.ToString()); Assert.True(File.Exists(shot)); r.HistorySurvives();
    }
    [Fact] public void AdapterProblems_ActuallyReachTheStartScreenText()
    {
        using var r = new Trial(); var bad = r.Put("BadVisibleAdapter.dll"); var result = Adapters.Load([bad]);
        var l = r.Launcher(); l.Loaded = result.Loaded; l.AdapterProblems = result.Problems;
        var text = DrawText(l.Draw); Assert.Contains("BadVisibleAdapter.dll", text); Assert.Contains("didn't load", text);
    }
}

/// <summary>Exported adapter fixture with a real public empty constructor.</summary>
public sealed class A073FirstAdapter : IProjectAdapter
{
    public string Name => "A073 first";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
/// <summary>A second exported adapter in the same assembly.</summary>
public sealed class A073SecondAdapter : IProjectAdapter
{
    public string Name => "A073 second";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
/// <summary>A controlled constructor fault; the serial ImGui collection restores it after each trial.</summary>
public sealed class A073ConstructorAdapter : IProjectAdapter
{
    public static bool Throw;
    public A073ConstructorAdapter() { if (Throw) throw new InvalidOperationException("A073 constructor broke"); }
    public string Name => "A073 ctor";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
/// <summary>Abstract types are ineligible.</summary>
public abstract class A073AbstractAdapter : IProjectAdapter
{
    public string Name => "Abstract";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
internal sealed class A073InternalAdapter : IProjectAdapter
{
    public string Name => "Internal";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
internal static class A073InternalContainer
{
    public sealed class NestedAdapter : IProjectAdapter
    {
        public string Name => "Not exported despite public nested declaration";
        public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
        public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
    }
}
/// <summary>A public type without an empty constructor.</summary>
public sealed class A073ArgumentAdapter(string name) : IProjectAdapter
{
    public string Name => name;
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
/// <summary>A private empty constructor is insufficient.</summary>
public sealed class A073PrivateConstructorAdapter : IProjectAdapter
{
    private A073PrivateConstructorAdapter() { }
    public string Name => "Private ctor";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}
