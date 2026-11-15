# See pictures of the game

The 2D garden and 3D physics island samples can send an AI assistant pictures of the running game. Neither opens a game window for this. This is a separate Play run; it does not take a picture of your desktop or attach to an already open game window. Other games need their own support for sending pictures through BED.

## In BED

1. Open the garden or physics island sample, save edits and [build it](build_game.md). Stop ordinary Play.
2. Choose **View > Game picture**. The View menu opens with **Alt+V**; the panel also appears in the command palette (**Ctrl+Shift+P**, search `Game picture`).
3. Press **Start picture run**. BED uses the saved-game Play command and asks for the usual approval if needed. The first picture appears with the game's description. No game window opens.
4. Press **Take picture** for another picture of the same paused game state, or **Stop picture run** to end the run. These picture requests do not advance game time.

This uses the same picture connection as assistant requests, so the project's [asset AI-use permissions](content_rights.md) still apply. Other games must support that connection; BED cannot take pictures of arbitrary desktop windows.

## With your AI assistant

1. Open the garden or physics island sample, save edits and [build it](build_game.md). Stop any ordinary Play run.
2. [Connect your AI app](connect_an_ai_app.md).
3. Ask: “Start this saved sample through BED's connection and take a picture. Hold right for half a second of game time, then show another picture and describe what changed. Stop the run when finished.”
4. Read the pictures, the game's description and the count of steps it took. Ask the assistant to explain what it observed.

In the garden, the supported keys are **left**, **right**, **jump** and **restart**. On the island, **left** and **right** turn the camera around the island, **jump** lifts the first saved dynamic body (the sample’s crate), and **restart** starts the saved bodies again. A jump sets its upward speed to 5 metres per second once per request; you can jump again in the air.

Each request can hold one key for more than zero and at most five seconds of game time. The game releases the key after the request. A picture request alone advances no game steps. The game pauses between requests, so game time here is different from waiting beside a running window.

If BED says to build first or approve the Play command, do that in BED and retry. An assistant cannot grant that permission itself. Ask it to stop this run before starting another.

A picture is evidence of what was drawn at that moment. It does not prove that a level, jump or whole game passed a test. Ask for “what happened”, with the returned picture, rather than a claim that everything works.

To try a jump value yourself, see [live values](live_values.md).
