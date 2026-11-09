"""INVENTED self-authored fixture tests; no game, compiler, network or GUI."""
from pathlib import Path
import json
import subprocess
import pytest
from check_docs_drift import audit, main, clean_cs

CS = """namespace Demo;
public class Widget
{
    public int Grow(int n) => n + 1;
    public string Title { get; } = "seed";
}
"""

@pytest.fixture
def repo(tmp_path):
    (tmp_path / "src").mkdir()
    (tmp_path / "docs").mkdir()
    (tmp_path / "tools").mkdir()
    (tmp_path / "src/Widget.cs").write_text(CS)
    return tmp_path

def run(repo, text, **kwargs):
    (repo / "README.md").write_text(text)
    return audit(repo, ["README.md"], find_program=lambda name: "/installed/" + name, **kwargs)

def kinds(result):
    return {row['kind'] for row in result['findings']}

def test_current_type_member_and_line_are_clean(repo):
    result = run(repo, "`Widget.Grow` is at `src/Widget.cs:4`.", near=0)
    assert result['findings'] == []
    assert any(x['kind'] == 'anchor' for x in result['checks'])

def test_planted_stale_line_is_valid_but_has_the_wrong_member(repo):
    result = run(repo, "`Widget.Grow` is at `src/Widget.cs:5`.", near=0)
    assert 'stale-anchor' in kinds(result)
    assert 'missing-path' not in kinds(result)

@pytest.mark.parametrize('number', [0, 99])
def test_out_of_range_line(repo, number):
    result = run(repo, f"`Widget.Grow` is at `src/Widget.cs:{number}`.")
    assert 'stale-line' in kinds(result)

def test_planted_rename_is_not_hidden_by_other_type_or_comment_or_string(repo):
    (repo / "src/Widget.cs").write_text(CS.replace('Grow(', 'Bud(') + '\n// Widget.Grow is retired\npublic class Other { public int Grow() => 1; public string Note = "Widget.Grow"; }')
    result = run(repo, "Use `Widget.Grow`.")
    assert 'missing-symbol' in kinds(result)

def test_ghost_type_in_comments_and_raw_string_does_not_exist(repo):
    with (repo / "src/Widget.cs").open('a') as f:
        f.write('\n// public class Ghost { }\n/* public class Ghost { } */\npublic class Text { public string Value = """public class Ghost { }"""; }')
    assert 'missing-symbol' in kinds(run(repo, 'Use `Ghost`.'))

@pytest.mark.parametrize('text', ['See `src/Gone.cs`.', 'See [source](src/Gone.cs).', 'Source: src/Gone.cs', 'See `tools/gone.py`.', 'See [page](docs/gone.md).'])
def test_planted_missing_file_is_found(repo, text):
    result = run(repo, text)
    assert kinds(result) & {'missing-path', 'missing-link'}

def test_links_are_relative_to_page(repo):
    (repo / 'docs/page.md').write_text('[source](../src/Widget.cs)')
    result = audit(repo, ['docs/page.md'])
    assert result['findings'] == []

def test_shorthand_citation_uses_same_paragraph_file(repo):
    result = run(repo, '`Widget.Grow`/`Widget.Title`: `src/Widget.cs:4`/`:5`.', near=0)
    assert result['findings'] == []
    assert sum(x['kind'] == 'anchor' for x in result['checks']) == 2

def test_shorthand_without_path_is_review_not_a_silent_pass(repo):
    result = run(repo, 'See `:4`.')
    assert result['counts']['review'] == 1

def test_named_anchor_after_citation_is_supported(repo):
    assert run(repo, '`src/Widget.cs:4` defines `Widget.Grow`.', near=0)['findings'] == []

def test_unanchored_cs_citation_requires_review(repo):
    assert 'unanchored-citation' in kinds(run(repo, 'The implementation is at `src/Widget.cs:4`.'))

def test_line_tolerance_is_explicit(repo):
    assert 'stale-anchor' not in kinds(run(repo, '`Widget.Grow`: `src/Widget.cs:5`.', near=1))
    assert 'stale-anchor' in kinds(run(repo, '`Widget.Grow`: `src/Widget.cs:5`.', near=0))

def test_framework_symbols_are_disclosed_not_claimed_as_repo_declarations(repo):
    result = run(repo, 'Returns `Task`.')
    assert result['findings'] == []
    assert result['notes'][0]['kind'] == 'framework-symbol'

def test_record_properties_interface_members_and_enum_members(repo):
    (repo / 'src/Models.cs').write_text('public record Choice(string Id, int Count = 1);\npublic interface IShape { int Size { get; } void Draw(); }\npublic enum Mode { Ready, Done }')
    result = run(repo, '`Choice.Count`, `IShape.Size`, `IShape.Draw`, `Mode.Done`.')
    assert result['findings'] == []

def test_qualified_type_and_member_match_namespace(repo):
    assert run(repo, '`Demo.Widget` and `Demo.Widget.Grow`.')['findings'] == []
    assert 'missing-symbol' in kinds(run(repo, '`Wrong.Widget.Grow`.'))

def test_doc_example_definitions_are_indexed_without_writing_a_source_file(repo):
    result = run(repo, 'Use `Example` and `Example.Go`.\n\n```csharp\npublic class Example { public void Go() { } }\n```\n')
    assert result['findings'] == []
    assert not (repo / 'Example.cs').exists()

def test_ambiguous_bare_filename_is_reported(repo):
    (repo / 'src/a').mkdir(); (repo / 'src/b').mkdir()
    for name in ['a', 'b']:
        (repo / f'src/{name}/Same.cs').write_text('class Any { }')
    assert 'ambiguous-path' in kinds(run(repo, 'See `Same.cs`.'))

def test_commands_are_never_executed_and_audit_is_read_only(repo, monkeypatch):
    def forbidden(*args, **kwargs):
        raise AssertionError('a docs audit tried to execute a command')
    monkeypatch.setattr(subprocess, 'run', forbidden)
    monkeypatch.setattr(subprocess, 'Popen', forbidden)
    (repo / 'tools/writes.py').write_text('raise AssertionError("must not run")')
    text = '```powershell\npython tools/writes.py\nNew-Item -ItemType Directory -Force should-not-exist\n```\n'
    (repo / 'README.md').write_text(text)
    before = {p.relative_to(repo).as_posix(): p.read_bytes() for p in repo.rglob('*') if p.is_file()}
    result = audit(repo, ['README.md'], find_program=lambda name: '/installed/' + name)
    after = {p.relative_to(repo).as_posix(): p.read_bytes() for p in repo.rglob('*') if p.is_file()}
    assert result['findings'] == [] and result['commands_executed'] == 0
    assert before == after and not (repo / 'should-not-exist').exists()

def test_command_missing_script_and_program_are_drift(repo):
    (repo / 'README.md').write_text('```powershell\npython tools/missing.py\n```\n')
    result = audit(repo, ['README.md'], find_program=lambda name: None)
    assert {'missing-program', 'missing-command-input'} <= kinds(result)

def test_missing_dotnet_project_is_drift(repo):
    result = run(repo, '```powershell\ndotnet run --project src/Missing.csproj -- --shot out.png\n```\n')
    assert 'missing-command-input' in kinds(result)
    assert not any('out.png' in x['message'] for x in result['findings'])

def test_declared_files_and_predicted_adapter_output_do_not_require_running_build(repo):
    (repo / 'src/Host.csproj').write_text('<Project />')
    text = '''<!-- file:.demo/Adapter.csproj -->
```xml
<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
```
<!-- file:.demo/data/items.json -->
```json
{"items":[]}
```
```powershell
New-Item -ItemType Directory -Force .demo/schemas
dotnet build .demo/Adapter.csproj
dotnet run --project src/Host.csproj -- --adapter .demo/bin/Debug/net10.0/Adapter.dll --data .demo/data --schemas .demo/schemas --shot .demo/new.png --settings .demo/settings
```
'''
    result = run(repo, text)
    assert result['findings'] == []
    assert not (repo / '.demo').exists()

def test_unknown_target_framework_is_review_not_assumed(repo):
    result = run(repo, '<!-- file:.demo/Adapter.csproj -->\n```xml\n<Project />\n```\n```powershell\ndotnet build .demo/Adapter.csproj\n```\n')
    assert 'unknown-build-output' in kinds(result)

def test_dynamic_command_is_not_silently_verified(repo):
    assert 'dynamic-command' in kinds(run(repo, '```powershell\n$x = "elsewhere"\n```\n'))

def test_external_links_are_not_fetched(repo):
    result = run(repo, '[manual](https://example.invalid/does-not-exist)')
    assert result['findings'] == [] and result['notes'][0]['kind'] == 'external-or-fragment-link'

def test_missing_page_and_invalid_near_are_honest(repo):
    assert audit(repo, ['absent.md'])['findings'][0]['kind'] == 'missing-page'
    with pytest.raises(ValueError):
        audit(repo, [], near=-1)

def test_cli_json_exit_and_invalid_option(repo, capsys):
    (repo / 'README.md').write_text('See `src/Missing.cs`.')
    assert main(['--root', str(repo), '--json', 'README.md']) == 1
    assert json.loads(capsys.readouterr().out)['counts']['drift'] == 1
    with pytest.raises(SystemExit) as error:
        main(['--near', '-1'])
    assert error.value.code == 2

def test_inline_command_is_inspected_not_misread_as_one_path(repo):
    (repo / 'Demo.slnx').write_text('<Solution />')
    assert run(repo, 'Run `dotnet test Demo.slnx`.')['findings'] == []
    assert 'missing-command-input' in kinds(run(repo, 'Run `dotnet test Missing.slnx`.'))

def test_prose_slash_list_is_not_a_proven_missing_path(repo):
    result = run(repo, 'Write original fixtures/prose/art.')
    assert not result['findings']
    assert result['notes'][0]['kind'] == 'unquoted-prose-pathlike'

def test_plain_table_directory_citation_is_checked(repo):
    assert 'missing-path' in kinds(run(repo, '| Path | Role |\n|---|---|\n| src/Missing | a layer |'))

def test_declaration_anchor_does_not_accept_an_unrelated_call(repo):
    (repo / 'src/Widget.cs').write_text(CS + '\npublic class Other\n{\n    public int Call(Widget w) => w.Grow(1);\n}\n')
    result = run(repo, '`Widget.Grow` is defined at `src/Widget.cs:10`.', near=0)
    assert 'stale-anchor' in kinds(result)

def test_remote_mention_does_not_become_false_stale_anchor(repo):
    text = '`Widget.Grow` ' + 'context ' * 60 + 'is discussed elsewhere. See `src/Widget.cs:5`.'
    result = run(repo, text, near=0)
    assert 'stale-anchor' not in kinds(result)
    assert 'unanchored-citation' in kinds(result)

def test_member_before_next_citation_is_not_stolen_by_previous_shorthand(repo):
    result = run(repo, '`Widget.Grow`: `src/Widget.cs:4`/`:5`; `Widget.Title`: `src/Widget.cs:5`.', near=0)
    assert 'stale-anchor' not in kinds(result)
    assert sum(x['kind'] == 'anchor' for x in result['checks']) == 2

def test_record_property_line_anchor_uses_its_declaration(repo):
    (repo / 'src/Models.cs').write_text('public record Choice(string Id, int Count = 1);')
    assert run(repo, '`Choice.Count`: `src/Models.cs:1`.', near=0)['findings'] == []

def test_declared_example_citation_is_not_silently_skipped(repo):
    page = '<!-- file:.demo/Example.cs -->\n```csharp\npublic class Example { }\n```\nSee `.demo/Example.cs:99`.'
    assert 'stale-line' in kinds(run(repo, page))

def test_shorthand_context_can_continue_in_section_but_resets_at_heading(repo):
    text = '## First\n\n`Widget.Grow`: `src/Widget.cs:4`.\n\nSee `:5`.\n\n## Second\n\nSee `:4`.'
    result = run(repo, text, near=0)
    assert sum(x['kind'] == 'unresolved-shorthand' for x in result['findings']) == 1
    assert any(x['kind'] == 'unanchored-citation' for x in result['findings'])

def test_unknown_framework_member_needs_review(repo):
    assert 'framework-member' in kinds(run(repo, 'Use `Task.DoesNotExist`.'))

def test_unmodeled_new_item_is_review_not_a_false_pass(repo):
    assert 'unsupported-new-item' in kinds(run(repo, '```powershell\nNew-Item -ItemType File new.txt\n```\n'))

def test_bare_prose_filename_is_not_silently_ignored(repo):
    assert 'missing-path' in kinds(run(repo, 'The results are in REPORT.md.'))

def test_prose_basename_can_name_an_explicitly_declared_walkthrough_file(repo):
    text = '<!-- file:.demo/data/items.json -->\n```json\n{}\n```\nThe items.json file is for this project.'
    assert run(repo, text)['findings'] == []

def test_fenced_string_filenames_are_not_external_citations(repo):
    assert run(repo, '```csharp\nvar name = "not-a-citation.json";\n```\n')['findings'] == []


def test_usage_citation_may_name_an_actual_call_site(repo):
    (repo / 'src/Widget.cs').write_text(CS + '\npublic class Other\n{\n    public int Call(Widget w) => w.Grow(1);\n}\n')
    result = run(repo, 'Calls `Widget.Grow` at `src/Widget.cs:10`.', near=0)
    assert result['findings'] == []

def test_plain_line_citation_is_checked_not_silently_skipped(repo):
    result = run(repo, '`Widget.Grow` is at src/Widget.cs:99.', near=0)
    assert 'stale-line' in kinds(result)

def test_plain_dot_directory_missing_file_is_found(repo):
    assert 'missing-path' in kinds(run(repo, 'Read .demo/data/missing.json.'))

def test_plain_filename_with_csv_extension_is_checked(repo):
    assert 'missing-path' in kinds(run(repo, 'See `rows.csv`.'))


def test_new_prose_clause_uses_nearest_symbol_not_old_unrelated_mention(repo):
    result = run(repo, '`Widget.Grow` finishes first. The property `Widget.Title` is at `src/Widget.cs:5`.', near=0)
    assert result['findings'] == []


def test_delegate_type_and_its_line_are_real_source_declarations(repo):
    (repo / 'src/Factory.cs').write_text('public delegate Widget? Factory(int count);')
    assert run(repo, '`Factory` is defined at `src/Factory.cs:1`.', near=0)['findings'] == []


def test_page_relative_link_cannot_be_rescued_by_unrelated_repo_root_file(repo):
    (repo / 'docs/page.md').write_text('[source](src/Widget.cs)')
    result = audit(repo, ['docs/page.md'])
    assert 'missing-link' in kinds(result)


@pytest.mark.parametrize('command', ['dotnet run --project', 'dotnet run --project src/Host.csproj -- --adapter'])
def test_required_command_input_option_needs_a_value(repo, command):
    (repo / 'src/Host.csproj').write_text('<Project />')
    assert 'missing-option-value' in kinds(run(repo, '```powershell\n' + command + '\n```\n'))
