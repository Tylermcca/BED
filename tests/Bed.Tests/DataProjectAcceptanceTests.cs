using System.Text;
using System.Text.Json;
using Bed;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>
/// INVENTED, self-authored black-box acceptance cases from the public comments and A041.
/// No game data, implementation-derived oracle, source modifications or skipped findings.
/// </summary>
public sealed class DataProjectAcceptanceTests : IDisposable
{
    private readonly string ownedParent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "A041-owned"));
    private readonly string root;
    private readonly string data;
    private readonly string schemas;
    private static readonly DateTime OldTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
    private const string ObjectSchema = """
        {"type":"object","required":["value"],"properties":{"value":{"type":"integer"},"name":{"type":"string"},"ratio":{"type":"number"},"$schema":{}},"additionalProperties":false}
        """;

    public DataProjectAcceptanceTests()
    {
        root = System.IO.Path.Combine(ownedParent, Guid.NewGuid().ToString("N"));
        data = System.IO.Path.Combine(root, "data");
        schemas = System.IO.Path.Combine(root, "schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
    }

    public void Dispose()
    {
        // Delete only this fixture's direct GUID child of its explicit temp owner.
        var resolved = System.IO.Path.GetFullPath(root);
        if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetDirectoryName(resolved), ownedParent)
            || !Guid.TryParseExact(System.IO.Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside the owned fixture directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    private string Data(string name, string text = """{"value":1}""")
    {
        var path = System.IO.Path.Combine(data, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private void Schema(string name = "one.schema.json", string text = ObjectSchema) =>
        File.WriteAllText(System.IO.Path.Combine(schemas, name), text, new UTF8Encoding(false));

    private DataProject Project(bool readOnly = false) => DataProject.Open(data, schemas, readOnly);
    private static DataFile Required(DataProject project, string name = "one.json") => Assert.Single(project.Files, f => f.Name == name);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static int Value(DataFile file) => file.Session!.Current.Root.GetProperty("value").GetInt32();

    private static void Edit(DataFile file, int value) =>
        Assert.True(file.Session!.Execute(new SetValue("/value", Json(value.ToString(System.Globalization.CultureInfo.InvariantCulture)))));

    private DataFile OpenOne(bool readOnly = false)
    {
        Schema(); Data("one.json");
        var file = Required(Project(readOnly));
        Assert.True(file.Open());
        return file;
    }

    // C01: "Every *.json file directly in the data folder is listed; each opens on demand into its own ...".
    [Fact]
    public void Empty_project_has_no_files_or_dirty_sessions()
    {
        var p = Project();
        Assert.Empty(p.Files);
        Assert.Empty(p.Dirty);
    }

    [Fact]
    public void Only_direct_json_files_are_listed()
    {
        Data("one.json"); Data("notes.txt", "not JSON"); Data("other.json.bak", "{}");
        var nested = System.IO.Path.Combine(data, "nested"); Directory.CreateDirectory(nested);
        File.WriteAllText(System.IO.Path.Combine(nested, "hidden.json"), "{}");
        Assert.Equal(new[] { "one.json" }, Project().Files.Select(f => f.Name));
    }

    // C02: "The project's files, by name in ordinal order ...".
    [Fact]
    public void File_names_follow_ordinal_order_rather_than_creation_order()
    {
        var names = new[] { "z.json", "é.json", "B.json", "a-one.json", "space name.json" };
        foreach (var n in names) Data(n);
        Assert.Equal(names.Order(StringComparer.Ordinal), Project().Files.Select(f => f.Name));
    }

    [Fact]
    public void Non_ascii_and_spaced_file_names_can_be_found_and_opened()
    {
        const string name = "灯 river.json"; Data(name); Schema("灯 river.schema.json");
        var f = Required(Project(), name);
        Assert.Equal(name, f.Name); Assert.True(f.Open()); Assert.Equal(1, Value(f));
    }


    // C04: "The file called name (wares.json), or null." Names remain exact, including Unicode/spaces.
    [Fact]
    public void Find_existing_unicode_spaced_name_returns_the_listed_file()
    {
        const string name = "灯 river.json"; Data(name);
        var p = Project(); var listed = Required(p, name);
        Assert.Same(listed, p.Find(name)); Assert.Equal(name, p.Find(name)!.Name);
    }

    // C05: "No data file is read until it's opened."
    [Fact]
    public void Exclusive_data_file_lock_does_not_prevent_lazy_project_listing()
    {
        var path = Data("one.json"); Schema();
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var f = Required(Project());
        Assert.Equal(DataFileState.NotOpened, f.State); Assert.Null(f.Session); Assert.Empty(f.Problems);
    }

    // C03: "The data folder, as a full path." / "The schema folder, as a full path."
    [Fact]
    public void Project_folder_properties_are_normalized_full_paths()
    {
        var p = DataProject.Open(System.IO.Path.Combine(data, "..", "data"), System.IO.Path.Combine(schemas, "..", "schemas"));
        Assert.Equal(System.IO.Path.GetFullPath(data), p.DataFolder);
        Assert.Equal(System.IO.Path.GetFullPath(schemas), p.SchemaFolder);
    }

    [Fact]
    public void Listed_file_exposes_its_name_and_full_path()
    {
        var path = Data("one.json"); var f = Required(Project());
        Assert.Equal("one.json", f.Name); Assert.Equal(System.IO.Path.GetFullPath(path), f.Path);
    }

    // C04: "The file called name (wares.json), or null."
    [Fact]
    public void Find_unknown_file_returns_null()
    {
        Data("one.json"); Assert.Null(Project().Find("absent.json"));
    }

    // C05: "No data file is read until it's opened."
    [Fact]
    public void Invalid_json_is_listed_without_being_parsed_at_project_open()
    {
        Data("one.json", "{broken"); Schema();
        var f = Required(Project());
        Assert.Equal(DataFileState.NotOpened, f.State); Assert.Null(f.Session); Assert.Empty(f.Problems);
    }

    [Fact]
    public void Listing_does_not_capture_data_before_first_open()
    {
        var path = Data("one.json", "{broken"); Schema(); var f = Required(Project());
        File.WriteAllText(path, """{"value":7}""");
        Assert.True(f.Open()); Assert.Equal(7, Value(f));
    }

    // C06: "Nothing is written unless a file is saved ...".
    [Fact]
    public void Opening_project_preserves_existing_data_bytes_and_timestamp()
    {
        var path = Data("one.json", """{ "value" : 1 }"""); File.SetLastWriteTimeUtc(path, OldTime);
        var bytes = File.ReadAllBytes(path); var time = File.GetLastWriteTimeUtc(path);
        _ = Project();
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    // C07: "A file's schema is named by its $schema field (the file name is used) or else by its own name ...".
    [Fact]
    public void Absent_schema_field_uses_data_basename()
    {
        Data("two.parts.json"); Schema("two.parts.schema.json");
        var f = Required(Project(), "two.parts.json");
        Assert.True(f.Open()); Assert.Equal("two.parts.schema.json", f.Schema);
    }

    [Fact]
    public void Bare_schema_name_selects_another_schema()
    {
        Data("one.json", """{"$schema":"chosen.schema.json","value":1}"""); Schema("chosen.schema.json");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal("chosen.schema.json", f.Schema);
    }

    [Fact]
    public void Relative_schema_path_uses_only_its_file_name()
    {
        Data("one.json", """{"$schema":"nested/chosen.schema.json","value":1}"""); Schema("chosen.schema.json");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal("chosen.schema.json", f.Schema);
    }

    [Fact]
    public void Schema_url_uses_only_its_file_name_without_fetching()
    {
        Data("one.json", """{"$schema":"https://example.invalid/contracts/chosen.schema.json","value":1}"""); Schema("chosen.schema.json");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal("chosen.schema.json", f.Schema);
    }

    // C08: "A file that can't be opened (bad JSON, no schema, invalid against its schema) is listed with the reasons and stays unopened".
    // Non-string routing policy isn't specified; without any possible own-name schema both policies must safely refuse.
    [Fact]
    public void Non_string_schema_without_an_available_own_schema_refuses_safely()
    {
        var path = Data("one.json", """{"$schema":42,"value":1}"""); var bytes = File.ReadAllBytes(path);
        var f = Required(Project());
        Assert.False(f.Open()); Assert.Null(f.Session); Assert.NotEmpty(f.Problems);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Explicit_missing_schema_does_not_fall_back_to_a_different_own_schema()
    {
        Data("one.json", """{"$schema":"absent.schema.json","value":1}"""); Schema();
        var f = Required(Project());
        Assert.False(f.Open()); Assert.NotEmpty(f.Problems); Assert.Null(f.Session);
    }

    // C09: "Every schema in the schema folder, as one set that can refer between files."
    [Fact]
    public void Schema_can_reference_another_file_in_the_schema_folder()
    {
        Schema("number.schema.json", """{"$id":"number.schema.json","type":"integer","minimum":0}""");
        Schema("one.schema.json", """{"type":"object","required":["value"],"properties":{"value":{"$ref":"number.schema.json"}}}""");
        Data("one.json"); var f = Required(Project());
        Assert.True(f.Open()); Assert.Empty(f.Problems); Assert.Equal(1, Value(f));
    }

    [Fact]
    public void Referenced_schema_constraints_are_applied()
    {
        Schema("number.schema.json", """{"$id":"number.schema.json","type":"integer","minimum":0}""");
        Schema("one.schema.json", """{"type":"object","properties":{"value":{"$ref":"number.schema.json"}}}""");
        Data("one.json", """{"value":-1}"""); var f = Required(Project());
        Assert.False(f.Open()); Assert.NotEmpty(f.Problems); Assert.Null(f.Session);
    }

    [Fact]
    public void Valid_array_root_opens_against_its_basename_schema()
    {
        Data("one.json", "[1,2]"); Schema(text: """{"type":"array","items":{"type":"integer"}}""");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal(2, f.Session!.Current.Root.GetArrayLength());
    }

    [Fact]
    public void Valid_scalar_root_opens_against_its_basename_schema()
    {
        Data("one.json", "17"); Schema(text: """{"type":"integer"}""");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal(17, f.Session!.Current.Root.GetInt32());
    }

    [Fact]
    public void Valid_null_root_opens_against_its_basename_schema()
    {
        Data("one.json", "null"); Schema(text: """{"type":"null"}""");
        var f = Required(Project()); Assert.True(f.Open()); Assert.Equal(JsonValueKind.Null, f.Session!.Current.Root.ValueKind);
    }

    [Fact]
    public void Invalid_json_open_fails_with_reasons_and_no_session()
    {
        Data("one.json", "{broken"); Schema(); var f = Required(Project());
        Assert.False(f.Open()); Assert.Equal(DataFileState.Refused, f.State);
        Assert.Null(f.Session); Assert.NotEmpty(f.Problems);
    }

    [Fact]
    public void All_independent_schema_issues_are_reported()
    {
        Schema(text: """{"type":"object","required":["value","name"],"properties":{"value":{"type":"integer","minimum":0},"name":{"type":"string","minLength":3}},"additionalProperties":false}""");
        Data("one.json", """{"value":-1,"name":"","unexpected":true}"""); var f = Required(Project());
        Assert.False(f.Open()); Assert.Null(f.Session); Assert.Equal(3, f.Problems.Count);
        Assert.All(f.Problems, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
        Assert.Contains(f.Problems, i => i.Message.Contains("value", StringComparison.Ordinal) || (i.Path is not null && i.Path.Contains("value", StringComparison.Ordinal)));
        Assert.Contains(f.Problems, i => i.Message.Contains("name", StringComparison.Ordinal) || (i.Path is not null && i.Path.Contains("name", StringComparison.Ordinal)));
        Assert.Contains(f.Problems, i => i.Message.Contains("unexpected", StringComparison.Ordinal) || (i.Path is not null && i.Path.Contains("unexpected", StringComparison.Ordinal)));
    }

    [Fact]
    public void Missing_schema_leaves_file_refused_with_reasons()
    {
        Data("one.json"); var f = Required(Project());
        Assert.False(f.Open()); Assert.Equal(DataFileState.Refused, f.State); Assert.Null(f.Session); Assert.NotEmpty(f.Problems);
    }

    [Fact]
    public void Refused_file_does_not_prevent_a_sibling_opening()
    {
        Data("bad.json", "{"); Data("one.json"); Schema();
        var p = Project(); Assert.False(Required(p, "bad.json").Open());
        var good = Required(p); Assert.True(good.Open()); Assert.Equal(1, Value(good));
    }

    // C10: "Opens it, if it isn't already."
    [Fact]
    public void Second_open_keeps_existing_session_and_unsaved_edit()
    {
        var f = OpenOne(); Edit(f, 2); var session = f.Session;
        Assert.True(f.Open()); Assert.Same(session, f.Session); Assert.Equal(2, Value(f)); Assert.True(f.Dirty);
        Assert.True(f.Session!.Undo()); Assert.Equal(1, Value(f));
    }

    [Fact]
    public void Second_open_does_not_replace_dirty_session_with_outside_content()
    {
        var f = OpenOne(); Edit(f, 2); var session = f.Session; File.WriteAllText(f.Path, """{"value":9}""");
        Assert.True(f.Open()); Assert.Same(session, f.Session); Assert.Equal(2, Value(f));
        Assert.Equal(DiskState.ChangedOnDisk, f.Session!.CheckDisk());
    }

    // C11: "Trying again reads the file again."
    [Fact]
    public void Failed_json_open_can_retry_after_disk_is_fixed_without_refresh()
    {
        var path = Data("one.json", "{"); Schema(); var f = Required(Project()); Assert.False(f.Open());
        File.WriteAllText(path, """{"value":5}""");
        Assert.True(f.Open()); Assert.Equal(5, Value(f)); Assert.Empty(f.Problems); Assert.Equal(DataFileState.Open, f.State);
    }

    [Fact]
    public void Failed_schema_validation_can_retry_corrected_data_without_refresh()
    {
        var path = Data("one.json", """{"value":"wrong"}"""); Schema(); var f = Required(Project()); Assert.False(f.Open());
        File.WriteAllText(path, """{"value":6}""");
        Assert.True(f.Open()); Assert.Equal(6, Value(f)); Assert.Empty(f.Problems);
    }

    [Fact]
    public void File_deleted_before_first_open_is_missing_and_has_reasons()
    {
        var path = Data("one.json"); Schema(); var f = Required(Project()); File.Delete(path);
        Assert.False(f.Open()); Assert.Equal(DataFileState.Missing, f.State); Assert.Null(f.Session); Assert.NotEmpty(f.Problems);
    }

    [Fact]
    public void Failed_missing_file_open_can_retry_after_recreation()
    {
        var path = Data("one.json"); Schema(); var f = Required(Project()); File.Delete(path); Assert.False(f.Open());
        File.WriteAllText(path, """{"value":8}""");
        Assert.True(f.Open()); Assert.Equal(8, Value(f)); Assert.Empty(f.Problems);
    }

    // C12: "The schema it was checked against, once opened or tried."
    [Fact]
    public void Schema_name_is_available_after_a_validation_refusal()
    {
        Data("one.json", """{"value":"wrong"}"""); Schema(); var f = Required(Project());
        Assert.False(f.Open()); Assert.Equal("one.schema.json", f.Schema);
    }

    [Fact]
    public void Opening_valid_file_does_not_rewrite_bytes_or_timestamp()
    {
        Schema(); var path = Data("one.json", """{ "value": 1 }"""); File.SetLastWriteTimeUtc(path, OldTime);
        var bytes = File.ReadAllBytes(path); var time = File.GetLastWriteTimeUtc(path);
        Assert.True(Required(Project()).Open());
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    // C13: "Saving a file with no edits does nothing and returns true."
    [Fact]
    public void Clean_save_preserves_byte_spelling_and_timestamp()
    {
        Schema(); var path = Data("one.json", """{ "value" : 1, "ratio":1.0 }"""); File.SetLastWriteTimeUtc(path, OldTime);
        var f = Required(Project()); Assert.True(f.Open()); var bytes = File.ReadAllBytes(path); var time = File.GetLastWriteTimeUtc(path);
        Assert.True(f.Save()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    // C14: "Saves its edits."
    [Fact]
    public void Dirty_save_persists_the_edit_and_resets_dirty()
    {
        var f = OpenOne(); Edit(f, 2); Assert.True(f.Dirty); Assert.True(f.Save());
        Assert.Equal(2, Json(File.ReadAllText(f.Path)).GetProperty("value").GetInt32());
        Assert.False(f.Dirty); Assert.False(f.Session!.Dirty); Assert.Empty(f.Problems);
    }

    // C15: DataFormat "keeping their style so an edit changes only the edited lines in a diff".
    [Fact]
    public void A_single_value_edit_changes_only_its_line()
    {
        Schema(); const string source = "{\n  \"name\": \"River 灯\",\n  \"ratio\": 1.0,\n  \"value\": 1\n}\n";
        Data("one.json", source); var f = Required(Project()); Assert.True(f.Open()); Edit(f, 2); Assert.True(f.Save());
        Assert.Equal(source.Replace("\"value\": 1", "\"value\": 2", StringComparison.Ordinal), File.ReadAllText(f.Path));
    }

    // C16: "Refused ... when the project is read-only, the file isn't open, or it was changed outside the editor ...".
    [Fact]
    public void Saving_unopened_file_refuses_without_rewriting_it()
    {
        var path = Data("one.json"); Schema(); var bytes = File.ReadAllBytes(path); var f = Required(Project());
        Assert.False(f.Save()); Assert.NotEmpty(f.Problems); Assert.Null(f.Session); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Read_only_project_refuses_dirty_save_and_keeps_its_edits()
    {
        var f = OpenOne(readOnly: true); var before = File.ReadAllBytes(f.Path); Edit(f, 2);
        Assert.False(f.Save()); Assert.NotEmpty(f.Problems); Assert.True(f.Dirty); Assert.Equal(2, Value(f));
        Assert.Equal(before, File.ReadAllBytes(f.Path));
    }

    [Fact]
    public void Outside_change_refuses_save_without_losing_either_version()
    {
        var f = OpenOne(); Edit(f, 2); const string external = """{"value":9}"""; File.WriteAllText(f.Path, external);
        Assert.False(f.Save()); Assert.True(f.Dirty); Assert.Equal(2, Value(f)); Assert.NotEmpty(f.Problems);
        Assert.Equal(external, File.ReadAllText(f.Path)); Assert.Equal(DiskState.ChangedOnDisk, f.Session!.CheckDisk());
    }

    // C17: "Saves every dirty file. Returns the files that weren't saved, each with why ...; empty when all were."
    [Fact]
    public void Save_all_with_no_dirty_files_returns_empty_and_writes_nothing()
    {
        Schema(); var path = Data("one.json"); File.SetLastWriteTimeUtc(path, OldTime);
        var p = Project(); var f = Required(p); Assert.True(f.Open()); var bytes = File.ReadAllBytes(path); var time = File.GetLastWriteTimeUtc(path);
        Assert.Empty(p.SaveAll()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Save_all_writes_dirty_file_but_does_not_rewrite_clean_sibling()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); var path = Data("two.json", """{ "value" : 3 }""");
        File.SetLastWriteTimeUtc(path, OldTime); var time = File.GetLastWriteTimeUtc(path); var bytes = File.ReadAllBytes(path);
        var p = Project(); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open()); Edit(one, 4);
        Assert.Empty(p.SaveAll()); Assert.False(one.Dirty); Assert.False(two.Dirty);
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Save_all_reports_only_conflicted_dirty_file_and_saves_other_edits()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open());
        Edit(one, 4); Edit(two, 5); File.WriteAllText(two.Path, """{"value":9}""");
        Assert.Same(two, Assert.Single(p.SaveAll())); Assert.NotEmpty(two.Problems); Assert.True(two.Dirty);
        Assert.False(one.Dirty); Assert.Equal(4, Json(File.ReadAllText(one.Path)).GetProperty("value").GetInt32());
        Assert.Equal(5, Value(two)); Assert.Equal(9, Json(File.ReadAllText(two.Path)).GetProperty("value").GetInt32());
    }

    [Fact]
    public void Read_only_save_all_returns_every_dirty_file_with_reasons()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(readOnly: true); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open());
        var before = p.Files.ToDictionary(f => f.Name, f => File.ReadAllBytes(f.Path));
        Edit(one, 4); Edit(two, 5); var refused = p.SaveAll();
        Assert.Equal(2, refused.Count); Assert.Contains(one, refused); Assert.Contains(two, refused);
        foreach (var f in refused) { Assert.True(f.Dirty); Assert.NotEmpty(f.Problems); Assert.Equal(before[f.Name], File.ReadAllBytes(f.Path)); }
    }

    [Fact]
    public void Save_all_does_not_attempt_refused_or_unopened_clean_files()
    {
        Schema(); Data("one.json"); Data("bad.json", "{"); Data("unopened.json", "not JSON");
        var p = Project(); var good = Required(p); Assert.True(good.Open()); Edit(good, 3);
        Assert.False(Required(p, "bad.json").Open()); var unopened = Required(p, "unopened.json");
        Assert.Empty(p.SaveAll()); Assert.Equal(DataFileState.NotOpened, unopened.State); Assert.Empty(unopened.Problems);
        Assert.False(good.Dirty); Assert.Equal(DataFileState.Refused, Required(p, "bad.json").State);
    }

    // C18: "Lists the folder again: new files are added; a file that's gone is kept, marked missing, so its unsaved edits aren't lost".
    [Fact]
    public void Refresh_adds_a_new_file_in_ordinal_position_without_opening_it()
    {
        Data("z.json"); var p = Project(); Data("a.json"); p.Refresh();
        Assert.Equal(new[] { "a.json", "z.json" }, p.Files.Select(f => f.Name));
        Assert.All(p.Files, f => { Assert.Equal(DataFileState.NotOpened, f.State); Assert.Null(f.Session); });
    }

    [Fact]
    public void Refresh_keeps_deleted_unopened_file_marked_missing()
    {
        var path = Data("one.json"); var p = Project(); var f = Required(p); File.Delete(path); p.Refresh();
        Assert.Same(f, Required(p)); Assert.Equal(DataFileState.Missing, f.State); Assert.Null(f.Session);
    }

    [Fact]
    public void Refresh_keeps_deleted_dirty_file_session_and_value()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); Edit(f, 2); var session = f.Session;
        File.Delete(f.Path); p.Refresh();
        Assert.Same(f, Required(p)); Assert.Same(session, f.Session); Assert.Equal(DataFileState.Missing, f.State);
        Assert.True(f.Dirty); Assert.Equal(2, Value(f)); Assert.Contains(f, p.Dirty);
    }

    [Fact]
    public void Refresh_keeps_deleted_clean_file_session()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); var session = f.Session;
        File.Delete(f.Path); p.Refresh();
        Assert.Same(session, f.Session); Assert.Equal(DataFileState.Missing, f.State); Assert.False(f.Dirty);
    }

    // C19: "a missing file that's back returns to unopened, or keeps its session."
    [Fact]
    public void Restored_unopened_file_returns_to_unopened()
    {
        var path = Data("one.json"); var p = Project(); var f = Required(p); File.Delete(path); p.Refresh();
        Data("one.json", """{"value":8}"""); p.Refresh();
        Assert.Same(f, Required(p)); Assert.Equal(DataFileState.NotOpened, f.State); Assert.Null(f.Session);
    }

    [Fact]
    public void Restored_file_keeps_its_dirty_session_instead_of_reloading()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); Edit(f, 2); var session = f.Session;
        File.Delete(f.Path); p.Refresh(); Data("one.json", """{"value":8}"""); p.Refresh();
        Assert.Same(session, f.Session); Assert.Equal(DataFileState.Open, f.State); Assert.Equal(2, Value(f)); Assert.True(f.Dirty);
    }

    // C20: CheckDisk "Compares the file on disk with what this session last loaded or saved."
    [Fact]
    public void Recreated_different_file_is_still_an_outside_change_after_refresh()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open());
        File.Delete(f.Path); p.Refresh(); Data("one.json", """{"value":8}"""); p.Refresh();
        Assert.Equal(DiskState.ChangedOnDisk, f.Session!.CheckDisk()); Assert.Equal(1, Value(f));
    }

    [Fact]
    public void Saving_edits_over_a_different_recreated_file_is_refused()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); Edit(f, 2);
        File.Delete(f.Path); p.Refresh(); const string replacement = """{"value":8}"""; Data("one.json", replacement); p.Refresh();
        Assert.False(f.Save()); Assert.NotEmpty(f.Problems); Assert.True(f.Dirty);
        Assert.Equal(2, Value(f)); Assert.Equal(replacement, File.ReadAllText(f.Path));
    }

    [Fact]
    public void Recreated_identical_loaded_bytes_are_unchanged_and_can_receive_edits()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); var original = File.ReadAllText(f.Path); Edit(f, 2);
        File.Delete(f.Path); p.Refresh(); Data("one.json", original); p.Refresh();
        Assert.Equal(DiskState.Unchanged, f.Session!.CheckDisk()); Assert.True(f.Save()); Assert.False(f.Dirty);
        Assert.Equal(2, Json(File.ReadAllText(f.Path)).GetProperty("value").GetInt32());
    }

    [Fact]
    public void Refresh_keeps_a_dirty_files_undo_history_across_deletion()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); Edit(f, 2);
        File.Delete(f.Path); p.Refresh();
        Assert.True(f.Session!.Undo()); Assert.Equal(1, Value(f)); Assert.False(f.Dirty); Assert.Equal(DataFileState.Missing, f.State);
    }

    // C21: "Drops the session (and any unsaved edits) so the file is read afresh next time it's opened."
    [Fact]
    public void Close_drops_dirty_session_and_unsaved_edits_without_writing()
    {
        var f = OpenOne(); var bytes = File.ReadAllBytes(f.Path); Edit(f, 2); f.Close();
        Assert.Null(f.Session); Assert.False(f.Dirty); Assert.Equal(DataFileState.NotOpened, f.State);
        Assert.Equal(bytes, File.ReadAllBytes(f.Path));
    }

    [Fact]
    public void Close_removes_file_from_project_dirty_list()
    {
        Schema(); Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open()); Edit(f, 2);
        Assert.Contains(f, p.Dirty); f.Close(); Assert.Empty(p.Dirty);
    }

    [Fact]
    public void Reopen_after_close_reads_new_disk_content_into_new_session()
    {
        var f = OpenOne(); var first = f.Session; Edit(f, 2); f.Close(); File.WriteAllText(f.Path, """{"value":8}""");
        Assert.True(f.Open()); Assert.NotSame(first, f.Session); Assert.Equal(8, Value(f)); Assert.False(f.Dirty);
        Assert.False(f.Session!.Undo());
    }

    [Fact]
    public void Close_and_reopen_after_conflict_rebases_on_the_outside_version()
    {
        var f = OpenOne(); Edit(f, 2); File.WriteAllText(f.Path, """{"value":8}"""); Assert.False(f.Save()); f.Close();
        Assert.True(f.Open()); Assert.Equal(8, Value(f)); Assert.Empty(f.Problems); Edit(f, 9);
        Assert.True(f.Save()); Assert.Equal(9, Json(File.ReadAllText(f.Path)).GetProperty("value").GetInt32());
    }

    // C22: "Throws DirectoryNotFoundException when either folder is missing, and JsonException when a schema isn't valid JSON."
    [Fact]
    public void Missing_data_folder_throws_directory_not_found()
    {
        Directory.Delete(data);
        Assert.Throws<DirectoryNotFoundException>(() => DataProject.Open(data, schemas));
    }

    [Fact]
    public void Missing_schema_folder_throws_directory_not_found()
    {
        Directory.Delete(schemas);
        Assert.Throws<DirectoryNotFoundException>(() => DataProject.Open(data, schemas));
    }

    [Fact]
    public void Malformed_schema_folder_entry_throws_json_exception()
    {
        Schema(text: "{bad");
        Assert.ThrowsAny<JsonException>(() => Project());
    }

    // C23: "each opens on demand into its own ... with its own undo, save and outside-change check."
    [Fact]
    public void Two_files_open_into_distinct_sessions()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(); var one = Required(p); var two = Required(p, "two.json");
        Assert.True(one.Open()); Assert.True(two.Open()); Assert.NotSame(one.Session, two.Session);
    }

    [Fact]
    public void Editing_one_file_never_dirties_a_clean_sibling()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open()); Edit(one, 2);
        Assert.True(one.Dirty); Assert.False(two.Dirty); Assert.Equal(1, Value(two)); Assert.Same(one, Assert.Single(p.Dirty));
    }

    [Fact]
    public void Undo_in_one_session_does_not_change_another_sessions_edit()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open()); Edit(one, 2); Edit(two, 3);
        Assert.True(one.Session!.Undo()); Assert.Equal(1, Value(one)); Assert.False(one.Dirty);
        Assert.Equal(3, Value(two)); Assert.True(two.Dirty); Assert.True(two.Session!.Undo()); Assert.Equal(1, Value(two));
    }

    [Fact]
    public void Saving_one_session_does_not_save_another_sessions_edit()
    {
        Schema(); Schema("two.schema.json"); Data("one.json"); Data("two.json");
        var p = Project(); var one = Required(p); var two = Required(p, "two.json"); Assert.True(one.Open()); Assert.True(two.Open()); Edit(one, 2); Edit(two, 3);
        Assert.True(one.Save()); Assert.False(one.Dirty); Assert.True(two.Dirty); Assert.Equal(1, Json(File.ReadAllText(two.Path)).GetProperty("value").GetInt32());
    }

    // C24: Dirty "True when the document differs from what was last loaded or saved."
    [Fact]
    public void Undo_to_loaded_state_removes_file_from_save_all_dirty_set()
    {
        Schema(); var path = Data("one.json"); var p = Project(); var f = Required(p); Assert.True(f.Open());
        File.SetLastWriteTimeUtc(path, OldTime); var time = File.GetLastWriteTimeUtc(path); var bytes = File.ReadAllBytes(path);
        Edit(f, 2); Assert.True(f.Session!.Undo()); Assert.False(f.Dirty); Assert.Empty(p.Dirty);
        Assert.Empty(p.SaveAll()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Undo_after_save_is_dirty_relative_to_the_saved_version()
    {
        var f = OpenOne(); Edit(f, 2); Assert.True(f.Save()); Assert.True(f.Session!.Undo());
        Assert.Equal(1, Value(f)); Assert.True(f.Dirty); Assert.Equal(2, Json(File.ReadAllText(f.Path)).GetProperty("value").GetInt32());
    }

    [Fact]
    public void Freshly_opened_session_reports_unchanged_disk()
    {
        var f = OpenOne(); Assert.Equal(DiskState.Unchanged, f.Session!.CheckDisk());
    }

    // C25: DiskState.Missing: "The file is gone."
    [Fact]
    public void Deleted_open_file_reports_missing_disk()
    {
        var f = OpenOne(); File.Delete(f.Path);
        Assert.Equal(DiskState.Missing, f.Session!.CheckDisk()); Assert.Equal(DataFileState.Missing, f.State);
    }

    // C26: DiskState.Unchanged: "The file holds what this session last loaded or saved."
    [Fact]
    public void Timestamp_only_change_with_identical_content_is_not_an_outside_content_change()
    {
        var f = OpenOne(); File.SetLastWriteTimeUtc(f.Path, OldTime);
        Assert.Equal(DiskState.Unchanged, f.Session!.CheckDisk());
    }

    [Fact]
    public void Successful_save_updates_outside_change_baseline()
    {
        var f = OpenOne(); Edit(f, 2); Assert.True(f.Save());
        Assert.Equal(DiskState.Unchanged, f.Session!.CheckDisk()); Edit(f, 3); Assert.True(f.Save());
        Assert.Equal(DiskState.Unchanged, f.Session.CheckDisk()); Assert.Equal(3, Json(File.ReadAllText(f.Path)).GetProperty("value").GetInt32());
    }
}
