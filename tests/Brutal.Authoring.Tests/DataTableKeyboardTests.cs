using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED self-authored fixtures; intended routes derive from A029 P0.7 and the U-rows.
[Collection(ImGuiCollection.Name)]
public sealed class DataTableKeyboardTests
{
    const string Schema = """
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
      "name":{"type":"string","title":"Name","minLength":1},
      "amount":{"type":"number","title":"Amount","minimum":0,"maximum":100},
      "count":{"type":"integer","title":"Count","minimum":0},
      "enabled":{"type":"boolean","title":"Enabled"},
      "mode":{"type":"string","title":"Mode","enum":["Quiet","Normal","Bright"]},
      "link":{"type":"string","title":"Link","x-ref":"/targets"}},
      "required":["name","amount","count","enabled","mode","link"]}},
      "targets":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}},"required":["id","name"]}}}}
    """;
    const string Text = """
    {"items":[
      {"name":"Amber","amount":30,"count":1,"enabled":false,"mode":"Quiet","link":"target-a"},
      {"name":"Birch","amount":10,"count":2,"enabled":true,"mode":"Normal","link":"target-b"},
      {"name":"Cobalt","amount":20,"count":3,"enabled":false,"mode":"Bright","link":"target-a"}],
    "targets":[{"id":"target-a","name":"Beacon"},{"id":"target-b","name":"Shelter"}]}
    """;

    // A029 P0.7 / U05: every exposed pointer action has a keyboard route.
    [Fact] public void U05_OutlineCanOpenAndReachTheTableWithoutPointer()
    {
        using var r=new Rig(tableOpen:false);
        r.Key(ImGuiKey.Tab,ctrl:true);r.Key(ImGuiKey.Enter);
        Assert.Equal("/items",r.Editor.Primary);
        r.Palette("editor.panel.editor.table");r.Key(ImGuiKey.Enter);
        Assert.True(r.Editor.IsOpen("editor.table"));
        r.Cell(0,0);
        r.Replace("Reopened");
        Assert.Equal("Reopened",r.Value(0,"name").GetString());
    }

    // P0.7 / U06: forward and reverse cell traversal, preserving document state.
    [Fact] public void U06_TabMovesAcrossEditableCells()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,0);r.Key(ImGuiKey.Tab);
        Assert.Equal(("/items/0","amount"),r.Table.Anchor);
        r.Untouched(before);
    }
    [Fact] public void U06_ShiftTabReturnsToThePreviousCell()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,1);r.Key(ImGuiKey.Tab,shift:true);
        Assert.Equal(("/items/0","name"),r.Table.Anchor);r.Untouched(before);
    }
    [Fact] public void P07_RightArrowMovesToTheAdjacentCell()
    {
        using var r=new Rig();r.Cell(0,1);r.Key(ImGuiKey.Escape);r.Key(ImGuiKey.RightArrow);
        Assert.Equal(("/items/0","count"),r.Table.Anchor);r.Replace("6");
        Assert.Equal(6,r.Value(0,"count").GetInt32());Assert.Equal(30,r.Value(0,"amount").GetDouble());
    }
    [Fact] public void P07_DownArrowKeepsColumnInTheNextRow()
    {
        using var r=new Rig();r.Cell(0,1);r.Key(ImGuiKey.Escape);r.Key(ImGuiKey.DownArrow);
        r.Replace("11");
        Assert.True(r.Value(1,"amount").GetDouble()==11 && r.Value(0,"amount").GetDouble()==30,
            $"After Escape, Down, Enter/type 11: {r.Session.Current.Root}; Anchor={r.Table.Anchor}");
        Assert.Equal(11,r.Value(1,"amount").GetDouble());Assert.Equal(30,r.Value(0,"amount").GetDouble());
        Assert.Equal(("/items/1","amount"),r.Table.Anchor);
    }
    [Fact] public void U06_TabReachesTheNextRowsNumberCell()
    {
        using var r=new Rig();r.Cell(1,1);r.Replace("17.25");
        Assert.Equal(17.25,r.Value(1,"amount").GetDouble());Assert.Equal(30,r.Value(0,"amount").GetDouble());
    }

    // P0.7 / U08: Enter confirms, Escape cancels, document undo owns only committed edits.
    [Fact] public void P07_NumberCellConfirmsWithEnter()
    {
        using var r=new Rig();r.Cell(0,1);r.Replace("7.25");
        Assert.Equal(7.25,r.Value(0,"amount").GetDouble());Assert.Single(r.Session.Journal);
    }
    [Fact] public void P07_IntegerCellConfirmsWithEnter()
    {
        using var r=new Rig();r.Cell(0,2);r.Replace("7");
        Assert.Equal(7,r.Value(0,"count").GetInt32());Assert.Single(r.Session.Journal);
    }
    [Fact] public void U08_EscapeCancelsTypedCellWithoutACommand()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,0);
        r.Key(ImGuiKey.A,ctrl:true);r.Type("Uncommitted");r.Key(ImGuiKey.Escape);
        r.Untouched(before);
    }
    [Fact] public void U08_OneDocumentUndoRestoresOneConfirmedCellEdit()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,1);r.Replace("7");
        Assert.Equal(7,r.Value(0,"amount").GetDouble());r.LeaveCells();r.Key(ImGuiKey.Z,ctrl:true);
        Assert.Same(before,r.Session.Current);r.Key(ImGuiKey.Z,ctrl:true);Assert.Same(before,r.Session.Current);
    }
    [Fact] public void U08_TextUndoDoesNotUndoAnEarlierCommittedCell()
    {
        using var r=new Rig();r.Cell(0,1);r.Replace("7");r.Key(ImGuiKey.Tab);
        r.Key(ImGuiKey.A,ctrl:true);r.Type("9");r.Key(ImGuiKey.Z,ctrl:true);r.Key(ImGuiKey.Escape);
        Assert.Equal(7,r.Value(0,"amount").GetDouble());Assert.Equal(1,r.Value(0,"count").GetInt32());
        Assert.Single(r.Session.Journal);
    }
    [Fact] public void U05_ChoiceCellAcceptsAnArrowSelectedOption()
    {
        using var r=new Rig();r.Cell(0,4);r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.DownArrow);r.Key(ImGuiKey.Enter);
        Assert.Equal("Normal",r.Value(0,"mode").GetString());Assert.Single(r.Session.Journal);
    }
    [Fact] public void U08_DropdownEscapeCancelsAndCanBeReopened()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,4);
        r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.DownArrow);r.Key(ImGuiKey.Escape);r.Untouched(before);
        r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.DownArrow);r.Key(ImGuiKey.Enter);
        Assert.Equal("Normal",r.Value(0,"mode").GetString());
    }
    [Fact] public void U05_CheckboxTogglesBySpaceAsOneUndoStep()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,3);r.Key(ImGuiKey.Space);
        Assert.True(r.Value(0,"enabled").GetBoolean());Assert.Single(r.Session.Journal);
        r.LeaveCells();r.Key(ImGuiKey.Z,ctrl:true);Assert.Same(before,r.Session.Current);
    }

    // P0.7 / U05/U20: filter and sort are view state, never source reordering.
    [Fact] public void U05_FilterCanBeTypedWithoutChangingTheDocument()
    {
        using var r=new Rig();var before=r.Session.Current;r.Key(ImGuiKey.Tab);r.Type("Birch");
        Assert.Equal("Birch",r.Table.Filter);Assert.Equal(new[]{"/items/1"},r.Table.Visible());r.Untouched(before);
    }
    [Fact] public void U06_FilterCanBeClearedEntirelyByKeyboard()
    {
        using var r=new Rig();var before=r.Session.Current;r.Key(ImGuiKey.Tab);r.Type("Birch");
        r.Key(ImGuiKey.A,ctrl:true);r.Key(ImGuiKey.Backspace);
        Assert.Equal("",r.Table.Filter);Assert.Equal(3,r.Table.Visible().Count);r.Untouched(before);
    }
    [Fact] public void U05_NumberHeaderSortsAscendingByEnter()
    {
        using var r=new Rig();var before=r.Session.Current;r.InsideTable();r.Tabs(2);r.Key(ImGuiKey.Enter);
        Assert.Equal(("amount",false),r.Table.Sort);Assert.Equal(new[]{"/items/1","/items/2","/items/0"},r.Table.Visible());
        r.Untouched(before);
    }
    [Fact] public void U05_NumberHeaderReversesSortByAnotherEnter()
    {
        using var r=new Rig();var before=r.Session.Current;r.InsideTable();r.Tabs(2);
        r.Key(ImGuiKey.Enter);r.Key(ImGuiKey.Enter);
        Assert.Equal(("amount",true),r.Table.Sort);Assert.Equal(new[]{"/items/0","/items/2","/items/1"},r.Table.Visible());r.Untouched(before);
    }
    [Fact] public void U20_EditAfterKeyboardSortTargetsTheUnderlyingItem()
    {
        using var r=new Rig();r.InsideTable();r.Tabs(2);r.Key(ImGuiKey.Enter);r.Tabs(7);
        Assert.Equal(("/items/1","amount"),r.Table.Anchor);r.Replace("13");
        Assert.Equal(13,r.Value(1,"amount").GetDouble());Assert.Equal(30,r.Value(0,"amount").GetDouble());
    }

    // P0.7 / U20: preview is not a mutation; Apply is atomic and one undo.
    [Fact] public void U05_FillPreviewIsReachableAndDoesNotCommit()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("8");
        Assert.NotNull(r.Table.Pending);Assert.True(r.Table.Pending.Ok);Assert.Equal(3,r.Table.Pending.Cells);r.Untouched(before);
    }
    [Fact] public void U08_FillApplyAndOneUndoCoverAllShownRows()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("8");r.ApplyPreview();
        Assert.Null(r.Table.Pending);Assert.All(Enumerable.Range(0,3),i=>Assert.Equal(8,r.Value(i,"amount").GetDouble()));
        Assert.Single(r.Session.Journal);r.Key(ImGuiKey.Z,ctrl:true);Assert.Same(before,r.Session.Current);
    }
    [Fact] public void U20_FilteredFillLeavesHiddenRowsUnchanged()
    {
        using var r=new Rig();r.FillPreview("8",filter:"Amber");Assert.Equal(1,r.Table.Pending!.Cells);r.ApplyPreview();
        Assert.Equal(8,r.Value(0,"amount").GetDouble());Assert.Equal(10,r.Value(1,"amount").GetDouble());Assert.Equal(20,r.Value(2,"amount").GetDouble());
    }
    [Fact] public void U08_FillCancelButtonLeavesDocumentAndHistoryUntouched()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("8");r.Tabs(2);r.Key(ImGuiKey.Enter);
        Assert.Null(r.Table.Pending);r.Untouched(before);
    }
    [Fact] public void U20_SchemaRejectedFillExplainsAndChangesNothing()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("-1");
        Assert.NotNull(r.Table.Pending);r.ApplyPreview();
        r.RefusalReason();r.Untouched(before);
    }
    [Fact] public void U05_PasteGridUsesClipboardAndOneUndoForTheWholeBatch()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,1);r.PastePreview("7.5\t4\n9.25\t5");
        Assert.NotNull(r.Table.Pending);Assert.True(r.Table.Pending.Ok);Assert.Equal(4,r.Table.Pending.Cells);
        r.ApplyPreview();Assert.Equal(7.5,r.Value(0,"amount").GetDouble());Assert.Equal(4,r.Value(0,"count").GetInt32());
        Assert.Equal(9.25,r.Value(1,"amount").GetDouble());Assert.Equal(5,r.Value(1,"count").GetInt32());
        Assert.Single(r.Session.Journal);r.Key(ImGuiKey.Z,ctrl:true);Assert.Same(before,r.Session.Current);
    }
    [Fact] public void U20_InvalidClipboardGridIsRefusedWhole()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,1);r.PastePreview("7\t4\n-1\t5");
        Assert.NotNull(r.Table.Pending);r.ApplyPreview();
        r.RefusalReason();r.Untouched(before);
    }
    [Fact] public void U05_ReferenceArrowAndBackAreKeyboardReachable()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,5);r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);
        Assert.Equal("/targets",r.Table.Shown);Assert.Equal("/targets/0",r.Editor.Primary);r.Untouched(before);
        r.LeaveCells();r.Tabs(5,shift:true);r.Key(ImGuiKey.Enter);
        Assert.Equal("/items",r.Table.Shown);Assert.Equal("/items/0",r.Editor.Primary);r.Untouched(before);
    }

    // U18 diagnostic identity is checked separately so a gap does not skip U20 atomic refusal coverage.
    [Fact] public void U18_FillRefusalNamesTheFieldAndRemedy()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("-1");r.ApplyPreview();r.Untouched(before);
        r.AmountRefusal();
    }
    [Fact] public void U18_PasteRefusalNamesTheFieldAndRemedy()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,1);r.PastePreview("7\t4\n-1\t5");r.ApplyPreview();r.Untouched(before);
        r.AmountRefusal();
    }

    [Fact] public void U05_TextSelectionCanCopyThroughTheSameClipboardProvider()
    {
        using var r=new Rig();var before=r.Session.Current;r.Cell(0,0);
        ImGui.SetClipboardText("unchanged sentinel");r.Key(ImGuiKey.A,ctrl:true);r.Key(ImGuiKey.C,ctrl:true);
        Assert.Equal("Amber",ImGui.GetClipboardText().ToString());r.Key(ImGuiKey.Escape);r.Untouched(before);
    }
    [Fact] public void U20_MalformedNumberFillPreviewExplainsAndChangesNothing()
    {
        using var r=new Rig();var before=r.Session.Current;r.FillPreview("seven");
        Assert.NotNull(r.Table.Pending);Assert.False(r.Table.Pending.Ok);
        Assert.Contains(r.Table.Pending.Problems,p=>p.Contains("number",StringComparison.OrdinalIgnoreCase));r.Untouched(before);
    }

    // P0.7 / U09: existing user shortcut overrides win over editor defaults.
    [Fact] public void U09_UserUndoBindingWinsForAKeyboardTableEdit()
    {
        using var r=new Rig(remapUndo:true);var before=r.Session.Current;r.Cell(0,1);r.Replace("7");r.LeaveCells();
        r.Key(ImGuiKey.Z,ctrl:true);Assert.Equal(7,r.Value(0,"amount").GetDouble());
        r.Key(ImGuiKey.U,ctrl:true);Assert.Same(before,r.Session.Current);
    }

    // P0.7: copy and keyboard range extension have intended spreadsheet outcomes, not an API fallback.
    [Fact] public void P07_CopyCellByKeyboardProducesItsValue()
    {
        using var r=new Rig();r.Cell(0,1);r.Key(ImGuiKey.Escape);ImGui.SetClipboardText("unchanged sentinel");
        r.Key(ImGuiKey.C,ctrl:true);Assert.Equal("30",ImGui.GetClipboardText().ToString());
    }
    [Fact] public void P07_ShiftRightExtendsACopyableCellRange()
    {
        using var r=new Rig();r.Cell(0,1);r.Key(ImGuiKey.Escape);ImGui.SetClipboardText("unchanged sentinel");
        r.Key(ImGuiKey.RightArrow,shift:true);r.Key(ImGuiKey.C,ctrl:true);
        Assert.Equal("30\t1",ImGui.GetClipboardText().ToString()!.TrimEnd('\r','\n'));
    }
    [Fact] public void P07_ShiftDownExtendsACopyableRowRange()
    {
        using var r=new Rig();r.Cell(0,1);r.Key(ImGuiKey.Escape);ImGui.SetClipboardText("unchanged sentinel");
        r.Key(ImGuiKey.DownArrow,shift:true);r.Key(ImGuiKey.C,ctrl:true);
        Assert.Equal("30\n10",ImGui.GetClipboardText().ToString()!.Replace("\r\n","\n").TrimEnd('\n'));
    }
    [Fact] public void U07_FillCanBeDiscoveredThroughTheCommandPalette()
    {
        using var r=new Rig();r.Palette("fill");
        Assert.Contains(r.Window.PaletteEntries("fill"),e=>e.Label.Contains("Fill",StringComparison.OrdinalIgnoreCase));
    }

    sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host = new(1600,900) { Drawing=false };
        public readonly DataFormat Format = new();
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly DataTableView Table;
        public readonly EditorWindow<DataDocument> Window;
        public readonly string Dir;
        public Action Draw => Window.DrawDocked;
        public Rig(bool tableOpen=true,bool remapUndo=false)
        {
            Dir=Path.Combine(Path.GetTempPath(),"a037-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            var schemas=new JsonSchemas().Add("keyboard",Schema);
            Session=DataKit.Session(Format.Read(Text),schemas,"keyboard");
            Editor=new(Session,DataEditing.OutlineFor(schemas,"keyboard"));
            var type=DataEditing.ObjectType(schemas,"keyboard");
            Table=new(Editor,type) { References=DataEditing.SchemaReferences(schemas,"keyboard") };
            Editor.Add(Table.AsPanel() with { OpenAtStart=tableOpen });
            var shortcuts=new Shortcuts();if(remapUndo)shortcuts.Set("editor.undo","Ctrl+U");
            Window=new(Editor,new Inspector<DataDocument>(Editor).Add(type),shortcuts);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Draw,5);
        }
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false,bool alt=false)=>Host.Press(Draw,key,ctrl,shift,alt);
        public void Type(string text)=>Host.Type(Draw,text);
        public void Tabs(int count,bool shift=false) { for(int i=0;i<count;i++)Key(ImGuiKey.Tab,shift:shift); }
        // Observed standard layout route: activate the scrolling child, then traverse its seven headers and identity.
        // No ID/focus injection, table setter, Execute, SetCell or command planner is used for any user action.
        public void InsideTable() { Key(ImGuiKey.DownArrow);Key(ImGuiKey.DownArrow);Key(ImGuiKey.Enter); }
        public void Cell(int row,int column)
        {
            var before=Session.Current;var commands=Session.Journal.Count;
            InsideTable();Tabs(8+8*row+column);
            Assert.Same(before,Session.Current);Assert.Equal(commands,Session.Journal.Count);
            Assert.Equal(($"/items/{row}",new[]{"name","amount","count","enabled","mode","link"}[column]),Table.Anchor);
        }
        public void Replace(string text)
        {
            if(!ImGui.GetIO().WantTextInput)Key(ImGuiKey.Enter);
            Key(ImGuiKey.A,ctrl:true);Type(text);Key(ImGuiKey.Enter);
        }
        public void LeaveCells() { Key(ImGuiKey.Escape);Key(ImGuiKey.Escape); }
        public void FillPreview(string text,string? filter=null)
        {
            Tabs(1);if(filter is not null)Type(filter);Tabs(1);Key(ImGuiKey.Enter);
            Key(ImGuiKey.DownArrow);Key(ImGuiKey.Enter); // Name -> Amount in the visible field chooser.
            Tabs(1);Type(text);Tabs(1);Key(ImGuiKey.Enter);
        }
        public void PastePreview(string text)
        {
            ImGui.SetClipboardText(text);LeaveCells();Tabs(1,shift:true);Key(ImGuiKey.Enter);
        }
        public void ApplyPreview() { Tabs(1);Key(ImGuiKey.Enter); }
        public void Palette(string query)
        {
            Key(ImGuiKey.P,ctrl:true,shift:true);Assert.True(Window.PaletteOpen);Type(query);
        }
        public JsonElement Value(int row,string field)=>Session.Current.At($"/items/{row}/{field}")!.Value;
        public void Untouched(DataDocument before)
        {
            Assert.Same(before,Session.Current);Assert.Empty(Session.Journal);Assert.Equal(Format.Write(before),Format.Write(Session.Current));
            Assert.Equal(0,Session.Revision);Assert.False(Session.Dirty);Assert.Equal(0,Session.History.UndoCount);Assert.False(Session.History.CanRedo);
        }
        public void RefusalReason()
        {
            Assert.Contains("at least",Table.Said);Assert.Contains("0",Table.Said);Assert.Contains(Table.Said,Ui());
        }
        public void AmountRefusal()
        {
            RefusalReason();Assert.Contains("amount",Table.Said,StringComparison.OrdinalIgnoreCase);
        }
        public string Ui()
        {
            var file=Path.Combine(Dir,"ui.txt");
            Host.Frame(()=>{ ImGui.LogToFile(0,file); Window.DrawDocked(); ImGui.LogFinish(); });
            return File.ReadAllText(file);
        }
        public void Dispose()
        {
            Host.Dispose();
            var prefix=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"a037-"));
            if(!Path.GetFullPath(Dir).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected test temp path.");
            Directory.Delete(Dir,true);
        }
    }
}
