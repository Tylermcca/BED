using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class FilesSavedEdit05Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("bed-files-saved-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    public FilesSavedEdit05Tests()
    {
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "values.schema.json"), """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}},"additionalProperties":false}""");
        foreach (var name in new[] { "a.json", "b.json" }) File.WriteAllText(Path.Combine(Data, name), "{\"value\":1}\n");
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
    private DataProject Open(bool readOnly = false)
    {
        var project = DataProject.Open(Data, Schemas, readOnly);
        project.SchemaFor = _ => "values.schema.json";
        foreach (var file in project.Files) Assert.True(file.Open());
        return project;
    }
    private static void Edit(DataFile file, int value) => Assert.True(file.Session!.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(value))));
    [Fact]
    public void Dirty_single_save_notifies_once_on_the_completion_thread_after_disk_and_session_are_saved()
    {
        var project = Open(); var file = project.Find("a.json")!; Edit(file, 2);
        var thread = Environment.CurrentManagedThreadId; var calls = 0;
        var observedThread = -1; bool? dirtyAtEvent = null; string? expected = null, disk = null; IReadOnlyList<DataFile>? received = null;
        project.FilesSaved += written =>
        {
            calls++; received = written; observedThread = Environment.CurrentManagedThreadId;
            dirtyAtEvent = file.Dirty; expected = file.Session!.ExpectedOnDisk; disk = File.ReadAllText(file.Path);
        };
        Assert.True(file.Save()); Assert.Equal(1, calls);
        Assert.Equal(thread, observedThread); Assert.False(dirtyAtEvent); Assert.NotNull(expected); Assert.Equal(expected, disk);
        Assert.Same(file, Assert.Single(received!));
        Assert.Throws<NotSupportedException>(() => ((IList<DataFile>)received!).Clear());
        Assert.True(file.Save()); Assert.Equal(1, calls);
    }
    [Fact]
    public void Clean_single_and_save_all_are_silent_and_keep_original_bytes()
    {
        var project = Open(); var before = project.Files.ToDictionary(f => f.Path, f => File.ReadAllBytes(f.Path));
        var calls = 0; project.FilesSaved += _ => calls++;
        Assert.True(project.Files[0].Save()); Assert.Empty(project.SaveAll());
        Assert.Equal(0, calls); Assert.All(before, p => Assert.Equal(p.Value, File.ReadAllBytes(p.Key)));
    }
    [Theory]
    [InlineData("host")]
    [InlineData("readonly")]
    [InlineData("outside")]
    public void Refused_single_saves_do_not_notify(string reason)
    {
        var project = Open(reason == "readonly"); var file = project.Files[0]; Edit(file, 2);
        var calls = 0; project.FilesSaved += _ => calls++;
        if (reason == "host") project.SaveCheck = _ => "refused for test";
        if (reason == "outside") File.WriteAllText(file.Path, "{\"value\":3}");
        Assert.False(file.Save()); Assert.True(file.Dirty); Assert.Equal(0, calls);
    }
    [Fact]
    public void Save_all_notifies_once_after_every_written_session_is_marked_saved()
    {
        var project = Open(); foreach (var file in project.Files) Edit(file, 4);
        var calls = 0; IReadOnlyList<DataFile>? received = null; int? dirtyAtEvent = null;
        (string? Expected, string Disk)[] snapshots = [];
        project.FilesSaved += written =>
        {
            calls++; received = written; dirtyAtEvent = project.Dirty.Count;
            snapshots = written.Select(f => (f.Session!.ExpectedOnDisk, File.ReadAllText(f.Path))).ToArray();
        };
        Assert.Empty(project.SaveAll()); Assert.Equal(1, calls); Assert.Equal(0, dirtyAtEvent);
        Assert.Equal(new[] { "a.json", "b.json" }, received!.Select(f => f.Name));
        Assert.Equal(2, snapshots.Length); Assert.All(snapshots, snapshot => Assert.Equal(snapshot.Expected, snapshot.Disk));
    }
    [Fact]
    public void Partly_refused_save_all_reports_only_the_files_actually_written()
    {
        var project = Open(); foreach (var file in project.Files) Edit(file, 5);
        project.SaveCheck = f => f.Name == "b.json" ? "refused for test" : null;
        var calls = 0; IReadOnlyList<DataFile>? received = null;
        project.FilesSaved += written => { calls++; received = written; };
        Assert.Equal("b.json", Assert.Single(project.SaveAll()).Name);
        Assert.Equal(1, calls); Assert.Equal("a.json", Assert.Single(received!).Name);
        Assert.False(project.Find("a.json")!.Dirty); Assert.True(project.Find("b.json")!.Dirty);
    }
    [Fact]
    public void Throwing_observer_cannot_break_a_completed_save_or_hide_it_from_later_observers()
    {
        var project = Open(); var file = project.Files[0]; Edit(file, 6); IReadOnlyList<DataFile>? later = null;
        project.FilesSaved += _ => throw new InvalidOperationException("observer failed");
        project.FilesSaved += written => later = written;
        Assert.True(file.Save()); Assert.False(file.Dirty); Assert.Same(file, Assert.Single(later!));
        using var saved = JsonDocument.Parse(File.ReadAllBytes(file.Path));
        Assert.Equal(6, saved.RootElement.GetProperty("value"u8).GetInt32());
    }
}
