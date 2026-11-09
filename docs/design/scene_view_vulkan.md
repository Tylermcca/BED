# Vulkan scene-view resource contract

Design only, against BED main 365e466. No renderer, host API, GPU allocation or native execution is added here. Scene and model-preview rendering share one host-owned service; a Play window remains a separate consumer/process design, not an excuse to borrow live editor handles across processes.

## Evidence: what exists

BED source references are relative to this repository. Binding references are relative to the BRUTAL source checkout, not a proposed BED dependency path.

- `src/Brutal.Authoring.Editor.App/WindowRenderer.cs:20-40,157-168`: the internal host owns a device/graphics queue, two frame slots, presentation images, render pass and ImGui backend creation. The backend receives that same device/queue/pass. Its descriptor pool size is currently 16, not an unlimited texture budget.
- `WindowRenderer.cs:177-216`: Draw waits for the frame fence, acquires, resets only after successful acquire, begins a command buffer and opens the presentation pass **before** calling its recording callback. No offscreen scene hook currently exists.
- `WindowRenderer.cs:248-288,290-300`: presentation has one color attachment and no depth; UNORM is preferred because ImGui colors are already encoded. Do not replace this pass with a depth scene pass or globally change UI color encoding.
- `WindowRenderer.cs:370-391`: swapchain recreation/disposal waits idle and destroys owned resources. This is not a device-loss recovery state machine.
- `src/Brutal.Authoring.Editor.App/EditorApp.cs`, Run and Loop: GLFW/ImGui lifetime surrounds WindowRenderer; ImGui.Render precedes renderer.Draw. Startup may choose OpenGL when Vulkan is unavailable. Existing dialogs/close/title callbacks must survive any future host extension.
- `src/Brutal.Authoring.Editor.ImGui/ViewportView.cs:10-16,60-76`: current viewport is CPU-projected, painter-sorted ImGui geometry. It is not depth-correct Vulkan rendering.
- BRUTAL skill `references/vulkan.md`, recipes 7.13, 7.29 and 7.30: swapchain/fence handling, depth attachments, image sampling/barriers and separate view/image disposal. Recipe verification belongs to the skill's author, not this task. Its sample snippets require checking current source before implementation.
- BRUTAL `Brutal.Vulkan.Abstractions/ImageEx.cs`, CreateInfo: image usage/format/sample count and allocator are explicit; Name is required in the inspected source. `ImageViewEx.cs`, Dispose frees the view, not the image allocation. Handles are aliased value types, not independent owners when copied.
- BRUTAL `Brutal.Vulkan.Abstractions/Extensions/PhysicalDeviceExtensions.cs:17`, FindSupportedFormat: select a supported format; do not assume every device supports one chosen depth format.
- BRUTAL `Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs:675-719`: AddTexture(sampler, imageView, imageLayout) returns ImTextureRef; RemoveTexture frees its descriptor. It does not allocate, transition, render, or destroy the caller's image/sampler.
- That backend at `199-215` does not execute custom ImDrawList callbacks; at `250-253` it binds texture IDs as descriptor sets. Skill `references/glfw-imgui.md` trap T47 explicitly warns against rendering custom content through those callbacks.

No package binaries, reflection, real assets or GPU probes were used. Source presence is not proof of native export availability or rendered correctness.

## One owner and borrowed handles

**BORROWED:** explicit Vulkan resource lifetime and offscreen image sampling follow the cited BRUTAL recipes. **MIXED:** extend the existing host, not a second window renderer. **INVENTED:** the following service and lease boundaries are the proposed BED contract; their names are descriptive, not callable APIs.

| Resource | Owner | Consumers and release rule |
|---|---|---|
| Instance, surface, device, queue, swapchain, frame command pools/fences | Existing WindowRenderer host | Scene service borrows them. Never disposes or independently submits to the host queue. |
| Scene service, device-generation token and deferred retirement queue | Host, created after backend setup | One service supplies scene views and previews; destroyed before ImGui backend and device. |
| Offscreen target set: color/depth allocations, views, framebuffer, sampling descriptor and sampler lease | Scene service | Panel receives an opaque image lease and dimensions, not disposal authority. |
| Immutable mesh/material uploads and compatible pipelines | Shared service cache, keyed by asset revision and device generation | Scene and preview lease the same resources; never a second asset identity database. |
| Camera, selection, document/session reference and desired panel size | Panel/typed document host | No GPU ownership; commands still govern edits and undo. |

All Vulkan command recording, descriptor operations and retirement occur on the owning render/UI thread. Background CPU preparation may produce immutable data, not submit commands or mutate device objects. Asset catalog leases identify content revisions; renderer leases additionally identify device generation and representation. Do not serialize native handles into scene files or pass them to a separate Play process.

## Target and depth contract

**INVENTED**, using verified primitive shapes rather than choosing unverified convenience APIs:

- Each visible view has its own color/depth target set and camera. Sharing an owner/cache does not mean scene and preview overwrite the same image.
- Initial implementation uses one sample per pixel. Color requires color-attachment and sampled-image support/usage; depth requires depth-stencil attachment support. Query limits/features before allocating. No assumption that a presentation image is sampleable.
- Select depth from supported candidates in the BRUTAL recipe, refuse clearly if none is usable. Stencil aspects are included only for a chosen combined format. Clear depth to 1, enable depth test/write with Less for the first opaque pipeline. Transparency sorting/material semantics are a later explicit contract, not solved by adding depth alone.
- Color and depth extent/sample count match their framebuffer/render pass. Dynamic viewport/scissor matches the actual target pixel dimensions. Finite dimensions are rounded consistently from panel size and framebuffer scale, clamped to device/budget limits; zero/minimized/hidden views do not allocate zero-size images.
- Keep UI composition encoding unchanged. Proposed first path produces display-encoded scene color into a supported UNORM sampled target so the current UNORM UI composition does not encode twice. Lighting calculations remain linear; the final scene output applies the display transfer once. Asset texture color-space metadata is separate. An alternative linear/sRGB target is not interchangeable without verifying the backend shader's sampling and final display transform. This choice needs owner-rendered color-ramp acceptance before implementation is called correct.
- Coordinate handedness, clip depth 0..1, projection Y orientation and texture UV orientation explicitly. BRUTAL recipe 7.29 documents its matrix convention/Y flip; don't assume System.Numerics matrices can be uploaded under that convention without a tested conversion. A synthetic asymmetric marker must prove orientation and picking agreement.

## Frame schedule and synchronization

**INVENTED.** A future narrow host seam must expose a pre-presentation-pass recording phase and safe frame-slot identity. The current Draw callback is already inside a pass and cannot be reused for a nested offscreen pass. No custom ImDrawList callback workaround.

1. After the applicable frame slot is safe, select/prepare that slot's target leases. During UI layout collect view requests, capture immutable document/camera/asset revisions and emit the selected texture references into ImGui draw data. New targets may be allocated before recording but are not sampled until rendered.
2. After ImGui.Render and command-buffer Begin, before the presentation BeginRenderPass, record scene and preview passes for requested targets. Clear their color/depth and draw the captured content.
3. Transition each color image from its tracked prior layout into color-attachment layout, then to ShaderReadOnlyOptimal before ImGui sampling. Use explicit dependencies from color attachment writes to fragment shader reads. Depth dependencies cover early/late fragment tests and depth writes. A descriptor's declared layout does not perform a transition.
4. Open the unchanged presentation pass, render ImGui draw data sampling those completed targets, then end/submit/present using the existing host synchronization.

Use a target set per in-flight frame slot initially. Reuse only after that slot's submission fence completes. UI collection must know which safe slot will be submitted; acquiring/resetting/submitting failures must not leave a falsely reusable target or an unsignaled fence waiting forever. This means the future seam includes frame preparation as well as pre-pass recording; merely adding an Action callback is insufficient. Retain reset-after-successful-acquire and per-swapchain-image present semaphore behavior.

A previous complete image may be retained for a hidden or unavailable view, but it must remain immutable and leased until all draws sampling it finish. Do not overwrite a shared last-frame image while another frame can sample it. All initial work uses the host graphics queue; separate transfer/compute queues would require a new ownership/semaphore contract.

## Resize, close, failure and recovery

**INVENTED.** Target state is Absent, Ready, ReplacementPending, Retiring or Unavailable, scoped by device generation and project admission.

- **Panel resize/DPI:** allocate a complete replacement target set and descriptors; publish atomically at a safe frame boundary. Retire old descriptors/framebuffers/views/images only after every sampling submission completes. Coalesce rapid resize requests. Allocation failure keeps a clearly identified last complete image or placeholder; never half-swap color and depth.
- **Window resize:** preserve existing out-of-date/suboptimal handling. Swapchain recreation and offscreen panel resizing are related requests, not identical resource lifetimes. Recreate backend-compatible state if format/image-count requirements change; verify backend MinImageCount/ImageCount assumptions instead of retaining stale initialization values blindly.
- **Minimize/zero extent:** skip target rendering/allocation and keep close/event handling alive. A zero extent is not an allocation failure and must not destroy the document/session.
- **Panel/project close:** stop accepting requests for its admission token, detach texture references from future UI, cancel late CPU completion publication, retire its target leases. Other previews/projects using shared immutable uploads retain their leases. Never dispose the host device on project close or mark a document saved.
- **Device loss:** stop submission and invalidate every native handle/descriptor by incrementing device generation. Report renderer unavailable while preserving CPU documents and undo. First implementation may require restarting the rendering host; no transparent recovery is claimed. If recreation is implemented later, all backend/target/upload state is rebuilt from CPU/catalog data, never reused by numeric handle coincidence.
- **Host shutdown:** stop new requests, finish or abandon GPU work according to actual device state, retire sampling descriptors while the backend's descriptor pool is still alive, destroy target framebuffers/views then image allocations, release shared pipelines/samplers/uploads, dispose backend, then host device/surface/instance. Existing WaitIdle is suitable only for a healthy device; loss/error cleanup needs an explicit best-effort idempotent path that does not wait forever.
- **Partial construction:** each successfully created resource has one owner immediately. Unwind only completed creations in reverse dependency order, including descriptors; no aliased handle is disposed twice.

Bound target count, dimensions, cache bytes and descriptor demand. Current pool size 16 cannot be treated as capacity for arbitrary previews times two frame slots. Before implementation chooses pool growth or a view limit, calculate the backend's actual descriptor allocation rules and test exhaustion/failure cleanup. No unbounded allocation on every frame or resize event.

## Backend and preview boundaries

**MIXED.** Vulkan is the first depth-correct path. Existing OpenGL fallback and headless CPU viewport remain usable as their current capabilities, clearly distinguished from a new depth renderer. Never pass a Vulkan ImTextureRef to OpenGL or claim painter sorting proves depth correctness. No OpenGL renderer implementation or second device is required by this contract.

Model preview uses the same target factory, depth/color policy, upload cache, scheduler and retirement queue as scene view, with its own camera/lighting/request key. The asset catalog supplies immutable revision leases; it does not own GPU textures. The scene document host owns selection/editing, not renderer disposal. Separate-window Play has independent host lifetime and receives data/revisions, not editor device handles. Docking across native windows would need explicit viewport/backend ownership review; this contract does not promise multi-window texture sharing.

## Implementation ownership and acceptance

**INVENTED**, future coordinated files only:

- Host owner: narrow frame preparation/pre-pass and lifetime seams in EditorApp.cs and WindowRenderer.cs, preserving close/dialog/title/UI-tree behavior.
- Renderer owner: new scene-view service/target/retirement files under the App rendering layer, with no graphics dependency added to pure scene/core data types.
- Panel owner: ViewportView or a new view adapter submits immutable requests and draws returned images; existing command/picking behavior is retained until parity is proven.
- Preview owner: uses the same service, not its own device or target cache implementation.

Before calling this working, owner-run synthetic tests must establish near/far overlap with reversed draw order; color/alpha/orientation; two independent simultaneous views; 1x/1.5x/2x sizes and rapid resizing; minimize/restore/close; missing/stale asset placeholders; descriptor exhaustion and partial creation; project close with frames in flight; device-loss state transitions; and backend fallback without dangling texture IDs. Verify no validation-layer errors, leaks or destruction while in use. Preserve existing UI input, text/contrast and viewport command tests; do not rebaseline failures into success.

This hand-in ran only source/document checks. No GPU, window, image capture, renderer prototype, benchmark or build/test was needed for this documentation card. Native exports, resource limits, depth correctness, color output, latency and recovery remain implementation acceptance work.
