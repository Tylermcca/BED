# Synthetic scene CPU costs (SC-S5a)

Measured 2026-11-09 in wt-codex-1, based on main a7754ac plus scene-host completion 71d09d0. Probe: `SceneCostProbeTests.Record_synthetic_cpu_submission_and_pick_costs`. No production source was changed.

## Workload and host

Windows 10.0.26300; .NET 10.0.13 x64; 16 logical processors reported. Release build. Existing HeadlessHost, 1280 by 800, `Drawing=false`: CPU ImGui draw submission and semantic tree recording, **not** software pixel rasterization, Vulkan, GPU presentation or an OS window. SceneViewport supplies the existing cube for each asset-free synthetic root object: 100 or 1,000 objects, 1,200 or 12,000 triangles, arranged in 25 columns. No parent hierarchy, imported assets or filesystem reads occur in measured actions.

Three warm-up frames, then 12 sequential samples per phase. Stopwatch wall time and GC.GetAllocatedBytesForCurrentThread deltas; no forced GC. Allocation figures exclude native and other-thread allocations. Item construction includes SceneViewport world-transform production and ToArray. Picking uses a prebuilt item array and a fixed ray hitting item_0. Frame includes ImGui frame submission, viewport drawing and tree completion. These phases overlap and must not be summed as independent parts of a frame.

## Captured results

| Objects | Phase | Median ms | Maximum ms | Median managed bytes | Maximum managed bytes |
|---:|---|---:|---:|---:|---:|
| 100 | Items/world transforms | 0.2190 | 0.3115 | 37,768 | 37,768 |
| 100 | Pick/prebuilt items | 0.1814 | 0.2445 | 26,432 | 26,432 |
| 100 | Frame/submission + tree | 3.4933 | 12.9191 | 243,952 | 243,952 |
| 1,000 | Items/world transforms | 7.4966 | 12.6839 | 376,168 | 376,168 |
| 1,000 | Pick/prebuilt items | 2.2506 | 7.5544 | 264,032 | 264,032 |
| 1,000 | Frame/submission + tree | 28.0121 | 34.4328 | 2,169,528 | 2,169,528 |

The first harness invocation used normal logging, which suppressed successful-case phase output. One subsequent detailed-logging collection produced the numbers above; there was no repetition to select a faster result. Both completed; functional fixture checks confirm object count, the expected pick identity, no journal edits and an unchanged draft. Those checks are not performance thresholds or a rendering acceptance result.

## Interpretation and next boundary

These observations justify investigating the existing per-frame world/item construction and managed allocation, not claiming an achieved target or a measured optimization saving. Source paths: `src/Brutal.Authoring.Editor/Viewport.cs` SceneViewport.Items/World and Picking.Pick; `src/Brutal.Authoring.Editor.ImGui/ViewportView.cs` Draw/DrawItems. S5b should preserve revision, source/mesh identity, transform hierarchy, selection/focus and picking parity before retaining any result. This flat-root workload does not establish a cache invalidation contract for changing mesh providers or parent chains. No cache is implemented here.

To collect again deliberately, run only the named probe filter with Release, --no-restore and console verbosity=detailed. Do not treat a full suite or repeated benchmark sweep as necessary. Host contention, JIT and GC can affect these small samples; no percentile, cross-machine or GPU claims are made.
