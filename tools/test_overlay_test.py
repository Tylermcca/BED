from pathlib import Path

from overlay_test import MERGED, merge


def test_a_stale_copy_adds_its_lines_and_erases_nothing(tmp_path: Path):
    repo = tmp_path / "repo.slnx"
    repo.write_text('<Solution>\n  <Project Path="src/A/A.csproj" />\n  <Project Path="src/New/New.csproj" />\n</Solution>\n', encoding="utf-8")
    card = tmp_path / "card.slnx"
    card.write_text('<Solution>\n  <Project Path="src/A/A.csproj" />\n  <Project Path="samples/Mine/Mine.csproj" />\n</Solution>\n', encoding="utf-8")
    assert merge(card, repo, MERGED["Brutal.Authoring.slnx"]) == ['<Project Path="samples/Mine/Mine.csproj" />']
    text = repo.read_text(encoding="utf-8")
    assert "src/New/New.csproj" in text and "samples/Mine/Mine.csproj" in text
    assert merge(card, repo, MERGED["Brutal.Authoring.slnx"]) == []   # merging twice adds nothing


def test_slashes_dont_make_a_reference_look_new(tmp_path: Path):
    repo = tmp_path / "t.csproj"
    repo.write_text('<ItemGroup>\n    <ProjectReference Include="..\..\src\A\A.csproj" />\n</ItemGroup>\n', encoding="utf-8")
    card = tmp_path / "c.csproj"
    card.write_text('<ItemGroup>\n    <ProjectReference Include="../../src/A/A.csproj" />\n</ItemGroup>\n', encoding="utf-8")
    assert merge(card, repo, MERGED["Brutal.Authoring.Tests.csproj"]) == []


def test_a_gate_run_keeps_the_evidence_a_card_left_in_its_gate_folder():
    # Read, not run: a real run builds the whole repository. A review run erased A088's raw measurements this way.
    source = (Path(__file__).parent / "overlay_test.py").read_text(encoding="utf-8")
    assert "shutil.rmtree(gate" not in source
    assert "shutil.rmtree(repo" in source and "shutil.rmtree(tmp" in source
