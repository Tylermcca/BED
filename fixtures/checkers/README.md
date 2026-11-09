# Hand-derived English draughts rule positions

44 positions, 71 complete expected move paths, and 40 manually specified after-boards. **INVENTED:** every position, explanation and board in this set is self-authored. **BORROWED:** the English draughts rules below are from the World Checkers/Draughts Federation (WCDF). No game code, existing game tests, move generator, engine, art or workbook supplied these expectations.

## Source and resolved rules

Primary source, read 2026-10-31: [WCDF Rules of Draughts, English](https://wcdf.net/rules/rules_of_checkers_english.pdf). References below use its printed section numbers, not PDF page numbers. The card explicitly intends this single real game's rules; no hybrid rule design is proposed.

- 1.8, 1.11, 1.13: twelve pieces per side in the starting rows; dark/red starts.
- 1.15–1.18, 1.21: men step and capture forwards; kings step one diagonal square and capture in either direction.
- 1.19–1.20: captures are compulsory; complete the selected sequence; choosing the longest is unnecessary; a victim is jumped once.
- 1.16, 1.19: crowning ends the man's turn, including during a capture.
- 1.30: having no move loses, whether blocked or without pieces.

This is a move-position oracle, not a full tournament adjudication corpus. Draw history, clocks, conduct and balloted openings are outside the card.

## Format

The top object has kind checkers-positions, integer version 1, rules english-draughts, source URL and positions. Each position has a unique id, why with WCDF sections and original reasoning, to_move, board and legal. Optional after has move, board and to_move.

Eight strings are rows 8 down to 1; columns are a through h. a1 is dark and a8 is light. "-" is an unplayable light square; "." is an empty dark square; d/l are dark/light men and D/L their kings. Dark men increase rank; light men decrease it. Dark begins on ranks 1–3, light on 6–8. This algebraic notation is the card's encoding, not WCDF's official 1–32 score notation.

Every legal path lists its start and every landing. Paths form a set: order is immaterial; duplicates are rejected as a fixture error. Empty legal means the side to move has no move and loses. A single after demonstrates the first listed path; captured pieces are removed, an arriving man crowned and turn exchanged. The after board is explicitly authored rather than produced by a move generator.

## How the positions were worked out

**INVENTED method:** place the named pieces on an otherwise empty board, inspect each moving-side piece's diagonals, suppress quiet moves if any capture exists, and trace each capture branch to its end or a crowning stop. Check that no unlisted piece contributes another move. This was done by hand for every row below. The data-writing helper only expands explicit square placements into board strings; it does not discover paths or produce after-boards.

For capture chains, track the set of already jumped pieces. A king may return to a vacated starting square, but cannot take a previous victim again. The loop row makes this distinction explicit.

| Position | Exact expected paths | Hand derivation (WCDF sections are also in positions.json) |
|---|---|---|
| opening | a3-b4; c3-b4; c3-d4; e3-d4; e3-f4; g3-f4; g3-h4 | Only the four dark front men can move. The edge a3 has b4; each of c3,e3,g3 has two vacant neighbours. Seven paths; rear ranks are occupied. (§1.8,1.11,1.13,1.15) |
| dark_centre_steps | c3-b4; c3-d4 | c3's forward neighbours b4,d4 are empty; no adjacent light piece. (§1.15) |
| light_centre_steps | f6-e5; f6-g5 | f6's lower-rank neighbours e5,g5 are empty; a1 is remote. (§1.15) |
| dark_left_edge | a3-b4 | Only b4 stays on the board from a3. (§1.15) |
| dark_right_edge | h2-g3 | Only g3 stays on the board from h2. (§1.15) |
| light_left_edge | a7-b6 | Only b6 stays on the board in light's forward direction. (§1.15) |
| light_right_edge | h6-g5 | Only g5 stays on the board from h6. (§1.15) |
| one_piece_partly_blocked | c3-d4; b4-a5; b4-c5 | b4 blocks c3's left step but can itself step to a5,c5; c3 retains d4. No jumps. (§1.15) |
| occupied_capture_landing | c3-b4; e5-d6; e5-f6 | d4 cannot be jumped by c3 because e5 is occupied. e5 cannot capture backwards. Quiet destinations are b4,d6,f6. (§1.15,1.18) |
| forced_capture_single | c3-e5 | c3 jumps d4 to e5; a1's quiet b2 and c3's quiet b4 are excluded. e5 has no further target. (§1.18,1.20) |
| light_forced_capture | f6-d4 | f6 jumps e5 to d4. Light's other quiet possibilities are excluded. No next target at c3/e3. (§1.18,1.20) |
| choice_one_man | c3-a5; c3-e5 | Both b4/a5 and d4/e5 are available jump pairs. Either endpoint has no continuation. (§1.18,1.20) |
| choice_two_men | a3-c5; e3-g5 | a3 can take b4, e3 can take f4. Both captures terminate; no quiet move is listed. (§1.18,1.20) |
| short_capture_need_not_be_longest | c3-a5; c3-e5-g7 | The b4 branch ends at a5 after one victim. The d4 branch must continue over f6 to g7. Both full paths are choices. (§1.19,1.20) |
| two_jumps_same_direction | a1-c3-e5 | a1 crosses b2 to c3, then d4 to e5. Stopping at c3 omits a continuation; e5 has none. (§1.18,1.19) |
| two_jumps_zigzag | c1-e3-c5 | c1 crosses d2 to e3, then turns across d4 to c5. No other first or second branch. (§1.18,1.19) |
| branch_after_first_jump | a1-c3-a5; a1-c3-e5 | First landing c3 is forced; its b4 and d4 targets give two terminal branches a5,e5. (§1.19,1.20) |
| branch_with_unequal_lengths | a1-c3-a5; a1-c3-e5-g7 | At c3 the a5 branch stops. The e5 branch has an extra f6/g7 leg. Both sequences, not their prefixes, are present. (§1.19,1.20) |
| light_two_jumps | h8-f6-d4 | h8 takes g7 to f6, then e5 to d4. Rank decreases on each leg. (§1.18,1.19) |
| light_branch_after_first | h8-f6-h4; h8-f6-d4 | h8's first f6 landing branches over g5 to h4 or e5 to d4; both then end. (§1.19,1.20) |
| dark_crown_quiet_edge | a7-b8 | a7 has only b8, where its after-board contains D rather than d. (§1.15,1.16) |
| dark_crown_quiet_choice | c7-b8; c7-d8 | Both b8,d8 are vacant crown-row neighbours. Chosen b8 is crowned. (§1.15,1.16) |
| light_crown_quiet_edge | h2-g1 | h2 has only g1 in light's forward direction, producing L. (§1.15,1.16) |
| light_crown_quiet_choice | f2-e1; f2-g1 | e1 and g1 are both vacant; either crowns the light man. (§1.15,1.16) |
| dark_capture_crown_stops | b6-d8 | b6 takes c7 to d8. e7/f6 would be a backward king jump, but this newly crowned man's turn has ended. (§1.16,1.19) |
| light_capture_crown_stops | g3-e1 | g3 takes f2 to e1. d2/c3 remains available only on a future king turn; d2 survives the after-board. (§1.16,1.19) |
| dark_king_four_steps | d4-c3; d4-e3; d4-c5; d4-e5 | All four adjacent diagonals from d4 are vacant; no adjacent opponent. Longer slides are absent. (§1.17) |
| light_king_four_steps | e5-d4; e5-f4; e5-d6; e5-f6 | All four adjacent diagonals from e5 are vacant. King directions are independent of side. (§1.17) |
| dark_king_corner_step | a1-b2 | Only b2 is an on-board neighbour; c3 and longer slides are absent. (§1.17) |
| king_forward_capture | c3-e5 | d4 is the sole adjacent target and e5 is vacant. Quiet king steps are suppressed. (§1.20,1.21) |
| king_backward_capture | e5-c3 | The lower-rank d4/c3 pair supplies the only capture. The king may take backwards. (§1.20,1.21) |
| light_king_backward_capture | c3-e5 | For light, d4/e5 is backward, but L can jump it. No other victim exists. (§1.20,1.21) |
| king_changes_rank_direction | c3-e5-g3 | The king goes upward over d4 and downward over f4. Already jumped d4 cannot be taken on the return diagonal. (§1.19,1.21) |
| king_capture_fork | c3-a5; c3-e5 | Two adjacent upper targets give two terminal paths; the king's quiet lower steps are excluded. (§1.20,1.21) |
| king_loop_no_piece_twice | c3-e5-g3-e1-c3; c3-e1-g3-e5-c3 | The four-victim diamond can be traversed in either direction and returns to vacated c3. A second circuit would jump the same victims twice and is excluded. (§1.19,1.20,1.21) |
| dark_man_cannot_capture_backward | e5-d6; e5-f6 | d4 is behind the man; c3 is not a jump destination. Empty forward d6,f6 remain. (§1.15,1.18,1.25.4) |
| light_man_cannot_capture_backward | c3-b2; c3-d2 | d4 lies behind light's forward direction. Empty b2,d2 are its two moves. (§1.15,1.18,1.25.4) |
| blocked_dark_loses | [] — side to move loses | b8 occupies a7's only neighbour; c9 is off-board, so a7 has neither step nor jump. Dark has no move. (§1.18,1.30) |
| blocked_light_loses | [] — side to move loses | g1 occupies h2's only forward neighbour; f0 is off-board. Light has no move. (§1.18,1.30) |
| no_dark_pieces_loses | [] — side to move loses | There is no dark starting square, hence no dark move. (§1.30) |
| no_light_pieces_loses | [] — side to move loses | There is no light starting square, hence no light move. (§1.30) |
| blocked_king_other_man_moves | b2-a3; b2-c3 | a1's king is blocked by its own b2 man and cannot jump a friend. That man has a3,c3; the side is not immobile. (§1.15,1.17,1.18) |
| man_captures_king | c3-e5 | A king on d4 is still a capturable opponent for the man at c3. The capturing man stays d on e5. (§1.18,1.20) |
| light_king_corner_not_flying | h8-g7 | Only adjacent g7 is available; f6,e5 and further same-diagonal squares are not extra moves. (§1.17) |

## Consistency checker and its limits

**INVENTED structural validator:** check_positions.py uses only Python's standard library. It checks header/fields/types, duplicate JSON keys/ids/moves, exact 8×8 row sizes, characters/parity, coordinates, moving-side ownership, supplied step/jump geometry, occupancy, capture victims, single-use victims, directional men and a crowning stop. It checks that after names a listed path and that the board/turn agree with that path's explicit effects.

It never enumerates neighbours to find legal moves, searches branches, extends a path, selects a longest capture, checks global capture compulsion, decides a winner or verifies the expected set is complete. In particular, deleting expected paths or truncating a structurally valid capture prefix may still pass. The pytest suite deliberately proves those limits. The hand derivations and review, not the checker, supply the legal-set oracle.

From the repo root (the two exact card gates use absolute outbox paths before acceptance):

~~~text
python fixtures/checkers/check_positions.py fixtures/checkers/positions.json
python -m pytest -q tools/test_checkers_positions.py
~~~

Exit 0 means structurally consistent; exit 1 lists problems. Pytest checks the real file and malformed copies in temporary folders, including bad squares, light-square pieces, bad characters, path geometry, after/crowning/captures, duplicate fields, invalid JSON and clean CLI failures. It imports no C# or engine.

## Seeded self-check

Python random.Random(180031).sample(positions_in_file_order,20) selected these twenty, exceeding the card's eight-position minimum. I reopened the saved boards and legal sets, re-derived each by hand and checked its named after-board. No move generator was used.

| Selected position | Independent re-derivation |
|---|---|
| king_backward_capture | Only e5 is dark. Its adjacent d4 victim has vacant c3 beyond; no other adjacent opponent exists. Therefore {e5-c3}, then D on c3 and d4 removed. PASS. |
| light_crown_quiet_choice | Light f2 has two vacant forward neighbours e1,g1. Remote a7 cannot be captured. Exactly two quiet promotions; chosen e1 becomes L and the turn is dark. PASS. |
| dark_crown_quiet_edge | Dark a7 has no left-file neighbour; b8 is empty and h6 remote. Exactly a7-b8; crown D on b8. PASS. |
| dark_capture_crown_stops | From b6 only c7/d8 is a capture pair. The d8 landing crowns the man. Do not continue over e7 to f6; preserve e7 and end at D:d8. PASS. |
| light_king_backward_capture | L:c3 has adjacent dark d4 and vacant e5. The king can increase rank despite light's usual forward direction. Exactly c3-e5, removing d4. PASS. |
| blocked_light_loses | Light h2's only forward neighbour g1 is occupied and f0 is outside. There is no light king or other light piece. The legal set is empty, so light loses. PASS. |
| king_loop_no_piece_twice | At c3 the only opponents adjacent are d4 and d2. Taking d4 forces e5-g3-e1-c3 through f4,f2,d2; taking d2 forces e1-g3-e5-c3 through f2,f4,d4. Returning c3 is allowed, but either further leg would reuse a victim. Exactly the two listed four-jump loops; only D:c3 remains. PASS. |
| light_king_corner_not_flying | L:h8 has only on-board adjacent g7. No opponent is adjacent. Exactly h8-g7, not h8-f6 or any longer slide; dark a1 remains. PASS. |
| no_light_pieces_loses | The board contains only d:f6. Light has no piece from which to start a path, hence no moves and a loss. PASS. |
| dark_right_edge | d:h2 has only the on-board forward neighbour g3, which is empty. l:b8 is remote. Exactly h2-g3. PASS. |
| occupied_capture_landing | c3's d4 victim cannot be taken because e5 is occupied by a dark man. c3 can use b4; e5 can use d6 or f6. d4 is backward from e5. Exactly three quiet paths; the chosen b4 move captures nothing. PASS. |
| blocked_king_other_man_moves | D:a1 is blocked by friendly b2 and cannot jump it. The b2 man has vacant a3,c3 and no adjacent enemy. Exactly b2-a3 and b2-c3; this blocked king is not a side-wide loss. PASS. |
| two_jumps_same_direction | a1 has b2/c3 as its sole jump. At c3 d4/e5 forces a second jump; e5 then has no adjacent forward opponent. Exactly a1-c3-e5, never the prefix a1-c3; both victims disappear. PASS. |
| dark_left_edge | Only b4 is an on-board forward neighbour of a3; it is empty and h8 is distant. Exactly a3-b4. PASS. |
| dark_crown_quiet_choice | c7 has vacant b8,d8. h2 cannot interact. Both one-step paths promote; chosen b8 holds D. PASS. |
| light_capture_crown_stops | g3 has f2/e1 as its sole capture pair. Reaching e1 crowns and stops, although a king could later take d2/c3. Exactly g3-e1, retaining d2 and putting L on e1. PASS. |
| light_crown_quiet_edge | From h2 the only on-board forward step is g1. a7 is remote. Exactly h2-g1; the after-board has L:g1. PASS. |
| light_king_four_steps | L:e5 has empty d4,f4,d6,f6 and no adjacent opponent. Exactly four adjacent quiet steps; none is a slide past those squares. PASS. |
| dark_king_four_steps | D:d4 has empty c3,e3,c5,e5 and no adjacent opponent. Exactly four quiet steps; l:h8 is too far for a jump. PASS. |
| king_capture_fork | D:c3 can jump b4/a5 or d4/e5. No lower adjacent opponent exists. At either landing there is no unused adjacent victim, so exactly c3-a5 and c3-e5; choosing a5 leaves l:d4. PASS. |

Result: **20/20 passed** against the rules and saved fixture; no correction/redraw was needed. The opening and all branching captures were also rechecked outside the seeded sample. This is independent human-style rule reasoning, not a claimed engine differential test. A later game implementation must match the expected paths; it must not become the fixture oracle.
