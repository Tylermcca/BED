using System.Text;
using System.Text.Json;
using Bed;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

// INVENTED: neutral fixtures and expectations, independently authored from public doc comments.
// Contract references below mean DataDocument.cs or JsonSchemas.cs; no implementation/test bodies used.
public sealed class DataAcceptanceTests
{
    private static readonly DataFormat Format = new("acceptance.data");
    private const string Styled = "{\n  \"keep\": { \"spelling\": 1.0, \"word\": \"café\" },\n  \"group\": {\n    \"amount\": 2,\n    \"label\": \"old\"\n  },\n  \"items\": [\n    { \"id\": \"a\", \"v\": 1e3 },\n    { \"id\": \"b\", \"v\": -0 },\n    { \"id\": \"c\", \"v\": 3 }\n  ],\n  \"tail\": \"stay\\nput\"\n}\n";

    private static JsonElement J(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static JsonSchemas Schemas(string schema = "{}") => new JsonSchemas().Add("fixture.schema.json", schema);
    private static DocumentSession<DataDocument> Session(string text, string schema = "{}") =>
        DataKit.Session(Format.Read(text), Schemas(schema), "fixture.schema.json", "acceptance.data");
    private static string Text(DocumentSession<DataDocument> session) => Format.Write(session.Current);
    private static JsonElement At(DataDocument doc, string pointer) => Assert.IsType<JsonElement>(doc.At(pointer));

    // Original line numbers, inclusive. The changed span can grow/shrink; all original prefix/suffix bytes must survive.
    // Insertion uses first = last + 1, so zero original lines are eligible. LF separators are included in each token.
    private static void OnlyLines(string before, string after, int first, int last)
    {
        var lines = before.Split('\n');
        Assert.EndsWith("\n", before);
        Assert.InRange(first, 1, lines.Length);
        Assert.InRange(last, first - 1, lines.Length - 1);
        var prefix = string.Join("\n", lines.Take(first - 1));
        if (first > 1) prefix += "\n";
        var suffix = string.Join("\n", lines.Skip(last));
        Assert.NotEqual(before, after);
        Assert.StartsWith(prefix, after, StringComparison.Ordinal);
        Assert.EndsWith(suffix, after, StringComparison.Ordinal);
        Assert.True(after.Length >= prefix.Length + suffix.Length, "Preserved regions must not overlap.");
    }

    private static string Edit(string text, ICommand<DataDocument> command, int first, int last, Action<DataDocument> valueCheck)
    {
        var before = Format.Read(text);
        var result = command.Apply(before);
        Assert.True(result.Ok, string.Join("; ", result.Issues.Select(i => i.ToString())));
        var after = Assert.IsType<DataDocument>(result.Document);
        valueCheck(after);
        Assert.Equal(text, Format.Write(before)); // Commands.cs:12: must not change the input.
        var output = Format.Write(after);
        OnlyLines(text, output, first, last);
        return output;
    }

    private static void Refused(ICommand<DataDocument> command, string path)
    {
        var session = Session(Styled);
        var original = session.Current;
        var journal = JournalFile.Write(session.Journal);
        var revision = session.Revision;
        Assert.False(session.Execute(command));
        Assert.Same(original, session.Current);
        Assert.Equal(Styled, Text(session));
        Assert.Equal(journal, JournalFile.Write(session.Journal));
        Assert.Equal(revision, session.Revision);
        Assert.False(session.History.CanUndo);
        Assert.False(session.History.CanRedo);
        Assert.False(session.Dirty);
        Assert.Contains(session.LastIssues, i => i.Severity == Severity.Error && i.Path == path &&
            !string.IsNullOrWhiteSpace(i.Message));
    }

    public static IEnumerable<object[]> UneditedFiles()
    {
        foreach (var text in new[]
        {
            "{\"a\":1}\n", "{ \"a\": 1 }\n", "[1,2,3]\n", "[ 1, 2, 3 ]\n",
            "{\n  \"z\": 1.0,\n  \"a\": 1e3,\n  \"n\": -0\n}\n",
            "{ \"a\": 1E+03, \"b\": 1.2300, \"c\": 0.0 }\n",
            "{ \"word\": \"café 雪 🚀\" }\n", "{\"escape\":\"line\\nquote\\\"control\\u0001\"}\n",
            "{ \"ascii\": \"\\u0061\", \"slash\": \"\\/\", \"back\": \"\\\\\" }\n",
            "{}\n", "[]\n", "{ \"empty\": {}, \"list\": [] }\n",
            "{\n  \"a\": [{ \"b\": [ { \"c\": { \"d\": 7 } } ] }]\n}\n",
            "[\n  [\n    [\n      []\n    ]\n  ]\n]\n", "true\n", "null\n", "\"scalar 雪\"\n", Styled
        }) yield return new object[] { text };
    }

    [Theory]
    [MemberData(nameof(UneditedFiles))]
    public void Untouched_source_round_trips_utf8_bytes(string text)
    {
        // DataDocument.cs:16-17: every unchanged part copied verbatim (including noncanonical original escapes).
        var doc = Format.Read(text);
        Assert.NotNull(doc.Source);
        Assert.Equal(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetBytes(Format.Write(doc)));
    }

    [Fact]
    public void Missing_final_newline_is_preserved_on_save()
    {
        // Existing files keep their exact ending; only newly constructed documents get a final newline.
        const string text = "{ \"n\": 1.0, \"word\": \"雪\" }";
        Assert.Equal(text, Format.Write(Format.Read(text)));
    }

    [Fact]
    public void New_document_uses_two_spaces_literal_unicode_lf_and_final_newline()
    {
        var text = Format.Write(new DataDocument(J("{\"first\":\"雪\",\"next\":{\"n\":1}}")));
        Assert.Equal("{\n  \"first\": \"雪\",\n  \"next\": {\n    \"n\": 1\n  }\n}\n", text);
    }

    [Fact]
    public void Format_names_the_kind_without_requiring_a_kind_field()
    {
        Assert.Equal("acceptance.data", Format.Kind);
        Assert.Equal(1, At(Format.Read("{\"n\":1}"), "/n").GetInt32());
    }

    [Fact]
    public void Deep_set_changes_only_original_line_four() =>
        Edit(Styled, new SetValue("/group/amount", J("7")), 4, 4, d => Assert.Equal(7, At(d, "/group/amount").GetInt32()));

    [Fact]
    public void Deep_string_set_preserves_every_other_original_escape_and_number() =>
        Edit(Styled, new SetValue("/group/label", J("\"new 雪\\n\\\"\"")), 5, 5,
            d => Assert.Equal("new 雪\n\"", At(d, "/group/label").GetString()));

    [Fact]
    public void Set_inside_inline_list_changes_only_its_containing_line()
    {
        const string text = "{\n  \"before\": -0,\n  \"a\": [ 1.0, { \"n\": 2 }, 1e3 ],\n  \"after\": \"\\u0061\"\n}\n";
        Edit(text, new SetValue("/a/1/n", J("9")), 3, 3, d => Assert.Equal(9, At(d, "/a/1/n").GetInt32()));
        var output = Format.Write(new SetValue("/a/1/n", J("9")).Apply(Format.Read(text)).Document!);
        Assert.Contains("[ 1.0,", output);
        Assert.Contains(", 1e3 ]", output);
    }

    [Fact]
    public void Set_in_inline_root_object_keeps_it_one_line()
    {
        const string text = "{ \"a\": 1, \"b\": 1.0 }\n";
        var output = Edit(text, new SetValue("/a", J("2")), 1, 1, d => Assert.Equal(2, At(d, "/a").GetInt32()));
        Assert.Equal(1, output.Count(c => c == '\n'));
        Assert.Contains("\"b\": 1.0", output);
    }

    [Fact]
    public void Replacing_root_makes_all_original_lines_eligible()
    {
        // Root replacement owns the entire original document; it carries no false one-line locality requirement.
        Edit(Styled, new SetValue("", J("{\"replacement\":true}")), 1, 13,
            d => Assert.True(At(d, "/replacement").GetBoolean()));
    }

    [Fact]
    public void Add_optional_property_changes_last_sibling_line_and_inserts_new_lines() =>
        Edit(Styled, new SetValue("/group/optional", J("true")), 5, 5,
            d => Assert.True(At(d, "/group/optional").GetBoolean()));

    [Fact]
    public void Remove_last_optional_property_changes_previous_comma_and_removed_line() =>
        Edit(Styled, new SetValue("/group/label", null), 4, 5, d => Assert.Null(d.At("/group/label")));

    [Fact]
    public void Remove_first_optional_property_changes_only_its_original_line() =>
        Edit(Styled, new SetValue("/group/amount", null), 4, 4, d => Assert.Null(d.At("/group/amount")));

    [Fact]
    public void Add_optional_property_to_inline_object_keeps_one_line()
    {
        var output = Edit(Styled, new SetValue("/keep/new", J("[]")), 2, 2, d => Assert.Equal(0, At(d, "/keep/new").GetArrayLength()));
        Assert.Equal(13, output.Count(c => c == '\n'));
    }

    [Fact]
    public void Remove_property_from_inline_object_changes_only_its_line() =>
        Edit(Styled, new SetValue("/keep/word", null), 2, 2, d => Assert.Null(d.At("/keep/word")));

    [Fact]
    public void Insert_item_before_middle_changes_no_original_lines()
    {
        Edit(Styled, new InsertItem("/items", 1, J("{\"id\":\"x\"}")), 9, 8,
            d => Assert.Equal(new[] { "a", "x", "b", "c" }, At(d, "/items").EnumerateArray().Select(v => v.GetProperty("id").GetString())));
    }

    [Fact]
    public void Append_item_changes_previous_last_comma_and_inserts_item() =>
        Edit(Styled, new InsertItem("/items", 3, J("{\"id\":\"x\"}")), 10, 10,
            d => Assert.Equal("x", At(d, "/items/3/id").GetString()));

    [Fact]
    public void Remove_middle_item_changes_only_its_original_line() =>
        Edit(Styled, new RemoveItem("/items/1"), 9, 9,
            d => Assert.Equal(new[] { "a", "c" }, At(d, "/items").EnumerateArray().Select(v => v.GetProperty("id").GetString())));

    [Fact]
    public void Remove_last_item_changes_previous_comma_and_removed_line() =>
        Edit(Styled, new RemoveItem("/items/2"), 9, 10,
            d => Assert.Equal(2, At(d, "/items").GetArrayLength()));

    [Fact]
    public void Move_item_changes_only_the_span_between_old_and_new_positions() =>
        Edit(Styled, new MoveItem("/items", 0, 2), 8, 10,
            d => Assert.Equal(new[] { "b", "c", "a" }, At(d, "/items").EnumerateArray().Select(v => v.GetProperty("id").GetString())));

    [Fact]
    public void Move_item_back_changes_only_the_span_between_positions() =>
        Edit(Styled, new MoveItem("/items", 2, 0), 8, 10,
            d => Assert.Equal(new[] { "c", "a", "b" }, At(d, "/items").EnumerateArray().Select(v => v.GetProperty("id").GetString())));

    [Fact]
    public void Insert_into_inline_list_does_not_expand_it()
    {
        const string text = "{\n  \"a\": [ 1.0, 2, -0 ],\n  \"b\": \"雪\"\n}\n";
        var output = Edit(text, new InsertItem("/a", 1, J("9")), 2, 2,
            d => Assert.Equal(new[] { 1d, 9d, 2d, 0d }, At(d, "/a").EnumerateArray().Select(v => v.GetDouble())));
        Assert.Equal(4, output.Count(c => c == '\n'));
        Assert.Contains("1.0", output);
        Assert.Contains("-0", output);
    }

    [Fact]
    public void Remove_from_inline_list_does_not_expand_it()
    {
        const string text = "{\n  \"a\": [ 1.0, 2, -0 ],\n  \"b\": \"雪\"\n}\n";
        var output = Edit(text, new RemoveItem("/a/1"), 2, 2, d => Assert.Equal(2, At(d, "/a").GetArrayLength()));
        Assert.Equal(4, output.Count(c => c == '\n'));
        Assert.Contains("1.0", output);
        Assert.Contains("-0", output);
    }

    [Fact]
    public void Move_inside_inline_list_changes_only_its_line()
    {
        const string text = "{\n  \"a\": [ 1, 2, 3 ],\n  \"b\": \"雪\"\n}\n";
        var output = Edit(text, new MoveItem("/a", 0, 2), 2, 2,
            d => Assert.Equal(new[] { 2, 3, 1 }, At(d, "/a").EnumerateArray().Select(v => v.GetInt32())));
        Assert.Equal(4, output.Count(c => c == '\n'));
    }

    [Fact]
    public void Set_refuses_missing_parent() => Refused(new SetValue("/absent/x", J("1")), "$.absent.x");
    [Fact]
    public void Set_refuses_scalar_parent() => Refused(new SetValue("/tail/x", J("1")), "$.tail.x");
    [Fact]
    public void Set_refuses_list_index_past_end() => Refused(new SetValue("/items/7", J("1")), "$.items[7]");
    [Fact]
    public void Set_refuses_removing_root() => Refused(new SetValue("", null), "$");
    [Fact]
    public void Set_refuses_non_index_list_segment() => Refused(new SetValue("/items/nope", J("1")), "$.items.nope");
    [Fact]
    public void Insert_refuses_missing_list() => Refused(new InsertItem("/absent", 0, J("1")), "$.absent");
    [Fact]
    public void Insert_refuses_non_list() => Refused(new InsertItem("/group", 0, J("1")), "$.group");
    [Fact]
    public void Insert_refuses_negative_index() => Refused(new InsertItem("/items", -1, J("1")), "$.items");
    [Fact]
    public void Insert_refuses_index_beyond_append() => Refused(new InsertItem("/items", 4, J("1")), "$.items");
    [Fact]
    public void Remove_refuses_root() => Refused(new RemoveItem(""), "$");
    [Fact]
    public void Remove_refuses_object_property() => Refused(new RemoveItem("/group/label"), "$.group.label");
    [Fact]
    public void Remove_refuses_missing_item() => Refused(new RemoveItem("/items/7"), "$.items[7]");
    [Fact]
    public void Move_refuses_non_list() => Refused(new MoveItem("/tail", 0, 0), "$.tail");
    [Fact]
    public void Move_refuses_negative_source() => Refused(new MoveItem("/items", -1, 0), "$.items");
    [Fact]
    public void Move_refuses_source_past_end() => Refused(new MoveItem("/items", 3, 0), "$.items");
    [Fact]
    public void Move_refuses_negative_destination() => Refused(new MoveItem("/items", 0, -1), "$.items");
    [Fact]
    public void Move_refuses_destination_past_end() => Refused(new MoveItem("/items", 0, 3), "$.items");

    [Fact]
    public void Set_refuses_negative_list_index() => Refused(new SetValue("/items/-1", J("1")), "$.items[-1]");
    [Fact]
    public void Remove_refuses_negative_list_index() => Refused(new RemoveItem("/items/-1"), "$.items[-1]");
    [Fact]
    public void Remove_refuses_non_index_list_segment() => Refused(new RemoveItem("/items/nope"), "$.items.nope");
    [Fact]
    public void Remove_refuses_missing_parent() => Refused(new RemoveItem("/absent/0"), "$.absent[0]");
    [Fact]
    public void Remove_refuses_scalar_parent() => Refused(new RemoveItem("/tail/0"), "$.tail[0]");
    [Fact]
    public void Move_refuses_missing_list() => Refused(new MoveItem("/absent", 0, 0), "$.absent");

    [Fact]
    public void Empty_list_refuses_remove_and_move_but_accepts_insert_at_zero()
    {
        var s = Session("{ \"a\": [] }\n");
        Assert.False(s.Execute(new RemoveItem("/a/0")));
        Assert.Contains(s.LastIssues, i => i.Path == "$.a[0]" && !string.IsNullOrWhiteSpace(i.Message));
        Assert.False(s.Execute(new MoveItem("/a", 0, 0)));
        Assert.Contains(s.LastIssues, i => i.Path == "$.a" && !string.IsNullOrWhiteSpace(i.Message));
        Assert.Equal("{ \"a\": [] }\n", Text(s));
        Assert.Empty(s.Journal);
        Assert.True(s.Execute(new InsertItem("/a", 0, J("7"))));
        Assert.Equal(7, At(s.Current, "/a/0").GetInt32());
    }

    [Fact]
    public void Validator_refusal_preserves_existing_history_journal_and_redo()
    {
        const string text = "{ \"n\": 1 }\n";
        var s = Session(text, "{\"properties\":{\"n\":{\"type\":\"integer\",\"maximum\":3}}}");
        Assert.True(s.Execute(new SetValue("/n", J("2"))));
        Assert.True(s.Undo());
        var journal = JournalFile.Write(s.Journal);
        var revision = s.Revision;
        Assert.False(s.Execute(new SetValue("/n", J("4"))));
        Assert.Equal(text, Text(s));
        Assert.Equal(journal, JournalFile.Write(s.Journal));
        Assert.Equal(revision, s.Revision);
        Assert.True(s.History.CanRedo);
        Assert.Contains(s.LastIssues, i => i.Path == "$.n" && i.Severity == Severity.Error && !string.IsNullOrWhiteSpace(i.Message));
        Assert.True(s.Redo());
        Assert.Equal(2, At(s.Current, "/n").GetInt32());
    }

    [Fact]
    public void Each_command_round_trips_through_catalog_without_losing_arguments()
    {
        var d = Format.Read(Styled);
        ICommand<DataDocument>[] commands = { new SetValue("/group/label", J("\"雪\"")), new InsertItem("/items", 1, J("{\"id\":\"x\"}")),
            new RemoveItem("/items/1"), new MoveItem("/items", 0, 2), new SetValue("/group/label", null),
            new SetValues([new SetValue("/group/label", J("\"a\"")), new SetValue("/group/label", J("\"b\""))]) };
        var catalog = DataKit.Catalog();
        Assert.Equal(5, catalog.Commands.Count);   // Claude: data.set_many added after A033 found per-cell fills quadratic
        foreach (var command in commands)
        {
            var entry = catalog.ToEntry(command);
            var decoded = catalog.FromEntry(entry);
            var expected = command.Apply(d);
            var actual = decoded.Apply(d);
            Assert.True(expected.Ok);
            Assert.True(actual.Ok);
            Assert.Equal(Format.Write(expected.Document!), Format.Write(actual.Document!));
        }
    }

    [Fact]
    public void Undo_and_redo_all_four_command_kinds_restore_identical_text()
    {
        var s = Session(Styled);
        ICommand<DataDocument>[] edits = { new SetValue("/group/amount", J("7")), new InsertItem("/items", 1, J("{\"id\":\"x\"}")),
            new RemoveItem("/items/2"), new MoveItem("/items", 0, 2) };
        var states = new List<string> { Styled };
        foreach (var edit in edits) { Assert.True(s.Execute(edit)); states.Add(Text(s)); }
        for (var i = edits.Length - 1; i >= 0; --i) { Assert.True(s.Undo()); Assert.Equal(states[i], Text(s)); }
        Assert.False(s.Undo());
        for (var i = 1; i < states.Count; ++i) { Assert.True(s.Redo()); Assert.Equal(states[i], Text(s)); }
        Assert.False(s.Redo());
    }

    [Fact]
    public void Serialized_journal_replays_edits_undo_redo_and_batch_to_identical_text()
    {
        var s = Session(Styled);
        Assert.True(s.Execute(new SetValue("/group/amount", J("8"))));
        Assert.True(s.Execute(new InsertItem("/items", 1, J("{\"id\":\"x\"}"))));
        Assert.True(s.Execute(new MoveItem("/items", 3, 0)));
        Assert.True(s.Execute(new RemoveItem("/items/2")));
        Assert.True(s.Undo());
        Assert.True(s.Redo());
        Assert.True(s.ExecuteBatch(new ICommand<DataDocument>[] { new SetValue("/group/label", null), new SetValue("/keep/new", J("true")) }));
        var replay = Session(Styled);
        replay.Apply(JournalFile.Read(JournalFile.Write(s.Journal)));
        Assert.Equal(Text(s), Text(replay));
        Assert.Equal(JournalFile.Write(s.Journal), JournalFile.Write(replay.Journal));
        Assert.Equal(Styled, Format.Write(Format.Read(Styled)));
    }

    [Fact]
    public void Batch_refusal_is_atomic_including_text_and_journal()
    {
        var s = Session(Styled);
        Assert.False(s.ExecuteBatch(new ICommand<DataDocument>[] { new SetValue("/group/amount", J("8")), new RemoveItem("/items/99") }));
        Assert.Equal(Styled, Text(s));
        Assert.Empty(s.Journal);
        Assert.False(s.History.CanUndo);
        Assert.Contains(s.LastIssues, i => i.Severity == Severity.Error && i.Path == "$.items[99]" && !string.IsNullOrWhiteSpace(i.Message));
    }

    [Fact]
    public void Save_and_open_preserve_the_actual_utf8_file_bytes()
    {
        // TEMP/TMP are placed by overlay_test.py under this card's .gate; no source/game files are touched.
        var dir = Path.Combine(Path.GetTempPath(), "a027-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "neutral.json");
            var s = Session(Styled);
            s.Save(path);
            Assert.Equal(Encoding.UTF8.GetBytes(Styled), File.ReadAllBytes(path));
            var opened = DataKit.Open(path, Schemas(), "fixture.schema.json", "acceptance.data");
            Assert.Equal(Styled, Text(opened));
            Assert.True(opened.Execute(new SetValue("/group/amount", J("6"))));
            opened.Save();
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(Encoding.UTF8.GetBytes(Text(opened)), bytes);
            OnlyLines(Styled, Encoding.UTF8.GetString(bytes), 4, 4);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Pointer_split_decodes_tilde_slash_and_empty_components() =>
        Assert.Equal(new[] { "a/b", "~key", "", "~1" }, Pointer.Split("/a~1b/~0key//~01"));
    [Fact]
    public void Pointer_join_escapes_components_and_round_trips()
    {
        string[] parts = { "a/b", "~key", "", "~1" };
        Assert.Equal("/a~1b/~0key//~01", Pointer.Join(parts));
        Assert.Equal(parts, Pointer.Split(Pointer.Join(parts)));
    }
    [Fact]
    public void Pointer_root_has_no_parts_and_addresses_whole_document()
    {
        Assert.Empty(Pointer.Split(""));
        Assert.Equal("", Pointer.Join(Array.Empty<string>()));
        Assert.Equal("$", Pointer.ToPath(""));
        Assert.Equal("", Pointer.FromPath("$"));
        var d = Format.Read(Styled);
        Assert.Equal(d.Root.GetRawText(), At(d, "").GetRawText());
    }
    [Fact]
    public void Pointer_addresses_escaped_property_names()
    {
        var d = Format.Read("{\"a/b\":{\"~key\":9},\"\":4}");
        Assert.Equal(9, At(d, "/a~1b/~0key").GetInt32());
        Assert.Equal(4, At(d, "/").GetInt32());
    }
    [Fact]
    public void Numeric_object_keys_are_distinct_from_array_indexes()
    {
        var d = Format.Read("{\"o\":{\"0\":\"zero\",\"01\":\"one\"},\"a\":[\"first\",\"second\"]}");
        Assert.Equal("zero", At(d, "/o/0").GetString());
        Assert.Equal("one", At(d, "/o/01").GetString());
        Assert.Equal("second", At(d, "/a/1").GetString());
        var changed = new SetValue("/o/01", J("\"changed\"")).Apply(d);
        Assert.True(changed.Ok);
        Assert.Equal("changed", At(changed.Document!, "/o/01").GetString());
        Assert.Equal("second", At(changed.Document!, "/a/1").GetString());
    }
    [Fact]
    public void Missing_or_unresolvable_pointers_return_no_value()
    {
        var d = Format.Read(Styled);
        foreach (var pointer in new[] { "/missing", "/items/99", "/items/-1", "/items/nope", "/tail/x", "/group/absent" })
            Assert.Null(d.At(pointer));
    }
    [Fact]
    public void Json_path_conversion_handles_the_documented_forms()
    {
        Assert.Equal("/factories/3/name", Pointer.FromPath("$.factories[3].name"));
        Assert.Equal("/odd key/0", Pointer.FromPath("$['odd key'][0]"));
        Assert.Equal("$.factories[3].name", Pointer.ToPath("/factories/3/name"));
        Assert.Equal("$['odd key'][0]", Pointer.ToPath("/odd key/0"));
    }
    [Theory]
    [InlineData("")]
    [InlineData("factories[3]")]
    [InlineData("$['unterminated]")]
    public void Non_paths_return_no_pointer(string path) => Assert.Null(Pointer.FromPath(path));

    [Fact]
    public void Non_numeric_unquoted_bracket_token_is_not_a_validator_json_path() =>
        Assert.Null(Pointer.FromPath("$.items[nope]"));

    [Fact]
    public void Validator_reports_nested_value_at_its_json_path()
    {
        var issues = Schemas("{\"properties\":{\"items\":{\"items\":{\"properties\":{\"n\":{\"type\":\"integer\"}}}}}}")
            .Validate(J("{\"items\":[{\"n\":\"bad\"}]}"), "fixture.schema.json");
        Assert.Contains(issues, i => i.Severity == Severity.Error && i.Path == "$.items[0].n" && !string.IsNullOrWhiteSpace(i.Message));
    }
    [Fact]
    public void Validator_reports_odd_property_path_unambiguously()
    {
        var issues = Schemas("{\"properties\":{\"odd key\":{\"type\":\"integer\"}}}").Validate(J("{\"odd key\":false}"), "fixture.schema.json");
        Assert.Contains(issues, i => i.Path == "$['odd key']" && !string.IsNullOrWhiteSpace(i.Message));
    }
    [Fact]
    public void Data_validator_maps_issue_to_top_level_list_item()
    {
        var validator = new DataValidator(Schemas("{\"properties\":{\"items\":{\"items\":{\"properties\":{\"n\":{\"type\":\"integer\"}}}}}}"), "fixture.schema.json");
        Assert.Equal("fixture.schema.json", validator.Schema);
        var issue = Assert.Single(validator.Validate(Format.Read("{\"items\":[{\"n\":\"bad\"}]}")));
        Assert.Equal("$.items[0].n", issue.Path);
        Assert.Equal("/items/0", issue.ObjectId);
    }
    [Fact]
    public void Data_validator_maps_nested_issue_to_top_level_field()
    {
        var validator = new DataValidator(Schemas("{\"properties\":{\"rules\":{\"properties\":{\"n\":{\"minimum\":2}}}}}"), "fixture.schema.json");
        var issue = Assert.Single(validator.Validate(Format.Read("{\"rules\":{\"n\":1}}")));
        Assert.Equal("$.rules.n", issue.Path);
        Assert.Equal("/rules", issue.ObjectId);
    }
    [Fact]
    public void Unknown_keyword_is_reported_with_name_and_instance_path()
    {
        var issues = Schemas("{\"properties\":{\"n\":{\"acceptanceUnknown\":true}}}").Validate(J("{\"n\":1}"), "fixture.schema.json");
        Assert.Contains(issues, i => i.Path == "$.n" && i.Message.Contains("acceptanceUnknown", StringComparison.Ordinal));
    }
    [Fact]
    public void Known_annotations_do_not_create_validation_errors()
    {
        var schema = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"$id\":\"urn:acceptance:annotation\",\"$comment\":\"own data\",\"title\":\"T\",\"description\":\"D\",\"default\":0,\"examples\":[1],\"deprecated\":true,\"readOnly\":true,\"writeOnly\":true}";
        Assert.Empty(Schemas(schema).Validate(J("false"), "fixture.schema.json"));
    }
    [Fact]
    public void Cross_file_ref_resolves_by_filename_and_enforces_target()
    {
        var schemas = new JsonSchemas().Add("common.schema.json", "{\"$defs\":{\"count\":{\"type\":\"integer\",\"minimum\":2}}}")
            .Add("main.schema.json", "{\"properties\":{\"n\":{\"$ref\":\"common.schema.json#/$defs/count\"}}}");
        Assert.Empty(schemas.Validate(J("{\"n\":3}"), "main.schema.json"));
        Assert.Contains(schemas.Validate(J("{\"n\":1}"), "main.schema.json"), i => i.Path == "$.n" && !string.IsNullOrWhiteSpace(i.Message));
    }
    [Fact]
    public void Cross_file_ref_resolves_by_id_and_enforces_target()
    {
        var schemas = new JsonSchemas().Add("common.schema.json", "{\"$id\":\"https://example.test/count\",\"$defs\":{\"count\":{\"type\":\"integer\",\"minimum\":2}}}")
            .Add("main.schema.json", "{\"properties\":{\"n\":{\"$ref\":\"https://example.test/count#/$defs/count\"}}}");
        Assert.Empty(schemas.Validate(J("{\"n\":3}"), "main.schema.json"));
        Assert.Contains(schemas.Validate(J("{\"n\":1}"), "main.schema.json"), i => i.Path == "$.n" && !string.IsNullOrWhiteSpace(i.Message));
        Assert.Equal(schemas.Find("common.schema.json")?.GetRawText(), schemas.Find("https://example.test/count")?.GetRawText());
        Assert.Null(schemas.Find("missing.schema.json"));
    }
    [Fact]
    public void Json_equality_uses_numeric_value_and_ignores_object_key_order()
    {
        Assert.True(JsonSchemas.Same(J("{\"a\":1.0,\"b\":2}"), J("{\"b\":2,\"a\":1e0}")));
        Assert.False(JsonSchemas.Same(J("[1,2]"), J("[2,1]")));
    }
}
