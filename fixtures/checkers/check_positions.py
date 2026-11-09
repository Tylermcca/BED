"""Validate hand-authored English-draughts fixtures; never generate legal moves.

Checks shape, square parity, supplied path geometry/occupancy and named after boards.
Does NOT search for captures, extend paths, enforce longest paths, decide losses,
or establish completeness of the expected legal set. Those oracles are by hand.
INVENTED validator, using only Python's standard library.
"""
import json
import re
import sys
from pathlib import Path

SIDES = {"dark": "dD", "light": "lL"}
SQUARE = re.compile(r"^[a-h][1-8]$")


def coordinate(name):
    """Return row/column in the stored top-to-bottom board, or None."""
    if not isinstance(name, str) or not SQUARE.fullmatch(name):
        return None
    return 8 - int(name[1]), ord(name[0]) - ord("a")


def dark_square(row, col):
    """a1 is dark; top-left a8 is light."""
    return (row + col) % 2 == 1


def board_problems(board, path):
    out = []
    if not isinstance(board, list) or len(board) != 8:
        return [f"{path}: expected eight row strings"]
    for row, text in enumerate(board):
        if not isinstance(text, str) or len(text) != 8:
            out.append(f"{path}[{row}]: expected eight characters")
            continue
        for col, cell in enumerate(text):
            where = f"{path}[{row}][{col}]"
            if cell not in ".-dlDL":
                out.append(f"{where}: bad character {cell!r}")
            elif dark_square(row, col):
                if cell == "-":
                    out.append(f"{where}: dark square must be '.' or a piece")
                elif (cell == "d" and row == 0) or (cell == "l" and row == 7):
                    out.append(f"{where}: man on its crown row must be a king")
            elif cell != "-":
                out.append(f"{where}: light square must be '-' (no piece or playable empty)")
    return out


def supplied_path(board, side, move, path):
    """Check only this supplied chain; return its resulting board and problems.

    No neighbour enumeration and no recursive search. Captured squares stay occupied
    during a chain and are removed at its end; a victim cannot be jumped twice.
    """
    out = []
    if not isinstance(move, list) or len(move) < 2:
        return None, [f"{path}: expected start and at least one landing"]
    coords = []
    for i, square in enumerate(move):
        xy = coordinate(square)
        if xy is None:
            out.append(f"{path}[{i}]: invalid square {square!r}")
        elif not dark_square(*xy):
            out.append(f"{path}[{i}]: {square} is a light square")
        coords.append(xy)
    if out:
        return None, out
    work = [list(row) for row in board]
    row, col = coords[0]
    piece = work[row][col]
    if piece not in SIDES[side]:
        return None, [f"{path}[0]: no {side} piece at {move[0]}"]
    captured = set()
    mode = None
    for i, (nr, nc) in enumerate(coords[1:], 1):
        dr, dc = nr - row, nc - col
        step = abs(dr)
        if step not in (1, 2) or abs(dc) != step:
            out.append(f"{path}[{i}]: segment must be one step or one jump diagonally")
            break
        if mode is None:
            mode = step
        if mode != step or (step == 1 and len(move) != 2):
            out.append(f"{path}[{i}]: cannot mix steps/jumps or chain quiet steps")
            break
        if piece.islower() and ((piece == "d" and dr >= 0) or (piece == "l" and dr <= 0)):
            out.append(f"{path}[{i}]: a man cannot travel backwards")
            break
        if work[nr][nc] != ".":
            out.append(f"{path}[{i}]: landing {move[i]} is occupied")
            break
        if step == 2:
            middle = ((row + nr) // 2, (col + nc) // 2)
            victim = work[middle[0]][middle[1]]
            if middle in captured:
                out.append(f"{path}[{i}]: cannot jump the same piece twice")
                break
            if victim not in SIDES["light" if side == "dark" else "dark"]:
                out.append(f"{path}[{i}]: jump midpoint has no opponent")
                break
            captured.add(middle)
        work[row][col] = "."
        if piece.islower() and ((piece == "d" and nr == 0) or (piece == "l" and nr == 7)):
            piece = piece.upper()
            if i != len(coords) - 1:
                out.append(f"{path}[{i}]: crowning ends this man's move")
        work[nr][nc] = piece
        row, col = nr, nc
    if out:
        return None, out
    for cr, cc in captured:
        work[cr][cc] = "."
    return ["".join(row) for row in work], []


def fields(value, required, optional, path):
    if not isinstance(value, dict):
        return [f"{path}: expected an object"]
    return ([f"{path}.{k}: missing field" for k in sorted(required - value.keys())]
            + [f"{path}.{k}: unknown field" for k in sorted(value.keys() - required - optional)])


def problems(data):
    """Return all independent format/consistency problems in a parsed fixture."""
    out = fields(data, {"kind", "version", "rules", "source", "positions"}, set(), "$")
    if not isinstance(data, dict):
        return out
    if data.get("kind") != "checkers-positions":
        out.append("$.kind: expected checkers-positions")
    if type(data.get("version")) is not int or data.get("version") != 1:
        out.append("$.version: expected integer 1")
    if data.get("rules") != "english-draughts":
        out.append("$.rules: expected english-draughts")
    if not isinstance(data.get("source"), str) or not data["source"].startswith(("https://", "http://")):
        out.append("$.source: expected rules URL")
    positions = data.get("positions")
    if not isinstance(positions, list) or not positions:
        return out + ["$.positions: expected a nonempty list"]
    ids = set()
    for index, pos in enumerate(positions):
        path = f"$.positions[{index}]"
        out += fields(pos, {"id", "why", "to_move", "board", "legal"}, {"after"}, path)
        if not isinstance(pos, dict):
            continue
        identity = pos.get("id")
        if not isinstance(identity, str) or not identity.strip():
            out.append(f"{path}.id: expected a nonempty string")
        elif identity in ids:
            out.append(f"{path}.id: duplicate id {identity!r}")
        else:
            ids.add(identity)
        if not isinstance(pos.get("why"), str) or not pos["why"].strip():
            out.append(f"{path}.why: expected rule citation/reason")
        side = pos.get("to_move")
        good_side = isinstance(side, str) and side in SIDES
        if not good_side:
            out.append(f"{path}.to_move: expected dark or light")
        board = pos.get("board")
        bad_board = board_problems(board, path + ".board")
        out += bad_board
        legal = pos.get("legal")
        if not isinstance(legal, list):
            out.append(f"{path}.legal: expected a list (empty is allowed)")
            legal = []
        seen = set()
        results = []
        for number, move in enumerate(legal):
            where = f"{path}.legal[{number}]"
            if isinstance(move, list) and all(isinstance(s, str) for s in move):
                key = tuple(move)
                if key in seen:
                    out.append(f"{where}: duplicate move")
                seen.add(key)
            if not bad_board and good_side:
                result, errors = supplied_path(board, side, move, where)
                out += errors
                results.append(result)
            else:
                # Even a malformed board does not hide independently bad names.
                if not isinstance(move, list) or len(move) < 2:
                    out.append(f"{where}: expected start and landing")
                elif any(coordinate(s) is None for s in move):
                    out.append(f"{where}: invalid square name")
                results.append(None)
        if "after" not in pos:
            continue
        after = pos["after"]
        out += fields(after, {"move", "board", "to_move"}, set(), path + ".after")
        if not isinstance(after, dict):
            continue
        out += board_problems(after.get("board"), path + ".after.board")
        expected_side = "light" if side == "dark" else "dark"
        if good_side and after.get("to_move") != expected_side:
            out.append(f"{path}.after.to_move: expected {expected_side}")
        after_move = after.get("move")
        if not isinstance(after_move, list) or len(after_move) < 2 or after_move not in legal:
            out.append(f"{path}.after.move: must name a listed legal move")
        else:
            result = results[legal.index(after_move)]
            if result is not None and result != after.get("board"):
                out.append(f"{path}.after.board: inconsistent with the named move, captures or crowning")
    return out


def unique_object(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise ValueError(f"duplicate JSON key {key!r}")
        obj[key] = value
    return obj


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"),
                      object_pairs_hook=unique_object,
                      parse_constant=lambda token: (_ for _ in ()).throw(ValueError(f"non-JSON number {token}")))


def main(args=None):
    args = sys.argv[1:] if args is None else args
    if len(args) != 1:
        print("ERROR: usage: check_positions.py <positions.json>")
        return 1
    try:
        data = read(args[0])
        errors = problems(data)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"ERROR: {args[0]}: {error}")
        return 1
    for error in errors:
        print("ERROR:", error)
    if errors:
        return 1
    print(f"OK: {len(data['positions'])} positions; "
          f"{sum(len(p['legal']) for p in data['positions'])} supplied moves structurally consistent "
          "(legal-set completeness is hand-derived)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
