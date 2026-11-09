#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Collections.Immutable;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Toy;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Independent public-contract tests (A009). Written before reading the existing editor tests.
/// INVENTED fixtures: a tiny nested board, property descriptors, and text-only assets; no game records.</summary>
public sealed class EditorAcceptanceTests
{
    private static Board Basis() => new([
        new("world", "World", 0, 0), new("room", "Room", 0, 0),
        new("a", "Alpha", 1, 0), new("b", "Beta", 2, 0), new("outside", "Outside", 3, 0)]);

    private sealed class Outline : IDocumentOutline<Board>
    {
        public IEnumerable<string> ObjectIds(Board d) => d.Items.Select(x => x.Id);
        public string? Label(Board d, string id) => d.Find(id)?.Label;
        public string? Parent(Board d, string id) => id switch
        {
            "room" => "world", "a" or "b" => "room", _ => null
        };
    }

    private static EditorModel<Board> Editor(IValidator<Board>? validator = null) =>
        new(new DocumentSession<Board>(BoardKit.Format, validator ?? BoardKit.Validator, BoardKit.Catalog(), Basis()), new Outline());

    private static Inspector<Board> Inspector(EditorModel<Board> e) => new Inspector<Board>(e).Add(
        new ObjectType<Board, Item>("accept.item", "Items", (d, id) => id is "a" or "b" ? d.Find(id) : null,
        [
            new("x", "Position X", PropertyKind.Number, i => i.X,
                (i, value) => new MoveItem(i.Id, Convert.ToDouble(value), i.Y), (_, _) => 0d,
                Help: "Position in metres."),
            new("label", "Label", PropertyKind.Text, i => i.Label),
            new("choice", "Style", PropertyKind.Choice, i => i.Label,
                (i, value) => new RenameItem(i.Id, (string)value!), Choices: ["Alpha", "Beta"], Help: "Choose a label.")
        ]));

    // EditorModel.cs:52-54: view state never dirties, enters undo or enters the journal.
    [Fact]
    public void ViewChangesLeaveCleanDocumentHistoryAndJournalUntouched()
    {
        var e = Editor(); var s = e.Session; var before = BoardKit.Format.Write(s.Current);
        e.Add(new Workspace("accept.layout", "Layout", ["toy.move"]));
        e.Select("a", "b"); e.Use("accept.layout"); e.FocusOn("room"); e.FocusOut(); e.Select();
        Assert.False(s.Dirty); Assert.Equal(0, s.Revision); Assert.Empty(s.Journal);
        Assert.False(s.History.CanUndo); Assert.False(s.History.CanRedo);
        Assert.Equal(before, BoardKit.Format.Write(s.Current));
    }

    [Fact]
    public void ViewChangesPreserveAnExistingRedoBranch()
    {
        var e = Editor(); var s = e.Session;
        Assert.True(s.Execute(new RenameItem("a", "Edited"))); Assert.True(s.Undo());
        string journal = JournalFile.Write(s.Journal); long revision = s.Revision;
        e.Select("b"); e.FocusOn("room"); e.FocusOut();
        Assert.True(s.History.CanRedo); Assert.Equal(revision, s.Revision); Assert.Equal(journal, JournalFile.Write(s.Journal));
        Assert.True(s.Redo()); Assert.Equal("Edited", s.Current.Find("a")!.Label);
    }

    // EditorModel.cs:81-88: selection order, last primary, unknown ids ignored, no-op returns false.
    [Fact]
    public void SelectionKeepsOrderAndLastPrimaryWhileIgnoringUnknownIds()
    {
        var e = Editor();
        Assert.True(e.Select("b", "absent", "a"));
        Assert.Equal(["b", "a"], e.Selection); Assert.Equal("a", e.Primary);
        Assert.False(e.Select("b", "a"));
    }

    [Fact]
    public void EmptyAndWhollyUnknownSelectionHaveNoPrimary()
    {
        var e = Editor(); Assert.False(e.Select("absent")); Assert.Null(e.Primary);
        e.Select("a"); Assert.True(e.Select()); Assert.Empty(e.Selection); Assert.Null(e.Primary);
    }

    [Fact]
    public void ToggleAddsThenRemovesAnObject()
    {
        var e = Editor(); e.Select("a"); Assert.True(e.Toggle("b"));
        Assert.Equal(["a", "b"], e.Selection); Assert.Equal("b", e.Primary);
        Assert.True(e.Toggle("b")); Assert.Equal(["a"], e.Selection); Assert.Equal("a", e.Primary);
    }

    [Fact]
    public void SelectionDropsDeletedObjectsAndUndoDoesNotRestoreViewState()
    {
        var e = Editor(); e.Select("a", "b");
        Assert.True(e.Session.Execute(new RemoveItem("b")));
        Assert.Equal(["a"], e.Selection); Assert.Equal("a", e.Primary);
        Assert.True(e.Session.Undo()); Assert.Equal(["a"], e.Selection);
    }

    [Fact]
    public void ViewChangedNotifiesSelectionFocusAndWorkspaceChanges()
    {
        var e = Editor(); e.Add(new Workspace("accept.layout", "Layout", [])); int notices = 0;
        e.ViewChanged += () => notices++;
        e.Select("a"); Assert.True(notices > 0); int prior = notices;
        e.FocusOn("room"); Assert.True(notices > prior); prior = notices;
        e.Use("accept.layout"); Assert.True(notices > prior);
    }

    // EditorModel.cs:108-126: registration order, new id, every lead command exists.
    [Fact]
    public void WorkspacesKeepRegistrationOrderAndShareTheOriginalSession()
    {
        var e = Editor(); var session = e.Session;
        e.Add(new Workspace("accept.one", "One", ["toy.add"])).Add(new Workspace("accept.two", "Two", ["toy.move"]));
        Assert.Equal(["accept.one", "accept.two"], e.Workspaces.Select(x => x.Id));
        e.Use("accept.two"); Assert.Same(session, e.Session); Assert.Equal("accept.two", e.Active!.Id);
        e.Use(null); Assert.Null(e.Active);
    }

    [Fact]
    public void DuplicateWorkspaceIdIsRejectedWithoutReplacingTheOriginal()
    {
        var e = Editor(); var original = new Workspace("accept.same", "First", []); e.Add(original);
        Assert.ThrowsAny<Exception>(() => e.Add(new Workspace("accept.same", "Second", [])));
        Assert.Same(original, Assert.Single(e.Workspaces));
    }

    [Fact]
    public void WorkspaceWithUnknownCommandIsRejectedWithoutRegistration()
    {
        var e = Editor();
        Assert.ThrowsAny<Exception>(() => e.Add(new Workspace("accept.bad", "Bad", ["toy.not_registered"])));
        Assert.Empty(e.Workspaces); Assert.Null(e.Active);
    }

    private sealed record Hidden() : ICommand<Board>
    {
        public CommandResult<Board> Apply(Board d) => CommandResult<Board>.Done(d);
    }

    private static EditorModel<Board> Palette()
    {
        // Deliberate registration order opposes search rank. All command types are registered explicitly.
        var c = new CommandCatalog<Board>()
            .Register<AddItem>("accept.add", 1, "Remove object")
            .Register<RemoveItem>("accept.remove", 1, "Batch Move")
            .Register<RenameItem>("accept.rename", 1, "Move label")
            .Register<MoveItem>("accept.move", 1, "Move object")
            .Register<Hidden>("accept.hidden", 1, "Move hidden", isInternal: true);
        return new(new DocumentSession<Board>(BoardKit.Format, BoardKit.Validator, c, Basis()), new Outline());
    }

    // EditorModel.cs:137-144: start, word-start, contains; workspace leads only within rank.
    [Fact]
    public void PaletteRanksPrefixThenWordStartThenContains()
    {
        var e = Palette();
        Assert.Equal(["accept.rename", "accept.move", "accept.remove", "accept.add"], e.Search("move").Select(x => x.Command.Id));
    }

    [Fact]
    public void WorkspaceLeadsWithinRankWithoutPromotingAContainsMatch()
    {
        var e = Palette(); e.Add(new Workspace("accept.w", "Work", ["accept.add", "accept.move"])); e.Use("accept.w");
        Assert.Equal(["accept.move", "accept.rename", "accept.remove", "accept.add"], e.Search("move").Select(x => x.Command.Id));
    }

    [Fact]
    public void PaletteNeverShowsInternalCommandsEvenForAnExactIdSearch()
    {
        var e = Palette();
        Assert.DoesNotContain(e.Search(""), x => x.Command.Id == "accept.hidden");
        Assert.Empty(e.Search("accept.hidden"));
    }

    [Fact]
    public void UnavailablePaletteCommandStaysVisibleWithItsExactReason()
    {
        var row = Assert.Single(Palette().Search("accept.rename", _ => "Select a label first."));
        Assert.False(row.Available); Assert.Equal("Select a label first.", row.Unavailable);
        Assert.True(Assert.Single(Palette().Search("accept.rename")).Available);
    }

    // EditorModel.cs:172-219: breadcrumb parent chain, step out, unknown focus, locking.
    [Fact]
    public void FocusBreadcrumbIsOutermostFirstAndStepsOutOneLevel()
    {
        var e = Editor(); Assert.True(e.FocusOn("a"));
        Assert.Equal(["world", "room", "a"], e.Focus); Assert.Equal("a", e.FocusedOn);
        Assert.True(e.FocusOut()); Assert.Equal("room", e.FocusedOn);
        Assert.True(e.FocusOut()); Assert.Equal("world", e.FocusedOn);
        Assert.True(e.FocusOut()); Assert.Empty(e.Focus); Assert.Null(e.FocusedOn); Assert.False(e.FocusOut());
    }

    [Fact]
    public void UnknownFocusLeavesTheCurrentBreadcrumbAlone()
    {
        var e = Editor(); e.FocusOn("room"); string[] before = e.Focus.ToArray();
        Assert.False(e.FocusOn("absent")); Assert.Equal(before, e.Focus);
    }

    [Fact]
    public void FocusAllowsDescendantsAndLocksOutsideAndUnknownObjects()
    {
        var e = Editor(); e.FocusOn("room");
        Assert.True(e.InFocus("a")); Assert.True(e.Editable("room")); Assert.True(e.Editable("b"));
        Assert.False(e.InFocus("outside")); Assert.False(e.Editable("outside")); Assert.False(e.Editable("absent"));
    }

    [Fact]
    public void LockedEditChangesNoDocumentHistoryOrJournalAndExplainsRefusal()
    {
        var e = Editor(); e.FocusOn("room"); string before = BoardKit.Format.Write(e.Session.Current);
        Assert.False(e.Execute(new MoveItem("outside", 9, 9), "outside"));
        Assert.Equal(before, BoardKit.Format.Write(e.Session.Current)); Assert.False(e.Session.Dirty);
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.History.CanUndo);
        Assert.NotEmpty(e.Refused); Assert.Contains(e.Refused, i => i.ObjectId == "outside");
    }

    [Fact]
    public void AllowedEditUsesSessionAndClearsPriorEditorRefusal()
    {
        var e = Editor(); e.FocusOn("room"); e.Execute(new MoveItem("outside", 9, 9), "outside");
        Assert.True(e.Execute(new MoveItem("a", 7, 8), "a")); Assert.Empty(e.Refused);
        Assert.Equal(7, e.Session.Current.Find("a")!.X); Assert.Single(e.Session.Journal); Assert.True(e.Session.Undo());
        Assert.Equal(1, e.Session.Current.Find("a")!.X);
    }

    [Fact]
    public void DeletedFocusedPlaceFallsBackToExistingParent()
    {
        var e = Editor(); e.FocusOn("room");
        Assert.True(e.Session.Execute(new RemoveItem("room")));
        Assert.Equal(["world"], e.Focus); Assert.Equal("world", e.FocusedOn);
        Assert.True(e.Session.Execute(new RemoveItem("world"))); Assert.Empty(e.Focus);
    }

    private sealed class CyclicOutline : IDocumentOutline<Board>
    {
        public IEnumerable<string> ObjectIds(Board d) => d.Items.Select(i => i.Id);
        public string? Label(Board d, string id) => d.Find(id)?.Label;
        public string? Parent(Board d, string id) => id switch { "a" => "b", "b" => "a", _ => null };
    }

    [Fact]
    public async Task CyclicOutlineDoesNotTrapFocusOrEditabilityQueries()
    {
        var e = new EditorModel<Board>(BoardKit.Session(Basis()), new CyclicOutline());
        await Task.Run(() => { e.FocusOn("a"); _ = e.InFocus("outside"); _ = e.Editable("b"); }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(e.Focus, id => Assert.NotNull(e.Session.Current.Find(id)));
        Assert.Equal(e.Focus.Count, e.Focus.Distinct().Count());
    }

    // Inspector.cs:94-100,105-106: mixed/default metadata; entire selection is one undo step.
    [Fact]
    public void InspectorShowsMixedValuesWithoutInventingACommonValue()
    {
        var e = Editor(); e.Select("a", "b"); var row = Inspector(e).Rows().Single(r => r.Id == "x");
        Assert.True(row.Mixed); Assert.Null(row.Value); Assert.True(row.Editable);
        Assert.True(row.HasDefault); Assert.True(row.Overridden);
    }

    [Fact]
    public void EqualSelectedValuesAreNotMixed()
    {
        var e = Editor(); e.Session.Execute(new MoveItem("b", 1, 0)); e.Select("a", "b");
        var row = Inspector(e).Rows().Single(r => r.Id == "x");
        Assert.False(row.Mixed); Assert.Equal(1d, row.Value);
    }

    [Fact]
    public void MultiSelectEditIsOneUndoStepAndOneRedoRestoresBoth()
    {
        var e = Editor(); e.Select("a", "b"); var i = Inspector(e); string before = BoardKit.Format.Write(e.Session.Current);
        Assert.True(i.Set("x", 9d)); Assert.Empty(i.LastIssues); Assert.Equal(1, e.Session.History.UndoCount);
        Assert.All(e.Session.Current.Items.Where(x => x.Id is "a" or "b"), x => Assert.Equal(9, x.X));
        Assert.True(e.Session.Undo()); Assert.Equal(before, BoardKit.Format.Write(e.Session.Current));
        Assert.False(e.Session.History.CanUndo); Assert.True(e.Session.Redo());
        Assert.Equal(9, e.Session.Current.Find("a")!.X); Assert.Equal(9, e.Session.Current.Find("b")!.X);
    }

    [Fact]
    public void ResetRestoresDefaultsForWholeSelectionInOneUndo()
    {
        var e = Editor(); e.Select("a", "b"); var i = Inspector(e); string before = BoardKit.Format.Write(e.Session.Current);
        Assert.True(i.Reset("x"));
        var row = i.Rows().Single(r => r.Id == "x");
        Assert.False(row.Mixed); Assert.False(row.Overridden); Assert.Equal(0d, row.Value);
        Assert.Equal(1, e.Session.History.UndoCount); Assert.True(e.Session.Undo());
        Assert.Equal(before, BoardKit.Format.Write(e.Session.Current));
    }

    [Fact]
    public void ReadOnlyPropertyRefusesSetAndReportsWhy()
    {
        var e = Editor(); e.Select("a"); var i = Inspector(e);
        Assert.False(i.Rows().Single(r => r.Id == "label").Editable);
        Assert.False(i.Set("label", "Changed")); Assert.NotEmpty(i.LastIssues);
        Assert.Equal("Alpha", e.Session.Current.Find("a")!.Label); Assert.Empty(e.Session.Journal);
    }

    [Fact]
    public void AnySelectedObjectOutsideFocusLocksAllInspectorRows()
    {
        var e = Editor(); e.FocusOn("a"); e.Select("a", "b"); var i = Inspector(e);
        Assert.All(i.Rows(), r => Assert.False(r.Editable));
        Assert.False(i.Set("x", 9d)); Assert.NotEmpty(i.LastIssues);
        Assert.Equal(1, e.Session.Current.Find("a")!.X); Assert.Equal(2, e.Session.Current.Find("b")!.X);
        Assert.Empty(e.Session.Journal);
    }

    [Fact]
    public void EmptySelectionHasNoTypeRowsOrEditableProperty()
    {
        var i = Inspector(Editor()); Assert.Null(i.SelectedType); Assert.Empty(i.Rows());
        Assert.False(i.Set("x", 9d)); Assert.NotEmpty(i.LastIssues);
    }

    [Fact]
    public void MixedObjectTypesHaveNoSharedInspectorType()
    {
        var e = Editor(); e.Select("a", "world"); var i = Inspector(e);
        Assert.Null(i.SelectedType); Assert.Empty(i.Rows());
    }

    [Fact]
    public void PropertyWithoutDefaultCannotReset()
    {
        var e = Editor(); e.Select("a"); var i = Inspector(e);
        Assert.False(i.Rows().Single(r => r.Id == "choice").HasDefault);
        Assert.False(i.Reset("choice")); Assert.NotEmpty(i.LastIssues); Assert.Empty(e.Session.Journal);
    }

    [Fact]
    public void InspectorPreservesPropertyDisplayOrderChoicesAndHelp()
    {
        var e = Editor(); e.Select("a"); var rows = Inspector(e).Rows();
        Assert.Equal(["x", "label", "choice"], rows.Select(r => r.Id));
        Assert.Equal("Position in metres.", rows[0].Help); Assert.Equal(PropertyKind.Number, rows[0].Kind);
        Assert.Equal(["Alpha", "Beta"], rows[2].Choices); Assert.Equal("Choose a label.", rows[2].Help);
    }

    [Fact]
    public void RejectedMultiEditIsAtomicAndLaterSuccessClearsIssues()
    {
        var e = Editor(); e.Select("a", "b"); var i = Inspector(e);
        Assert.False(i.Set("x", double.NaN)); Assert.NotEmpty(i.LastIssues);
        Assert.Equal(1, e.Session.Current.Find("a")!.X); Assert.Equal(2, e.Session.Current.Find("b")!.X);
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.History.CanUndo);
        Assert.True(i.Set("x", 4d)); Assert.Empty(i.LastIssues);
    }

    private sealed class WarningValidator : IValidator<Board>
    {
        public IReadOnlyList<Issue> Validate(Board d) =>
            [Issue.Warning("Check Alpha.", "a", "$.items[2]"), Issue.Warning("Check outside.", "outside"), Issue.Warning("Global.")];
    }

    [Fact]
    public void InspectorIssuesConcernOnlyTheSelectedObjects()
    {
        var e = Editor(new WarningValidator()); e.Select("a"); var issues = Inspector(e).Issues();
        Assert.Equal("a", Assert.Single(issues).ObjectId); Assert.Equal("Check Alpha.", issues[0].Message);
    }

    // Shortcuts.cs:28-29,96-149: errors, preferences, clash refusal, settings round trip.
    [Theory]
    [InlineData("shift+ctrl+z", "Ctrl+Shift+Z")]
    [InlineData("alt+f12", "Alt+F12")]
    [InlineData("Space", "Space")]
    [InlineData("CTRL+7", "Ctrl+7")]
    public void ChordsParseCaseInsensitivelyAndPrintCanonically(string input, string expected)
    {
        var chord = Chord.Parse(input); Assert.Equal(expected, chord.ToString()); Assert.Equal(chord, Chord.Parse(chord.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+DefinitelyNotAKey")]
    [InlineData("F13")]
    [InlineData("Ctrl+Shift")]
    public void InvalidChordThrowsFormatExceptionWithAnExplanation(string input)
    {
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<FormatException>(() => Chord.Parse(input)).Message));
    }

    [Fact]
    public void UserChoiceWinsEvenWhenPluginDefaultsArriveLater()
    {
        var s = new Shortcuts(); s.Set("accept.action", "Alt+J"); s.Default("accept.action", "Ctrl+J");
        Assert.Equal(Chord.Parse("Alt+J"), s.For("accept.action"));
        Assert.Equal("accept.action", s.Action(Chord.Parse("Alt+J")));
    }

    [Fact]
    public void NullOverrideDisablesAndResetRestoresDefault()
    {
        var s = new Shortcuts().Default("accept.action", "Alt+J"); s.Set("accept.action", null);
        Assert.Null(s.For("accept.action")); Assert.Null(s.Action(Chord.Parse("Alt+J")));
        s.ResetToDefault("accept.action"); Assert.Equal(Chord.Parse("Alt+J"), s.For("accept.action"));
    }

    [Fact]
    public void ClashTriggersNothingAndIssueNamesEveryAction()
    {
        var s = new Shortcuts().Default("accept.one", "Alt+J").Default("accept.two", "Alt+J");
        Assert.Null(s.Action(Chord.Parse("Alt+J"))); string issues = string.Join("\n", s.Clashes().Select(i => i.Message));
        Assert.Contains("accept.one", issues); Assert.Contains("accept.two", issues);
        s.Set("accept.two", "Alt+K"); Assert.Empty(s.Clashes()); Assert.Equal("accept.one", s.Action(Chord.Parse("Alt+J")));
    }

    [Fact]
    public void SettingsRoundTripKeepsOverridesAndExplicitDisable()
    {
        var s = new Shortcuts().Default("accept.disabled", "Alt+L");
        s.Set("accept.action", "Alt+J"); s.Set("accept.disabled", null); string text = s.WriteOverrides();
        var copy = new Shortcuts().Default("accept.disabled", "Alt+L");
        Assert.Empty(copy.ReadOverrides(text)); Assert.Equal(Chord.Parse("Alt+J"), copy.For("accept.action"));
        Assert.Null(copy.For("accept.disabled")); Assert.Equal(text, copy.WriteOverrides());
    }

    [Fact]
    public void BadShortcutSettingsLineIsReportedWhileOtherChoiceLoads()
    {
        var source = new Shortcuts(); source.Set("accept.bad", "Alt+J"); source.Set("accept.good", "Alt+K");
        string text = source.WriteOverrides().Replace("Alt+J", "Alt+DefinitelyNotAKey", StringComparison.Ordinal);
        var loaded = new Shortcuts(); var issues = loaded.ReadOverrides(text);
        Assert.NotEmpty(issues); Assert.Null(loaded.For("accept.bad"));
        Assert.Equal(Chord.Parse("Alt+K"), loaded.For("accept.good"));
    }

    [Fact]
    public void AllEditorDefaultsAreReachableThroughTheirDocumentedChords()
    {
        var s = new Shortcuts();
        Assert.NotEmpty(Shortcuts.EditorDefaults);
        foreach (var entry in Shortcuts.EditorDefaults)
        {
            Assert.Equal(Chord.Parse(entry.Value), s.For(entry.Key));
            Assert.Equal(entry.Key, s.Action(Chord.Parse(entry.Value)));
        }
    }

    // AssetBrowser.cs:18-20,35-69: path order, separate unimported list, search/badges/refresh.
    private sealed class Folder : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "A009-" + Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Root);
        public string Write(string name, string text = "self-authored fixture")
        {
            string path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text); return path;
        }
        public AssetRecord Import(string name, string licence = "self-authored", string importer = "accept.mesh")
        {
            string path = Write(name);
            using var json = JsonDocument.Parse("{}");
            return AssetRecord.Create(path, importer, 1, json.RootElement,
                new Provenance("A009 fixture", licence, "INVENTED test text"), AiUse.Forbidden);
        }
        public void Dispose()
        {
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe scratch path.");
            Directory.Delete(Root, true);
        }
    }

    [Fact]
    public void BrowserListsImportedAssetsInPathOrderWithTheirPermanentIds()
    {
        using var f = new Folder(); var z = f.Import("z.mesh"); var a = f.Import("folder/a.mesh");
        var b = new AssetBrowser(f.Root, ".mesh");
        Assert.Equal(["folder/a.mesh", "z.mesh"], b.Entries.Select(e => e.RelativePath.Replace('\\', '/')));
        Assert.Equal([a.Guid, z.Guid], b.Entries.Select(e => e.Guid)); Assert.Empty(b.NotImported);
        Assert.All(b.Entries, e => { Assert.False(e.HasProblems); Assert.Equal(AiUse.Forbidden, e.AiUse); });
    }

    [Fact]
    public void BrowserSearchFindsNamePathGuidPrefixImporterAndLicence()
    {
        using var f = new Folder(); var asset = f.Import("folder/Alpha.mesh", "accept-licence", "accept.importer"); f.Import("other.mesh", "other-licence", "other.importer");
        var b = new AssetBrowser(f.Root, ".mesh");
        foreach (string query in new[] { "ALPHA", Path.Combine("folder", "Alpha").ToUpperInvariant(), asset.Guid[..8].ToUpperInvariant(), "ACCEPT.IMPORTER", "ACCEPT-LICENCE" })
            Assert.Equal(asset.Guid, Assert.Single(b.Search(query)).Guid);
        Assert.Equal(2, b.Search("").Count); Assert.Empty(b.Search("not-present-anywhere"));
    }

    [Fact]
    public void FilesWithoutSidecarsAreSeparateFromImportedEntries()
    {
        using var f = new Folder(); f.Import("ready.mesh"); f.Write("waiting.mesh"); f.Write("ignore.txt");
        var b = new AssetBrowser(f.Root, ".mesh");
        Assert.Single(b.Entries); Assert.Equal(["waiting.mesh"], b.NotImported);
        Assert.DoesNotContain(b.Entries, x => x.Name == "waiting.mesh");
    }

    [Fact]
    public void ChangedSourceGetsProblemBadgeAndProblemsOnlyFilter()
    {
        using var f = new Folder(); var changed = f.Import("changed.mesh"); f.Import("clean.mesh");
        File.AppendAllText(Path.Combine(f.Root, "changed.mesh"), " changed");
        var b = new AssetBrowser(f.Root, ".mesh"); var entry = b.Entries.Single(x => x.Guid == changed.Guid);
        Assert.True(entry.HasProblems); Assert.NotEmpty(entry.Issues);
        Assert.Equal(changed.Guid, Assert.Single(b.Search("", problemsOnly: true)).Guid);
        Assert.Empty(b.Search("clean.mesh", problemsOnly: true));
    }

    [Fact]
    public void MissingLicenceGetsAProblemBadge()
    {
        using var f = new Folder(); var asset = f.Import("unlicensed.mesh", "");
        var entry = Assert.Single(new AssetBrowser(f.Root, ".mesh").Entries);
        Assert.Equal(asset.Guid, entry.Guid); Assert.True(entry.HasProblems); Assert.NotEmpty(entry.Issues);
    }

    [Fact]
    public void MissingSourceRetainsItsIdentityAndProblemBadge()
    {
        using var f = new Folder(); var asset = f.Import("gone.mesh"); File.Delete(Path.Combine(f.Root, "gone.mesh"));
        var entry = Assert.Single(new AssetBrowser(f.Root, ".mesh").Entries);
        Assert.Equal(asset.Guid, entry.Guid); Assert.True(entry.HasProblems);
    }

    [Fact]
    public void DuplicatePermanentIdsAreReportedAsGeneralProblems()
    {
        using var f = new Folder(); var asset = f.Import("first.mesh"); f.Write("second.mesh");
        File.WriteAllText(AssetRecord.SidecarFor(Path.Combine(f.Root, "second.mesh")), (asset with { Source = "second.mesh" }).Write());
        var b = new AssetBrowser(f.Root, ".mesh");
        Assert.NotEmpty(b.General); Assert.Contains(b.General, i => i.Message.Contains(asset.Guid, StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableSidecarIsGeneralProblemRatherThanACrash()
    {
        using var f = new Folder(); f.Write("bad.mesh"); f.Write("bad.mesh.asset.json", "{ invalid");
        Assert.NotEmpty(new AssetBrowser(f.Root, ".mesh").General);
    }

    [Fact]
    public void RefreshDiscoversNewImportAndDoesNotReplaceOldGuid()
    {
        using var f = new Folder(); var original = f.Import("old.mesh"); var b = new AssetBrowser(f.Root, ".mesh");
        f.Import("new.mesh"); Assert.Single(b.Entries); b.Refresh(); Assert.Equal(2, b.Entries.Count);
        Assert.Equal(original.Guid, b.Entries.Single(x => x.Name == "old.mesh").Guid);
    }

    [Fact]
    public void RefreshMovesNewlyImportedFileOutOfTheSeparateList()
    {
        using var f = new Folder(); f.Write("later.mesh"); var b = new AssetBrowser(f.Root, ".mesh");
        Assert.Equal(["later.mesh"], b.NotImported);
        using var json = JsonDocument.Parse("{}");
        var record = AssetRecord.Create(Path.Combine(f.Root, "later.mesh"), "accept.mesh", 1, json.RootElement,
            new Provenance("A009", "self-authored", "INVENTED fixture"), AiUse.Forbidden);
        b.Refresh(); Assert.Empty(b.NotImported); Assert.Equal(record.Guid, Assert.Single(b.Entries).Guid);
    }

    private sealed class RefuseNineOnBeta : IValidator<Board>
    {
        public IReadOnlyList<Issue> Validate(Board d) => d.Find("b")!.X == 9
            ? [Issue.Error("Beta cannot be at nine.", "b", "$.items[3].x")] : BoardKit.Validator.Validate(d);
    }

    [Fact]
    public void FiniteValidatorRefusalRollsBackWholeMultiEditAndSuccessClearsIssues()
    {
        var e = Editor(new RefuseNineOnBeta()); e.Select("a", "b"); var i = Inspector(e);
        string before = BoardKit.Format.Write(e.Session.Current);
        Assert.False(i.Set("x", 9d)); Assert.Contains(i.LastIssues, x => x.ObjectId == "b");
        Assert.Equal(before, BoardKit.Format.Write(e.Session.Current)); Assert.Equal(0, e.Session.Revision);
        Assert.False(e.Session.Dirty); Assert.Empty(e.Session.Journal); Assert.False(e.Session.History.CanUndo);
        Assert.True(i.Set("x", 4d)); Assert.Empty(i.LastIssues);
    }

    [Fact]
    public void DuplicateIdsStillBadgeBothAffectedEntries()
    {
        using var f = new Folder(); var asset = f.Import("one.mesh"); f.Write("two.mesh");
        File.WriteAllText(AssetRecord.SidecarFor(Path.Combine(f.Root, "two.mesh")), (asset with { Source = "two.mesh" }).Write());
        var b = new AssetBrowser(f.Root, ".mesh");
        Assert.Equal(2, b.Entries.Count); Assert.All(b.Entries, e => Assert.True(e.HasProblems));
        Assert.Equal(2, b.Search("", problemsOnly: true).Count);
    }

    [Fact]
    public void InspectorRejectsDuplicateTypeIdAndKeepsOriginalDescriptors()
    {
        var e = Editor(); e.Select("a"); var i = Inspector(e);
        var duplicate = new ObjectType<Board, Item>("accept.item", "Replacement", (d, id) => d.Find(id), []);
        Assert.ThrowsAny<Exception>(() => i.Add(duplicate));
        Assert.Equal(["x", "label", "choice"], i.Rows().Select(r => r.Id));
    }

    [Fact]
    public void UnknownInspectorPropertyRefusesWithoutChangingAnything()
    {
        var e = Editor(); e.Select("a", "b"); var i = Inspector(e);
        string before = BoardKit.Format.Write(e.Session.Current);
        Assert.False(i.Set("missing_property", 1d)); Assert.NotEmpty(i.LastIssues);
        Assert.Equal(before, BoardKit.Format.Write(e.Session.Current)); Assert.Empty(e.Session.Journal);
        Assert.Equal(0, e.Session.Revision); Assert.False(e.Session.History.CanUndo);
    }
}
