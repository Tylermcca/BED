using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED: adversarial fixtures, independent candidate oracles and timing bounds.
// BORROWED: JSON Schema anyOf/oneOf/ref semantics and the existing DataKit/Inspector command contract.
// Self-authored only; no game, workbook, art, window, new dependency or product-source modification.
[CollectionDefinition("Union facets acceptance", DisableParallelization = true)]
public sealed class UnionFacetsAcceptanceCollection { }

[Collection("Union facets acceptance")]
public sealed class UnionFacetsAcceptanceTests(ITestOutputHelper output)
{
    private sealed record Rig(JsonSchemas Schemas, DocumentSession<DataDocument> Session,
        EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector);

    private static Rig Open(string schema, string text, string selected = "")
    {
        var schemas = new JsonSchemas().Add("doc", schema);
        var session = DataKit.Session(new DataFormat().Read(text), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.OutlineFor(schemas, "doc"));
        editor.Select(selected);
        Assert.Equal(selected, editor.Primary);
        return new(schemas, session, editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc")));
    }

    private static InspectorRow Row(Rig r, string key) => r.Inspector.Rows().Single(x => x.Id == key);
    private static void Good(Rig r, string json) =>
        Assert.Empty(r.Schemas.Validate(new DataFormat().Read(json).Root, "doc"));
    private static void Bad(Rig r, string json) =>
        Assert.NotEmpty(r.Schemas.Validate(new DataFormat().Read(json).Root, "doc"));

    [Fact]
    public void Disjoint_anyOf_choices_include_every_legal_value_and_preserve_strings()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"enum":["amber","blue"]},{"const":"none"}]}}}""", """{"v":"amber"}""");
        Assert.Equal(["amber", "blue", "none"], Row(r, "v").Choices);
        foreach (var value in Row(r, "v").Choices)
        {
            Good(r, JsonSerializer.Serialize(new { v = value }));
            Assert.True(r.Inspector.Set("v", value));
            Assert.Equal(JsonValueKind.String, r.Session.Current.At("/v")!.Value.ValueKind);
        }
        Assert.False(r.Inspector.Set("v", "unknown"));
    }

    [Fact]
    public void Overlapping_oneOf_enums_do_not_offer_a_value_the_validator_refuses()
    {
        var r = Open("""{"type":"object","properties":{"v":{"oneOf":[{"enum":["left","shared"]},{"enum":["right","shared"]}]}}}""", """{"v":"left"}""");
        Good(r, """{"v":"right"}""");
        Bad(r, """{"v":"shared"}""");
        Assert.DoesNotContain("shared", Row(r, "v").Choices);
        Assert.Equal(["left", "right"], Row(r, "v").Choices);
    }

    [Fact]
    public void Numeric_oneOf_overlap_uses_value_equality_not_number_spelling()
    {
        var r = Open("""{"type":"object","properties":{"v":{"oneOf":[{"enum":[1,2]},{"enum":[1.0,3]}]}}}""", """{"v":2}""");
        Good(r, """{"v":3}""");
        Bad(r, """{"v":1}""");
        Assert.Equal(["2", "3"], Row(r, "v").Choices);
    }

    [Fact]
    public void Outer_minimum_filters_numeric_union_choices()
    {
        var r = Open("""{"type":"object","properties":{"v":{"minimum":2,"anyOf":[{"enum":[1,2]},{"const":3}]}}}""", """{"v":2}""");
        Bad(r, """{"v":1}""");
        Good(r, """{"v":3}""");
        Assert.DoesNotContain("1", Row(r, "v").Choices);
        Assert.Equal(["2", "3"], Row(r, "v").Choices);
    }

    [Fact]
    public void Enum_branch_sibling_constraints_are_not_discarded()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"enum":[1,2],"minimum":2},{"const":3}]}}}""", """{"v":2}""");
        Bad(r, """{"v":1}""");
        Assert.Equal(["2", "3"], Row(r, "v").Choices);
    }

    [Fact]
    public void Ref_sibling_constraints_are_not_discarded_when_collecting_choices()
    {
        var r = Open("""{"type":"object","$defs":{"colors":{"enum":["red","blue"]}},"properties":{"v":{"anyOf":[{"$ref":"#/$defs/colors","not":{"const":"red"}},{"const":"none"}]}}}""", """{"v":"blue"}""");
        Bad(r, """{"v":"red"}""");
        Good(r, """{"v":"none"}""");
        Assert.Equal(["blue", "none"], Row(r, "v").Choices);
    }

    [Fact]
    public void A_direct_enum_and_union_are_intersected_for_offered_choices()
    {
        var r = Open("""{"type":"object","properties":{"v":{"enum":["a","b"],"anyOf":[{"enum":["a"]},{"const":"c"}]}}}""", """{"v":"a"}""");
        Bad(r, """{"v":"b"}""");
        Assert.Equal(["a"], Row(r, "v").Choices);
    }

    [Fact]
    public void Enum_branch_type_does_not_turn_an_invalid_number_into_an_offered_choice()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"enum":["text",7],"type":"string"},{"const":"none"}]}}}""", """{"v":"text"}""");
        Bad(r, """{"v":7}""");
        Assert.Equal(["text", "none"], Row(r, "v").Choices);
    }

    [Fact]
    public void Multi_hop_external_refs_keep_the_target_base_uri()
    {
        var schemas = new JsonSchemas()
            .Add("root", """{"$id":"https://a129.invalid/a/root.json","type":"object","$defs":{"pick":{"enum":["wrong"]}},"properties":{"v":{"anyOf":[{"$ref":"../b/bridge.json"},{"const":"none"}]}}}""")
            .Add("bridge", """{"$id":"https://a129.invalid/b/bridge.json","$ref":"#/$defs/pick","$defs":{"pick":{"enum":["navy","traders"]}}}""");
        var d = new DataFormat().Read("""{"v":"navy"}""");
        Assert.Empty(schemas.Validate(d.Root, "root"));
        var session = DataKit.Session(d, schemas, "root");
        var editor = new EditorModel<DataDocument>(session, DataEditing.OutlineFor(schemas, "root"));
        editor.Select("");
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "root"));
        var row = inspector.Rows().Single(x => x.Id == "v");
        Assert.DoesNotContain("wrong", row.Choices);
        Assert.Equal(["navy", "traders", "none"], row.Choices);
    }

    [Fact]
    public void Embedded_resource_ids_keep_their_own_choice_reference_base()
    {
        var r = Open("""{"$id":"https://a129.invalid/root.json","type":"object","$defs":{"pick":{"enum":["wrong"]},"embedded":{"$id":"nested.json","$defs":{"pick":{"enum":["right"]}},"anyOf":[{"$ref":"#/$defs/pick"},{"const":"none"}]}},"properties":{"v":{"$ref":"nested.json"}}}""", """{"v":"right"}""");
        Good(r, """{"v":"right"}""");
        Assert.DoesNotContain("wrong", Row(r, "v").Choices);
        Assert.Equal(["right", "none"], Row(r, "v").Choices);
    }

    [Fact]
    public void A_ten_hop_fixed_value_ref_chain_does_not_hide_the_other_alternative()
    {
        var defs = new Dictionary<string, object>();
        for (var i = 0; i < 10; i++) defs["hop" + i] = new Dictionary<string, string> { ["$ref"] = "#/$defs/hop" + (i + 1) };
        defs["hop10"] = new { @enum = new[] { "a", "b" } };
        var schema = JsonSerializer.Serialize(new Dictionary<string, object> {
            ["type"]="object", ["$defs"]=defs, ["properties"]=new { v=new { anyOf=new object[] {
                new Dictionary<string,string>{{"$ref","#/$defs/hop0"}}, new { @const="none" } } } } });
        var r = Open(schema, """{"v":"a"}""");
        Good(r, """{"v":"none"}""");
        Assert.Equal(["a", "b", "none"], Row(r, "v").Choices);
    }

    [Fact]
    public void Semantic_duplicate_strings_are_not_repeated_in_anyOf_choices()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"enum":["a"]},{"enum":["\u0061","b"]}]}}}""", """{"v":"a"}""");
        Good(r, """{"v":"b"}""");
        Assert.Equal(["a", "b"], Row(r, "v").Choices);
    }

    private const string Switched = """
        {"type":"object","properties":{"effect":{"type":"object","properties":{"mode":{"type":"string"}},"anyOf":[
        {"required":["mode","amount"],"properties":{"mode":{"const":"a"},"amount":{"type":"number","minimum":0,"title":"Low","description":"First branch."}}},
        {"required":["mode","amount"],"properties":{"mode":{"const":"b"},"amount":{"type":"number","minimum":10,"title":"High","description":"Second branch."}}}
        ]}}}
        """;

    [Fact]
    public void Unique_object_branch_supplies_the_correct_title_help_and_range()
    {
        var r = Open(Switched, """{"effect":{"mode":"b","amount":20}}""", "/effect");
        var row = Row(r, "amount");
        Assert.Equal("High", row.Label);
        Assert.Equal("Second branch.", row.Help);
        Assert.Equal(new NumberRange(10, false, null, false), row.Range);
    }

    [Fact]
    public void Discriminator_edit_and_undo_redo_refresh_branch_facets()
    {
        var r = Open(Switched, """{"effect":{"mode":"a","amount":20}}""", "/effect");
        Assert.Equal("Low", Row(r, "amount").Label);
        Assert.True(r.Inspector.Set("mode", "b"));
        Assert.Equal("High", Row(r, "amount").Label);
        Assert.Equal("Second branch.", Row(r, "amount").Help);
        Assert.True(r.Session.Undo());
        Assert.Equal("Low", Row(r, "amount").Label);
        Assert.True(r.Session.Redo());
        Assert.Equal("High", Row(r, "amount").Label);
    }

    [Fact]
    public void Several_matching_anyOf_branches_do_not_supply_arbitrary_child_hints()
    {
        var r = Open("""{"type":"object","properties":{"effect":{"type":"object","anyOf":[{"properties":{"n":{"type":"number","minimum":0,"description":"A"}}},{"properties":{"n":{"type":"number","minimum":1,"description":"B"}}}]}}}""", """{"effect":{"n":2}}""", "/effect");
        Assert.Null(Row(r, "n").Range);
        Assert.Equal("", Row(r, "n").Help);
    }

    [Fact]
    public void No_matching_branch_does_not_supply_false_facets_when_At_inspects_an_invalid_record()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"effect":{"anyOf":[{"type":"object","properties":{"n":{"type":"number","maximum":10,"description":"A"}}},{"type":"object","properties":{"n":{"type":"number","minimum":50,"description":"B"}}}]}}}""");
        var doc = new DataFormat().Read("""{"effect":{"n":25}}""");
        Assert.NotEmpty(schemas.Validate(doc.Root, "doc"));
        var row = DataEditing.ObjectType(schemas, "doc").Rows(doc, ["/effect"]).Single(x => x.Id == "n");
        Assert.Null(row.Range);
        Assert.Equal("", row.Help);
    }

    [Fact]
    public void The_session_rejects_invalid_starting_records_before_inspection()
    {
        const string schema = """{"type":"object","properties":{"v":{"anyOf":[{"type":"number","maximum":10},{"type":"number","minimum":50}]}}}""";
        var schemas = new JsonSchemas().Add("doc", schema);
        var doc = new DataFormat().Read("""{"v":25}""");
        Assert.NotEmpty(schemas.Validate(doc.Root, "doc"));
        Assert.Throws<InvalidDocumentException>(() => DataKit.Session(doc, schemas, "doc"));
    }

    [Fact]
    public void Numeric_union_range_does_not_claim_a_schema_valid_alternative_is_out_of_range()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"type":"number","minimum":0,"maximum":10},{"type":"number","minimum":20,"maximum":30}]}}}""", """{"v":5}""");
        Good(r, """{"v":25}""");
        var range = Row(r, "v").Range;
        Assert.True(range is null || (range.Max is null || range.Max >= 25),
            "The inspector calls 10 the maximum, while the unchanged schema permits 25.");
    }

    [Fact]
    public void Schema_replacement_invalidates_cached_choice_rows()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"enum":["a","b"]},{"const":"none"}]}}}""", """{"v":"a"}""");
        Assert.Contains("b", Row(r, "v").Choices);
        r.Schemas.Add("doc", """{"type":"object","properties":{"v":{"anyOf":[{"enum":["a","c"]},{"const":"none"}]}}}""");
        Assert.Contains("c", Row(r, "v").Choices);
        Assert.DoesNotContain("b", Row(r, "v").Choices);
    }

    [Fact]
    public void Boolean_fixed_choices_preserve_boolean_values()
    {
        var r = Open("""{"type":"object","properties":{"v":{"oneOf":[{"const":true},{"const":false}]}}}""", """{"v":true}""");
        Assert.Equal(PropertyKind.Choice, Row(r, "v").Kind);
        Assert.True(r.Inspector.Set("v", "false"));
        Assert.Equal(JsonValueKind.False, r.Session.Current.At("/v")!.Value.ValueKind);
        Assert.True(r.Session.Undo());
        Assert.Equal(JsonValueKind.True, r.Session.Current.At("/v")!.Value.ValueKind);
    }

    [Fact]
    public void Integer_facet_refuses_fractional_numeric_api_input_instead_of_rounding_it()
    {
        var r = Open("""{"type":"object","properties":{"v":{"anyOf":[{"type":"integer","minimum":2},{"type":"string","const":"none"}]}}}""", """{"v":2}""");
        Bad(r, """{"v":1.5}""");
        Assert.Equal(PropertyKind.Integer, Row(r, "v").Kind);
        Assert.False(r.Inspector.Set("v", 1.5), "1.5 was accepted by rounding it to 2.");
        Assert.False(r.Session.History.CanUndo);
    }

    [Fact]
    public void List_item_help_survives_the_matching_array_alternative()
    {
        var r = Open("""{"type":"object","properties":{"values":{"anyOf":[{"type":"array","items":{"type":"number","description":"Measured amount."}},{"type":"null"}]}}}""", """{"values":[1,2]}""", "/values");
        Assert.All(r.Inspector.Rows(), row => {
            Assert.Equal(PropertyKind.Number, row.Kind);
            Assert.Equal("Measured amount.", row.Help);
        });
    }

    [Fact]
    public void Productive_recursive_union_lookup_terminates_and_reaches_leaf_metadata()
    {
        var schema = """{"type":"object","$defs":{"node":{"anyOf":[{"type":"object","required":["leaf"],"properties":{"leaf":{"enum":["a","b"]}},"additionalProperties":false},{"type":"object","required":["child"],"properties":{"child":{"$ref":"#/$defs/node"}},"additionalProperties":false}]}},"properties":{"tree":{"$ref":"#/$defs/node"}}}""";
        object value = new { leaf = "a" };
        for (var i = 0; i < 18; i++) value = new { child = value };
        var r = Open(schema, JsonSerializer.Serialize(new { tree = value }), "/tree");
        var path = "/tree" + string.Concat(Enumerable.Repeat("/child", 18)) + "/leaf";
        var watch = Stopwatch.StartNew();
        var nodes = r.Schemas.At(r.Session.Current.Root, "doc", path);
        watch.Stop();
        Assert.Contains(nodes, s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("enum", out var e) && e.GetArrayLength() == 2);
        output.WriteLine("Productive recursion18: " + watch.Elapsed.TotalMilliseconds + "ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Finite recursive lookup exceeded its bounded five-second budget.");
    }

    [Fact]
    public void A_thousand_table_rows_keep_correct_facets_within_a_bounded_lookup_budget()
    {
        var schema = """{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","anyOf":[{"required":["tag"],"properties":{"tag":{"const":"a"},"n":{"type":"number","minimum":0,"description":"A"}}},{"required":["tag"],"properties":{"tag":{"const":"b"},"n":{"type":"number","minimum":10,"description":"B"}}}]}}}}""";
        var text = JsonSerializer.Serialize(new { rows = Enumerable.Range(0, 1000).Select(i => new { tag = i % 2 == 0 ? "a" : "b", n = 20 }) });
        var schemas = new JsonSchemas().Add("doc", schema);
        var doc = new DataFormat().Read(text);
        var type = DataEditing.ObjectType(schemas, "doc");
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            var n = type.Rows(doc, ["/rows/" + i]).Single(x => x.Id == "n");
            Assert.Equal(i % 2 == 0 ? "A" : "B", n.Help);
            Assert.Equal(i % 2 == 0 ? 0 : 10, n.Range!.Min);
        }
        watch.Stop();
        output.WriteLine("Rows1000: " + watch.Elapsed.TotalMilliseconds + "ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "1000 bounded metadata lookups exceeded ten seconds.");
    }

    [Fact]
    public async Task An_unused_recursive_alternative_does_not_crash_or_hang_inspection() =>
        await CheckUnusedAlternative(false, nameof(An_unused_recursive_alternative_does_not_crash_or_hang_inspection));

    [Fact]
    public async Task An_unused_recursive_object_alternative_does_not_crash_or_hang_inspection() =>
        await CheckUnusedAlternative(true, nameof(An_unused_recursive_object_alternative_does_not_crash_or_hang_inspection));

    private async Task CheckUnusedAlternative(bool objectValue, string probeName)
    {
        const string marker = "BRUTAL_UNION_FACET_CHILD";
        const string scalarSchema = """{"type":"object","$defs":{"loop":{"$ref":"#/$defs/loop"}},"properties":{"v":{"anyOf":[{"const":"none"},{"$ref":"#/$defs/loop"}]}}}""";
        const string objectSchema = """{"type":"object","$defs":{"loop":{"$ref":"#/$defs/loop"}},"properties":{"v":{"anyOf":[{"type":"object","required":["safe"],"properties":{"safe":{"const":true}},"additionalProperties":false},{"$ref":"#/$defs/loop"}]}}}""";
        var schemas = new JsonSchemas().Add("doc", objectValue ? objectSchema : scalarSchema);
        var doc = new DataFormat().Read(objectValue ? """{"v":{"safe":true}}""" : """{"v":"none"}""");
        // The first branch settles validity without visiting the recursive alternative.
        Assert.Empty(schemas.Validate(doc.Root, "doc"));
        if (Environment.GetEnvironmentVariable(marker) == "child")
        {
            Assert.NotEmpty(schemas.At(doc.Root, "doc", "/v"));
            return;
        }
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "Brutal.Authoring.Tests.csproj")))
            project = project.Parent;
        Assert.NotNull(project);
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = project.FullName
        };
        // The child runs the same build as this one: a Release parent never silently tests a stale Debug binary (found by A224).
        var configuration = typeof(UnionFacetsAcceptanceTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false).OfType<System.Reflection.AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration;
        Assert.False(string.IsNullOrEmpty(configuration), "The test assembly doesn't say its build configuration.");
        var results = Directory.CreateTempSubdirectory("union-facet-probe-").FullName;
        try
        {
            // The exact current assembly avoids project/MSBuild startup; no build, restore or stale configuration.
            foreach (var arg in new[] { "vstest", typeof(UnionFacetsAcceptanceTests).Assembly.Location,
                "/Blame:CollectHangDump;TestTimeout=10m", "/TestCaseFilter:FullyQualifiedName~" + probeName,
                "/Logger:console;verbosity=minimal", "/Logger:trx;LogFileName=probe.trx", "/ResultsDirectory:" + results })
                start.ArgumentList.Add(arg);
            start.Environment[marker] = "child";
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            var exited = child.WaitForExitAsync();
            var finished = await Task.WhenAny(exited, Task.Delay(30000)) == exited;
            if (!finished) child.Kill(entireProcessTree: true);
            await exited;
            var detail = (await stdout) + (await stderr);
            output.WriteLine(detail.Length > 3000 ? detail[^3000..] : detail);
            Assert.True(finished, "Metadata inspection failed its bounded thirty-second child-process deadline.");
            Assert.Equal(0, child.ExitCode); // A fatal stack overflow is a finding, never an expected pass.
            // Do not mistake an empty discovery result for a passing isolated control.
            var receipt = XDocument.Load(Path.Combine(results, "probe.trx")); var ns = receipt.Root!.Name.Namespace;
            var result = Assert.Single(receipt.Descendants(ns + "UnitTestResult")); Assert.Equal("Passed", result.Attribute("outcome")?.Value);
            var definition = Assert.Single(receipt.Descendants(ns + "UnitTest"), test => test.Attribute("id")?.Value == result.Attribute("testId")?.Value);
            var method = definition.Element(ns + "TestMethod")!;
            Assert.Equal(typeof(UnionFacetsAcceptanceTests).FullName, method.Attribute("className")?.Value);
            Assert.Equal(probeName, method.Attribute("name")?.Value);
        }
        finally
        {
            var owned = Path.GetFullPath(results); var temporary = Path.GetFullPath(Path.GetTempPath());
            Assert.StartsWith(temporary, owned, StringComparison.OrdinalIgnoreCase); Directory.Delete(owned, true);
        }
    }

    [Theory]
    [InlineData("number")]
    [InlineData("boolean")]
    public void A_mixed_scalar_union_keeps_an_existing_string_editable_as_text(string alternative)
    {
        var schema = JsonSerializer.Serialize(new { type = "object", properties = new {
            v = new { anyOf = new[] { new { type = "string" }, new { type = alternative } } } } });
        var r = Open(schema, """{"v":"pending"}""");
        Good(r, """{"v":"changed"}""");
        Assert.Equal(PropertyKind.Text, Row(r, "v").Kind);
        Assert.True(r.Inspector.Set("v", "changed"), string.Join("; ", r.Inspector.LastIssues));
        Assert.Equal(JsonValueKind.String, r.Session.Current.At("/v")!.Value.ValueKind);
        Assert.Equal("changed", r.Session.Current.At("/v")!.Value.GetString());
        Assert.True(r.Session.Undo());
        Assert.Equal("pending", r.Session.Current.At("/v")!.Value.GetString());
    }
}

