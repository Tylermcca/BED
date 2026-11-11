using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Xunit;

namespace Bed.Tests;

// INVENTED: independent acceptance fixtures, derived from docs/spec/first_slice.md.
// No game types, private records, existing test helpers or sample implementation are used.
public sealed class AcceptanceTests
{
    public sealed record Meter(int Value, string Label = "meter");
    public sealed record SetMeter(int Value) : ICommand<Meter>
    {
        public CommandResult<Meter> Apply(Meter document) => CommandResult<Meter>.Done(document with { Value = Value });
    }
    public sealed record RefuseMeter() : ICommand<Meter>
    {
        public CommandResult<Meter> Apply(Meter document) => CommandResult<Meter>.Refused(Issue.Error("Choose an allowed value.", "meter", "$.value"));
    }
    public sealed record WarnMeter(int Value) : ICommand<Meter>
    {
        public CommandResult<Meter> Apply(Meter document) =>
            new(document with { Value = Value }, [Issue.Warning("Check this value.", "meter", "$.value")]);
    }
    public sealed record ResolveMeter(int? Value = null) : ICommand<Meter>
    {
        public CommandResult<Meter> Apply(Meter document)
        {
            int settled = Value ?? document.Value + 7;
            return CommandResult<Meter>.Done(document with { Value = settled }, new ResolveMeter(settled));
        }
    }
    public sealed class MeterValidator : IValidator<Meter>
    {
        public IReadOnlyList<Issue> Validate(Meter document) =>
            document.Value is < 0 or > 100
                ? [Issue.Error("Use a value from 0 to 100.", "meter", "$.value")]
                : [];
    }
    public sealed class MeterFormat : IDocumentFormat<Meter>
    {
        public string Kind => "acceptance.meter";
        public int Version => 1;
        public Meter Read(string text)
        {
            var r = StrictReader.Parse(text);
            r.Only("value", "label");
            return new(r.Int("value"), r.String("label"));
        }
        public string Write(Meter document)
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, Canonical.WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("value", document.Value);
                writer.WriteString("label", document.Label);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(bytes.ToArray()) + "\n";
        }
    }
    private static CommandCatalog<Meter> Catalog() => new CommandCatalog<Meter>()
        .Register<SetMeter>("meter.set", 1, "Set value")
        .Register<RefuseMeter>("meter.refuse", 1, "Refuse value")
        .Register<WarnMeter>("meter.warn", 1, "Warn value")
        .Register<ResolveMeter>("meter.resolve", 1, "Resolve value", isInternal: true);
    private static DocumentSession<Meter> Session(int value = 0, int capacity = 256) =>
        new(new MeterFormat(), new MeterValidator(), Catalog(), new Meter(value), capacity);
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("authoring-acceptance-").FullName;
        public string File(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private sealed record CliResult(int Exit, string Out, string Err);
    private static CliResult Cli(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return new(Toy.Program.Run(args, output, error), output.ToString(), error.ToString());
    }
    private static string NewBoard(Scratch scratch, string name = "board.json")
    {
        string path = scratch.File(name);
        Assert.Equal(0, Cli("new", path).Exit);
        return path;
    }
    private static string Journal(Scratch scratch, string entries, string name = "edits.json")
    {
        string path = scratch.File(name);
        System.IO.File.WriteAllText(path, "{\"kind\":\"journal\",\"version\":1,\"entries\":[" + entries + "]}");
        return path;
    }
    private const string Add = "{\"op\":\"do\",\"command\":\"toy.add\",\"version\":1,\"args\":{\"id\":\"lamp\",\"label\":\"Lamp\",\"x\":1,\"y\":2}}";
    private static string Do(string command, string args, int version = 1) =>
        "{\"op\":\"do\",\"command\":\"" + command + "\",\"version\":" + version + ",\"args\":" + args + "}";
    private static string WriteBoardNode(Scratch scratch, Action<JsonObject> mutate)
    {
        string path = NewBoard(scratch);
        var root = JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
        mutate(root);
        System.IO.File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    [Fact] public void HoldingAnImmutableDocumentCannotEditTheSession()
    {
        var s = Session();
        Meter held = s.Current;
        Meter changed = held with { Value = 99 };
        Assert.Equal(99, changed.Value);
        Assert.Equal(0, s.Current.Value);
        Assert.Equal(0, s.Revision);
    }
    [Fact] public void SuccessfulEditCommitsOneRevisionHistoryStepAndJournalEntry()
    {
        var s = Session();
        Assert.True(s.Execute(new SetMeter(4)));
        Assert.Equal(4, s.Current.Value);
        Assert.Equal(1, s.Revision);
        Assert.Equal(1, s.History.UndoCount);
        Assert.Single(s.Journal);
        Assert.False(s.History.CanRedo);
    }
    [Fact] public void CommandRefusalPreservesDocumentRevisionUndoRedoAndJournal()
    {
        var s = Session();
        s.Execute(new SetMeter(4)); s.Execute(new SetMeter(8)); s.Undo();
        string before = JournalFile.Write(s.Journal);
        long revision = s.Revision;
        int undo = s.History.UndoCount;
        Assert.False(s.Execute(new RefuseMeter()));
        Assert.Equal(4, s.Current.Value);
        Assert.Equal(revision, s.Revision);
        Assert.Equal(undo, s.History.UndoCount);
        Assert.True(s.History.CanRedo);
        Assert.Equal(before, JournalFile.Write(s.Journal));
        Assert.Equal("meter", Assert.Single(s.LastIssues).ObjectId);
        Assert.True(s.Redo());
        Assert.Equal(8, s.Current.Value);
    }
    [Fact] public void WholeCandidateValidationRefusesAnOtherwiseSuccessfulCommand()
    {
        var s = Session();
        string before = JournalFile.Write(s.Journal);
        Assert.False(s.Execute(new SetMeter(101)));
        Assert.Equal(0, s.Current.Value);
        Assert.Equal(0, s.Revision);
        Assert.False(s.History.CanUndo);
        Assert.False(s.History.CanRedo);
        Assert.Equal(before, JournalFile.Write(s.Journal));
        Assert.Equal("$.value", Assert.Single(s.LastIssues).Path);
    }
    [Fact] public void WarningDoesNotRefuseAnEdit()
    {
        var s = Session();
        Assert.True(s.Execute(new WarnMeter(7)));
        Assert.Equal(7, s.Current.Value);
        Assert.Single(s.Journal);
    }
    [Fact] public void ErrorIssueHasSeverityObjectPathAndActionablePrintedMessage()
    {
        var issue = Issue.Error("Use a value from 0 to 100.", "meter", "$.value");
        Assert.Equal(Severity.Error, issue.Severity);
        Assert.Equal("meter", issue.ObjectId);
        Assert.Equal("$.value", issue.Path);
        Assert.Equal("error [meter $.value]: Use a value from 0 to 100.", issue.ToString());
    }
    [Fact] public void InfoAndWarningDoNotCountAsErrors()
    {
        Issue[] mild = [new(Severity.Info, "Value loaded."), Issue.Warning("Check this value.")];
        Assert.False(mild.HasErrors());
        Assert.True(mild.Append(Issue.Error("Choose a valid value.")).HasErrors());
    }
    [Fact] public void BatchCommitsOneRevisionOneUndoStepAndOneJournalEntry()
    {
        var s = Session();
        Assert.True(s.ExecuteBatch([new SetMeter(2), new SetMeter(9)]));
        Assert.Equal(9, s.Current.Value);
        Assert.Equal(1, s.Revision);
        Assert.Equal(1, s.History.UndoCount);
        Assert.Equal("batch", Assert.Single(s.Journal).Op);
        Assert.True(s.Undo());
        Assert.Equal(0, s.Current.Value);
    }
    [Fact] public void BatchRefusalDoesNotLeakAnEarlierChildEdit()
    {
        var s = Session();
        s.Execute(new SetMeter(1)); s.Execute(new SetMeter(2)); s.Undo();
        string before = JournalFile.Write(s.Journal);
        long revision = s.Revision;
        Assert.False(s.ExecuteBatch([new SetMeter(7), new RefuseMeter()]));
        Assert.Equal(1, s.Current.Value);
        Assert.Equal(revision, s.Revision);
        Assert.Equal(1, s.History.UndoCount);
        Assert.True(s.History.CanRedo);
        Assert.Equal(before, JournalFile.Write(s.Journal));
    }
    [Fact] public void InvalidFinalBatchCandidateIsRejectedAtomically()
    {
        var s = Session();
        Assert.False(s.ExecuteBatch([new SetMeter(1), new SetMeter(101)]));
        Assert.Equal(0, s.Current.Value);
        Assert.Equal(0, s.Revision);
        Assert.Empty(s.Journal);
        Assert.False(s.History.CanUndo);
    }
    [Fact] public void UndoAndRedoEachIncrementRevisionAndRecordTheirOperation()
    {
        var s = Session();
        s.Execute(new SetMeter(4));
        Assert.True(s.Undo());
        Assert.Equal(2, s.Revision);
        Assert.Equal("undo", s.Journal[1].Op);
        Assert.True(s.Redo());
        Assert.Equal(3, s.Revision);
        Assert.Equal("redo", s.Journal[2].Op);
        Assert.Equal(4, s.Current.Value);
    }
    [Fact] public void CommittingAfterUndoClearsRedo()
    {
        var s = Session();
        s.Execute(new SetMeter(1)); s.Execute(new SetMeter(2)); s.Undo();
        Assert.True(s.Execute(new SetMeter(3)));
        Assert.False(s.History.CanRedo);
        Assert.False(s.Redo());
        Assert.Equal(3, s.Current.Value);
    }
    [Fact] public void CapacityForgetsOldStepsWithoutUndoingTheirEffects()
    {
        var s = Session(capacity: 2);
        s.Execute(new SetMeter(1)); s.Execute(new SetMeter(2)); s.Execute(new SetMeter(3));
        Assert.Equal(3, s.Current.Value);
        Assert.Equal(2, s.History.UndoCount);
        Assert.True(s.Undo()); Assert.Equal(2, s.Current.Value);
        Assert.True(s.Undo()); Assert.Equal(1, s.Current.Value);
        Assert.False(s.Undo()); Assert.Equal(1, s.Current.Value);
    }
    [Fact] public void SettledGeneratedArgumentsAreJournaledAndReplayable()
    {
        var s = Session(3);
        Assert.True(s.Execute(new ResolveMeter()));
        var recorded = Assert.IsType<ResolveMeter>(s.Catalog.FromEntry(Assert.Single(s.Journal)));
        Assert.Equal(10, recorded.Value);
        var replay = DocumentSession<Meter>.Replay(new Meter(3), s.Journal, new MeterFormat(), new MeterValidator(), Catalog());
        Assert.Equal(s.Current, replay.Current);
    }
    [Fact] public void CatalogueUsesStableIdsLabelsVersionsAndHidesInternalCommands()
    {
        var catalog = Catalog();
        var info = catalog.InfoFor(new SetMeter(1));
        Assert.Equal("meter.set", info.Id); Assert.Equal(1, info.Version); Assert.Equal("Set value", info.Label);
        Assert.Single(catalog.Search("METER.SET"));
        Assert.Contains(catalog.Search("value"), c => c.Id == "meter.set");
        Assert.DoesNotContain(catalog.Search(""), c => c.Id == "meter.resolve");
    }
    [Fact] public void NewSessionHasNoFileAndSaveMakesItCleanAndUnchangedOnDisk()
    {
        using var scratch = new Scratch();
        var s = Session();
        Assert.Equal(DiskState.NoFile, s.CheckDisk());
        s.Execute(new SetMeter(4));
        Assert.True(s.Dirty);
        s.Save(scratch.File("meter.json"));
        Assert.False(s.Dirty);
        Assert.Equal(DiskState.Unchanged, s.CheckDisk());
    }
    [Fact] public void UndoBackToSavedBytesMakesDirtyFalse()
    {
        using var scratch = new Scratch();
        var s = Session(); s.Save(scratch.File("meter.json"));
        s.Execute(new SetMeter(4)); Assert.True(s.Dirty);
        s.Undo(); Assert.False(s.Dirty);
        s.Redo(); Assert.True(s.Dirty);
    }
    [Fact] public void SameLengthOutsideEditIsDetected()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(1); s.Save(path);
        string before = System.IO.File.ReadAllText(path);
        string changed = before.Replace("\"value\": 1", "\"value\": 2");
        Assert.NotEqual(before, changed); Assert.Equal(before.Length, changed.Length);
        System.IO.File.WriteAllText(path, changed);
        Assert.Equal(DiskState.ChangedOnDisk, s.CheckDisk());
    }
    [Fact] public void MissingFileIsReported()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(); s.Save(path); System.IO.File.Delete(path);
        Assert.Equal(DiskState.Missing, s.CheckDisk());
    }
    [Fact] public void SaveLoadSaveProducesIdenticalBytes()
    {
        using var scratch = new Scratch();
        string first = scratch.File("first.json"), second = scratch.File("second.json");
        var s = Session(); s.Save(first);
        var loaded = DocumentSession<Meter>.Open(first, new MeterFormat(), new MeterValidator(), Catalog());
        loaded.Save(second);
        Assert.Equal(System.IO.File.ReadAllBytes(first), System.IO.File.ReadAllBytes(second));
    }
    [Fact] public void FailedSavePreservesOldFileDirtyStateAndLeavesNoTemporaryFile()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(1); s.Save(path); s.Execute(new SetMeter(2));
        byte[] old = System.IO.File.ReadAllBytes(path);
        string[] names = Directory.GetFiles(scratch.Root).Order().ToArray();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => s.Save());
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Save unexpectedly succeeded.");
        }
        Assert.Equal(old, System.IO.File.ReadAllBytes(path));
        Assert.Equal(names, Directory.GetFiles(scratch.Root).Order().ToArray());
        Assert.True(s.Dirty);
        Assert.Equal(2, s.Current.Value);
    }
    [Fact] public void SuccessfulRevertDropsHistoryJournalAndUnsavedChanges()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(1); s.Save(path); s.Execute(new SetMeter(2));
        Assert.True(s.Revert());
        Assert.Equal(1, s.Current.Value);
        Assert.False(s.History.CanUndo); Assert.False(s.History.CanRedo);
        Assert.Empty(s.Journal); Assert.Empty(s.LastIssues); Assert.False(s.Dirty);
    }
    [Fact] public void InvalidRevertPreservesDocumentRevisionHistoryAndJournal()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(1); s.Save(path); s.Execute(new SetMeter(2));
        string journal = JournalFile.Write(s.Journal); long revision = s.Revision;
        System.IO.File.WriteAllText(path, "{\"value\":101,\"label\":\"meter\"}");
        Assert.False(s.Revert());
        Assert.Equal(2, s.Current.Value); Assert.Equal(revision, s.Revision);
        Assert.Equal(1, s.History.UndoCount); Assert.Equal(journal, JournalFile.Write(s.Journal));
        Assert.NotEmpty(s.LastIssues);
    }
    [Fact] public void UnreadableRevertPreservesStateAndExplainsFailure()
    {
        using var scratch = new Scratch();
        string path = scratch.File("meter.json");
        var s = Session(); s.Save(path); s.Execute(new SetMeter(2));
        System.IO.File.Delete(path);
        Assert.False(s.Revert());
        Assert.Equal(2, s.Current.Value); Assert.Single(s.Journal); Assert.NotEmpty(s.LastIssues);
    }
    [Fact] public void InvalidDocumentNeverBecomesASession()
    {
        Assert.Throws<InvalidDocumentException>(() => Session(101));
    }
    [Theory]
    [InlineData("{\"a\":1,\"extra\":2}", "$.extra")]
    [InlineData("{\"a\":1,\"a\":2}", "$.a")]
    public void StrictObjectRejectsUnknownAndDuplicatePropertiesWithPath(string text, string expectedPath)
    {
        var error = Assert.Throws<DocumentFormatException>(() => StrictReader.Parse(text).Only("a"));
        Assert.Equal(expectedPath, error.Path);
    }
    [Theory]
    [InlineData("{}", "missing")]
    [InlineData("{\"number\":\"bad\"}", "wrong type")]
    [InlineData("{\"number\":1e999}", "non-finite")]
    public void StrictNumberRejectsMissingWrongTypeAndOverflowWithPath(string text, string reason)
    {
        Assert.NotEmpty(reason);
        var error = Assert.Throws<DocumentFormatException>(() => StrictReader.Parse(text).Double("number"));
        Assert.Equal("$.number", error.Path);
    }
    [Fact] public void JournalWriterUsesCanonicalReadableLfTextWithFinalNewline()
    {
        var entry = JournalEntry.Do("meter.set", 1, JsonSerializer.SerializeToElement(new { value = 3, label = "café" }));
        string text = JournalFile.Write([entry]);
        Assert.EndsWith("\n", text); Assert.DoesNotContain("\r", text); Assert.Contains("café", text);
        Assert.Equal(text, JournalFile.Write(JournalFile.Read(text)));
        Assert.True(text.IndexOf("\"kind\"", StringComparison.Ordinal) < text.IndexOf("\"version\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"version\"", StringComparison.Ordinal) < text.IndexOf("\"entries\"", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("{\"kind\":\"other\",\"version\":1,\"entries\":[]}")]
    [InlineData("{\"kind\":\"journal\",\"version\":2,\"entries\":[]}")]
    [InlineData("{\"kind\":\"journal\",\"version\":1,\"entries\":[{\"op\":\"mystery\"}]}")]
    [InlineData("{\"kind\":\"journal\",\"version\":1,\"entries\":[{\"op\":\"batch\",\"batch\":[{\"op\":\"undo\"}]}]}")]
    public void JournalRejectsWrongHeaderUnknownOpAndNonCommandBatch(string text)
    {
        var error = Assert.Throws<DocumentFormatException>(() => JournalFile.Read(text));
        Assert.StartsWith("$", error.Path);
    }
    [Theory]
    [InlineData("meter.unknown", 1, "{\"value\":1}")]
    [InlineData("meter.set", 2, "{\"value\":1}")]
    [InlineData("meter.set", 1, "{\"value\":\"bad\"}")]
    [InlineData("meter.set", 1, "{\"value\":1,\"extra\":1}")]
    public void CatalogueRejectsUnknownIdVersionAndBadArguments(string id, int version, string args)
    {
        var error = Assert.Throws<DocumentFormatException>(() => Catalog().FromEntry(JournalEntry.Do(id, version, JsonDocument.Parse(args).RootElement.Clone()), "$.entries[0]"));
        Assert.StartsWith("$.entries[0]", error.Path);
    }
    [Fact] public void ReplayStopsAtFirstRefusalAndNamesItsIndex()
    {
        var s = Session();
        var entries = new[] { s.Catalog.ToEntry(new SetMeter(1)), s.Catalog.ToEntry(new SetMeter(101)), s.Catalog.ToEntry(new SetMeter(7)) };
        var error = Assert.Throws<ReplayException>(() => s.Apply(entries));
        Assert.Equal(1, error.Entry); Assert.Equal(1, s.Current.Value);
        Assert.Single(s.Journal); Assert.NotEmpty(error.Issues);
    }
    [Fact] public void TwoReplaysHaveIdenticalDocumentAndJournalBytesIncludingBatchUndoRedo()
    {
        var s = Session();
        s.Execute(new ResolveMeter()); s.ExecuteBatch([new SetMeter(2), new SetMeter(9)]); s.Undo(); s.Redo();
        var one = DocumentSession<Meter>.Replay(new Meter(0), s.Journal, new MeterFormat(), new MeterValidator(), Catalog());
        var two = DocumentSession<Meter>.Replay(new Meter(0), s.Journal, new MeterFormat(), new MeterValidator(), Catalog());
        Assert.Equal(Encoding.UTF8.GetBytes(one.Format.Write(one.Current)), Encoding.UTF8.GetBytes(two.Format.Write(two.Current)));
        Assert.Equal(Encoding.UTF8.GetBytes(JournalFile.Write(one.Journal)), Encoding.UTF8.GetBytes(JournalFile.Write(two.Journal)));
        Assert.Equal(9, one.Current.Value);
    }
    [Fact] public void CliNewAndCheckSucceedButNewNeverOverwrites()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        Assert.Equal(0, Cli("check", path).Exit);
        Assert.Equal(2, Cli("new", path).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact] public void CliApplyAndTwoReplaysProduceIdenticalBoardBytes()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string basePath = scratch.File("base.json"); System.IO.File.Copy(path, basePath);
        string journal = Journal(scratch, Add + ",{\"op\":\"undo\"},{\"op\":\"redo\"}");
        Assert.Equal(0, Cli("apply", path, journal).Exit);
        string one = scratch.File("one.json"), two = scratch.File("two.json");
        Assert.Equal(0, Cli("replay", basePath, journal, one).Exit);
        Assert.Equal(0, Cli("replay", basePath, journal, two).Exit);
        Assert.Equal(System.IO.File.ReadAllBytes(one), System.IO.File.ReadAllBytes(two));
        Assert.Equal(System.IO.File.ReadAllBytes(path), System.IO.File.ReadAllBytes(one));
    }
    [Fact] public void CliRefusedJournalLeavesBoardUnwrittenAndReturnsOne()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        string journal = Journal(scratch, Add + "," + Do("toy.remove", "{\"id\":\"absent\"}"));
        Assert.Equal(1, Cli("apply", path, journal).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact] public void CliRefusedReplayCreatesNoOutput()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch), output = scratch.File("out.json");
        string journal = Journal(scratch, Do("toy.remove", "{\"id\":\"absent\"}"));
        Assert.Equal(1, Cli("replay", path, journal, output).Exit);
        Assert.False(System.IO.File.Exists(output));
    }
    [Fact] public void CliMalformedAndUnreadableFilesReturnTwo()
    {
        using var scratch = new Scratch();
        Assert.Equal(2, Cli("check", scratch.File("absent.json")).Exit);
        string malformed = scratch.File("bad.json"); System.IO.File.WriteAllText(malformed, "{");
        Assert.Equal(2, Cli("check", malformed).Exit);
        string board = NewBoard(scratch);
        Assert.Equal(2, Cli("apply", board, malformed).Exit);
    }
    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("new")]
    [InlineData("check")]
    [InlineData("apply")]
    [InlineData("replay")]
    public void CliWrongArgumentsReturnTwo(string command)
    {
        Assert.Equal(2, command.Length == 0 ? Cli().Exit : Cli(command).Exit);
    }
    [Fact] public void CliCommandSearchFindsLabelsAndIds()
    {
        var all = Cli("commands");
        Assert.Equal(0, all.Exit);
        foreach (string id in new[] { "toy.add", "toy.move", "toy.rename", "toy.remove" }) Assert.Contains(id, all.Out);
        var search = Cli("commands", "toy.move"); Assert.Equal(0, search.Exit);
        Assert.Contains("toy.move", search.Out); Assert.DoesNotContain("toy.remove", search.Out);
    }
    [Theory]
    [InlineData("kind", "unrecognized")]
    [InlineData("version", 999)]
    [InlineData("extra", true)]
    public void CliRejectsUnsupportedBoardHeadersAndUnknownProperties(string property, object value)
    {
        using var scratch = new Scratch();
        string path = WriteBoardNode(scratch, root => root[property] = JsonSerializer.SerializeToNode(value));
        Assert.Equal(2, Cli("check", path).Exit);
    }
    [Theory]
    [InlineData("Upper", "Label")]
    [InlineData("one-two", "Label")]
    [InlineData("1first", "Label")]
    [InlineData("okay", "")]
    [InlineData("okay", "   ")]
    public void CliRefusesInvalidItemIdsAndBlankLabels(string id, string label)
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string args = JsonSerializer.Serialize(new { id, label, x = 0, y = 0 });
        Assert.Equal(1, Cli("apply", path, Journal(scratch, Do("toy.add", args))).Exit);
    }
    [Fact] public void CliRefusesDuplicateIdsAndTooLongLabelsWithoutWriting()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        Assert.Equal(1, Cli("apply", path, Journal(scratch, Add + "," + Add)).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
        string args = JsonSerializer.Serialize(new { id = "valid_1", label = new string('a', 81), x = 0, y = 0 });
        Assert.Equal(1, Cli("apply", path, Journal(scratch, Do("toy.add", args))).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact]
    public void CliAllowsBoundaryLabelLengthAndRecordsOptionalGeneratedId()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string args = JsonSerializer.Serialize(new { label = new string('a', 80), x = 0, y = 0 });
        Assert.Equal(0, Cli("apply", path, Journal(scratch, Do("toy.add", args))).Exit);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        string id = doc.RootElement.GetProperty("items")[0].GetProperty("id").GetString()!;
        Assert.Matches("^item_[0-9]+$", id);
    }
    [Fact] public void CliCanonicalBoardIsUtf8WithoutBomReadableAndLfTerminated()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string args = JsonSerializer.Serialize(new { id = "cafe", label = "café", x = 0.1, y = -2.5 });
        Assert.Equal(0, Cli("apply", path, Journal(scratch, Do("toy.add", args))).Exit);
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("café", text); Assert.Contains("0.1", text);
        Assert.EndsWith("\n", text); Assert.DoesNotContain("\r", text);
    }

    [Fact] public void ActualToyGeneratedIdIsSettledInItsJournal()
    {
        var s = Toy.BoardKit.Session(Toy.Board.Empty);
        Assert.True(s.Execute(new Toy.AddItem(null, "Generated", 0, 0)));
        var resolved = Assert.IsType<Toy.AddItem>(s.Catalog.FromEntry(Assert.Single(s.Journal)));
        Assert.NotNull(resolved.Id);
        Assert.Matches("^item_[0-9]+$", resolved.Id!);
        var replay = DocumentSession<Toy.Board>.Replay(Toy.Board.Empty, s.Journal, Toy.BoardKit.Format, Toy.BoardKit.Validator, Toy.BoardKit.Catalog());
        Assert.Equal(Toy.BoardKit.Format.Write(s.Current), Toy.BoardKit.Format.Write(replay.Current));
    }
    [Fact] public void EightyCharacterLabelWithExplicitIdIsValid()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string args = JsonSerializer.Serialize(new { id = "valid", label = new string('a', 80), x = 0, y = 0 });
        Assert.Equal(0, Cli("apply", path, Journal(scratch, Do("toy.add", args))).Exit);
    }
    [Theory]
    [InlineData("missing", "$.items[0].label")]
    [InlineData("duplicate", "$.items[0].x")]
    [InlineData("wrongtype", "$.items[0].x")]
    [InlineData("nonfinite", "$.items[0].x")]
    public void BoardFormatRefusesMalformedItemsAndNamesTheExactPath(string mutation, string expected)
    {
        var board = new Toy.Board(System.Collections.Immutable.ImmutableArray.Create(new Toy.Item("one", "Label", 0, 0)));
        var root = JsonNode.Parse(Toy.BoardKit.Format.Write(board))!.AsObject();
        var item = root["items"]![0]!.AsObject();
        if (mutation == "missing") item.Remove("label");
        if (mutation == "wrongtype") item["x"] = "bad";
        string text = root.ToJsonString();
        if (mutation == "duplicate") text = text.Replace("\"x\":0", "\"x\":0,\"x\":1");
        if (mutation == "nonfinite") text = text.Replace("\"x\":0", "\"x\":1e999");
        var error = Assert.Throws<DocumentFormatException>(() => Toy.BoardKit.Format.Read(text));
        Assert.Equal(expected, error.Path);
    }
    [Fact]
    public void CliUnknownJournalCommandIsBadInputTwo()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        Assert.Equal(2, Cli("apply", path, Journal(scratch, Do("toy.unknown", "{}"))).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact]
    public void CliJournalArgumentVersionMismatchIsBadInputTwo()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        Assert.Equal(2, Cli("apply", path, Journal(scratch, Do("toy.remove", "{\"id\":\"one\"}", 999))).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact]
    public void CliBadlyTypedJournalArgumentsAreBadInputTwo()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch); byte[] before = System.IO.File.ReadAllBytes(path);
        Assert.Equal(2, Cli("apply", path, Journal(scratch, Do("toy.remove", "{\"id\":42}"))).Exit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
    [Fact] public void CliConcurrentDiskEditReturnsThreeAndDoesNotOverwriteOutsideChanges()
    {
        using var scratch = new Scratch();
        string path = NewBoard(scratch);
        string journal = Journal(scratch, Add);
        string outside = System.IO.File.ReadAllText(path) + " ";
        bool hookRan = false;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = Toy.Program.Run(["apply", path, journal], output, errors, () =>
        {
            hookRan = true;
            System.IO.File.WriteAllText(path, outside, new UTF8Encoding(false));
        });
        Assert.True(hookRan);
        Assert.Equal(3, exit);
        Assert.Equal(outside, System.IO.File.ReadAllText(path));
        Assert.Empty(output.ToString());
        Assert.Contains("changed on disk", errors.ToString());
    }

    [Fact] public void MissingNonNullableCommandArgumentIsBadInputWithPath()
    {
        var error = Assert.Throws<DocumentFormatException>(() => Catalog().FromEntry(
            JournalEntry.Do("meter.set", 1, JsonDocument.Parse("{}").RootElement.Clone()), "$.entries[2]"));
        Assert.StartsWith("$.entries[2]", error.Path);
    }
    [Fact] public void UnreadableReplayEntryHasItsPathAndStopsBeforeLaterEdits()
    {
        var s = Session();
        var entries = new[] {
            s.Catalog.ToEntry(new SetMeter(1)),
            JournalEntry.Do("meter.unknown", 1, JsonDocument.Parse("{}").RootElement.Clone()),
            s.Catalog.ToEntry(new SetMeter(7))
        };
        var error = Assert.Throws<DocumentFormatException>(() => s.Apply(entries));
        Assert.StartsWith("$.entries[1]", error.Path);
        Assert.Equal(1, s.Current.Value);
        Assert.Single(s.Journal);
    }
}
