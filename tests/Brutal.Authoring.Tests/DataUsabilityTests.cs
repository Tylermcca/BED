using System.Globalization;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures and event sequences. Acceptance oracle: usability_profile.md, not editor implementation.
[Collection(ImGuiCollection.Name)]
public sealed class DataUsabilityTests
{
    // Each U label refers to the profile checklist; narrow tests never certify an entire row.
    [Fact] public void U01_ReproducibleCpuBackendAndKeyboardConfiguration()
    {
        using var r = new Rig();
        var io = ImGui.GetIO();
        Assert.True(io.ConfigFlags.HasFlag(ImGuiConfigFlags.NavEnableKeyboard));
        Assert.True(io.ConfigNavCursorVisibleAlways);
        Assert.False(io.ConfigNavEscapeClearFocusItem);
        Assert.Contains("1.92.2", ImGui.GetVersion().ToString());
        r.Host.Drawing=true; r.Host.Frame(r.Draw);
        Assert.True(r.Host.Covered()>1000);
    }
    [Fact] public void U02_EmptyDataNeedsNoSetupToOpenSavePrompt()
    {
        using var r=new Rig("{\"items\":[]}"); r.Key(ImGuiKey.S,ctrl:true);
        Assert.True(r.Window.SaveAsOpen); Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U05_OutlineToItemToTextFieldEditAndConfirmWithoutPointer()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace("Desk Lamp");
        Assert.Equal("Desk Lamp",r.Name()); Assert.Equal("Lamp",r.Name(1)); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U05_ChoiceDropdownByKeyboard()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(1); r.Key(ImGuiKey.Enter); r.Key(ImGuiKey.DownArrow); r.Key(ImGuiKey.Enter);
        Assert.Equal("Normal",r.Value("mode").GetString()); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U05_ToggleByKeyboard()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(3); r.Key(ImGuiKey.Space);
        Assert.False(r.Value("enabled").GetBoolean()); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U06_BackwardTraversalReachesSameNameField()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(1); r.Key(ImGuiKey.Tab,shift:true); r.Replace("Backward");
        Assert.Equal("Backward",r.Name());
    }
    [Fact] public void U06_NavigationHighlightDoesNotSelectUntilActivation()
    {
        using var r=new Rig(); r.Key(ImGuiKey.Tab,ctrl:true); r.Key(ImGuiKey.DownArrow);
        Assert.Null(r.Editor.Primary); r.Key(ImGuiKey.Enter); Assert.Equal("/items/0",r.Editor.Primary);
        Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U06_EmptyOutlineDoesNotTrapKeyboardSave()
    {
        using var r=new Rig("{\"items\":[]}"); r.Key(ImGuiKey.Tab,ctrl:true); r.Key(ImGuiKey.DownArrow); r.Key(ImGuiKey.Enter);
        r.Key(ImGuiKey.S,ctrl:true); Assert.True(r.Window.SaveAsOpen); r.Key(ImGuiKey.Escape); Assert.False(r.Window.SaveAsOpen);
    }
    [Fact] public void U07_PaletteOpensSearchByStableIdAndActivatesSaveAs()
    {
        using var r=new Rig(); r.Palette("editor.file_save_as"); Assert.Single(r.Window.PaletteEntries("editor.file_save_as")); r.Host.Frames(r.Draw,5); r.Key(ImGuiKey.Tab); r.Key(ImGuiKey.Enter);
        Assert.True(r.Window.SaveAsOpen); Assert.False(r.Window.PaletteOpen); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U07_PaletteSearchByLabelAndArrowsActivatesSaveAsOnce()
    {
        using var r=new Rig(); r.Palette("Save as"); Assert.Single(r.Window.PaletteEntries("Save as")); r.Key(ImGuiKey.DownArrow); r.Key(ImGuiKey.Enter);
        Assert.True(r.Window.SaveAsOpen); Assert.False(r.Window.PaletteOpen); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U07_UnavailableCommandSaysWhatToDoAndDoesNotRun()
    {
        using var r=new Rig(); r.Palette("data.set");
        var ui=r.Ui(); Assert.Contains("needs details",ui); Assert.Contains("inspector",ui);
        r.Key(ImGuiKey.Enter); Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U08_DocumentUndoAndRedoByKeys()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace("Desk Lamp"); r.LeaveField();
        r.Key(ImGuiKey.Z,ctrl:true); Assert.Equal("Lamp",r.Name());
        r.Key(ImGuiKey.Y,ctrl:true); Assert.Equal("Desk Lamp",r.Name()); Assert.Equal(3,r.Session.Journal.Count);
    }
    [Fact] public void U08_TextUndoDoesNotAlsoUndoCommittedDocument()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace("Desk Lamp");
        r.Key(ImGuiKey.Enter); r.Key(ImGuiKey.A,ctrl:true); r.Type("Draft"); Assert.Contains("Draft",r.Ui()); r.Key(ImGuiKey.Z,ctrl:true); Assert.DoesNotContain("Draft",r.Ui());
        Assert.Equal("Desk Lamp",r.Name()); Assert.Single(r.Session.Journal);
        r.Key(ImGuiKey.Escape); Assert.Equal("Desk Lamp",r.Name());
    }
    [Fact] public void U08_FAndDeleteBelongToTextField()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Key(ImGuiKey.Enter); r.Key(ImGuiKey.A,ctrl:true); r.Key(ImGuiKey.F); r.Type("F"); r.Key(ImGuiKey.Delete);
        Assert.Null(r.Editor.FocusedOn); Assert.Equal("Lamp",r.Name()); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U08_SectionFocusAndEscapeByKeysAreViewState()
    {
        using var r=new Rig(); r.SelectSection(); r.Key(ImGuiKey.F); Assert.Equal("/items",r.Editor.FocusedOn);
        r.Key(ImGuiKey.Escape); Assert.Null(r.Editor.FocusedOn); Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U08_PaletteEscapeConsumesBeforeSectionFocusOut()
    {
        using var r=new Rig(); r.SelectSection(); r.Key(ImGuiKey.F); r.Palette(""); r.Key(ImGuiKey.Escape);
        Assert.False(r.Window.PaletteOpen); Assert.Equal("/items",r.Editor.FocusedOn);
        r.Key(ImGuiKey.Escape); Assert.Null(r.Editor.FocusedOn);
    }
    [Fact] public void U08_ChoicePopupEscapeConsumesBeforeSectionFocusOut()
    {
        using var r=new Rig(); r.SelectSection(); r.Key(ImGuiKey.F);
        r.Key(ImGuiKey.DownArrow); r.Key(ImGuiKey.Enter); Assert.Equal("/items/0",r.Editor.Primary);
        r.Field(1); r.Key(ImGuiKey.Enter); Assert.Contains("Bright",r.Ui()); r.Key(ImGuiKey.Escape);
        Assert.Equal("/items",r.Editor.FocusedOn); Assert.Equal("Quiet",r.Value("mode").GetString()); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U09_RemappedShortcutIsUsedByRealKeyBridge()
    {
        var shortcuts=new Shortcuts(); shortcuts.Set("editor.save","Ctrl+Alt+S");
        using var r=new Rig(shortcuts:shortcuts); r.Key(ImGuiKey.S,ctrl:true); Assert.False(r.Window.SaveAsOpen);
        r.Key(ImGuiKey.S,ctrl:true,alt:true); Assert.True(r.Window.SaveAsOpen);
    }
    [Fact] public void U09_ClashingShortcutFiresNothingAndExplainsBothActions()
    {
        var shortcuts=new Shortcuts(); shortcuts.Set("editor.undo","Ctrl+S");
        using var r=new Rig(shortcuts:shortcuts); r.Key(ImGuiKey.S,ctrl:true);
        Assert.False(r.Window.SaveAsOpen); Assert.Contains("clash",r.Window.Status,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("editor.undo",r.Window.Status); Assert.Contains("editor.save",r.Window.Status); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U10_CancelSavePromptCanReopenAndCompleteByKeyboard()
    {
        using var r=new Rig(); r.Key(ImGuiKey.S,ctrl:true); r.Key(ImGuiKey.Escape); Assert.False(r.Window.SaveAsOpen);
        r.Key(ImGuiKey.S,ctrl:true); var path=Path.Combine(r.Dir,"reopened.json"); r.SavePath(path);
        Assert.True(File.Exists(path)); Assert.False(r.Window.SaveAsOpen); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U17_RefusedNameHasReadableWordsWithoutColor()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace("");
        Assert.NotEmpty(r.Window.Status); Assert.Contains("empty",r.Window.Status,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lamp",r.Window.Status); Assert.Equal("Lamp",r.Name());
    }
    [Fact] public void U18_InvalidNameSaysFieldAndRemedyPersistently()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace(""); r.Host.Frames(r.Draw,10);
        Assert.Contains("name",r.Window.Status,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/items/0",r.Window.Status);
        Assert.True(r.Window.Status.Contains("character",StringComparison.OrdinalIgnoreCase) || r.Window.Status.Contains("empty",StringComparison.OrdinalIgnoreCase), r.Window.Status);
        Assert.Equal("Lamp",r.Name());
    }
    [Fact] public void U18_InvalidNumberNamesFieldAndRemedy()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(2); r.Replace("99");
        Assert.Equal(1,r.Value("amount").GetDouble()); Assert.Contains("amount",r.Window.Status,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10",r.Window.Status); Assert.Contains("at most",r.Window.Status,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void U18_RejectedTextRetainedForCorrection()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); var rejected=new string('X',81); r.Replace(rejected);
        Assert.Equal("Lamp",r.Name()); Assert.Contains(rejected,r.Ui());
        r.Key(ImGuiKey.Enter); r.Key(ImGuiKey.End); r.Key(ImGuiKey.Backspace); r.Key(ImGuiKey.Enter);
        Assert.Equal(new string('X',80),r.Name()); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U20_RefusedEditPreservesHistoryJournalAndDirtyState()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); var before=r.Session.Current.Root.GetRawText(); r.Replace("");
        Assert.Equal(before,r.Session.Current.Root.GetRawText()); Assert.Empty(r.Session.Journal);
        Assert.False(r.Session.History.CanUndo); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U20_FailedSaveKeepsExistingFileAndDirtyWork()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace("Desk Lamp"); r.LeaveField();
        var sentinel=Path.Combine(r.Dir,"sentinel.txt"); File.WriteAllText(sentinel,"keep me");
        r.Key(ImGuiKey.S,ctrl:true); r.SavePath(r.Dir);
        Assert.True(r.Session.Dirty); Assert.Equal("Desk Lamp",r.Name()); Assert.Equal("keep me",File.ReadAllText(sentinel));
        Assert.Single(r.Session.Journal); Assert.Contains("Couldn't save",r.Window.Status); Assert.True(r.Window.SaveAsOpen);
    }
    [Fact] public void U22_DuplicateLabelsKeepSecondItemIdentityDuringRename()
    {
        using var r=new Rig(); r.SelectItem(1); r.Field(0); r.Replace("Desk Lamp");
        Assert.Equal("Lamp",r.Name()); Assert.Equal("Desk Lamp",r.Name(1)); Assert.Equal("/items/1",r.Editor.Primary);
    }
    [Fact] public void U23_UnicodeCodepointsSurviveTypedEditSaveUndoRedoAndReload()
    {
        using var r=new Rig(); const string text="Grüße Straße Café Cafe\u0301 Łódź 東京 灯台";
        r.SelectItem(); r.Field(0); r.Replace(text); r.LeaveField(); r.Key(ImGuiKey.S,ctrl:true);
        var path=Path.Combine(r.Dir,"unicode.json"); r.SavePath(path); Assert.Equal(text,DataKit.Open(path,r.Schemas,"sample").Current.At("/items/0/name")!.Value.GetString());
        r.Key(ImGuiKey.Z,ctrl:true); Assert.Equal("Lamp",r.Name()); r.Key(ImGuiKey.Y,ctrl:true); Assert.Equal(text,r.Name());
    }
    [Fact] public void U24_MaximumNameAcceptedAndLongerNameRefused()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Replace(new string('A',80)); Assert.Equal(new string('A',80),r.Name());
        r.Key(ImGuiKey.Enter); r.Key(ImGuiKey.A,ctrl:true); r.Type(new string('B',81)); r.Key(ImGuiKey.Enter);
        Assert.Equal(new string('A',80),r.Name()); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U27_DecimalDotByKeyboardUsesInvariantNumber()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(2); r.Replace("1.5"); Assert.Equal(1.5,r.Value("amount").GetDouble());
    }
    [Fact] public void U27_AmbiguousDecimalCommaIsRejectedWithRemedy()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(2); r.Replace("1,5");
        Assert.Equal(1,r.Value("amount").GetDouble()); Assert.Empty(r.Session.Journal);
        Assert.Contains(".",r.Window.Status); Assert.Contains("number",r.Window.Status,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void U27_EnglishAndGermanCulturesSaveIdenticalBytes()
    {
        var old=CultureInfo.CurrentCulture;
        try {
            byte[] Save(string locale) { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(locale); using var r=new Rig(); r.SelectItem(); r.Field(2); r.Replace("1.5"); r.LeaveField(); r.Key(ImGuiKey.S,ctrl:true); var path=Path.Combine(r.Dir,"number.json"); r.SavePath(path); Assert.Equal(1.5,r.Value("amount").GetDouble()); return File.ReadAllBytes(path); }
            Assert.Equal(Save("en-US"),Save("de-DE"));
        } finally { CultureInfo.CurrentCulture=old; }
    }
    [Fact] public void U07_PaletteCancelRestoresInvokingFieldNavigation()
    {
        using var r=new Rig(); r.SelectItem(); r.Field(0); r.Palette("save"); r.Key(ImGuiKey.Escape);
        r.Replace("After cancel"); Assert.Equal("After cancel",r.Name()); Assert.Single(r.Session.Journal);
    }
    [Fact] public void U19_CurrentIssueActivatesByKeyboardAndShowsStableTargetAndField()
    {
        using var r=new Rig(withWarning:true); r.SelectItem(); r.Field(3); r.Key(ImGuiKey.Tab); r.Key(ImGuiKey.Enter);
        Assert.Contains("Showing",r.Window.Status); Assert.Contains("$.items[0].name",r.Window.Status);
        Assert.Equal("/items/0",r.Editor.Primary); Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U19_FileFailureKeepsHonestPathAndExistingSelection()
    {
        using var r=new Rig(); r.SelectItem(); r.Key(ImGuiKey.S,ctrl:true); r.SavePath(r.Dir);
        Assert.Equal("/items/0",r.Editor.Primary); Assert.Contains(r.Dir,r.Window.Status); Assert.Contains("Couldn't save",r.Window.Status);
        Assert.Empty(r.Session.Journal); Assert.False(r.Session.Dirty);
    }
    [Fact] public void U08_TextEscapeConsumesBeforeSectionFocusOut()
    {
        using var r=new Rig(); r.SelectSection(); r.Key(ImGuiKey.F); r.Key(ImGuiKey.DownArrow); r.Key(ImGuiKey.Enter);
        r.Field(0); r.Key(ImGuiKey.Enter); r.Type("draft"); r.Key(ImGuiKey.Escape);
        Assert.Equal("/items",r.Editor.FocusedOn); Assert.Equal("Lamp",r.Name()); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U09_RemovedBindingStillHasPaletteKeyboardRoute()
    {
        var shortcuts=new Shortcuts(); shortcuts.Set("editor.save",null);
        using var r=new Rig(shortcuts:shortcuts); r.Key(ImGuiKey.S,ctrl:true); Assert.False(r.Window.SaveAsOpen);
        r.Palette("editor.file_save_as"); r.Key(ImGuiKey.Tab); r.Key(ImGuiKey.Enter); Assert.True(r.Window.SaveAsOpen);
    }
    [Fact] public void U09_PersistedOverrideReachesKeyBridgeInFreshHost()
    {
        var configured=new Shortcuts(); configured.Set("editor.save","Ctrl+Alt+S"); var text=configured.WriteOverrides();
        var restored=new Shortcuts(); Assert.Empty(restored.ReadOverrides(text)); using var r=new Rig(shortcuts:restored);
        r.Key(ImGuiKey.S,ctrl:true,alt:true); Assert.True(r.Window.SaveAsOpen); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void U20_OutsideEditConflictDoesNotOverwriteExternalFile()
    {
        using var r=new Rig(); r.SelectItem(); r.Key(ImGuiKey.S,ctrl:true); var path=Path.Combine(r.Dir,"conflict.json"); r.SavePath(path);
        // Return from the file prompt to the inspector using window navigation.
        r.Key(ImGuiKey.Tab,ctrl:true); r.Replace("Unsaved work"); r.LeaveField(); Assert.Equal("Unsaved work",r.Name()); Assert.True(r.Session.Dirty);
        // A self-authored outside file edit is a failure fixture, not an editor operation.
        var external="{\"items\":[]}\n"; File.WriteAllText(path,external);
        r.Key(ImGuiKey.S,ctrl:true);
        Assert.Equal(external,File.ReadAllText(path)); Assert.Equal("Unsaved work",r.Name()); Assert.True(r.Session.Dirty); Assert.Single(r.Session.Journal);
    }
    // A self-authored warning uses the public validator seam; it never changes the document.
    private sealed class WarningValidator(JsonSchemas schemas) : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document) => new DataValidator(schemas,"sample").Validate(document)
            .Concat([Issue.Warning("Check Name on Lamp; enter a clearer name.","/items/0","$.items[0].name")]).ToList();
    }
    private sealed class Rig : IDisposable
    {
        public readonly string Dir = Directory.CreateTempSubdirectory("a031-").FullName;
        public readonly HeadlessHost Host = new(1280, 720) { Drawing = false };
        public readonly JsonSchemas Schemas;
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly Inspector<DataDocument> Inspector;
        public readonly EditorWindow<DataDocument> Window;
        public Action Draw => Window.DrawDocked;
        public Rig(string? json = null, Shortcuts? shortcuts = null, bool withWarning = false)
        {
            Schemas = new JsonSchemas().Add("sample", """
                {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
                  "name":{"type":"string","title":"Name","minLength":1,"maxLength":80,"description":"Enter 1 to 80 characters."},
                  "mode":{"type":"string","title":"Mode","enum":["Quiet","Normal","Bright"]},
                  "amount":{"type":"number","title":"Amount","minimum":0,"maximum":10,"description":"Enter a number from 0 to 10."},
                  "enabled":{"type":"boolean","title":"Enabled"}},"required":["name","mode","amount","enabled"]}}}}
                """);
            var document = new DataFormat().Read(json ?? """
                {"items":[{"name":"Lamp","mode":"Quiet","amount":1,"enabled":true},{"name":"Lamp","mode":"Normal","amount":2,"enabled":false}]}
                """);
            Session = withWarning ? new DocumentSession<DataDocument>(new DataFormat(), new WarningValidator(Schemas), DataKit.Catalog(), document) : DataKit.Session(document, Schemas, "sample");
            Editor = new EditorModel<DataDocument>(Session, DataEditing.Outline);
            Inspector = new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(Schemas,"sample"));
            Window = new EditorWindow<DataDocument>(Editor, Inspector, shortcuts);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Draw, 5);
        }
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false,bool alt=false) => Host.Press(Draw,key,ctrl,shift,alt);
        public void Type(string text) => Host.Type(Draw,text);
        public void SelectSection() { Key(ImGuiKey.Tab,ctrl:true); Key(ImGuiKey.Enter); Assert.Equal("/items",Editor.Primary); }
        public void SelectItem(int index=0) { Key(ImGuiKey.Tab,ctrl:true); for(var i=0;i<=index;i++) Key(ImGuiKey.DownArrow); Key(ImGuiKey.Enter); Assert.Equal($"/items/{index}",Editor.Primary); }
        public void Field(int index) { Key(ImGuiKey.Tab,ctrl:true); for(var i=0;i<index;i++) Key(ImGuiKey.Tab); }
        public void Replace(string text)
        {
            // Tab enters text editing immediately; window navigation only highlights it.
            // Observe ImGui capture rather than forcing focus or touching the editor model.
            if (!ImGui.GetIO().WantTextInput) Key(ImGuiKey.Enter);
            Key(ImGuiKey.A,ctrl:true); Key(ImGuiKey.Backspace); Type(text); Key(ImGuiKey.Enter);
        }
        public void LeaveField() => Key(ImGuiKey.Tab,ctrl:true);
        public void Palette(string query) { Key(ImGuiKey.P,ctrl:true,shift:true); Assert.True(Window.PaletteOpen); Type(query); }
        public void SavePath(string path) { Assert.True(Window.SaveAsOpen); Key(ImGuiKey.A,ctrl:true); Type(path); Key(ImGuiKey.Enter); }
        public JsonElement Value(string key) => Session.Current.At("/items/0/"+key)!.Value;
        public string Ui()
        {
            // Public ImGui text capture observes what is drawn. Depth 0 avoids opening trees.
            var file=Path.Combine(Dir,Guid.NewGuid()+".txt");
            Host.Frame(()=> { ImGui.LogToFile(0,file); Window.DrawDocked(); ImGui.LogFinish(); });
            return File.ReadAllText(file);
        }
        public string Name(int item=0) => Session.Current.At($"/items/{item}/name")!.Value.GetString()!;
        public void Dispose() { Host.Dispose(); Directory.Delete(Dir,true); }
    }
}
