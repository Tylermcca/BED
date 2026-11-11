from pathlib import Path
import re,textwrap
import pytest
ROOT=Path(__file__).resolve().parent.parent
@pytest.mark.parametrize('tag',['ADMIT','SAVED','TICK','CHECK','TEXT'])
def test_published_region_is_the_actual_compiled_sample(tag):
 source=(ROOT/'samples/StarterAdapter/ManagedSourceHooks.cs').read_text(encoding='utf-8-sig')
 guide=(ROOT/'docs/guide/writing_an_adapter.md').read_text(encoding='utf-8-sig')
 actual=re.search(r'    // DOC19 '+tag+r' BEGIN\n(.*?)    // DOC19 '+tag+r' END',source,re.S)
 shown=re.search(r'<!-- DOC19 '+tag+r' -->\s*```csharp\n(.*?)\n```',guide,re.S)
 assert actual and shown,tag
 assert textwrap.dedent(actual.group(1)).rstrip()==shown.group(1)

def test_hook_examples_keep_real_declarations_and_owner_save_boundaries():
 data=(ROOT/'src/Bed.Data/DataProject.cs').read_text(encoding='utf-8-sig')
 workspace=(ROOT/'src/Bed.Editor.ImGui/DataWorkspace.cs').read_text(encoding='utf-8-sig')
 ui=(ROOT/'src/Bed.Editor.ImGui/Ui.cs').read_text(encoding='utf-8-sig')
 for declaration in ('Func<string, bool>? admit','event Action<IReadOnlyList<DataFile>>? FilesSaved','Func<DataFile, string?>? SaveCheck','BeingSaved(DataFile file)'):assert declaration in data
 for declaration in ('event Action? Ticked','bool BackgroundSaves','void FinishSaves()'):assert declaration in workspace
 assert 'bool InputTextMultiline(' in ui
 guide=(ROOT/'docs/guide/writing_an_adapter.md').read_text(encoding='utf-8-sig')
 for boundary in ('no-edit or refused save raises none','does not cancel the task','another open file\'s unsaved draft','same document as the table/Inspector','not a timer'):assert boundary in guide
 sample=(ROOT/'samples/StarterAdapter/ManagedSourceHooks.cs').read_text(encoding='utf-8-sig')
 assert 'ManagedSourceHooks.cs' in (ROOT/'tests/Bed.Tests/Bed.Tests.csproj').read_text()
 assert 'sample.source.text' in sample and 'editor.Execute(new SetValue(' in sample
