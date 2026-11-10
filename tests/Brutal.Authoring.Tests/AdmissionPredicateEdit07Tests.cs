using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class AdmissionPredicateEdit07Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("admit-data-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    public AdmissionPredicateEdit07Tests()
    {
        foreach (var folder in new[] { "game", "story/templates", "raw", "design" }) Directory.CreateDirectory(Path.Combine(Data, folder));
        Directory.CreateDirectory(Schemas);
        foreach (var name in new[] { "game/one.json", "story/bundles.json", "story/templates/two.json" }) File.WriteAllText(Path.Combine(Data, name), "{\"n\":1}\n");
        File.WriteAllText(Path.Combine(Data, "raw/bad.json"), "unreadable JSON outside scope");
        File.WriteAllText(Path.Combine(Data, "design/bad.json"), "also outside scope");
        File.WriteAllText(Path.Combine(Schemas, "data.schema.json"), "{\"type\":\"object\"}");
    }
    public void Dispose() { Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); Directory.Delete(root, true); }
    private static bool Allowed(string name) => name.StartsWith("game/", StringComparison.Ordinal) || name.StartsWith("story/", StringComparison.Ordinal);
    private DataProject Open()
    {
        var project = DataProject.Open(Data, Schemas, recursive: true, admit: Allowed);
        project.SchemaFor = _ => "data.schema.json";
        return project;
    }
    [Fact]
    public void Only_admitted_names_open_and_snapshot_copies_keep_the_same_boundary()
    {
        var project = Open(); Assert.Equal(3, project.Files.Count);
        Assert.Null(project.Find("raw/bad.json")); Assert.Null(project.Find("design/bad.json"));
        foreach (var file in project.Files) Assert.True(file.Open());
        var snapshot = CheckSnapshot.Capture(project); Assert.DoesNotContain(snapshot.Names, name => name.StartsWith("data/raw/", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(Data, "raw/new.json"), "still excluded"); Assert.Equal(snapshot.Signature, CheckSnapshot.SignatureOf(project));
        File.WriteAllText(Path.Combine(Data, "story/templates/new.json"), "{}"); Assert.NotEqual(snapshot.Signature, CheckSnapshot.SignatureOf(project));
        var copy = snapshot.OpenCopy(Path.Combine(root, "copy"), out _);
        Assert.Equal(3, copy.Files.Count); Assert.False(copy.CreateFromText("raw/no.json", "{}", out _));
    }
    [Fact]
    public void File_operations_and_public_session_saves_cannot_write_excluded_names()
    {
        var project = Open(); var file = project.Find("game/one.json")!; Assert.True(file.Open());
        Assert.False(project.CreateFromText("raw/no.json", "{}", out var why)); Assert.Contains("admitted file scope", why);
        Assert.False(project.Create("raw/no.json", "data.schema.json", out _));
        Assert.False(project.Rename(file.Name, "raw/moved.json", out _));
        Assert.Throws<IOException>(() => file.Session!.Save(Path.Combine(Data, "raw/no.json")));
        Assert.False(File.Exists(Path.Combine(Data, "raw/no.json")));
        Assert.True(file.Session!.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(2)))); Assert.True(file.Save());
        Assert.True(project.CreateFromText("story/templates/copy.json", "{}", out why), why);
    }
    [Fact]
    public void Predicate_errors_fail_closed_and_predicate_receives_only_canonical_names()
    {
        Assert.Throws<IOException>(() => DataProject.Open(Data, Schemas, recursive: true, admit: _ => throw new InvalidOperationException("broken policy")));
        Assert.Throws<ArgumentException>(() => DataProject.Open(Data, Schemas, admit: Allowed));
        var names = new List<string>();
        var project = DataProject.Open(Data, Schemas, recursive: true, admit: name => { names.Add(name); return Allowed(name); });
        Assert.Equal(3, project.Files.Count); Assert.NotEmpty(names);
        Assert.All(names, name => { Assert.DoesNotContain('\\', name); Assert.DoesNotContain("..", name); Assert.EndsWith(".json", name); });
        Assert.False(project.CreateFromText("../escape.json", "{}", out _));
    }
    [Fact]
    public void Default_null_and_original_six_argument_binary_entry_keep_all_recursive_JSON_files()
    {
        var defaults = DataProject.Open(Data, Schemas, recursive: true); Assert.Equal(5, defaults.Files.Count);
        var old = typeof(DataProject).GetMethod("Open", [typeof(string), typeof(string), typeof(bool), typeof(Func<string, string>), typeof(Func<string, IValidator<DataDocument>>), typeof(bool)]);
        Assert.NotNull(old);
        var legacy = Assert.IsType<DataProject>(old.Invoke(null, [Data, Schemas, false, null, null, true])); Assert.Equal(5, legacy.Files.Count);
    }
}
