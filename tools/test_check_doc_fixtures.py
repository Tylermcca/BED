import json

import check_doc
import check_fixtures


def test_doc_gate_finds_missing_sections_mentions_and_sources():
    text = "## Findings\nBrutal.Core and https://example.org/a\n"
    assert check_doc.problems(text, ["## Findings"], ["Brutal.Core"], 1) == []
    p = check_doc.problems(text, ["## Gaps"], ["Brutal.Jolt"], 3)
    assert len(p) == 3


def test_fixture_gate(tmp_path):
    d = tmp_path / "fixtures" / "import"
    d.mkdir(parents=True)
    assert check_fixtures.problems(tmp_path)[0].startswith("no ")
    (d / "make_fixtures.py").write_text("# generator", encoding="utf-8")
    (d / "cube.glb").write_bytes(b"glTF")
    cases = [{"id": c, "file": "cube.glb" if c == "IM-01" else {"generated_by": "make_fixtures.py:case"},
              "outcome": "imports" if c == "IM-01" else "refused", "diagnostics": [] if c == "IM-01" else ["why"],
              "checks": {"triangles": 12}, "why": "..."} for c in check_fixtures.CASES]
    (d / "expected.json").write_text(json.dumps({"kind": "import-fixtures", "version": 1, "cases": cases}), encoding="utf-8")
    assert check_fixtures.problems(tmp_path) == []
    cases[3]["diagnostics"] = []
    (d / "expected.json").write_text(json.dumps({"kind": "import-fixtures", "version": 1, "cases": cases[:-1]}), encoding="utf-8")
    p = check_fixtures.problems(tmp_path)
    assert any("IM-11" in x for x in p) and any("cases[3]" in x for x in p)
