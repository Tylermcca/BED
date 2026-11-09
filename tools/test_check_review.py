import csv

import pytest

from check_review import SHEETS, problems

IDS = ["AT-001", "MF-002", "OSS-001"]
GOOD = "\n".join([f"- D{d}: AGREE, because." for d in range(1, 11)] +
                 ["- AT-001: OK, checked.", "- MF-002: FIX, row 6 is the overview.", "- OSS-001: CORRECT, no LICENSE yet.",
                  "## Access", "## Isolation", "## Community release", "## On board"])


def test_complete_review_passes():
    assert problems(GOOD, IDS) == []


def test_missing_stance_row_and_section_fail():
    bad = GOOD.replace("D3: AGREE", "D3: maybe").replace("- MF-002: FIX, row 6 is the overview.", "").replace("## On board", "")
    p = problems(bad, IDS)
    assert any(x.startswith("D3") for x in p)
    assert any(x.startswith("MF-002") for x in p)
    assert any("On board" in x for x in p)


def test_d10_is_required_and_d1_does_not_stand_in_for_it():
    p = problems(GOOD.replace("- D10: AGREE, because.", ""), IDS)
    assert p == ["D10: no line for this decision"]


def test_community_release_section_required():
    assert any("Community release" in x for x in problems(GOOD.replace("## Community release", ""), IDS))


@pytest.mark.skipif(not all(s.exists() for s in SHEETS), reason="the worksheets are private planning notes, not in the public repo")
def test_both_worksheets_are_read_and_ids_are_unique():
    ids = [r["ID"] for s in SHEETS for r in csv.DictReader(s.open(encoding="utf-8"))]
    assert len(ids) == len(set(ids)) == 52
    assert {"OSS-001", "OSS-008", "AT-001", "LIC-004"} <= set(ids)


def test_other_cards_name_their_own_decisions_and_sections():
    text = "- D11: AGREE.\n- D12: AMEND, see below.\n- FUT-001: OK.\n## Goals\n## Research"
    assert problems(text, ["FUT-001"], ["D11", "D12"], ["## Goals", "## Research"]) == []
    assert problems(text, ["FUT-001", "FUT-002"], ["D11", "D12", "D13"], ["## Goals"]) == ["D13: no line for this decision", "FUT-002: row not answered"]
