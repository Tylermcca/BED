# Saved physics sample

This original headless sample uses plain primitive bodies. It has no renderer, audio device or editor dependency. A game owns the physics world; opening the editor or reading a scene never starts one.

Build with `dotnet build samples/PhysicsStarter/PhysicsStarter.csproj`. The verified target is Windows x64 with the .NET 10 runtime. Its native dependency is the existing Jolt 2026.10.2 pin with Core 2026.10.1. Keep the complete managed runtime, `runtimes/win-x64/native/jolt.dll` and notices together; the exact game-owned inventory lives in the shared PhysicsRuntimeInventory helper. The native hash is checked against the verified binary when loaded. The pure SceneStarter game and its export inventory retain their existing behavior.

## Enable a saved project

Install this profile's runtime inventory under the project's `game` folder. Declare the existing sceneBuild mapping with `physics:true`, for example:

`{"sceneBuild":{"version":1,"sceneVersion":2,"source":"data/level.json","generated":".bed/generated/scenes/level.json","spaces":["3d","2d-xy"],"physics":true}}`

The sample adapter then supplies `game/PhysicsStarter.exe` for ordinary saved-file Play and Build and Play. Existing explicitly saved Play settings remain authoritative; replace an older game's command deliberately. Physics build inputs include the saved level, project settings, published cache pointer and this exact installed runtime/notice inventory. Changed contents invalidate the stamp even with preserved timestamps. Build requires a complete published asset generation.

Run `game/PhysicsStarter.exe --compile-scene2 .` from the project to prepare saved content, then `game/PhysicsStarter.exe --project . --seconds 1`. The duration is bounded to 0..60 seconds. It reports body/tick counts and dropped catch-up time; there is no live authoring or stdout command protocol.

The compiler expands accepted prefabs, fits mesh-bounds from that exact published model snapshot, retains local dimensions/center and emits only primitive bodies. A separate game-owned `scene2-runtime.v2` descriptor declares `jolt-2026.10.2-v1`. The generated scene, descriptor and exact asset generation are checked again before the content stamp is written last. Missing capability, unsupported profiles, stale inputs, corrupt assets, image-as-model and degenerate fits refuse. The old runtime refuses physics instead of ignoring bodies.

## Native profile

Saved gravity and fixed step are authoritative. Mass is total kilograms, friction is the saved dimensionless value. Apply uniform world scale once to dimensions and center. Dynamic density is mass divided by scaled primitive volume; normal finite volume/density/inertia and normal finite reciprocal mass/inertia are required. Extreme values refuse before native mass arithmetic. Native resulting mass and finite inertia are checked, including the pinned capsule principal-axis behavior. Body origin stays the authored origin when a translated shape offsets its center of mass.

One game thread owns all native create/update/query/remove/destroy operations. Only one such world is active in a process. Borrowed-body readback happens after Update joins jobs and never disposes a borrowed handle. Creation failure unwinds existing bodies, worlds, filters, jobs, allocator, registered types and factory. No missing mass-override, body-lock wrapper or bulk-kinematic native export is used.

The profile uses two collision layers (static and moving); static/moving and moving/moving collide. Capacity is 4,096 bodies and 16,384 body-pair/contact slots, a 16 MiB temporary allocator and one worker. Linear/angular damping are zero, sleeping is disabled, and the verified pin's default solver, discrete motion quality and friction combiner are retained. Friction readback and actual rough/zero-friction sliding are checked. Capsules use a positive cylindrical half-height. Kinematic control uses scalar MoveKinematic at the saved boundary. XY uses native Plane2D DOFs, with forbidden-axis velocity/target requests refused; no post-step clamping.

The loop consumes a bounded accumulator, at most eight fixed steps per iteration. Excess whole-step time is dropped and reported. Poses remain game-owned overlays; no simulated transform writes the authoring level.

## Export

The adapter declares this profile's exact native/managed files and notices separately from the pure sample. Export admits saved generated content and the current producing asset policy, builds, smoke-checks and copies through the common export path. Changed catalog, policy, saved scene, runtime binary or stamp refuses the captured operation. The copied game runs without BED, authoring definitions, original model sources or the original cache. It carries its own generated scene/descriptor and verified cache closure. Additional asset notices unsupported by the existing sample exporter still refuse plainly.
