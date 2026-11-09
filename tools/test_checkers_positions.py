"""INVENTED tests of the fixture checker, not a draughts move generator."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

import pytest

ROOT = Path(__file__).resolve().parents[1]
CHECKER = ROOT / "fixtures" / "checkers" / "check_positions.py"
POSITIONS = CHECKER.with_name("positions.json")
spec = importlib.util.spec_from_file_location("a018_check_positions", CHECKER)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


def data():
    return checker.read(POSITIONS)


def at(document, identity):
    return next(p for p in document["positions"] if p["id"] == identity)


def change_cell(board, square, cell):
    row, col = checker.coordinate(square)
    line = list(board[row])
    line[col] = cell
    board[row] = "".join(line)


def broken(tmp_path, modify):
    document = data()
    modify(document)
    p = tmp_path / "broken.json"
    p.write_text(json.dumps(document, ensure_ascii=False), encoding="utf-8")
    return p, checker.problems(checker.read(p))


def test_real_file_has_at_least_forty_independent_positions():
    document = data()
    assert len(document["positions"]) >= 40
    assert checker.problems(document) == []
    assert document["source"] == "https://wcdf.net/rules/rules_of_checkers_english.pdf"


def test_real_file_cli_succeeds_without_game_code():
    result = subprocess.run([sys.executable, str(CHECKER), str(POSITIONS)], capture_output=True, text=True, timeout=10)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "OK: 44 positions" in result.stdout
    assert "hand-derived" in result.stdout


@pytest.mark.parametrize("square", ["i4", "a0", "a9", "A3", "c33", 23])
def test_bad_square_in_temporary_copy_is_refused(tmp_path, square):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps")["legal"][0].__setitem__(1, square))
    assert any("invalid square" in e for e in errors)


def test_piece_on_light_square_in_temporary_copy_is_refused(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "dark_centre_steps")["board"], "a8", "d"))
    assert any("light square" in e for e in errors)


def test_bad_character_in_temporary_copy_is_refused(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "dark_centre_steps")["board"], "b8", "?"))
    assert any("bad character" in e for e in errors)


@pytest.mark.parametrize("board", [[], ["........"] * 7, ["........"] * 9, ["......."] * 8, [None] * 8])
def test_bad_board_dimensions_are_listed(tmp_path, board):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps").__setitem__("board", board))
    assert any("eight" in e for e in errors)


def test_playable_empty_and_unplayable_markers_are_distinct(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "dark_centre_steps")["board"], "a1", "-"))
    assert any("dark square" in e for e in errors)


def test_light_square_is_not_a_landing(tmp_path):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps")["legal"][0].__setitem__(1, "b3"))
    assert any("light square" in e for e in errors)


def test_wrong_side_cannot_supply_a_path(tmp_path):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps").__setitem__("to_move", "light"))
    assert any("no light piece" in e for e in errors)


def test_jump_requires_an_opponent_at_midpoint(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "forced_capture_single")["board"], "d4", "."))
    assert any("midpoint has no opponent" in e for e in errors)


def test_occupied_landing_is_reported(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "forced_capture_single")["board"], "e5", "d"))
    assert any("landing" in e and "occupied" in e for e in errors)


def test_same_victim_twice_is_reported_without_search(tmp_path):
    def mutate(d):
        p = at(d, "king_loop_no_piece_twice")
        p["legal"] = [["c3", "e5", "c3"]]
        p.pop("after")
    _, errors = broken(tmp_path, mutate)
    assert any("same piece twice" in e for e in errors)


@pytest.mark.parametrize("move", [["c3"], [], "c3-b4", ["c3", "f6"], ["c3", "b4", "a5"]])
def test_bad_supplied_path_chain_is_reported(tmp_path, move):
    def mutate(d):
        p = at(d, "dark_centre_steps")
        p["legal"] = [move]
        p.pop("after")
    _, errors = broken(tmp_path, mutate)
    assert errors


def test_duplicate_moves_and_ids_are_reported(tmp_path):
    def mutate(d):
        p = at(d, "dark_centre_steps")
        p["legal"].append(p["legal"][0][:])
        d["positions"].append(copy.deepcopy(p))
    _, errors = broken(tmp_path, mutate)
    assert any("duplicate move" in e for e in errors)
    assert any("duplicate id" in e for e in errors)


def test_after_move_must_name_one_supplied_move(tmp_path):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps")["after"].__setitem__("move", ["c3", "e5"]))
    assert any("must name a listed legal move" in e for e in errors)


def test_after_turn_must_change(tmp_path):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps")["after"].__setitem__("to_move", "dark"))
    assert any("after.to_move" in e for e in errors)


def test_after_capture_must_remove_victim(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "forced_capture_single")["after"]["board"], "d4", "l"))
    assert any("inconsistent with" in e for e in errors)


def test_after_must_crown_man(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "dark_crown_quiet_edge")["after"]["board"], "b8", "d"))
    assert any("crown" in e for e in errors)


def test_after_crowning_capture_keeps_uncaptured_piece(tmp_path):
    _, errors = broken(tmp_path, lambda d: change_cell(at(d, "dark_capture_crown_stops")["after"]["board"], "e7", "."))
    assert any("inconsistent with" in e for e in errors)


def test_continuing_a_newly_crowned_man_is_reported(tmp_path):
    def mutate(d):
        p = at(d, "dark_capture_crown_stops")
        p["legal"] = [["b6", "d8", "f6"]]
        p.pop("after")
    _, errors = broken(tmp_path, mutate)
    assert any("crowning ends" in e for e in errors)


def test_checker_does_not_generate_or_require_missing_legal_moves(tmp_path):
    # Deliberately remove the opening oracle. A structure checker cannot establish
    # completeness; this succeeds, and explains why the manual audit is essential.
    def mutate(d):
        p = at(d, "opening")
        p["legal"] = []
        p.pop("after")
    _, errors = broken(tmp_path, mutate)
    assert errors == []


def test_checker_does_not_search_for_capture_continuations(tmp_path):
    def mutate(d):
        p = at(d, "two_jumps_same_direction")
        p["legal"] = [["a1", "c3"]]
        p.pop("after")
    _, errors = broken(tmp_path, mutate)
    assert errors == []  # Valid supplied geometry; incomplete legal oracle needs human review.


@pytest.mark.parametrize("field,value", [("kind", "other"), ("version", True), ("rules", "international"), ("source", ""), ("positions", {})])
def test_header_shape_is_checked(tmp_path, field, value):
    _, errors = broken(tmp_path, lambda d: d.__setitem__(field, value))
    assert any(f"$.{field}" in e for e in errors)


def test_cli_lists_multiple_problems_and_exits_one(tmp_path):
    def mutate(d):
        change_cell(at(d, "dark_centre_steps")["board"], "a8", "d")
        change_cell(at(d, "light_centre_steps")["board"], "b8", "?")
    path, _ = broken(tmp_path, mutate)
    result = subprocess.run([sys.executable, str(CHECKER), str(path)], capture_output=True, text=True, timeout=10)
    assert result.returncode == 1
    assert "light square" in result.stdout and "bad character" in result.stdout


@pytest.mark.parametrize("text", ["{", '{"kind":"one","kind":"two"}', '{"value":NaN}'])
def test_malformed_or_ambiguous_json_is_a_clean_failure(tmp_path, capsys, text):
    path = tmp_path / "bad.json"
    path.write_text(text, encoding="utf-8")
    assert checker.main([str(path)]) == 1
    assert "ERROR" in capsys.readouterr().out


def test_missing_file_and_bad_usage_are_clean_failures(tmp_path, capsys):
    assert checker.main([str(tmp_path / "absent.json")]) == 1
    assert checker.main([]) == 1
    assert capsys.readouterr().out.count("ERROR") == 2

@pytest.mark.parametrize("after", [None, [], {}, {"move": None, "board": None, "to_move": []}])
def test_malformed_after_objects_report_errors_without_crashing(tmp_path, after):
    _, errors = broken(tmp_path, lambda d: at(d, "dark_centre_steps").__setitem__("after", after))
    assert errors


def test_missing_after_move_with_malformed_legal_entry_is_a_clean_error(tmp_path):
    def mutate(d):
        p = at(d, "dark_centre_steps")
        p["legal"] = [None]
        p["after"].pop("move")
    _, errors = broken(tmp_path, mutate)
    assert any("after.move" in e for e in errors)
