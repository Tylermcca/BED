using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Brutal.Authoring.Tests;

// PREPARED ONLY: no compilation or metadata/binary read performed in A291.
// INVENTED negative identity models, not game content or compiled negative binaries.
public sealed class GenericLibraryBoundaryTests
{
    private static readonly string[] ExpectedLibraries =
    [
        "Brutal.Authoring", "Brutal.Authoring.Data", "Brutal.Authoring.Editor",
        "Brutal.Authoring.Import", "Brutal.Authoring.Agent",
        "Brutal.Authoring.Editor.ImGui", "Brutal.Authoring.Editor.Text"
    ];
    // The public scope's example consumer roots; a maintainer adds their own games' names in the untracked
    // tools/generic_boundary_local.json, which this test and tools/check_generic_boundary.py both read when it exists.
    private static readonly string[] PublicConsumers = ["ExampleGame", "SampleConsumer"];
    private static readonly string[] SyntheticConsumers = ["Zorbit", "Quill", "Gizmo", "Acme", "CardBash"];
    private static readonly Lazy<string[]> AllConsumers = new(() =>
    {
        var repo = Environment.GetEnvironmentVariable("AUTHORING_BOUNDARY_REPO") ?? RepoRoot();
        var local = Path.Combine(Path.GetFullPath(repo), "tools", "generic_boundary_local.json");
        var extra = File.Exists(local)
            ? JsonDocument.Parse(File.ReadAllText(local)).RootElement.GetProperty("consumer_roots").EnumerateArray().Select(x => x.GetString()!)
            : [];
        return [.. PublicConsumers.Concat(SyntheticConsumers).Concat(extra).Distinct(StringComparer.Ordinal)];
    });
    private static string[] KnownConsumers => AllConsumers.Value;
    private sealed record Scope(string[] Assemblies, string[] Support, HashSet<string> EngineFree, HashSet<string> NoImCode);
    private sealed record Census(string Name, string[] References, string[] Identities, string[] ResourceNames);

    // The repository that holds this test: the nearest parent with Brutal.Authoring.slnx (no other folder is searched).
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Brutal.Authoring.slnx"))) return d.FullName;
        throw new InvalidOperationException("Brutal.Authoring.slnx not found above the test output; set AUTHORING_BOUNDARY_REPO.");
    }

    private static Scope ReadScope()
    {
        var repo = Environment.GetEnvironmentVariable("AUTHORING_BOUNDARY_REPO") ?? RepoRoot();
        var path = Path.Combine(Path.GetFullPath(repo), "tools", "generic_boundary_scope.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(PublicConsumers, root.GetProperty("consumer_roots").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var assemblies = root.GetProperty("projects").EnumerateArray().Select(x => x.GetProperty("assembly").GetString()!).ToArray();
        Assert.Equal(ExpectedLibraries, assemblies); // cannot silently drop a library
        var support = root.GetProperty("support_projects").EnumerateArray().Select(x => Path.GetFileNameWithoutExtension(x.GetString()!)!).ToArray();
        Assert.Equal(new[] { "ImCode", "ImCode.Text" }, support);
        return new(assemblies, support,
            root.GetProperty("engine_free").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal),
            root.GetProperty("no_imcode").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal));
    }

    private static string[] Words(string value) =>
        Regex.Split(value, "[^A-Za-z0-9]+")
            .SelectMany(part => Regex.Matches(part, "[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+")
                .Select(m => m.Value.ToLowerInvariant())).ToArray();

    private static string? Consumer(string value)
    {
        var words = Words(value);
        foreach (var root in KnownConsumers)
        {
            var wanted = Words(root);
            for (var i = 0; i + wanted.Length <= words.Length; i++)
                if (words.AsSpan(i, wanted.Length).SequenceEqual(wanted.AsSpan())) return root;
        }
        return null;
    }

    private static bool LegalResource(string name) =>
        Regex.IsMatch(name.Replace('\\', '/').Split('/')[^1],
            "(?:^|[._-])(?:licen[cs]e|notice|credits)(?:[._-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> Problems(string expected, Census census, Scope scope)
    {
        var problems = new List<string>();
        if (!string.Equals(expected, census.Name, StringComparison.Ordinal))
            problems.Add($"Expected assembly {expected}, got {census.Name}.");
        foreach (var name in new[] { census.Name }.Concat(census.References).Concat(census.Identities))
            if (Consumer(name) is { } root) problems.Add($"{expected}: consumer identity {name} ({root}).");
        foreach (var name in census.ResourceNames)
            if (!LegalResource(name) && Consumer(name) is { } root)
                problems.Add($"{expected}: consumer resource identity {name} ({root}); resource bytes not read.");
        if (scope.EngineFree.Contains(expected))
            foreach (var reference in census.References)
                if (reference.StartsWith("Brutal.", StringComparison.OrdinalIgnoreCase)
                    && !reference.StartsWith("Brutal.Authoring", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{expected}: engine reference {reference} in an engine-free layer.");
        if (scope.NoImCode.Contains(expected))
            foreach (var reference in census.References)
                if (reference.Equals("ImCode", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("ImCode.", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{expected}: ImCode belongs only in the optional Text layer.");
        return problems;
    }

    // Future binary METADATA ONLY: no Assembly.Load, GetTypes, constructors, attributes or resource streams.
    private static Census ReadBuiltMetadata(string expected)
    {
        // Fixed, reviewed names only; no recursive binary hunt and no package/cache inspection.
        var path = Path.Combine(AppContext.BaseDirectory, expected + ".dll");
        Assert.True(File.Exists(path), $"Missing {path}; build the scoped test output after toolchain authority is restored.");
        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        Assert.True(pe.HasMetadata, $"No managed metadata in {path}.");
        var reader = pe.GetMetadataReader();
        Assert.True(reader.IsAssembly, $"Not an assembly: {path}.");
        var identities = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var item = reader.GetTypeDefinition(handle);
            identities.Add(reader.GetString(item.Namespace)); identities.Add(reader.GetString(item.Name));
        }
        foreach (var handle in reader.TypeReferences)
        {
            var item = reader.GetTypeReference(handle);
            identities.Add(reader.GetString(item.Namespace)); identities.Add(reader.GetString(item.Name));
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var item = reader.GetExportedType(handle);
            identities.Add(reader.GetString(item.Namespace)); identities.Add(reader.GetString(item.Name));
        }
        identities.AddRange(reader.MethodDefinitions.Select(h => reader.GetString(reader.GetMethodDefinition(h).Name)));
        identities.AddRange(reader.FieldDefinitions.Select(h => reader.GetString(reader.GetFieldDefinition(h).Name)));
        identities.AddRange(reader.PropertyDefinitions.Select(h => reader.GetString(reader.GetPropertyDefinition(h).Name)));
        identities.AddRange(reader.EventDefinitions.Select(h => reader.GetString(reader.GetEventDefinition(h).Name)));
        return new(reader.GetString(reader.GetAssemblyDefinition().Name),
            reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray(),
            identities.ToArray(),
            reader.ManifestResources.Select(h => reader.GetString(reader.GetManifestResource(h).Name)).ToArray());
    }

    [Fact]
    public void Technical_scope_keeps_all_seven_libraries_and_two_bundled_support_assemblies()
    {
        var scope = ReadScope();
        Assert.Equal(7, scope.Assemblies.Length); Assert.Equal(2, scope.Support.Length);
    }

    [Fact]
    public void All_seven_built_library_metadata_surfaces_remain_generic()
    {
        var scope = ReadScope();
        foreach (var name in scope.Assemblies)
            Assert.Empty(Problems(name, ReadBuiltMetadata(name), scope));
    }

    [Fact]
    public void Bundled_text_support_metadata_has_no_game_identity()
    {
        var scope = ReadScope();
        foreach (var name in scope.Support)
            Assert.Empty(Problems(name, ReadBuiltMetadata(name), scope));
    }

    [Theory]
    [InlineData("Zorbit.Core")]
    [InlineData("Quill.Model")]
    [InlineData("Gizmo.Rules")]
    [InlineData("Acme.Core")]
    [InlineData("CardBash.Tables")]
    [InlineData("Brutal.Authoring.AcmeAdapter")]
    [InlineData("ReadAcmeRecipes")]
    [InlineData("Generic.Quill+Nested")]
    public void Synthetic_consumer_reference_type_and_member_identities_fail(string identity)
    {
        Assert.NotNull(Consumer(identity));
        var scope = new Scope([], [], [], []);
        Assert.NotEmpty(Problems("Generic", new("Generic", [identity], [], []), scope));
        Assert.NotEmpty(Problems("Generic", new("Generic", [], [identity], []), scope));
    }

    [Theory]
    [InlineData("Brutal.Authoring.Data")]
    [InlineData("Brutal.ImGui")]
    [InlineData("Ahwoo.Brutal.Core")]
    [InlineData("ImCode.Text")]
    [InlineData("Acmestic")]
    [InlineData("Zorbitude")]
    public void Generic_and_upstream_identities_are_not_game_roots(string identity) => Assert.Null(Consumer(identity));

    [Fact]
    public void Required_legal_resource_names_remain_allowed_without_reading_their_bytes()
    {
        var scope = new Scope([], [], [], []);
        Assert.Empty(Problems("Generic", new("Generic", [], [], ["Zorbit-NOTICE.txt", "Acme-LICENSE.txt", "CardBash.CREDITS.txt"]), scope));
        Assert.NotEmpty(Problems("Generic", new("Generic", [], [], ["Acme.rules.json"]), scope));
    }

    [Fact]
    public void Identity_mismatch_and_existing_role_constraints_still_fail()
    {
        var scope = new Scope([], [], new HashSet<string>(StringComparer.Ordinal) { "Brutal.Authoring" },
            new HashSet<string>(StringComparer.Ordinal) { "Brutal.Authoring.Editor.ImGui" });
        Assert.NotEmpty(Problems("Expected", new("Wrong", [], [], []), scope));
        Assert.NotEmpty(Problems("Brutal.Authoring", new("Brutal.Authoring", ["Brutal.Core"], [], []), scope));
        Assert.Empty(Problems("Brutal.Authoring", new("Brutal.Authoring", ["Brutal.Authoring.Data"], [], []), scope));
        Assert.NotEmpty(Problems("Brutal.Authoring.Editor.ImGui", new("Brutal.Authoring.Editor.ImGui", ["ImCode.Text"], [], []), scope));
    }
}
