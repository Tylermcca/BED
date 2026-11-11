"""Pin everyday data instructions to their real controls and important scope guards."""
from pathlib import Path
import re

import pytest

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "src/Bed.Editor.ImGui"


def read(path):
    return path.read_text(encoding="utf-8-sig")


def guide():
    return read(ROOT / "docs/guide/data_workflow.md")


@pytest.mark.parametrize("file,wrapper,label,shown", [
    ("DataTableView.cs", "InputText", "Filter##table", "Filter"),
    ("DataTableView.cs", "SmallButton", "Back", "Back"),
    ("DataTableView.cs", "Button", "Fill shown rows", "Fill shown rows"),
    ("DataTableView.cs", "SmallButton", "Apply", "Apply"),
    ("DataTableView.cs", "SmallButton", "Cancel", "Cancel"),
    ("DataTableView.Bulk.cs", "Button", "Fill selected rows", "Fill selected rows"),
    ("DataTableView.Bulk.cs", "SmallButton", "Earlier refused rows", "Earlier refused rows"),
    ("DataTableView.Bulk.cs", "SmallButton", "More refused rows", "More refused rows"),
    ("DataWorkspace.FindReplace.cs", "InputText", "Find value", "Find value"),
    ("DataWorkspace.FindReplace.cs", "InputText", "Replace with", "Replace with"),
    ("DataWorkspace.FindReplace.cs", "Button", "Preview matches", "Preview matches"),
    ("DataWorkspace.FindReplace.cs", "Button", "Replace chosen", "Replace chosen"),
    ("DataWorkspace.FindReplace.cs", "Button", "Close find and replace", "Close find and replace"),
    ("InspectorController.cs", "SmallButton", "reset", "reset"),
])
def test_documented_controls_keep_their_actual_labels(file, wrapper, label, shown):
    assert f'Ui.{wrapper}("{label}"u8' in read(UI / file)
    assert f"**{shown}**" in guide()


def test_named_panels_and_default_find_replace_binding_exist():
    workspace = read(UI / "DataWorkspace.cs")
    assert 'new("project.references", "Used by", DrawReferences' in workspace
    assert 'if (Map is not null) editor.Add(ReferencesPanel)' in workspace
    assert '.Default(EditorWindow<DataDocument>.PanelAction+"project.find_replace","Ctrl+H")' in workspace
    assert 'Ui.Begin("Inspector###inspector"u8)' in read(UI / "EditorWindow.cs")
    for label in ("Used by", "Inspector", "Ctrl+H"):
        assert f"**{label}**" in guide()
    assert '"Find and replace",DrawFindReplace,OpenAtStart:false' in read(UI / "DataWorkspace.FindReplace.cs")


def test_fill_scope_includes_hidden_selection_and_preserves_preview_guards():
    bulk = read(UI / "DataTableView.Bulk.cs")
    table = read(UI / "DataTableView.cs")
    for source in ('if(selection.Count<2)', 'Items().Where(selected.Contains)',
                   'hidden by the filter', 'Ctrl+click or Ctrl+Space', 'Shift+click',
                   'Editor.Session.Revision,Editor.Session.Current,Shown,[..selection]'):
        assert source in bulk
    assert 'Checks on the whole file still run at Apply.' in table
    assert 'new SetValues(' in table and 'Editor.Execute(' in table
    text = guide()
    for phrase in ('Hidden selected rows remain targets', 'A refused batch changes no rows',
                   'One document\nundo', 'whole-document checks'):
        assert phrase in text


def test_replace_exact_scalar_scope_and_preview_limit_are_documented():
    source = read(UI / "DataWorkspace.FindReplace.cs")
    for clause in ('JsonValueKind.Array or JsonValueKind.Object',
                   'property.Name=="$schema"', 'JsonSchemas.Same(value,find)',
                   'private const int ReplaceLimit=5000', 'Documents.IsScene(file.Name)',
                   'if(!file.Open())', 'file.ReadOnlyReason',
                   'replacePreview=false;replaceMatches.Clear()'):
        assert clause in source
    for phrase in ('whole values', 'root `$schema`', 'unsearched', '5,000', 'Read-only matches',
                   'quoted text', 'clears the preview and choices'):
        assert phrase in guide()


def test_replace_draft_and_partial_application_limits_match_the_code():
    source = read(UI / "DataWorkspace.FindReplace.cs")
    preflight = source.index('captured.Session.Validator.Validate(candidate.Document!)')
    apply = source.index('plan.Editor.ExecuteAt(plan.Source.Revision')
    assert preflight < source.index('foreach(var plan in plans)') < apply
    for guard in ('source.Session.Revision!=source.Revision', 'DiskState.Unchanged',
                  'Stop Play before replacing values.', 'Changed {done} files.',
                  'One undo step per file; save to keep the changes.'):
        assert guard in source
    assert 'source.Session.Save(' not in source
    for phrase in ('before applying any replacement', 'later host-policy refusal',
                   '**one undo step per file**', 'changes drafts'):
        assert phrase in guide()


def test_reference_scope_and_pointer_jumps_keep_their_implementation():
    workspace = read(UI / "DataWorkspace.cs")
    table = read(UI / "DataTableView.cs")
    assert 'map.ReferencesInto(Project, new ReferenceTarget(file, item))' in workspace
    assert 'GoTo(r.At)' in workspace
    assert 'Ui.SmallButton($"->##go{item}/{cell.Id}")' in table
    assert 'Follow reference' in table
    for phrase in ('JSON pointer', 'unsaved source edits', 'unmapped text', '**Back**'):
        assert phrase in guide()


def test_guide_links_resolve_and_everyday_workflow_is_first_in_index():
    index = read(ROOT / "docs/guide/README.md")
    # The newcomer walkthrough comes first (UX04, 2026-11-10); everyday data editing is the next entry.
    assert re.search(r'^1\. \[Start here\]\(start_here\.md\)', index, re.M)
    assert re.search(r'^2\. \[Edit project data\]\(data_workflow\.md\)', index, re.M)
    assert '[Build a data editor](editing_data.md)' in index
    for target in re.findall(r'\]\(([^)]+)\)', guide()):
        assert (ROOT / "docs/guide" / target.split("#")[0]).is_file(), target
