# Share a game's live values

An adapter opts in through the default `IProjectAdapter.LiveValues(ProjectPaths)` hook. It returns immutable `LiveValueTarget` declarations and starts no process. Existing adapters inherit an empty result. BED then says **This game doesn't share live values.** There is no automatic garden fallback.

## Declare the readout

Use `Bed.Editor.LiveValueTarget` and `LiveValue`. One target names an exact game object and up to sixteen values in their runtime reply order. Each value has an ID, a readable label and units. Supplying both finite `minimum` and `maximum` enables trials inside that inclusive range; omitting them makes the value read-only. A finite current value outside the trial range stays readable, but cannot be tried or kept outside that range. Optional `trialLabel` keeps a game's wording. IDs must be unique; BED captures at most 64 targets and eight input names per target.

The selected editor object chooses a matching target. When needed, the **Live object** list chooses another. Stop before changing the live object. The adapter chooses names and ranges; BED does not inspect arbitrary game memory or guess fields.

The public sample implementations are [the adapter's declarations](../samples/SceneStarterAdapter/LiveValues.cs), [the garden](../samples/SceneStarter/Garden/GardenAgentPlay.cs) and [the physics island](../samples/PhysicsStarter/PhysicsLiveValues.cs). The garden retains its five readouts, jump-strength range, quarter-second inputs and explicit Keep. The island exposes each dynamic body's height, vertical speed, world gravity and body mass, plus `jump` for the selected body. Jump sets its upward speed to 5 m/s once per input request; repeated requests can lift it in the air.

## Connect the game

Use `Bed.Runtime.LiveValuesHost.Target` to read the object requested by BED. Enter the special no-window loop only when `BED_AI_OBSERVE` is `1`; ordinary Play must retain its normal entry point. Validate that target against objects your game owns, then call `LiveValuesHost.Run` with:

- the selected object ID;
- a callback returning the game's monotonically increasing frame count;
- a callback returning the declared values in order;
- a trial callback that accepts only declared field IDs and independently checks their ranges;
- an advance callback that handles only declared input names and advances at most the supplied quarter second of simulation time;
- the child's standard input, output and error streams.

All callbacks run serially on the calling game thread. This keeps native physics mutations at a simulation boundary. The island uses Jolt's existing `SetGravity` and `MotionProperties.ScaleToMass`; changing mass also scales inertia and preserves pose and velocity. The readout checks actual native mass.

The helper sends a greeting, answers ordered requests, and exits on EOF, stop or refusal. It limits requests to 512 characters and snapshots to sixteen finite numbers. Reserve stdout for this channel. BED owns the exact approved Play child, enforces ten-second replies, checks the target/value count and trial ranges, and stops that child on invalid output. No desktop keys, other process attachment or authoring-file writes are provided. Simulation pauses between requests.

## Keep is separate from trying

An optional `saveField` identifies an existing scene-v2 object field under `/properties/` or `/body/`. It is not a file path. **Keep this value** uses the original scene's checked edit/save route and only the confirmed field value; a different owner, another selected object, a dirty draft or an outside file change refuses it. Other document types can share and try values but do not gain an automatic save route.

The garden keeps `/properties/jumpStrength`. The island keeps only `/body/massKg` on the chosen crate or other dynamic body; its world-gravity trial has no Keep field. Trying either changes no saved files. Rebuild after a Keep before another ordinary Play or export.

The scoped `Live01Tests`, `Live02Tests` and `AiShotTests` cover both games without opening game windows, the original garden workflow, real native mass/gravity, confirmed saves, missing opt-in and unchanged picture commands.
