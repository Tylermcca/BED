# Try a project with Play

Play is supplied by the project adapter and host. Some samples provide an in-editor preview; a linked game runs through a separately declared process host. A scene file or an enabled editor alone does not supply a game executable.

For a public example of an in-editor game, see [the checkers walkthrough](make_a_game.md). Do not assume that its preview contract applies to every linked game.

## Start linked Play

1. Open a project and document whose host has connected linked Play.
2. Choose **Play**.
3. If there are unsaved changes, read **Play runs your saved files** and choose **Save**, **Don't Save** or **Cancel**.

Save must succeed before the saved-input launch proceeds. Don't Save runs the saved input rather than applying the draft to the game. Cancel leaves the current editing state in place. A failed save is not permission to launch a different version silently.

The linked session reports **Starting** while it waits for the game to load the captured input. **Running** means the linked readiness exchange completed. A process merely starting is not the same result. Read any failure message rather than assuming a blank or delayed preview is ready.

## Stop and return to editing

Choose **Stop** to request that the linked game finish. **Stopping** remains active until the owned session completes or its failure/deadline path ends it. Do not treat the first Stop click as proof that every owned resource has already closed.

Play does not turn runtime changes into authored edits. Stop before saving a scene. A host may refuse scene Play with **Scene Play is not connected yet.** That is a missing project connection, not a file error you can fix by changing the scene JSON.

Pause, single-step, hot reload and live write-back are not promised by this linked Play route. A Console may be supplied by the host; its presence is not guaranteed for every adapter.

## Source reference

- [Editor Play decision](../../src/Brutal.Authoring.Editor.ImGui/EditorWindow.cs)
- [Saved-input bridge](../../src/Brutal.Authoring.Editor.ImGui/LinkedPlayPreview.cs)
- [Linked session status and Stop](../../src/Brutal.Authoring.Editor.ImGui/LinkedPlayPanel.cs)
