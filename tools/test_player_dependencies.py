from pathlib import Path
import check_player_dependencies as guard


def project(root, name, content=""):
    path = root / name / (name + ".csproj")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("<Project>" + content + "</Project>")
    return str(path.relative_to(root))


def test_bundled_players_have_no_editor_dependencies():
    assert guard.check() == []


def test_transitive_editor_reference_names_the_whole_chain(tmp_path):
    project(tmp_path, "Bed.Editor.App")
    project(tmp_path, "Rules", '<ItemGroup><ProjectReference Include="../Bed.Editor.App/Bed.Editor.App.csproj" /></ItemGroup>')
    player = project(tmp_path, "Player", '<ItemGroup><ProjectReference Include="../Rules/Rules.csproj" /></ItemGroup>')
    assert guard.check(tmp_path, [player]) == [player + " -> Rules.csproj -> Bed.Editor.App.csproj"]


def test_runtime_only_chain_is_allowed_and_cycles_terminate(tmp_path):
    project(tmp_path, "Bed.Runtime", '<ItemGroup><ProjectReference Include="../Player/Player.csproj" /></ItemGroup>')
    player = project(tmp_path, "Player", '<ItemGroup><ProjectReference Include="../Bed.Runtime/Bed.Runtime.csproj" /></ItemGroup>')
    assert guard.check(tmp_path, [player]) == []


def test_conditional_and_package_editor_references_cannot_hide(tmp_path):
    project(tmp_path, "Bed.Editor")
    player = project(tmp_path, "Player", '<ItemGroup Condition="false"><ProjectReference Include="../Bed.Editor/Bed.Editor.csproj" /><PackageReference Include="Bed.Editor.ImGui" /></ItemGroup>')
    assert len(guard.check(tmp_path, [player])) == 2


def test_release_refuses_before_any_build_when_a_player_needs_the_editor(tmp_path, monkeypatch):
    import package_release
    import pytest
    monkeypatch.setattr(guard, "check", lambda root: ["Player -> Bed.Editor.App"])
    def unexpected(*args, **kwargs):
        raise AssertionError("No build may start before the player dependency check passes")
    monkeypatch.setattr(package_release, "run", unexpected)
    with pytest.raises(RuntimeError, match="Player -> Bed.Editor.App"):
        package_release.build(tmp_path / "stage", "win-x64")
    assert not (tmp_path / "stage").exists()
