using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RecursiveDataProjectEdit03Tests : IDisposable
{
    private readonly string temporary = Directory.CreateTempSubdirectory("nested-data-").FullName;
    private readonly List<string> links = [];
    private string Data => Path.Combine(temporary, "data");
    private string Schemas => Path.Combine(temporary, "schemas");
    public RecursiveDataProjectEdit03Tests()
    {
        Directory.CreateDirectory(Path.Combine(Data, "nested")); Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Data, "values.json"), "{\"n\":1}\n");
        File.WriteAllText(Path.Combine(Data, "nested", "values.json"), "{\"n\":2}\n");
        File.WriteAllText(Path.Combine(Schemas, "values.schema.json"), "{\"type\":\"object\",\"properties\":{\"n\":{\"type\":\"integer\",\"minimum\":0}}}");
    }
    private DataProject Open(bool recursive = true) => DataProject.Open(Data, Schemas, recursive: recursive);
    private void Junction(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(5000)); Assert.True(process.ExitCode == 0, said);
        }
        else Directory.CreateSymbolicLink(link, target);
        links.Add(link);
    }
    public void Dispose()
    {
        foreach (var link in links) if (Directory.Exists(link)) Directory.Delete(link);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(temporary), StringComparison.OrdinalIgnoreCase);
        Directory.Delete(temporary, recursive: true);
    }

    [Fact]
    public void Flat_default_is_unchanged_and_nested_names_keep_filename_schema_binding()
    {
        var flat = Open(false); Assert.False(flat.Recursive); Assert.Equal("values.json", Assert.Single(flat.Files).Name);
        var project = Open(); Assert.True(project.Recursive);
        Assert.Equal(new[] { "nested/values.json", "values.json" }, project.Files.Select(f => f.Name));
        var nested = project.Find("nested/values.json")!; Assert.True(nested.Open()); Assert.Equal("values.schema.json", nested.Schema);
        Assert.Equal(2, nested.Session!.Current.At("/n")!.Value.GetInt32());
        Assert.True(flat.Files[0].Open()); Assert.Equal(1, flat.Files[0].Session!.Current.At("/n")!.Value.GetInt32());
    }

    [Fact]
    public void Nested_edit_save_rename_delete_restore_and_new_file_keep_normal_checks_and_bytes()
    {
        var project = Open(); project.SchemaFor = _ => "values.schema.json";
        var file = project.Find("nested/values.json")!; Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(7))));
        Assert.True(file.Save()); Assert.Equal(7, JsonDocument.Parse(File.ReadAllText(file.Path)).RootElement.GetProperty("n").GetInt32());
        Assert.False(file.Session.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(-1))));
        var bytes = File.ReadAllBytes(file.Path);
        Assert.True(project.Rename(file.Name, "other/deep/moved.json", out var why), why);
        Assert.False(File.Exists(file.Path)); Assert.Equal(bytes, File.ReadAllBytes(project.Find("other/deep/moved.json")!.Path));
        Assert.True(project.Delete("other/deep/moved.json", out why), why);
        Assert.True(project.Restore("other/deep/moved.json", out why), why);
        Assert.Equal(bytes, File.ReadAllBytes(project.Find("other/deep/moved.json")!.Path));
        Assert.True(project.Create("created/values.json", "values.schema.json", out why), why);
        Assert.True(project.CreateFromText("copied/values.json", "{\"n\":8}\n", out why), why);
        Assert.False(project.CreateFromText("copied/values.json", "{\"n\":9}", out why));
        Assert.Equal("{\"n\":8}\n", File.ReadAllText(project.Find("copied/values.json")!.Path));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("nested/../../outside.json")]
    [InlineData("/outside.json")]
    [InlineData("C:/outside.json")]
    [InlineData("nested\\outside.json")]
    [InlineData("nested//outside.json")]
    [InlineData("nested/./outside.json")]
    [InlineData("nested/.. /outside.json")]
    [InlineData("nested./outside.json")]
    [InlineData("NUL.json")]
    [InlineData("nested/COM1.json")]
    [InlineData("values.json/new.json")]
    public void Traversal_and_noncanonical_names_are_refused_without_writes(string name)
    {
        var project = Open();
        var before = Directory.GetFiles(temporary, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        Assert.False(project.CreateFromText(name, "{\"n\":3}", out var why)); Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.False(project.CreateFromText(name, "{}", out _, preview: true));
        Assert.False(project.Create(name, "values.schema.json", out _));
        Assert.False(project.Rename("values.json", name, out _)); Assert.False(project.Delete(name, out _)); Assert.False(project.Restore(name, out _));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(temporary, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Snapshot_owns_nested_drafts_and_new_external_files_and_reopens_the_same_relative_names()
    {
        var project = Open(); var file = project.Find("nested/values.json")!; Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(6))));
        Assert.Throws<IOException>(() => file.Session.Save(Path.Combine(Data, "not-admitted.txt")));
        Assert.False(File.Exists(Path.Combine(Data, "not-admitted.txt")));
        var snapshot = CheckSnapshot.Capture(project); Assert.Equal("{\"n\": 6}\n", snapshot.Text("data/nested/values.json"));
        var copy = snapshot.OpenCopy(Path.Combine(temporary, "snapshot"), out _);
        Assert.True(copy.Recursive); Assert.True(copy.ReadOnly); Assert.True(copy.Find("nested/values.json")!.Open());
        Assert.Equal(6, copy.Find("nested/values.json")!.Session!.Current.At("/n")!.Value.GetInt32());
        File.WriteAllText(Path.Combine(Data, "nested", "added.json"), "{\"n\":9}");
        Assert.NotEqual(snapshot.Signature, CheckSnapshot.SignatureOf(project));
        Assert.Equal("{\"n\":9}", CheckSnapshot.Capture(project).Text("data/nested/added.json"));
        Assert.Equal("{\"n\":2}\n", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Junctions_are_not_admitted_and_replacing_an_open_folder_refuses_reads_and_writes()
    {
        var outside = Directory.CreateDirectory(Path.Combine(temporary, "outside")).FullName;
        var outsidePath = Path.Combine(outside, "values.json"); File.WriteAllText(outsidePath, "{\"n\":77}\n");
        Junction(Path.Combine(Data, "linked"), outside);
        var project = Open(); Assert.Null(project.Find("linked/values.json"));
        Assert.False(project.CreateFromText("linked/new.json", "{}", out var why)); Assert.Contains("junction or symbolic link", why);
        var file = project.Find("nested/values.json")!; Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(8))));
        Directory.Move(Path.Combine(Data, "nested"), Path.Combine(temporary, "original-nested"));
        Junction(Path.Combine(Data, "nested"), outside);
        Assert.NotNull(file.Session.FileAccessRefusal);
        Assert.False(file.Save()); Assert.False(file.Open()); Assert.False(file.Repair());
        Assert.Equal(DiskState.ChangedOnDisk, file.Session.CheckDisk());
        Assert.False(file.Session.Revert()); Assert.Contains(file.Session.LastIssues, issue => issue.Message.Contains("junction or symbolic link", StringComparison.Ordinal));
        Assert.Throws<IOException>(() => file.Session.Save());
        Assert.Throws<IOException>(() => file.Session.Save(Path.Combine(temporary, "escaped.json")));
        Assert.False(File.Exists(Path.Combine(temporary, "escaped.json")));
        Assert.False(project.Rename(file.Name, "renamed.json", out _)); Assert.False(project.Delete(file.Name, out _));
        Assert.Throws<IOException>(() => CheckSnapshot.Capture(project));
        Assert.Equal("{\"n\":77}\n", File.ReadAllText(outsidePath)); Assert.False(File.Exists(Path.Combine(outside, "new.json")));
        Assert.True(file.Dirty); Assert.Equal(1, file.Session.History.UndoCount);
        project.Refresh(); Assert.Equal(DataFileState.Refused, file.State);
    }

    [Fact]
    public void Recursive_SaveAs_creates_an_admitted_checked_copy_and_keeps_source_draft_history()
    {
        using var host = new HeadlessHost(900, 700) { Drawing = false }; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var project = Open(); project.SchemaFor = _ => "values.schema.json";
        using var workspace = new DataWorkspace(project, new Shortcuts()); Assert.True(workspace.Show("nested/values.json"));
        var source = workspace.Window!.Editor.Session;
        Assert.True(source.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(10))));
        Assert.False(workspace.Window.SaveAs(Path.Combine(temporary, "outside.json")));
        Assert.False(File.Exists(Path.Combine(temporary, "outside.json")));
        Assert.True(workspace.Window.SaveAs(Path.Combine(Data, "saved/copy.json")), workspace.Window.Status);
        Assert.Equal("saved/copy.json", workspace.Active!.Name); Assert.False(workspace.Window!.Editor.Session.Dirty);
        Assert.Equal(10, workspace.Window.Editor.Session.Current.At("/n")!.Value.GetInt32());
        Assert.True(source.Dirty); Assert.Equal(1, source.History.UndoCount); Assert.Same(source, project.Find("nested/values.json")!.Session);
        Assert.Equal(2, JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "nested/values.json"))).RootElement.GetProperty("n").GetInt32());
        project.FileCheck = _ => "host refuses copies";
        Assert.False(workspace.Window.SaveAs(Path.Combine(Data, "saved/refused.json"))); Assert.False(File.Exists(Path.Combine(Data, "saved/refused.json")));
    }
    [Fact]
    public void Default_session_without_policy_keeps_existing_SaveAs_CheckDisk_and_Revert_behavior()
    {
        var file = Open(false).Find("values.json")!; Assert.True(file.Open());
        var session = file.Session!; Assert.Null(session.FileAccessRefusal);
        var target = Path.Combine(temporary, "normal-session-copy.json");
        session.Save(target); Assert.Equal(target, session.FilePath); Assert.Equal(DiskState.Unchanged, session.CheckDisk());
        File.WriteAllText(target, "{\"n\":4}\n"); Assert.Equal(DiskState.ChangedOnDisk, session.CheckDisk());
        Assert.True(session.Revert()); Assert.Equal(4, session.Current.At("/n")!.Value.GetInt32());
        Assert.Equal(DiskState.Unchanged, session.CheckDisk());
    }

    [Fact]
    public void Linked_root_and_linked_snapshot_destination_are_refused_before_data_access()
    {
        var alias = Path.Combine(temporary, "alias"); Junction(alias, Data);
        Assert.Throws<IOException>(() => DataProject.Open(alias, Schemas, recursive: true));
        var snapshot = CheckSnapshot.Capture(Open());
        var copy = Directory.CreateDirectory(Path.Combine(temporary, "unsafe-copy")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(temporary, "outside-copy")).FullName;
        var protectedFile = Path.Combine(outside, "values.json"); File.WriteAllText(protectedFile, "leave intact");
        Junction(Path.Combine(copy, "data"), outside);
        Assert.Throws<IOException>(() => snapshot.OpenCopy(copy, out _));
        Assert.Equal("leave intact", File.ReadAllText(protectedFile));
    }
    [Fact]
    public void Flat_refresh_and_checks_keep_their_existing_missing_folder_behavior()
    {
        var project = Open(false);
        Directory.Move(Data, Path.Combine(temporary, "removed-data"));
        Assert.Throws<DirectoryNotFoundException>(project.Refresh);
        var snapshot = CheckSnapshot.Capture(project);
        Assert.DoesNotContain(snapshot.Names, name => name.StartsWith("data/", StringComparison.Ordinal));
    }
    [Fact]
    public void Already_compiled_adapters_keep_the_original_five_argument_Open_entry_point()
    {
        var entry = typeof(DataProject).GetMethod("Open", [typeof(string), typeof(string), typeof(bool),
            typeof(Func<string, string>), typeof(Func<string, IValidator<DataDocument>>)]);
        Assert.NotNull(entry);
        var project = Assert.IsType<DataProject>(entry.Invoke(null, [Data, Schemas, false, null, null]));
        Assert.False(project.Recursive); Assert.Equal("values.json", Assert.Single(project.Files).Name);
    }
}
