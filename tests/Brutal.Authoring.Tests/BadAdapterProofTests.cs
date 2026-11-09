using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
using PersonApp = Brutal.Authoring.Launcher.Program;
using AgentApp = Brutal.Authoring.Agent.Cli.Program;

namespace Brutal.Authoring.Tests;

// INVENTED assemblies and project. No game data. Every subprocess is headless; every launcher call has --shot.
[Collection(ImGuiCollection.Name)]
public sealed class BadAdapterProofTests : IDisposable, IClassFixture<BadAdapterProofTests.Prebuild>
{
    private readonly ITestOutputHelper? output;
    private readonly Prebuild? prebuild;
    private string root;
    public BadAdapterProofTests(ITestOutputHelper output, Prebuild prebuild)
    { this.output=output;this.prebuild=prebuild;root=""; }
    private BadAdapterProofTests() => root=Directory.CreateTempSubdirectory("bad-adapter-proof-").FullName;

    // Compilation alone is parallel. Each case retains its own source, random assembly names,
    // deployed directory and embedded Data path; loader/UI observations remain serialized.
    public sealed class Prebuild : IAsyncLifetime
    {
        private readonly Dictionary<string,BadAdapterProofTests> cases=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Dictionary<string,(string? Source,string Dll)>> binaries=new(StringComparer.Ordinal);
        public async Task InitializeAsync()
        {
            string[] kinds=["not-assembly","dependency","framework","no-type","no-constructor","recognises-throws",
                "open-throws","open-null","missing-data","missing-schemas","multiple","two","duplicate-checks","rule","schema","auto","control"];
            foreach(var kind in kinds){cases.Add(kind,new());binaries.Add(kind,new(StringComparer.Ordinal));}
            using var slots=new SemaphoreSlim(2);
            await Task.WhenAll(kinds.Select(async kind=>
            {
                await slots.WaitAsync();
                try
                {
                    var fixture=cases[kind];fixture.Project(kind);
                    async Task Add(string mode,string? source=null)=>binaries[kind].Add(mode,(source,await fixture.Build(mode,source)));
                    if(kind=="multiple")await Add(kind,Header.Replace("@ATTRIBUTE@", "")+fixture.AdapterSource("First","FirstAdapter")+fixture.AdapterSource("Second","SecondAdapter"));
                    else if(kind=="two"){await Add("First");await Add("Second");}
                    else await Add(kind);
                }
                finally{slots.Release();}
            }));
        }
        public string Root(string kind)=>cases[kind].root;
        public string Built(string root,string kind,string? source)
        {
            var key=cases.Single(p=>p.Value.root==root).Key;
            var result=binaries[key][kind];Assert.Equal(result.Source,source);return result.Dll;
        }
        public Task DisposeAsync()
        {
            foreach(var fixture in cases.Values)if(Directory.Exists(fixture.root))fixture.Dispose();
            return Task.CompletedTask;
        }
    }
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private string Marker => Path.Combine(root, "opened.txt");
    private string Evidence => Environment.GetEnvironmentVariable("A200_EVIDENCE_ROOT") ?? Path.Combine(root, "evidence");
    private string EvidenceCase => Path.Combine(Evidence, Path.GetFileName(root));
    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("A200_EVIDENCE_ROOT") is not null)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(EvidenceCase, Path.GetRelativePath(root, path)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(path, dest, true);
            }
        }
        var full = Path.GetFullPath(root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("bad-adapter-proof-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test cleanup target");
        try { Directory.Delete(root, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { output?.WriteLine("Owned fixture DLL remains mapped until testhost exit: " + root); }
    }
    private ProjectPaths Project(string kind)
    {
        if(prebuild is not null)root=prebuild.Root(kind);
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Data, "items.json"), "{\"items\":[{\"id\":\"self_authored\",\"value\":7}]}");
        File.WriteAllText(Path.Combine(Schemas, "items.schema.json"), kind == "schema" ? "{A200 intentionally invalid JSON" : "{\"type\":\"object\"}");
        return new(kind == "missing-data" ? Path.Combine(root, "missing-data") : Data, kind == "missing-schemas" ? Path.Combine(root, "missing-schemas") : Schemas);
    }
    private const string Header = """
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
@ATTRIBUTE@
namespace A200SelfAuthored;
public sealed class A200Rule : IValidator<DataDocument>
{
 public IReadOnlyList<Issue> Validate(DataDocument d) => throw new InvalidOperationException("A200 self-authored rule crash");
}
public sealed class A200Check : ICheck
{
 public CheckDefinition Definition => new("a200.probe", "Self-authored check", "No game data", TimeSpan.FromSeconds(1));
 public Task<CheckReport> RunAsync(CheckSnapshot s,IProgress<CheckProgress> p,CancellationToken c) => Task.FromResult(new CheckReport(true,[],"Self-authored"));
}
""";
    private const string Body = """
public sealed class @CLASS@ : @BASE@IProjectAdapter
{
 private static readonly string Mode = "@MODE@";
 @CONSTRUCTOR@
 public string Name => "A200 " + Mode;
 public bool Recognises(ProjectPaths p) => Mode == "recognises-throws" ? throw new InvalidOperationException("A200 self-authored recognition crash") : Mode != "auto" || string.Equals(Path.GetFullPath(p.Data), @SCOPE@, StringComparison.OrdinalIgnoreCase);
 public void Configure(EditorModel<DataDocument> editor, DataTableView table) {}
 public DataProject Open(ProjectPaths p)
 {
  File.AppendAllText(Path.Combine(Path.GetDirectoryName(p.Data)!, "opened.txt"), Name + Environment.NewLine);
  if(Mode == "open-throws") throw new InvalidOperationException("A200 self-authored open crash");
  if(Mode == "open-null") return null!;
  return DataProject.Open(p.Data,p.Schemas,rules: _ => Mode == "rule" ? new A200Rule() : null);
 }
 public IEnumerable<ICheck> Checks => Mode == "duplicate-checks" ? new ICheck[]{new A200Check(),new A200Check()} : Array.Empty<ICheck>();
}
""";
    private string AdapterSource(string mode, string name = "CaseAdapter", bool dependency = false) =>
        Body.Replace("@CLASS@", name).Replace("@BASE@", dependency ? "A200Dependency.MissingBase," : "").Replace("@MODE@", mode).Replace("@SCOPE@", JsonSerializer.Serialize(Data))
            .Replace("@CONSTRUCTOR@", mode == "no-constructor" ? "private " + name + "() {}" : "");
    private async Task<string> Build(string kind, string? custom = null)
    {
        if(prebuild is not null)return prebuild.Built(root,kind,custom);
        if (kind == "not-assembly") { var fake = Path.Combine(root, "NotManagedAdapter.dll"); File.WriteAllText(fake, "A200 self-authored non-.NET bytes"); return fake; }
        string? dependency = null;
        if (kind == "dependency") dependency = await Compile("namespace A200Dependency; public class MissingBase {}", "Dependency", null, false);
        var attr = kind == "framework" ? "[assembly: System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v999.0\")]" : "";
        var code = custom ?? (kind == "no-type" ? "public class OrdinarySelfAuthoredType {}" : Header.Replace("@ATTRIBUTE@", attr) + AdapterSource(kind, dependency: dependency is not null));
        var built = await Compile(code, "Adapter", dependency, kind == "framework");
        var deployed = Directory.CreateDirectory(Path.Combine(root, "deployed", Guid.NewGuid().ToString("N"))).FullName;
        var target = Path.Combine(deployed, Path.GetFileName(built)); File.Copy(built, target); // Deliberately do not deploy the missing dependency.
        return target;
    }
    private async Task<string> Compile(string source, string suffix, string? dependency, bool declaredFutureTarget)
    {
        var name = "A200" + Guid.NewGuid().ToString("N") + suffix; var folder = Directory.CreateDirectory(Path.Combine(root, "build", name)).FullName;
        File.WriteAllText(Path.Combine(folder, "Fixture.cs"), source);
        File.WriteAllText(Path.Combine(folder, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), "<Project/>"); File.WriteAllText(Path.Combine(folder, "Directory.Packages.props"), "<Project/>");
        var assemblies = new[] { typeof(IProjectAdapter).Assembly, typeof(DataProject).Assembly, typeof(Issue).Assembly, typeof(EditorModel<>).Assembly }.Distinct();
        var refs = string.Concat(assemblies.Select(a => "<Reference Include=\"" + a.GetName().Name + "\"><HintPath>" + SecurityElement.Escape(a.Location) + "</HintPath><Private>false</Private></Reference>"));
        if (dependency is not null) refs += "<Reference Include=\"" + Path.GetFileNameWithoutExtension(dependency) + "\"><HintPath>" + SecurityElement.Escape(dependency) + "</HintPath><Private>false</Private></Reference>";
        var project = Path.Combine(folder, name + ".csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>" + name + "</AssemblyName>" + (declaredFutureTarget ? "<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>" : "") + "<EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries><EnableSourceLink>false</EnableSourceLink></PropertyGroup><ItemGroup>" + refs + "</ItemGroup></Project>");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "build", project, "-c", "Release", "--nologo", "-v:quiet", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:EnableSourceControlManagerQueries=false", "-p:EnableSourceLink=false", "-p:NuGetAudit=false" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var log = await stdout + await stderr; File.WriteAllText(Path.Combine(folder, "build.log"), log); Assert.True(process.ExitCode == 0, log);
        return Path.Combine(folder, "bin", "Release", "net10.0", name + ".dll");
    }
    private sealed record Observation(int LoadCount, string[] LoadProblems, bool DirectOpened, bool HasWorkspace, bool HasCurrent, int DirectRecent,
        string DirectMessage, string? DirectCrash, string? Using, string[] Checks, string DirectText, int PersonExit, string PersonOut, string PersonError, string? PersonCrash,
        int PersonRecent, int AgentExit, string AgentOut, string AgentError, string? AgentCrash, string[] OpenedMarkers);
    private Observation Observe(ProjectPaths paths, string[] adapters, string? agentAdapter = null)
    {
        var beforeData = File.ReadAllBytes(Path.Combine(Data, "items.json")); var beforeSchema = File.ReadAllBytes(Path.Combine(Schemas, "items.schema.json"));
        var loaded = Adapters.Load(adapters); var recent = new RecentProjects(Path.Combine(root, "direct-settings", "recent.json"));
        var launcher = new ProjectLauncher(recent) { Loaded = loaded.Loaded, AdapterProblems = loaded.Problems };
        bool opened = false;
        // A rejected DLL is a loader refusal; do not call the generic plain-data Open API after it.
        // The actual --adapter command below must independently enforce its refusal and Recent rollback.
        var directError = loaded.Problems.Count == 0 ? Record.Exception(() => opened = launcher.Open(paths)) : null;
        string text = "";
        if (directError is null)
        {
            // The initial Files row says can't open; this is the production handler reached by clicking it.
            if (launcher.Workspace is { } workspace && workspace.Project.Files.FirstOrDefault(f => f.State == DataFileState.Refused) is { } refused)
                workspace.Show(refused.Name);
            using var host = new HeadlessHost(1100, 800) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            host.Frames(launcher.Draw, 3);
            host.Frame(() => { ImGui.LogToClipboard(); launcher.Draw(); ImGui.LogFinish(); });
            text = ImGui.GetClipboardText().ToString() ?? "";
        }
        var personOut = new StringWriter(); var personError = new StringWriter(); var settings = Path.Combine(root, "person-settings"); int personCode = -999;
        var args = new List<string> { "--data", paths.Data, "--schemas", paths.Schemas, "--settings", settings, "--shot", Path.Combine(root, "person.png") };
        foreach (var adapter in adapters) { args.Add("--adapter"); args.Add(adapter); }
        var personCrash = Record.Exception(() => personCode = PersonApp.Run(args.ToArray(), personOut, personError, settings));
        var agentOut = new StringWriter(); var agentError = new StringWriter(); int agentCode = -999;
        var agentCrash = Record.Exception(() => agentCode = AgentApp.Run(["--data", paths.Data, "--schemas", paths.Schemas, "--adapter", agentAdapter ?? adapters[0], "describe"], TextReader.Null, agentOut, agentError));
        Assert.Equal(beforeData, File.ReadAllBytes(Path.Combine(Data, "items.json"))); Assert.Equal(beforeSchema, File.ReadAllBytes(Path.Combine(Schemas, "items.schema.json")));
        var observed = new Observation(loaded.Loaded.Count, loaded.Problems.ToArray(), opened, launcher.Workspace is not null, launcher.Current is not null, recent.Items.Count,
            launcher.Said, directError?.ToString(), launcher.Using?.Name, launcher.Workspace?.Checks.Registered.Select(c => c.Id).ToArray() ?? [], text,
            personCode, personOut.ToString(), personError.ToString(), personCrash?.ToString(), new RecentProjects(Path.Combine(settings, "recent.json")).Items.Count,
            agentCode, agentOut.ToString(), agentError.ToString(), agentCrash?.ToString(), File.Exists(Marker) ? File.ReadAllLines(Marker) : []);
        File.WriteAllText(Path.Combine(root, "observation.json"), JsonSerializer.Serialize(observed, new JsonSerializerOptions { WriteIndented = true }));
        output?.WriteLine(JsonSerializer.Serialize(observed)); return observed;
    }
    private static bool Advice(string message) => new[] { "fix", "choose", "check the path", "check --", "browse", "rebuild", "compile", "copy", "beside", "install", "IProjectAdapter", "constructor", "valid JSON", "remove", "use " }.Any(s => message.Contains(s, StringComparison.OrdinalIgnoreCase));
    [Theory]
    [InlineData("not-assembly")] [InlineData("dependency")] [InlineData("framework")]
    [InlineData("no-type")] [InlineData("no-constructor")] [InlineData("recognises-throws")]
    [InlineData("open-throws")] [InlineData("open-null")] [InlineData("missing-data")] [InlineData("missing-schemas")]
    public async Task Adapter_cases_follow_the_latest_documented_host_contract_and_report_a_fix(string kind)
    {
        var paths = Project(kind); var dll = await Build(kind); var seen = Observe(paths, [dll]);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash);
        // Latest inbox correction: optional explicit-load failures open plain data with disclosure;
        // recognition exceptions are a disclosed non-match. These are established supported paths.
        if (kind is "not-assembly" or "dependency" or "no-type" or "no-constructor")
        {
            Assert.Equal(0, seen.LoadCount); Assert.NotEmpty(seen.LoadProblems);
            Assert.False(seen.HasWorkspace); Assert.False(seen.HasCurrent); Assert.Equal(0, seen.DirectRecent);
            Assert.Equal(0, seen.PersonExit); Assert.Equal(1, seen.PersonRecent); Assert.False(string.IsNullOrWhiteSpace(seen.PersonError));
            Assert.NotEqual(0, seen.AgentExit); Assert.True(Advice(seen.AgentError)); return;
        }
        if (kind == "recognises-throws")
        {
            Assert.True(seen.DirectOpened); Assert.Null(seen.Using); Assert.Equal(0, seen.PersonExit); Assert.Equal(0, seen.AgentExit);
            Assert.Contains("couldn't tell whether", seen.DirectMessage); Assert.Contains("so it wasn't used", seen.DirectText);
            Assert.Contains("couldn't tell whether", seen.PersonOut); return;
        }
        if (kind == "framework")
        {
            // Explicit owner clarification: metadata is informational; the runtime decides compatibility.
            var adapter = Assert.Single(Adapters.Load([dll]).Loaded);
            Assert.Equal(".NETCoreApp,Version=v999.0", adapter.GetType().Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName);
            Assert.True(seen.DirectOpened); Assert.True(seen.HasWorkspace); Assert.True(seen.HasCurrent);
            Assert.Equal(0, seen.PersonExit); Assert.Equal(0, seen.AgentExit); return;
        }
        Assert.False(seen.DirectOpened); Assert.False(seen.HasWorkspace); Assert.False(seen.HasCurrent); Assert.Equal(0, seen.DirectRecent);
        Assert.NotEqual(0, seen.PersonExit); Assert.Equal(0, seen.PersonRecent); Assert.NotEqual(0, seen.AgentExit);
        Assert.False(string.IsNullOrWhiteSpace(seen.PersonError)); Assert.False(string.IsNullOrWhiteSpace(seen.AgentError));
        Assert.True(Advice(seen.PersonError), "Person's refusal must name a fix."); Assert.True(Advice(seen.AgentError), "Agent's refusal must name a fix.");
    }
    [Fact]
    public async Task Several_adapter_types_load_and_the_one_adapter_cli_explains_its_refusal()
    {
        var paths = Project("multiple"); var source = Header.Replace("@ATTRIBUTE@", "") + AdapterSource("First", "FirstAdapter") + AdapterSource("Second", "SecondAdapter");
        var dll = await Build("multiple", source); var seen = Observe(paths, [dll]);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash); Assert.Equal(2, seen.LoadCount); Assert.Empty(seen.LoadProblems);
        Assert.True(seen.DirectOpened); Assert.Equal(0, seen.PersonExit); Assert.Equal("A200 First", seen.Using); Assert.Contains("A200 First", seen.PersonOut);
        Assert.DoesNotContain(seen.OpenedMarkers, m => m.Contains("Second", StringComparison.Ordinal));
        Assert.NotEqual(0, seen.AgentExit); Assert.Contains("one", seen.AgentError, StringComparison.OrdinalIgnoreCase);
        Assert.True(Advice(seen.AgentError));
    }
    [Fact]
    public async Task Two_recognisers_use_the_first_and_disclose_the_selected_adapter()
    {
        var paths = Project("two"); var first = await Build("First"); var second = await Build("Second"); var seen = Observe(paths, [first, second], first);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash); Assert.Equal(2, seen.LoadCount); Assert.True(seen.DirectOpened);
        Assert.Equal("A200 First", seen.Using); Assert.Contains("A200 First", seen.PersonOut); Assert.Equal(0, seen.PersonExit); Assert.Equal(0, seen.AgentExit);
        Assert.DoesNotContain(seen.OpenedMarkers, m => m.Contains("Second", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Duplicate_checks_open_with_a_visible_warning_instead_of_crashing_either_host()
    {
        var paths = Project("duplicate-checks"); var dll = await Build("duplicate-checks"); var seen = Observe(paths, [dll]);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash); Assert.True(seen.DirectOpened); Assert.Equal(0, seen.PersonExit); Assert.Equal(0, seen.AgentExit);
        Assert.Contains("checks weren't added", seen.DirectMessage); Assert.Contains("checks weren't added", seen.PersonOut);
        Assert.Contains("checks weren't added", seen.DirectText); Assert.DoesNotContain("a200.probe", seen.Checks);
        Assert.True(seen.AgentOut.Contains("check", StringComparison.OrdinalIgnoreCase) || seen.AgentError.Contains("check", StringComparison.OrdinalIgnoreCase), "Agent must disclose omitted checks.");
    }
    [Theory]
    [InlineData("rule")] [InlineData("schema")]
    public async Task A_rule_or_schema_fault_is_contained_and_its_actual_diagnostic_is_visible(string kind)
    {
        var paths = Project(kind); var dll = await Build(kind); var seen = Observe(paths, [dll]);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash);
        var person = seen.PersonOut + seen.PersonError + seen.DirectText;
        Assert.Contains(kind == "rule" ? "A200 self-authored rule crash" : "schema", person, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(kind == "rule" ? "A200 self-authored rule crash" : "schema", seen.AgentOut + seen.AgentError, StringComparison.OrdinalIgnoreCase);
        if (!seen.DirectOpened) { Assert.False(seen.HasWorkspace); Assert.False(seen.HasCurrent); Assert.Equal(0, seen.DirectRecent); Assert.Equal(0, seen.PersonRecent); }
    }
    [Fact]
    public async Task A_self_authored_adapter_discovered_in_the_programs_adapters_folder_is_used()
    {
        var paths = Project("auto"); var dll = await Build("auto");
        var folder = Path.Combine(AppContext.BaseDirectory, "adapters", "a200-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var deployed = Path.Combine(folder, Path.GetFileName(dll)); File.Copy(dll, deployed);
            Assert.Contains(deployed, Adapters.In(Path.Combine(AppContext.BaseDirectory, "adapters")));
            var stdout = new StringWriter(); var stderr = new StringWriter(); var settings = Path.Combine(root, "auto-settings");
            var code = PersonApp.Run(["--data", paths.Data, "--schemas", paths.Schemas, "--shot", Path.Combine(root, "auto.png")], stdout, stderr, settings);
            output?.WriteLine(stdout + "\n" + stderr); Assert.Equal(0, code); Assert.Contains("A200 auto", stdout.ToString()); Assert.Contains("A200 auto", File.ReadAllText(Marker));
        }
        finally
        {
            var full = Path.GetFullPath(folder); var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "adapters")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a200-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected adapter cleanup boundary");
            try { Directory.Delete(full, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { output?.WriteLine("Owned automatic adapter remains mapped until testhost exit: " + full); }
        }
    }
    [Fact]
    public async Task A_valid_control_opens_with_the_adapter_and_preserves_the_authored_input()
    {
        var paths = Project("control"); var dll = await Build("control"); var seen = Observe(paths, [dll]);
        Assert.Null(seen.DirectCrash); Assert.Null(seen.PersonCrash); Assert.Null(seen.AgentCrash); Assert.True(seen.DirectOpened); Assert.True(seen.HasWorkspace); Assert.True(seen.HasCurrent);
        Assert.Equal(1, seen.DirectRecent); Assert.Equal(1, seen.PersonRecent); Assert.Equal(0, seen.PersonExit); Assert.Equal(0, seen.AgentExit);
        Assert.Equal("A200 control", seen.Using); Assert.Contains("A200 control", seen.PersonOut); using var summary = JsonDocument.Parse(seen.AgentOut); Assert.Equal("items.json", summary.RootElement.GetProperty("files"u8)[0].GetProperty("file"u8).GetString());
    }
}
