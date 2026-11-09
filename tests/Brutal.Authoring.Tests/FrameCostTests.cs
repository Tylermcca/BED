using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>What's asked every frame costs nothing until the document changes, and still gives the right answer when it does.</summary>
public sealed class FrameCostTests
{
    private sealed class CountedFormat : IDocumentFormat<DataDocument>
    {
        private readonly DataFormat inner = new();
        public int Writes { get; private set; }
        public string Kind => inner.Kind;
        public int Version => inner.Version;
        public DataDocument Read(string text) => inner.Read(text);
        public string Write(DataDocument document)
        {
            Writes++;
            return inner.Write(document);
        }
    }

    private sealed class CountedRules : IValidator<DataDocument>
    {
        public int Runs { get; private set; }
        public IReadOnlyList<Issue> Validate(DataDocument document)
        {
            Runs++;
            return document.At("/rows/0/n")!.Value.GetInt32() < 0 ? [Issue.Error("n can't be negative", "/rows/0")] : [];
        }
    }

    private static DataDocument Start => new(JsonDocument.Parse("""{"rows":[{"id":"a","n":1},{"id":"b","n":2}]}""").RootElement.Clone());
    private static SetValue N(int value) => new("/rows/1/n", JsonSerializer.SerializeToElement(value));

    [Fact]
    public void Asking_whether_it_is_dirty_serializes_once_per_document()
    {
        var format = new CountedFormat();
        var session = new DocumentSession<DataDocument>(format, new CountedRules(), DataKit.Catalog(), Start);
        var before = format.Writes;
        for (var i = 0; i < 100; i++) Assert.False(session.Dirty);
        Assert.Equal(before + 1, format.Writes);

        Assert.True(session.Execute(N(5)));
        Assert.True(session.Dirty);
        Assert.True(session.Dirty);
        Assert.True(session.Undo());
        Assert.False(session.Dirty);   // the same text as saved, by a different route
        Assert.True(session.Redo());
        Assert.True(session.Dirty);
    }

    [Fact]
    public void Saving_answers_again_though_the_document_is_the_same_one()
    {
        var folder = Directory.CreateTempSubdirectory("frame-cost-").FullName;
        try
        {
            var session = new DocumentSession<DataDocument>(new DataFormat(), new CountedRules(), DataKit.Catalog(), Start);
            Assert.True(session.Execute(N(5)));
            Assert.True(session.Dirty);
            session.Save(Path.Combine(folder, "a.json"));
            Assert.False(session.Dirty);
            Assert.True(session.Execute(N(6)));
            Assert.True(session.Dirty);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void The_inspectors_issues_validate_once_per_document()
    {
        var rules = new CountedRules();
        var session = new DocumentSession<DataDocument>(new DataFormat(), rules, DataKit.Catalog(), Start);
        var editor = new EditorModel<DataDocument>(session, new FlatOutline<DataDocument>(d => d.At("/rows")!.Value.EnumerateArray().Select((r, i) => ($"/rows/{i}", r.GetProperty("id"u8).GetString()!)).ToArray()));
        var inspector = new Inspector<DataDocument>(editor);
        Assert.True(editor.Select("/rows/0"));
        var runs = rules.Runs;
        for (var i = 0; i < 100; i++) Assert.Empty(inspector.Issues());
        Assert.InRange(rules.Runs, runs + 1, runs + 3);   // a slow machine may cross a second

        // A rule may read other files, so the same document is judged again before long.
        var later = rules.Runs;
        Thread.Sleep(1100);
        Assert.Empty(inspector.Issues());
        Assert.Equal(later + 1, rules.Runs);
        Assert.True(session.Execute(N(5)));
        runs = rules.Runs;
        Assert.Empty(inspector.Issues());
        Assert.Equal(runs + 1, rules.Runs);   // a new document is judged afresh
    }

    [Fact]
    public void A_folded_lists_rows_are_kept_for_a_type_that_says_they_can_be_and_follow_the_document()
    {
        var schemas = new JsonSchemas().Add("t", """{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}}}}}""");
        var session = DataKit.Session(new DataFormat().Read("""{"rows":[{"tags":["a","b"]},{"tags":["c"]}]}"""), schemas, "t");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "t"));
        Assert.True(editor.Select("/rows/0"));
        var tags = inspector.Rows().Single(r => r.Id == "tags");
        var first = inspector.Nested(tags);
        Assert.Same(first, inspector.Nested(tags));
        Assert.Equal(2, first.Count);

        Assert.True(editor.Select("/rows/1"));   // another object
        Assert.Single(inspector.Nested(inspector.Rows().Single(r => r.Id == "tags")));

        Assert.True(editor.Select("/rows/0"));
        Assert.True(session.Execute(new InsertItem("/rows/0/tags", 2, JsonSerializer.SerializeToElement("d"))));   // a new document
        Assert.Equal(3, inspector.Nested(inspector.Rows().Single(r => r.Id == "tags")).Count);

        schemas.Add("t", """{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string","maxLength":9}}}}}}}""");
        var before = inspector.Nested(inspector.Rows().Single(r => r.Id == "tags"));
        schemas.Add("t", """{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"tags":{"type":"array","items":{"enum":["a","b","c","d"]}}}}}}}""");
        var after = inspector.Nested(inspector.Rows().Single(r => r.Id == "tags"));   // found by A091: the same document, a new schema
        Assert.NotSame(before, after);
        Assert.Contains("d", after[0].Choices);

        Assert.True(editor.FocusOn("/rows/1"));   // out of focus now: not editable, whatever was kept
        var locked = inspector.Nested(inspector.Rows().Single(r => r.Id == "tags"));
        Assert.All(locked, r => Assert.False(r.Editable));
    }

    private sealed class LiveRows : IObjectType<DataDocument>
    {
        public int Asked { get; private set; }
        public string Id => "live";
        public string Label => "Live";
        public IReadOnlyList<InspectorRow> Rows(DataDocument d, IReadOnlyList<string> ids) => [new("r", "r", PropertyKind.Text, "v", false, true, false, false, [], "")];
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d, IReadOnlyList<string> ids, string propertyId)
        {
            Asked++;
            return [];
        }
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d, IReadOnlyList<string> ids, string propertyId, object? value, bool reset) => null;
    }

    [Fact]
    public void A_type_that_doesnt_say_stays_live()
    {
        var session = DataKit.Session(new DataFormat().Read("""{"rows":[{"tags":[]}]}"""), new JsonSchemas().Add("t", """{"type":"object"}"""), "t");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var type = new LiveRows();
        var inspector = new Inspector<DataDocument>(editor).Add(type);
        Assert.True(editor.Select("/rows/0"));
        var row = inspector.Rows()[0];
        inspector.Nested(row);
        inspector.Nested(row);
        Assert.Equal(2, type.Asked);
    }
}
