# The checkers project

Try the standard opening or a small checkers endgame.

1. Open this folder in BED with the checkers adapter loaded.
2. Choose a position. In its Board panel, click a square to change what stands on it.
3. Use Match to play inside the editor.

"Play in its own window" starts the game itself on the position you are editing.

The standalone player loads the same saved positions through `Checkers.Rules`, without the BED editor. Click a piece and its landing squares; arrows move the highlighted square and Enter or Space chooses it. N starts the saved position again, C plays a computer move, U takes back the last move, and Escape closes the player. `play-checkers position.json --shot picture.png` draws without opening a window.
