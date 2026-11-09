using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED: neutral generated records and event sequences; no game/workbook/art inputs.
// Oracle sentences are quoted beside each acceptance test and repeated in the report's seeded draw.
[Collection(ImGuiCollection.Name)]
public sealed class LongViewAcceptanceTests
{
    // A093: "Data a person can't reach ... is always a gap."
    [Fact] public void L01_All_unset_long_object_has_a_reveal_control()
    {
        using var r = new Rig(bundle: new JsonObject());
        Assert.NotNull(r.Window.InspectorPlace(r.Row("bundle").Id, InspectorPart.Unset));
        r.Part(r.Row("bundle"), InspectorPart.Unset);
        r.Open(r.Row("bundle"));
        Assert.NotNull(r.Window.InspectorPlace(r.Child("bundle", 0).Id));
    }

    // A093: "A field set to JSON null ... is any real data hidden?"
    [Fact] public void L02_Explicit_json_null_is_shown_without_revealing_unset()
    {
        using var r = new Rig(bundle: new JsonObject { ["p00"] = null });
        Assert.NotNull(r.Window.InspectorPlace(r.Child("bundle", 0).Id));
        Assert.Equal("null", r.Child("bundle", 0).Value);
    }

    // A093: "a nested object that's present but empty".
    [Fact] public void L03_Explicit_empty_object_is_shown()
    {
        using var r = new Rig(bundle: new JsonObject { ["p00"] = new JsonObject() });
        Assert.NotNull(r.Window.InspectorPlace(r.Child("bundle", 0).Id));
        Assert.Equal(JsonValueKind.Object, r.Session.Current.At("/items/0/bundle/p00")!.Value.ValueKind);
    }

    // A093: "a false ... is any real data hidden?"
    [Fact] public void L04_False_is_shown_and_can_be_toggled_by_mouse()
    {
        using var r = new Rig(bundle: new JsonObject { ["p00"] = false });
        var row = r.Child("bundle", 0); r.ClickField(row);
        Assert.True(r.Session.Current.At("/items/0/bundle/p00")!.Value.GetBoolean());
        Assert.Equal(1, r.Session.History.UndoCount);
    }

    // A093: "a 0 ... is any real data hidden?"
    [Fact] public void L05_Zero_is_visible_and_editable_by_keyboard()
    {
        using var r = new Rig(bundle: new JsonObject { ["p00"] = 0 });
        r.Replace(r.Child("bundle", 0), "2", enter: true);
        Assert.Equal(2, r.Session.Current.At("/items/0/bundle/p00")!.Value.GetInt32());
    }

    // InspectorRow: "True when a value differs from its default (show it marked, with a reset)."
    [Fact] public void L06_An_overridden_default_remains_visible_and_marked()
    {
        using var r = new Rig(bundle: new JsonObject { ["p00"] = "override" });
        r.SchemaBundleProperty("p00")["default"] = "base"; r.RefreshSchema();
        var row = r.Child("bundle", 0); Assert.True(row.Overridden); Assert.True(row.HasDefault);
        Assert.NotNull(r.Window.InspectorPlace(row.Id));
        Assert.True(r.Inspector.Reset(row.Id)); Assert.Equal("base", r.Session.Current.At("/items/0/bundle/p00")!.Value.GetString());
    }

    // A093: "a mixed multi-selection ... is any real data hidden?"
    [Fact] public void L07_Mixed_existing_values_are_visible_without_unset()
    {
        using var r = new Rig();
        Assert.True(r.Session.Execute(new SetValue("/items/1/bundle/p00", JsonSerializer.SerializeToElement("different"))));
        r.Editor.Select("/items/0", "/items/1"); r.Frames();
        var row = r.Child("bundle", 0); Assert.True(row.Mixed); Assert.NotNull(r.Window.InspectorPlace(row.Id));
    }

    // Inspector.Nested: "focus locking applies".
    [Fact] public void L08_Focus_locked_existing_values_are_visible_but_not_editable()
    {
        using var r = new Rig(); Assert.True(r.Editor.FocusOn("/items/1")); r.Editor.Select("/items/0"); r.Frames();
        var row = r.Child("bundle", 0); Assert.False(row.Editable); Assert.NotNull(r.Window.InspectorPlace(row.Id));
        var before = r.Session.Current; Assert.False(r.Inspector.Set(row.Id, "locked")); Assert.Same(before, r.Session.Current);
    }

    // EditorWindow: "the rest are a button away".
    [Fact] public void L09_Unset_reveal_and_hide_do_not_change_the_document()
    {
        using var r = new Rig(); var before = r.Session.Current;
        Assert.Null(r.Window.InspectorPlace(r.Child("bundle", 1).Id));
        r.Part(r.Row("bundle"), InspectorPart.Unset);
        r.Open(r.Row("bundle"));
        Assert.NotNull(r.Window.InspectorPlace(r.Child("bundle", 1).Id));
        r.Part(r.Row("bundle"), InspectorPart.Unset);
        Assert.Null(r.Window.InspectorPlace(r.Child("bundle", 1).Id)); Assert.Same(before, r.Session.Current); Assert.Empty(r.Session.Journal);
    }

    // A093: "Is a required field that's missing hidden, when that's what someone most needs to see?"
    [Fact] public void L10_A_new_required_missing_field_has_a_reachable_repair_route()
    {
        using var r = new Rig(bundle: new JsonObject());
        r.BundleSchema["required"] = new JsonArray("p00"); r.RefreshSchema();
        r.Clock.Advance(TimeSpan.FromMilliseconds(1100)); // Respect the documented advisory issue TTL; this test targets reachability.
        Assert.Contains(r.Inspector.Issues(), i => i.Message.Contains("p00", StringComparison.Ordinal));
        Assert.NotNull(r.Window.InspectorPlace(r.Row("bundle").Id, InspectorPart.Unset));
    }

    // PagedRows: "A long list, a page at a time".
    [Fact] public void L11_First_page_draws_fifty_items_without_drawing_the_next_page()
    {
        using var r = new Rig(); r.OpenValues();
        Assert.True(r.Shown(0)); Assert.True(r.Shown(49)); Assert.False(r.Shown(50)); r.Untouched();
    }

    // InspectorPart.Later: "A long list\'s button for the page after."
    [Fact] public void L12_Later_is_a_real_click_and_reaches_item_fifty_one()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Later);
        Assert.True(r.Shown(50)); Assert.True(r.Shown(99)); Assert.False(r.Shown(0)); r.Untouched();
    }

    // InspectorPart.Earlier: "A long list\'s button for the page before."
    [Fact] public void L13_Earlier_returns_to_the_first_items()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Later); r.Part(r.Row("values"), InspectorPart.Earlier);
        Assert.True(r.Shown(0)); Assert.False(r.Shown(50)); r.Untouched();
    }

    // A093: "InspectorPart ... First, Last".
    [Fact] public void L14_Last_then_First_reaches_both_ends()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last);
        Assert.True(r.Shown(122)); Assert.False(r.Shown(0)); r.Part(r.Row("values"), InspectorPart.First);
        Assert.True(r.Shown(0)); Assert.False(r.Shown(122)); r.Untouched();
    }

    // A093: "Go to with 0".
    [Fact] public void L15_Go_to_zero_reaches_the_first_page_without_mutation()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last); r.Go("values", "0");
        Assert.True(r.Shown(0)); r.Untouched();
    }

    // A093: "Go to ... a page".
    [Fact] public void L16_Go_to_fifty_one_uses_one_based_item_numbers()
    {
        using var r = new Rig(); r.OpenValues(); r.Go("values", "51");
        Assert.True(r.Shown(50)); Assert.False(r.Shown(0)); r.Untouched();
    }

    // A093: "Go to ... with a huge number".
    [Fact] public void L17_Largest_integer_goes_to_the_last_item()
    {
        using var r = new Rig(); r.OpenValues(); r.Go("values", int.MaxValue.ToString(CultureInfo.InvariantCulture));
        Assert.True(r.Shown(122)); r.Untouched();
    }

    // A093: "Go to ... with a huge number".
    [Fact] public void L18_Overflow_input_cannot_damage_data_or_disable_later_navigation()
    {
        using var r = new Rig(); r.OpenValues(); r.Go("values", "999999999999999");
        r.Go("values", "51"); Assert.True(r.Shown(50)); r.Untouched();
    }

    // A093: "Go to ... with text pasted in".
    [Fact] public void L19_Pasted_nonnumeric_text_leaves_navigation_usable()
    {
        using var r = new Rig(); r.OpenValues(); r.Go("values", "word", paste: true);
        r.Go("values", "101", paste: true); Assert.True(r.Shown(122)); r.Untouched();
    }

    // A093: "one buffer per list, and keeps what was typed".
    [Fact] public void L20_Go_to_buffers_stay_independent_between_two_lists()
    {
        using var r = new Rig(); r.OpenValues(); r.Go("values", "51");
        r.Open(r.Row("second")); r.Go("second", "101");
        r.Part(r.Row("values"), InspectorPart.First);
        r.Part(r.Row("values"), InspectorPart.GoTo); r.Key(ImGuiKey.Enter); r.Frames();
        Assert.True(r.Shown(50)); Assert.True(r.Shown(122, "second")); r.Untouched();
    }

    // A093: "Another object with the same field name".
    [Fact] public void L21_Page_and_fold_state_do_not_leak_to_another_object()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last);
        r.Select(1); r.OpenValues(); Assert.True(r.Shown(0)); Assert.False(r.Shown(122));
        r.Select(0); Assert.True(r.Shown(122)); r.Untouched();
    }

    // A093: "an undo that shrinks the list".
    [Fact] public void L22_Shrinking_and_undoing_a_list_keeps_its_items_reachable()
    {
        using var r = new Rig(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last);
        Assert.True(r.Session.Execute(new SetValue("/items/0/values", JsonSerializer.SerializeToElement(new[] { "short" }))));
        r.Frames(); Assert.True(r.Shown(0)); Assert.Equal("short", r.Value(0));
        Assert.True(r.Session.Undo()); r.Frames(); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last); Assert.True(r.Shown(122)); Assert.Equal("row0122", r.Value(122));
    }

    // A093: "Add when a list is exactly at a page's edge".
    [Fact] public void L23_Add_at_fifty_items_reveals_the_new_fifty_first_item()
    {
        using var r = new Rig(count: 50); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Add);
        Assert.Equal(51, r.Session.Current.At("/items/0/values")!.Value.GetArrayLength()); Assert.True(r.Shown(50));
        Assert.Equal(1, r.Session.History.UndoCount);
    }

    // A093: "a page and a selection ... an undo that shrinks the list".
    [Fact] public void L24_Remove_last_page_item_and_undo_preserves_the_list()
    {
        using var r = new Rig(count: 51); r.OpenValues(); r.Part(r.Row("values"), InspectorPart.Last);
        r.Part(r.Child("values", 50), InspectorPart.Remove); Assert.Equal(50, r.Session.Current.At("/items/0/values")!.Value.GetArrayLength());
        Assert.True(r.Shown(0)); Assert.True(r.Session.Undo()); r.Frames(); r.OpenValues(); Assert.Equal(51, r.Session.Current.At("/items/0/values")!.Value.GetArrayLength()); Assert.Equal("row0050", r.Value(50));
    }

    // FieldsHeld: "How many text boxes the inspector is holding on to."
    [Fact] public void L25_Visiting_more_than_4096_text_fields_keeps_memory_bounded()
    {
        using var r = new Rig(count: 4200); r.VisitEveryPage();
        Assert.InRange(r.Window.FieldsHeld, 1, 4096); Assert.Equal(4200, r.Session.Current.At("/items/0/values")!.Value.GetArrayLength()); r.Untouched();
    }

    // TextField: "What was typed is committed on Enter or on leaving the box."
    [Fact] public void L26_Leaving_a_focused_field_with_Later_commits_typing_once()
    {
        using var r = new Rig(); r.OpenValues(); r.Replace(r.Child("values", 0), "typed", enter: false);
        r.Part(r.Row("values"), InspectorPart.Later);
        Assert.Equal("typed", r.Value(0)); Assert.Equal(1, r.Session.History.UndoCount);
    }

    // ForgetFieldsOutOfSight: "unless it holds a refused edit, which stays to be corrected".
    [Fact] public void L27_Refused_text_survives_page_eviction_and_can_be_corrected()
    {
        using var r = new Rig(count: 4200); r.OpenValues();
        const string refused = "too_long_for_the_schema";
        r.Replace(r.Child("values", 0), refused, enter: true); Assert.Equal("row0000", r.Value(0));
        Assert.Contains("14 characters", r.Window.Status, StringComparison.Ordinal);
        r.VisitEveryPage(); r.Part(r.Row("values"), InspectorPart.First);
        Assert.Equal(refused, r.Copy(r.Child("values", 0)));
        r.Replace(r.Child("values", 0), "fixed", enter: true); Assert.Equal("fixed", r.Value(0));
    }

    // FieldsHeld: "A box out of sight comes back with the value it should show."
    [Fact] public void L28_An_evicted_field_returns_with_the_current_document_value()
    {
        using var r = new Rig(count: 4200); r.VisitEveryPage();
        Assert.True(r.Session.Execute(new SetValue("/items/0/values/0", JsonSerializer.SerializeToElement("fresh"))));
        r.Frames(); r.Part(r.Row("values"), InspectorPart.First);
        Assert.Equal("fresh", r.Copy(r.Child("values", 0)));
    }

    // JsonSchemas.Generation: "Goes up each time a schema is added or replaced".
    [Fact] public void L29_Replacing_a_schema_refreshes_nested_choices_without_a_document_edit()
    {
        using var r = new Rig(); var old = r.SchemaBundleProperty("p00"); old["enum"] = new JsonArray("seed", "blue"); r.RefreshSchema();
        Assert.Contains("blue", r.Child("bundle", 0).Choices);
        var before = r.Session.Current; var generation = r.Schemas.Generation;
        old["enum"] = new JsonArray("seed", "green"); r.RefreshSchema();
        Assert.True(r.Schemas.Generation > generation); Assert.Contains("green", r.Child("bundle", 0).Choices);
        Assert.DoesNotContain("blue", r.Child("bundle", 0).Choices); Assert.Same(before, r.Session.Current);
    }

    // IObjectType.RowsVersion: "Null, the default, keeps none".
    [Fact] public void L30_A_provider_with_no_version_stays_live()
    {
        using var r = new Rig(); var t = new ChangingType(); var inspector = new Inspector<DataDocument>(r.Editor).Add(t);
        var row = inspector.Rows().Single(); Assert.Equal("old", inspector.Nested(row).Single().Value);
        t.External = "new"; Assert.Equal("new", inspector.Nested(row).Single().Value);
    }

    // IObjectType.RowsVersion: "until one of those or this changes".
    [Fact] public void L31_A_versioned_provider_refreshes_after_its_dependency_version_changes()
    {
        using var r = new Rig(); var t = new ChangingType { Version = 1 }; var inspector = new Inspector<DataDocument>(r.Editor).Add(t);
        var row = inspector.Rows().Single(); Assert.Equal("old", inspector.Nested(row).Single().Value);
        t.External = "new"; t.Version = 2; Assert.Equal("new", inspector.Nested(row).Single().Value);
    }

    // A093: "a type reporting a version that never changes while reading the schemas".
    [Fact] public void L32_A_constant_version_provider_can_opt_out_of_cached_rows()
    {
        using var r = new Rig(); var t = new ChangingType { Version = 1 }; var inspector = new Inspector<DataDocument>(r.Editor).Add(t);
        var row = inspector.Rows().Single(); Assert.Equal("old", inspector.Nested(row).Single().Value);
        t.External = "new";
        // The host MAY cache under the unchanged token. Do not require stale data as conformance:
        // an eager implementation is permitted too. The provider must opt out to promise a live value.
        _ = inspector.Nested(row);
        // A provider that cannot number this change must opt out; it is not a cache-invalidating promise by the host.
        t.Version = null; Assert.Equal("new", inspector.Nested(row).Single().Value);
    }

    // A093: "Generation when an unrelated schema is added".
    [Fact] public void L33_Unrelated_schema_generation_does_not_lose_active_typing()
    {
        using var r = new Rig(); r.OpenValues(); var row = r.Child("values", 0); r.Replace(row, "pending", enter: false);
        var before = r.Schemas.Generation; r.Schemas.Add("unrelated", """{"type":"string"}"""); r.Frames();
        Assert.True(r.Schemas.Generation > before); Assert.Equal("pending", r.Copy(row));
        r.Key(ImGuiKey.Enter); Assert.Equal("pending", r.Value(0));
    }

    // Inspector.Issues: "only a second, since a game's rule may read other files".
    [Fact] public void L34_External_file_issues_expire_after_a_clock_second()
    {
        using var r = new Rig(external: true); Assert.Empty(r.Inspector.Issues());
        File.WriteAllText(r.RuleFile, "blocked");
        r.Clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(r.Inspector.Issues());
        r.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Contains(r.Inspector.Issues(), i => i.Message.Contains("external", StringComparison.Ordinal));
    }

    // Inspector.Issues: "issues ... that concern the selected objects".
    [Fact] public void L35_Cached_issues_are_filtered_for_the_current_selection()
    {
        using var r = new Rig(external: true); File.WriteAllText(r.RuleFile, "blocked");
        r.Clock.Advance(TimeSpan.FromMilliseconds(1100)); Assert.Single(r.Inspector.Issues());
        r.Select(1); Assert.Empty(r.Inspector.Issues()); r.Select(0); Assert.Single(r.Inspector.Issues());
    }

    // Goal4: "A rejected edit leaves the document and its history unchanged."
    [Fact] public void L36_Fresh_candidate_validation_is_not_bypassed_by_cached_clean_issues()
    {
        using var r = new Rig(external: true); Assert.Empty(r.Inspector.Issues());
        File.WriteAllText(r.RuleFile, "blocked"); var before = r.Session.Current;
        Assert.False(r.Session.Execute(new SetValue("/items/0/title", JsonSerializer.SerializeToElement("candidate"))));
        Assert.Same(before, r.Session.Current); Assert.Empty(r.Session.Journal);
    }

    // SaveRefusal: "Checked by Save and Save as before anything is written."
    [Fact] public void L37_Save_uses_a_live_guard_even_while_presented_issues_are_cached()
    {
        using var r = new Rig(external: true); r.Session.Save(r.File);
        Assert.True(r.Session.Execute(new SetValue("/items/0/title", JsonSerializer.SerializeToElement("draft"))));
        Assert.Empty(r.Inspector.Issues()); var before = File.ReadAllBytes(r.File);
        r.Window.SaveRefusal = () => File.ReadAllText(r.RuleFile) == "blocked" ? "external input: correct the rule file" : null;
        File.WriteAllText(r.RuleFile, "blocked"); Assert.False(r.Window.Run("editor.save"));
        Assert.Equal(before, File.ReadAllBytes(r.File)); Assert.True(r.Session.Dirty); Assert.Contains("external", r.Window.Status);
    }

    // Inspector.Issues: "The issues from the document's validator".
    [Fact] public void L38_A_document_revision_refreshes_presented_issues_immediately()
    {
        using var r = new Rig(external: true); Assert.Empty(r.Inspector.Issues());
        // Warning changes with document content, without invalidating the candidate.
        Assert.True(r.Session.Execute(new SetValue("/items/0/title", JsonSerializer.SerializeToElement("warn"))));
        Assert.Contains(r.Inspector.Issues(), i => i.Message.Contains("document warning", StringComparison.Ordinal));
    }


    // TextField: "What was typed is committed on Enter or on leaving the box."
    [Fact] public void L39_Changing_selection_with_uncommitted_typing_does_not_silently_discard_it()
    {
        using var r = new Rig(); r.OpenValues(); r.Replace(r.Child("values", 0), "pending", enter: false);
        r.Select(1);
        Assert.Equal("pending", r.Session.Current.At("/items/0/values/0")!.Value.GetString());
        Assert.Equal(1, r.Session.History.UndoCount);
    }

    // A093: "a focused or read-only object: is any real data hidden?"
    [Fact] public void L40_A_read_only_provider_shows_its_existing_values_without_an_edit_control()
    {
        using var r = new Rig(readOnly: true);
        var row = r.Child("bundle", 0); Assert.False(row.Editable);
        Assert.NotNull(r.Window.InspectorPlace(row.Id)); r.ClickField(row);
        Assert.False(ImGui.GetIO().WantTextInput); r.Untouched();
    }

    private sealed class ReadOnlyRows(IObjectType<DataDocument> inner) : IObjectType<DataDocument>
    {
        public string Id => "neutral.readonly"; public string Label => "Read only"; public long? RowsVersion => inner.RowsVersion;
        private static IReadOnlyList<InspectorRow> Lock(IReadOnlyList<InspectorRow> rows) =>
            rows.Select(r => r with { Editable = false, CanAdd = false, CanRemove = false }).ToList();
        public IReadOnlyList<InspectorRow> Rows(DataDocument d, IReadOnlyList<string> ids) => Lock(inner.Rows(d, ids));
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d, IReadOnlyList<string> ids, string p) => Lock(inner.NestedRows(d, ids, p));
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d, IReadOnlyList<string> ids, string p, object? v, bool reset) => null;
    }

    private sealed class ChangingType : IObjectType<DataDocument>
    {
        public string Id => "neutral.changing"; public string Label => "Neutral";
        public long? Version; public long? RowsVersion => Version; public string External = "old";
        public IReadOnlyList<InspectorRow> Rows(DataDocument d, IReadOnlyList<string> ids) =>
            [new("parent", "Parent", PropertyKind.Text, "object", false, false, false, false, [], "")];
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d, IReadOnlyList<string> ids, string p) =>
            [new("child", "Child", PropertyKind.Text, External, false, false, false, false, [], "")];
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d, IReadOnlyList<string> ids, string p, object? v, bool reset) => null;
    }

    private sealed class ExternalRules(JsonSchemas schemas, string file) : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d)
        {
            var issues = new DataValidator(schemas, "neutral").Validate(d).ToList();
            if (File.ReadAllText(file) == "blocked") issues.Add(Issue.Error("external input: correct the rule file", "/items/0", "/items/0/title"));
            if (d.At("/items/0/title")!.Value.GetString() == "warn") issues.Add(Issue.Warning("document warning", "/items/0", "/items/0/title"));
            return issues;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            timestamp = checked(timestamp + elapsed.Ticks);
        }
    }

    private sealed class Rig : IDisposable
    {
        public readonly string Dir = Directory.CreateTempSubdirectory("a093-neutral-").FullName;
        public readonly HeadlessHost Host = new(1700, 1800) { Drawing = false };
        public readonly JsonSchemas Schemas = new();
        public readonly TestClock Clock = new();
        public readonly JsonObject Schema;
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly Inspector<DataDocument> Inspector;
        public readonly EditorWindow<DataDocument> Window;
        private readonly string initial;
        public string RuleFile => Path.Combine(Dir, "external-rule.txt");
        public string File => Path.Combine(Dir, "neutral.json");
        public JsonObject BundleSchema => Schema["properties"]!["items"]!["items"]!["properties"]!["bundle"]!.AsObject();
        public Action Draw => Window.DrawDocked;
        public Rig(int count = 123, JsonObject? bundle = null, bool external = false, bool readOnly = false)
        {
            bundle ??= new JsonObject { ["p00"] = "seed" };
            var props = new JsonObject();
            for (var i = 0; i < 64; i++) props[$"p{i:00}"] = new JsonObject { ["type"] = "string" };
            foreach (var p in bundle) props[p.Key] = new JsonObject { ["type"] = p.Value is null ? "null" : p.Value.GetValueKind() switch {
                JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Number => "number", _ => "string" } };
            var listSchema = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["maxLength"] = 14 } };
            Schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["items"] = new JsonObject {
                ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject {
                    ["title"] = new JsonObject { ["type"] = "string" },
                    ["bundle"] = new JsonObject { ["type"] = "object", ["properties"] = props },
                    ["values"] = listSchema, ["second"] = listSchema.DeepClone()
                } } } } };
            var items = new JsonArray();
            for (var n = 0; n < 2; n++) items.Add(new JsonObject {
                ["title"] = "neutral", ["bundle"] = bundle.DeepClone(),
                ["values"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create($"row{i:0000}")).ToArray()),
                ["second"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create($"other{i:0000}")).ToArray()) });
            Schemas.Add("neutral", Schema.ToJsonString()); System.IO.File.WriteAllText(RuleFile, "clear");
            var doc = new DataFormat().Read(new JsonObject { ["items"] = items }.ToJsonString());
            Session = external ? new DocumentSession<DataDocument>(new DataFormat(), new ExternalRules(Schemas, RuleFile), DataKit.Catalog(), doc)
                               : DataKit.Session(doc, Schemas, "neutral");
            Editor = new EditorModel<DataDocument>(Session, DataEditing.Outline);
            var type = DataEditing.ObjectType(Schemas, "neutral");
            Inspector = new Inspector<DataDocument>(Editor, Clock).Add(readOnly ? new ReadOnlyRows(type) : type);
            Window = new EditorWindow<DataDocument>(Editor, Inspector);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO()); Editor.Select("/items/0");
            Host.Frames(Draw, 5); Focus(); initial = Session.Current.Root.GetRawText();
        }
        public void Dispose()
        {
            Host.Dispose();
            var here = Path.GetFullPath(Dir); var temp = Path.GetFullPath(Path.GetTempPath());
            Assert.True(here.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(here).StartsWith("a093-neutral-", StringComparison.Ordinal));
            Directory.Delete(here, recursive: true);
        }
        public void Frames() => Host.Frames(Draw, 3);
        private void Focus() { Host.Frame(() => { ImGui.SetWindowFocus("Inspector###inspector"u8); Draw(); }); Frames(); }
        public void Key(ImGuiKey key, bool ctrl = false) => Host.Press(Draw, key, ctrl: ctrl);
        public InspectorRow Row(string id) => Inspector.Rows().Single(r => r.Id == id);
        public InspectorRow Child(string parent, int index) => Inspector.Nested(Row(parent))[index];
        public bool Shown(int i, string parent = "values") => Window.InspectorPlace(Child(parent, i).Id) is not null;
        public string? Value(int i) => Session.Current.At($"/items/0/values/{i}")!.Value.GetString();
        public JsonObject SchemaBundleProperty(string id) => BundleSchema["properties"]![id]!.AsObject();
        public void RefreshSchema() { Schemas.Add("neutral", Schema.ToJsonString()); Frames(); }
        
        public void Select(int n)
        {
            float2 at = default;
            Host.Frame(() => {
                Draw();
                ImGui.Begin("Outliner###outliner"u8);
                at = ImGui.GetWindowPos() + ImGui.GetCursorStartPos() + new float2(35, (n + 1) * ImGui.GetTextLineHeightWithSpacing() + 6);
                ImGui.End();
            });
            Host.Click(Draw, at.X, at.Y); Frames();
            Assert.Equal($"/items/{n}", Editor.Primary);
            Focus(); Top();
        }
        private void Top() { Host.MoveMouse(Draw, 1550, 200); ImGui.GetIO().AddMouseWheelEvent(0, 100); Frames(); }
        private float2 Visible(InspectorRow row, InspectorPart part = InspectorPart.Field)
        {
            Focus(); float2? at = Window.InspectorPlace(row.Id, part);
            Assert.True(at is not null, $"Control missing: {row.Id}/{part}");
            for (var n = 0; n < 150 && (at!.Value.Y < 40 || at.Value.Y > Host.Height - 45); n++)
            {
                Host.MoveMouse(Draw, at.Value.X + 6, 400);
                ImGui.GetIO().AddMouseWheelEvent(0, at.Value.Y > Host.Height - 45 ? -4 : 4); Frames();
                at = Window.InspectorPlace(row.Id, part); Assert.NotNull(at);
            }
            Assert.InRange(at!.Value.Y, 40, Host.Height - 45); return at.Value;
        }
        public void Part(InspectorRow row, InspectorPart part) { var at = Visible(row, part); Host.Click(Draw, at.X + 5, at.Y + 6); Frames(); }
        public void ClickField(InspectorRow row) { var at = Visible(row); Host.Click(Draw, at.X + 6, at.Y + 7); Frames(); }
        public void Open(InspectorRow row)
        {
            if (Inspector.Nested(row).Count == 0) return;
            if (Window.InspectorPlace(Inspector.Nested(row)[0].Id) is not null || Window.InspectorPlace(row.Id, InspectorPart.First) is not null) return;
            var at = Visible(row); Host.Click(Draw, at.X - 12, at.Y + 7); Frames();
            Assert.True(Window.InspectorPlace(Inspector.Nested(row)[0].Id) is not null || Window.InspectorPlace(row.Id, InspectorPart.First) is not null, $"Opening {row.Label} did not reveal children.");
        }
        public void OpenValues() => Open(Row("values"));
        public void Replace(InspectorRow row, string text, bool enter)
        {
            ClickField(row); Assert.True(ImGui.GetIO().WantTextInput, $"Not a text field: {row.Label}");
            Key(ImGuiKey.A, ctrl: true); Key(ImGuiKey.Backspace); Host.Type(Draw, text);
            if (enter) { Key(ImGuiKey.Enter); Frames(); }
        }
        public string Copy(InspectorRow row)
        {
            ClickField(row); Key(ImGuiKey.A, ctrl: true); Key(ImGuiKey.C, ctrl: true);
            return ImGui.GetClipboardText().ToString() ?? "";
        }
        public void Go(string list, string wanted, bool paste = false)
        {
            Part(Row(list), InspectorPart.GoTo); Key(ImGuiKey.A, ctrl: true); Key(ImGuiKey.Backspace);
            if (paste) { ImGui.SetClipboardText(wanted); Key(ImGuiKey.V, ctrl: true); } else Host.Type(Draw, wanted);
            Key(ImGuiKey.Enter); Frames();
        }
        public void VisitEveryPage()
        {
            OpenValues(); Part(Row("values"), InspectorPart.First);
            var count = Session.Current.At("/items/0/values")!.Value.GetArrayLength();
            for (var i = 50; i < count; i += 50) Part(Row("values"), InspectorPart.Later);
            Assert.True(Shown(count - 1));
        }
        public void Untouched() { Assert.Equal(initial, Session.Current.Root.GetRawText()); Assert.Empty(Session.Journal); Assert.False(Session.Dirty); }
    }
}
