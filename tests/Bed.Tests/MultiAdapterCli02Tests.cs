using System.Diagnostics;
using System.Security;
using System.Text.Json;
using Bed.Agent.Cli;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MultiAdapterCli02Tests(MultiAdapterCli02Tests.Fixture fixture) : IClassFixture<MultiAdapterCli02Tests.Fixture>
{
    public sealed class Fixture : IAsyncLifetime
    {
        private readonly string root = Directory.CreateTempSubdirectory("multi-adapter-cli02-").FullName;
        public string Multi { get; private set; } = "";
        public string Single { get; private set; } = "";
        private const string Source = """
            using System;
            using System.IO;
            using System.Collections.Generic;
            using Bed.Data;
            using Bed.Editor;
            using Bed.Editor.ImGui;
            namespace OwnMultiAdapter;
            public abstract class AdapterBase : IProjectAdapter
            {
                protected abstract string Id { get; }
                public string Name => Id;
                public virtual bool Recognises(ProjectPaths paths) => Path.GetFileName(paths.Data) == Id.ToLowerInvariant();
                public DataProject Open(ProjectPaths paths)
                {
                    File.WriteAllText(Path.Combine(paths.Data,"chosen.txt"),Name);
                    return DataProject.Open(paths.Data,paths.Schemas,recursive:true,admit: name => name != "hidden.json");
                }
                public IEnumerable<string> ProtectedNames => new[]{"forbidden"+Id.ToLowerInvariant()};
                public void Configure(EditorModel<DataDocument> editor,DataTableView table) {}
            }
            @TYPES@
            """;
        public async Task InitializeAsync()
        {
            Multi = await Build("""
                public sealed class Decliner : AdapterBase { protected override string Id => "Decliner"; public override bool Recognises(ProjectPaths p) => false; }
                public sealed class Fault : AdapterBase { protected override string Id => "Fault"; public override bool Recognises(ProjectPaths p) => Path.GetFileName(p.Data)=="throw" ? throw new InvalidOperationException("own recognition fixture fault") : false; }
                public sealed class Alpha : AdapterBase { protected override string Id => "Alpha"; public override bool Recognises(ProjectPaths p) => Path.GetFileName(p.Data) is "alpha" or "overlap" or "throw"; }
                public sealed class Beta : AdapterBase { protected override string Id => "Beta"; public override bool Recognises(ProjectPaths p) => Path.GetFileName(p.Data) is "beta" or "overlap"; }
                """);
            Single = await Build("public sealed class Legacy : AdapterBase { protected override string Id => \"Legacy\"; public override bool Recognises(ProjectPaths p) => false; }");
        }
        private async Task<string> Build(string types)
        {
            var name = "OwnCli02" + Guid.NewGuid().ToString("N"); var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            File.WriteAllText(Path.Combine(folder, "Fixture.cs"), Source.Replace("@TYPES@", types));
            File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), "<Project/>");
            File.WriteAllText(Path.Combine(folder, "Directory.Packages.props"), "<Project/>");
            File.WriteAllText(Path.Combine(folder, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            var assemblies = new[] { typeof(IProjectAdapter).Assembly, typeof(DataProject).Assembly, typeof(Issue).Assembly, typeof(EditorModel<>).Assembly }.Distinct();
            var references = string.Concat(assemblies.Select(a => "<Reference Include=\"" + a.GetName().Name + "\"><HintPath>" + SecurityElement.Escape(a.Location) + "</HintPath><Private>false</Private></Reference>"));
            var project = Path.Combine(folder, name + ".csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>" + name + "</AssemblyName><EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries></PropertyGroup><ItemGroup>" + references + "</ItemGroup></Project>");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "build", project, "-c", "Release", "--nologo", "-v:quiet", "-nodeReuse:false" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.True(process.ExitCode == 0, await output + await error);
            return Path.Combine(folder, "bin/Release/net10.0", name + ".dll");
        }
        public ProjectPaths Project(string kind)
        {
            var parent = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
            var data = Directory.CreateDirectory(Path.Combine(parent, kind)).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(parent, "schemas")).FullName;
            File.WriteAllText(Path.Combine(data, "items.json"), """{"items":[{"name":"Original"}]}""");
            File.WriteAllText(Path.Combine(data, "hidden.json"), """{"name":"Hidden original"}""");
            File.WriteAllText(Path.Combine(schemas, "items.schema.json"), "{}");
            File.WriteAllText(Path.Combine(schemas, "hidden.schema.json"), "{}");
            return new(data, schemas);
        }
        public Task DisposeAsync()
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } // Loaded fixture DLLs may stay mapped until testhost exit.
            return Task.CompletedTask;
        }
    }
    private static (int Exit, string Output, string Error) Call(ProjectPaths paths, string dll, params string[] action)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var code = Program.Run(["--data", paths.Data, "--schemas", paths.Schemas, "--adapter", dll, .. action], TextReader.Null, output, error);
        return (code, output.ToString(), error.ToString());
    }
    [Theory]
    [InlineData("alpha", "Alpha")]
    [InlineData("beta", "Beta")]
    [InlineData("overlap", "Alpha")]
    [InlineData("throw", "Alpha")]
    public void Edits_choose_the_recogniser_and_keep_its_admission_protection_and_checked_save(string kind, string selected)
    {
        var paths = fixture.Project(kind); var file = Path.Combine(paths.Data, "items.json"); var original = File.ReadAllBytes(file);
        var blocked = Call(paths, fixture.Multi, "set", "items.json", "/items/0/name", JsonSerializer.Serialize("forbidden" + selected.ToLowerInvariant()), "--save");
        Assert.NotEqual(0, blocked.Exit); Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal(selected, File.ReadAllText(Path.Combine(paths.Data, "chosen.txt")));
        Assert.Contains("Using adapter " + selected, blocked.Error, StringComparison.Ordinal);
        if (kind == "throw") Assert.Contains("own recognition fixture fault", blocked.Error, StringComparison.Ordinal);
        var hidden = Path.Combine(paths.Data, "hidden.json"); var hiddenBytes = File.ReadAllBytes(hidden);
        var excluded = Call(paths, fixture.Multi, "set", "hidden.json", "/name", "\"Must stay hidden\"", "--save");
        Assert.NotEqual(0, excluded.Exit); Assert.Equal(hiddenBytes, File.ReadAllBytes(hidden));
        var saved = Call(paths, fixture.Multi, "set", "items.json", "/items/0/name", "\"Accepted\"", "--save");
        Assert.True(saved.Exit == 0, saved.Error + saved.Output);
        Assert.Equal("Accepted", JsonDocument.Parse(File.ReadAllBytes(file)).RootElement.GetProperty("items")[0].GetProperty("name").GetString());
    }
    [Theory]
    [InlineData("alpha", "Alpha")]
    [InlineData("beta", "Beta")]
    [InlineData("overlap", "Alpha")]
    [InlineData("throw", "Alpha")]
    public void Headless_ui_and_scripting_choose_the_same_adapter(string kind, string selected)
    {
        var paths = fixture.Project(kind); var file = Path.Combine(paths.Data, "items.json"); var bytes = File.ReadAllBytes(file);
        var result = Call(paths, fixture.Multi, "ui", "--json", "--script", "[{\"key\":\"Ctrl+S\"}]");
        Assert.True(result.Exit == 0, result.Error + result.Output);
        Assert.Equal(selected, File.ReadAllText(Path.Combine(paths.Data, "chosen.txt")));
        Assert.Contains("Using adapter " + selected, result.Error, StringComparison.Ordinal);
        Assert.Equal(2, result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("hidden.json", result.Output, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }
    [Fact]
    public void A_multi_adapter_DLL_without_a_match_refuses_both_routes_without_a_plain_open()
    {
        var paths = fixture.Project("unknown"); var file = Path.Combine(paths.Data, "items.json"); var bytes = File.ReadAllBytes(file);
        foreach (var action in new[] { new[] { "set", "items.json", "/items/0/name", "\"Must not save\"", "--save" }, new[] { "ui", "--json" } })
        {
            var result = Call(paths, fixture.Multi, action); Assert.Equal(2, result.Exit);
            Assert.Contains("recognises this project", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(paths.Data, "chosen.txt"))); Assert.Equal(bytes, File.ReadAllBytes(file));
        }
    }
    [Fact]
    public void Explicit_single_adapter_keeps_legacy_nonrecognising_behavior()
    {
        var paths = fixture.Project("unknown");
        var result = Call(paths, fixture.Single, "describe"); Assert.True(result.Exit == 0, result.Error + result.Output);
        Assert.Equal("Legacy", File.ReadAllText(Path.Combine(paths.Data, "chosen.txt")));
        result = Call(paths, fixture.Single, "ui", "--json"); Assert.True(result.Exit == 0, result.Error + result.Output);
    }
}
