# Try live values in the garden

The **Live values** panel shows the round friend's position, movement speed and jump strength while the garden plays without a game window. These steps use the 2D garden sample. The 3D physics island sample also shares each falling body's height, up-and-down speed, gravity and mass; use **jump** to lift the selected body, once per press. Other games show a plain note until they choose to share values.

1. Open the garden, save other edits and [build the saved game](build_game.md). Stop ordinary Play.
2. In the **Outliner**, select the round friend, `round_friend`.
3. Use **Live values** and choose **Start live values** (runs without a game window). If the panel was closed, open the command palette and choose **Show Live values**.
4. Read X and Y position, X and Y speed, and jump strength. X and Y give the left and bottom edges of the friend's touch box, in metres; speeds are metres per second. The game takes steps while BED updates and pauses between requests.
5. Type a value from 1 to 25 into **Try jump strength (1-25)**. Wait for the game to confirm it. Its next jump uses that strength. The quarter-second buttons try left, right, jump or restart.

Trying a value changes only this running game. Your saved level stays unchanged. **Keep this value** writes the confirmed jump strength into the level. It keeps that one setting, not the friend's current position or speed. Save other edits first; if the file changed elsewhere, BED refuses to overwrite it.

Choose **Stop program** in the **Play console** when done. Unkept trials disappear with the run. After keeping a value, build again before ordinary Play or Export uses it.

Next: [ask an AI to look at the game](see_the_game.md) or [Export](exporting.md).
