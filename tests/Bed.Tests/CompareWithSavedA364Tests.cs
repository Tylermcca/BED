using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Launcher;
using Xunit;

namespace Bed.Tests;

public sealed class CompareWithSavedA364Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("compare-saved-").FullName;
    public void Dispose() => Directory.Delete(root, recursive: true);

    private (DataFile File, EditorModel<DataDocument> Editor, CompareFilePanel Panel) Open()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(schemas, "items.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(data, "items.json"), """{"rows":[{"id":"a","value":1}]}""");
        var project = DataProject.Open(data, schemas);
        var file = Assert.Single(project.Files);
        Assert.True(file.Open());
        var editor = new EditorModel<DataDocument>(file.Session!, DataEditing.Outline);
        var panel = new CompareFilePanel(file, editor, project.Schemas);
        editor.Add(panel.Panel());
        return (file, editor, panel);
    }

    [Fact]
    public async Task One_edited_row_compares_without_saving_or_mutating_the_draft()
    {
        var (file, editor, panel) = Open();
        var saved = File.ReadAllBytes(file.Path);
        Assert.True(editor.Session.Execute(new SetValue("/rows/0/value", JsonDocument.Parse("2").RootElement.Clone())));
        var draft = editor.Session.Current;
        var journal = editor.Session.Journal.ToArray();
        var command = Assert.Single(panel.Panel().Commands!, c => c.Id == "editor.compare.saved");
        Assert.Equal("Compare with saved", command.Label);
        Assert.Null(command.Unavailable!());
        Assert.True(command.Run());
        Assert.True(editor.IsOpen("editor.compare"));
        var result = await panel.Pending!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(result.Error);
        Assert.Same(draft, result.Current);
        Assert.True(result.Result!.Complete);
        var change = Assert.Single(result.Result.Changes);
        Assert.Equal("changed", change.Kind);
        Assert.Equal("/rows/0/value", change.BeforeAt);
        Assert.Equal("/rows/0/value", change.AfterAt);
        Assert.Equal("1", change.Before);
        Assert.Equal("2", change.After);
        Assert.Equal(saved, File.ReadAllBytes(file.Path));
        Assert.Same(draft, editor.Session.Current);
        Assert.Equal(journal, editor.Session.Journal);
        Assert.True(editor.Session.Dirty);
    }

    [Fact]
    public async Task Missing_disk_file_reports_error_without_recreating_it()
    {
        var (file, editor, panel) = Open();
        var draft = editor.Session.Current;
        File.Delete(file.Path);
        Assert.True(Assert.Single(panel.Panel().Commands!, c => c.Id == "editor.compare.saved").Run());
        var result = await panel.Pending!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(result.Error);
        Assert.Null(result.Result);
        Assert.Same(draft, result.Current);
        Assert.Same(draft, editor.Session.Current);
        Assert.False(File.Exists(file.Path));
        Assert.Empty(editor.Session.Journal);
    }
}
