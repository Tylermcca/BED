# Saved physics sample

This original sample is a small physics toy. It loads a saved level, runs its primitive bodies in Jolt and draws the level in a window with the level's own models, material colours and textures. It has no audio device or editor dependency. A game owns the physics world; opening the editor or reading a scene never starts one.

Build with `dotnet build samples/PhysicsStarter/PhysicsStarter.csproj`. The verified target is Windows x64 with the .NET 10 runtime. Its native dependencies are the existing Jolt 2026.10.2 pin with Core 2026.10.1, and GLFW 2026.10.4 with OpenGL 2026.10.1 for the window (the same pins BED's own window uses). Keep the complete managed runtime, `runtimes/win-x64/native/jolt.dll`, `glfw3.dll`, `glad.dll` and notices together; the exact game-owned inventory lives in the shared PhysicsRuntimeInventory helper. The native hash is checked against the verified binary when loaded. The pure SceneStarter game and its export inventory retain their existing behavior.

## Enable a saved project

Install this profile's runtime inventory under the project's `game` folder. Declare the existing sceneBuild mapping with `physics:true`, for example:

`{"sceneBuild":{"version":1,"sceneVersion":2,"source":"data/level.json","generated":".bed/generated/scenes/level.json","spaces":["3d","2d-xy"],"physics":true}}`

The sample adapter then supplies `game/PhysicsStarter.exe` for ordinary saved-file Play and Build and Play. Existing explicitly saved Play settings remain authoritative; replace an older game's command deliberately. Physics build inputs include the saved level, project settings, published cache pointer and this exact installed runtime/notice inventory. Changed contents invalidate the stamp even with preserved timestamps. Build requires a complete published asset generation.

Run `game/PhysicsStarter.exe --compile-scene2 .` from the project to prepare saved content, then `game/PhysicsStarter.exe --project .` to play. There is no live authoring or stdout command protocol.

## Play

`--project .` on its own opens a 1280 x 720 window (resizable) and runs the saved level live:

- **Left click** drops a copy of the level's first dynamic body (the sample's prefab crate, same model and body) from above the clicked point on the island, or above the island centre when the click misses it. At most 40 dropped crates stay; the oldest goes first, and any that fall off the island are cleared.
- **Right-drag**, **A/D** or the **left/right arrows** orbit the camera; the **mouse wheel** zooms.
- **R** resets to the saved level; **Esc** or closing the window quits.

The controls are also in the window's title. The island is drawn with OpenGL 4.1: a sky gradient, a sea, one sun with a shadow map and a soft sky/ground ambient. The sample's textures carry the light level BED's own view bakes into them; the game divides that level out using each face's model-space normal, so its real lighting doesn't darken faces twice and BED's view is unchanged. If no OpenGL 4.1 window can be made, the game says so and runs one second without a window instead.

## Headless runs

Any of these options keeps it a console program with no window; BED's Play and Export checks and the tests use them:

- `--seconds S` runs S seconds (0..60) by the clock.
- `--frames N` runs exactly N fixed steps (0..100000) without waiting on the clock, for repeatable checks. Use `--seconds` or `--frames`, not both.
- `--headless` runs one second by the clock.
- `--drops K` (0..40, with `--frames`) drops K crates at repeatable points, one each half simulated second from the first step.
- `--shot file.png` renders the state after `--frames` steps (default 0) to a 1280 x 720 PNG through a hidden window and a multisampled offscreen framebuffer. Nothing appears on screen, but it needs a PC with OpenGL 4.1.

On exit every run prints the saved body count, fixed ticks and dropped catch-up time, the simulated seconds, how many dropped crates remain, where the highest saved dynamic body (the top of a stack) started and came to rest, and how far each kinematic body travelled.

## Kinematic motion

The saved format has no motion field, so the game applies one fixed rule: every kinematic body slides back and forth along world X around its saved position, `X = saved X + 1.5 * sin(2 * pi * t / 4)` metres at simulated time `t` seconds, keeping its saved Y, Z and rotation. Each fixed step sets the next target through scalar MoveKinematic, so dynamic bodies it touches are pushed by the simulation.

The compiler expands accepted prefabs, fits mesh-bounds from that exact published model snapshot, retains local dimensions/center and emits only primitive bodies. A separate game-owned `scene2-runtime.v2` descriptor declares `jolt-2026.10.2-v1`. The generated scene, descriptor and exact asset generation are checked again before the content stamp is written last. Missing capability, unsupported profiles, stale inputs, corrupt assets, image-as-model and degenerate fits refuse. The old runtime refuses physics instead of ignoring bodies.

## Native profile

Saved gravity and fixed step are authoritative. Mass is total kilograms, friction is the saved dimensionless value. Apply uniform world scale once to dimensions and center. Dynamic density is mass divided by scaled primitive volume; normal finite volume/density/inertia and normal finite reciprocal mass/inertia are required. Extreme values refuse before native mass arithmetic. Native resulting mass and finite inertia are checked, including the pinned capsule principal-axis behavior. Body origin stays the authored origin when a translated shape offsets its center of mass.

One game thread owns all native create/update/query/remove/destroy operations. Only one such world is active in a process. Borrowed-body readback happens after Update joins jobs and never disposes a borrowed handle. Creation failure unwinds existing bodies, worlds, filters, jobs, allocator, registered types and factory. No missing mass-override, body-lock wrapper or bulk-kinematic native export is used.

The profile uses two collision layers (static and moving); static/moving and moving/moving collide. Capacity is 4,096 bodies and 16,384 body-pair/contact slots, a 16 MiB temporary allocator and one worker. Linear/angular damping are zero, sleeping is disabled, and the verified pin's default solver, discrete motion quality and friction combiner are retained. Friction readback and actual rough/zero-friction sliding are checked. Capsules use a positive cylindrical half-height. Kinematic control uses scalar MoveKinematic at the saved boundary. XY uses native Plane2D DOFs, with forbidden-axis velocity/target requests refused; no post-step clamping.

The loop consumes a bounded accumulator, at most eight fixed steps per iteration. Excess whole-step time is dropped and reported. Poses remain game-owned overlays; no simulated transform writes the authoring level.

## Export

The adapter declares this profile's exact native/managed files and notices separately from the pure sample. Export admits saved generated content and the current producing asset policy, builds, smoke-checks and copies through the common export path. Changed catalog, policy, saved scene, runtime binary or stamp refuses the captured operation. The copied game runs without BED, authoring definitions, original model sources or the original cache. It carries its own generated scene/descriptor and verified cache closure. Additional asset notices unsupported by the existing sample exporter still refuse plainly.
