# Synthetic data performance cases

**INVENTED:** a neutral fixture and public-API benchmark for this library's data editor. No game files, real workbook values or licensed assets are used. No new dependency is introduced.

`make_data.py` writes a schema and either 1,000 or 10,000 rows into the caller's temporary directory. Seed `330031` controls Python's local `random.Random`. Every row has exactly 20 scalar fields (`id`, `name`, `category`, `active`, `score`, `n00` through `n14`), a two-object `tags` list and a `metadata` object. The schema uses internal `$ref`, an enum and numeric ranges. Numeric edits deliberately use negative values outside the generated nonnegative values, within the schema's range. IDs retain leading zeroes.

Run the generator in a temporary directory, rather than checking generated files into source control:

```powershell
$folder = Join-Path $env:TEMP ('authoring-perf-' + [Guid]::NewGuid().ToString('N'))
python fixtures/data-perf/make_data.py $folder --rows 10000
```

The tests invoke this same Python generator with `CreateNoWindow`, create a private temporary directory per case and remove it on completion. Only Python's standard library is required. The overlay gate redirects temporary files under its `.gate/tmp`.

## Measurement contract

Use `dotnet test --filter FullyQualifiedName~DataPerformanceTests` from the repository. All ImGui cases use the existing nonparallel ImGui collection and open no window. Each operation receives a warm-up and five fresh independent setups. The median is the third sorted measured value. The reported five-run p95 is the nearest-rank maximum; five samples cannot characterize a long-tail distribution reliably. Fixtures, schema loading, scenario setup and verification occur outside the stopwatch. Small success/count assertions remain inside the timed edit, fill, paste and undo delegates; these are part of the reported wrapper cost. No validation is disabled.

Managed allocations use `GC.GetAllocatedBytesForCurrentThread` around the same action. These figures exclude native allocation, allocations on other threads and setup. They describe allocation churn rather than retained or peak memory. No forced collection is performed between samples. Timings describe this machine under its observed load, with no claim of exclusive CPU access.

| Operation | Timed scope |
| --- | --- |
| read | `File.ReadAllText` and `DataFormat.Read`; OS file caches are warm |
| validate | whole-file `JsonSchemas.Validate` over an already parsed document |
| open | `DataKit.Open`, editor model, schema object type, table panel, inspector and editor window construction; no frame |
| first-false / first-true | first-ever `HeadlessHost.Frame` of a fresh table/context, Drawing false/true respectively |
| idle-false / idle-true | third frame after two untimed settling frames of that same context |
| filter | assign `needle`, evaluate `Visible`; selects exactly every hundredth name |
| sort | numeric ascending `n00` and evaluate `Visible`; file order remains unchanged |
| cell | `SetCell` changes row zero's score; validated and journaled |
| fill | `PlanFill` and `Apply` change the score of **every** shown row as one undo step |
| paste | `PlanPaste` and `Apply`, a 10-row by 5-column TSV rectangle starting at row zero/n00 |
| undo | undo one already committed cell edit |
| save | atomically save a one-cell-edited document to a new temporary path; reload and validate afterward |
| replay | parse a serialized one-edit journal and `DocumentSession.Replay` from the original base, including session validation/construction |
| columns / visible | direct public `Columns` / unfiltered-unsorted `Visible` calls |
| rows20 / rows-all | public `IObjectType.Rows` once per pointer for the first 20 / every visible row; no ImGui calls |

The frame workload is the table in a fixed 1200x720 ImGui window on a 1280x800 `HeadlessHost`, with no filter/sort/selection. It does not include the editor's outliner or inspector drawing. A true-drawing frame checks that pixels were covered. First-frame timing includes ImGui/font setup work performed by `Frame`; context construction is outside it. Drawing false still runs UI/native draw-list generation and texture updates. Drawing true adds this host's **CPU rasterizer**; these are neither GPU measurements nor interactive FPS claims. Public attribution calls are independent workloads, not an additive profiler trace of an actual frame.

The tests assert loaded shape, all validation results, exact filter membership, the entire expected sorted pointer sequence, full-document invariance for view state, changed values and undo restoration. Fill and paste assert exact cell/command counts, the full document against an independently constructed expected JSON tree (so unintended cells are caught), one journal step containing exactly one `data.set_many` command leaf (a public batch wrapper is permitted), and restoration of the entire original document on undo. Save reloads byte-parsed JSON, compares the whole document and validates it. Replay compares the whole final document and journal length.

Diagnostic-only environment variables: `AUTHORING_PERF_CASE=1000:fill` selects a comma-separated subset; `AUTHORING_PERF_PREFLIGHT=1` takes one diagnostic run instead of a warm-up plus five. `AUTHORING_PERF_LOG` appends JSONL sample/summary records to a scratch path. Unset both selection/preflight variables for an acceptance run. A diagnostic run is never evidence that the complete suite passed.

## Baseline and fixed ceilings

Current measured ceilings follow below. Ceilings are frozen constants in the tests, not a multiplier of the current run's own time. A skip denotes an observed finding, never a successful budget test. A measured 10k operation over two seconds is retained as an explicit performance-finding skip, with its observed time. There are no missing medians in the current calibration.

Each trial constructs a fresh table, so `Columns`, plain `Visible`, filter and sort begin with cold **view caches**. Public `IObjectType.Rows` calls bypass the table's private row cache and measure raw row-description work. The first-frame cases also start cold. The idle cases deliberately warm that same table/context with two frames. `PlanPaste` warms columns/rows within its own timed operation; this is included work, not an untimed prerequisite. The schema registry is reused within a case after warm-up. These same construction scopes were used for the before/after comparisons.

Calibration machine: `ME`, AMD Ryzen 7 5700X3D (8 cores / 16 logical processors), x64, Windows `10.0.26300`; RAM **34,255,085,568 bytes** (about 31.9 GiB), Python **3.12.10**, .NET SDK **10.0.402**, runtime **10.0.13**, **Debug**, 2026-10-31. No exclusive-machine/CPU reservation is claimed.

## Current measured baseline

All 38 operations completed one warm-up plus five measured, correctness-checked trials in one process after the table/fill fixes. There was no frame exception. Budgets are fixed `max(100 ms, ceil(10 * baseline median))`; the floor protects tiny operations from scheduler noise. No ceiling is computed from the test's current time. This catches large regressions at these sizes, rather than proving asymptotic complexity.

| Rows | Operation | Median ms | Five-run p95 ms | Managed bytes, median | Ceiling ms | Budget result |
| ---: | --- | ---: | ---: | ---: | ---: | --- |
| 1,000 | cell | 72.2697 | 81.9869 | 42,135,248 | 723 | enforced |
| 1,000 | columns | 34.4593 | 38.5430 | 47,159,488 | 345 | enforced |
| 1,000 | fill | 401.8040 | 591.8209 | 150,867,296 | 4,019 | enforced |
| 1,000 | filter | 82.5181 | 94.8354 | 95,384,768 | 826 | enforced |
| 1,000 | first-false | 36.6964 | 36.8410 | 50,206,672 | 367 | enforced |
| 1,000 | first-true | 187.3539 | 189.9738 | 50,206,712 | 1,874 | enforced |
| 1,000 | idle-false | 0.5104 | 0.9578 | 99,944 | 100 | enforced |
| 1,000 | idle-true | 152.1224 | 152.7359 | 99,904 | 1,522 | enforced |
| 1,000 | open | 73.3052 | 73.9589 | 50,592,296 | 734 | enforced |
| 1,000 | paste | 105.6918 | 110.5077 | 90,734,200 | 1,057 | enforced |
| 1,000 | read | 2.6592 | 2.7161 | 4,023,272 | 100 | enforced |
| 1,000 | replay | 138.5884 | 142.0139 | 88,485,864 | 1,386 | enforced |
| 1,000 | rows-all | 70.5561 | 72.7448 | 94,175,800 | 706 | enforced |
| 1,000 | rows20 | 1.3776 | 1.4872 | 1,883,320 | 100 | enforced |
| 1,000 | save | 15.4406 | 33.2633 | 15,548,288 | 155 | enforced |
| 1,000 | sort | 82.0013 | 83.4240 | 95,233,160 | 821 | enforced |
| 1,000 | undo | 0.1153 | 0.1207 | 113,928 | 100 | enforced |
| 1,000 | validate | 64.5993 | 64.7112 | 38,041,560 | 646 | enforced |
| 1,000 | visible | 0.0916 | 0.1120 | 56,832 | 100 | enforced |
| 10,000 | cell | 680.8858 | 692.6222 | 421,173,168 | 6,809 | enforced |
| 10,000 | columns | 35.8049 | 35.9970 | 47,663,488 | 359 | enforced |
| 10,000 | fill | 2667.7235 | 2684.1075 | 1,504,815,008 | 26,678 | finding; skipped |
| 10,000 | filter | 1357.6075 | 1436.9471 | 953,919,512 | 13,577 | enforced |
| 10,000 | first-false | 41.7623 | 54.9953 | 51,790,712 | 418 | enforced |
| 10,000 | first-true | 191.2270 | 200.1358 | 51,790,712 | 1,913 | enforced |
| 10,000 | idle-false | 1.1400 | 3.3043 | 603,904 | 100 | enforced |
| 10,000 | idle-true | 153.1525 | 154.2967 | 603,944 | 1,532 | enforced |
| 10,000 | open | 726.8605 | 739.9673 | 506,565,800 | 7,269 | enforced |
| 10,000 | paste | 729.1455 | 764.6803 | 470,924,104 | 7,292 | enforced |
| 10,000 | read | 24.4993 | 27.2673 | 40,104,344 | 245 | enforced |
| 10,000 | replay | 1394.3418 | 1400.5034 | 886,384,320 | 13,944 | enforced |
| 10,000 | rows-all | 1029.9168 | 1054.0473 | 941,759,800 | 10,300 | enforced |
| 10,000 | rows20 | 1.3699 | 1.3863 | 1,883,320 | 100 | enforced |
| 10,000 | save | 128.4695 | 130.7854 | 155,836,456 | 1,285 | enforced |
| 10,000 | sort | 1189.8498 | 1229.2774 | 952,602,328 | 11,899 | enforced |
| 10,000 | undo | 1.0046 | 1.0309 | 1,145,824 | 100 | enforced |
| 10,000 | validate | 640.1610 | 666.0957 | 381,181,176 | 6,402 | enforced |
| 10,000 | visible | 0.8135 | 1.1734 | 632,832 | 100 | enforced |

The remaining 10k full-column fill costs **2667.7235 ms** and allocates **1,504,815,008 managed bytes** cumulatively. Its budget case is explicitly `Skip = "perf finding: fill 2667.7235 ms"`. That is a remaining finding, not a passing latency budget. Its 26.678-second fixed regression ceiling is retained for a deliberate remeasurement after a further fix. The other 37 cases enforce their ceilings. Fill/paste also require exactly one public journal command leaf named `data.set_many`, allowing an optional batch wrapper. This structural R2 guard rejects the old many-`data.set` quadratic implementation even where a generous timing ceiling alone would not catch it.

Warm 10k table logic (`Drawing=false`) is **1.1400 ms**; with the CPU rasterizer it is **153.1525 ms**. The proposed 16.7 ms idle target is met by the warm logic workload and **not met by this CPU-rasterized workload**. This does not establish GPU or interactive-host frame time.

At 10k rows, cold `Columns` costs 35.8049 ms / 47,663,488 managed bytes; plain `Visible` costs 0.8135 ms / 632,832; uncached public `Rows` for 20 rows costs 1.3699 ms / 1,883,320, and for every row 1029.9168 ms / 941,759,800. Filter and sort start with cold row caches and cost 1357.6075 / 1189.8498 ms, allocating 953,919,512 / 952,602,328 bytes. This supports row-description/schema work as a cold-path allocation hot spot; the cached idle frame allocates only 603,904 bytes. These independent scopes are not an additive trace of a frame.

Other 10k allocation hot spots are editor open (506,565,800 bytes), full validation (381,181,176), one-cell edit (421,173,168), paste (470,924,104), and one-edit replay (886,384,320). Allocation churn is not peak/retained memory; native memory remains unmeasured.

## Before/after evidence

The original implementation exhausted the BRUTAL ImString arena on the 10k idle frame (`ArgumentOutOfRangeException: Cannot grow beyond MaxCapacity`, `StringBuilder.GrowBy` -> `ImString.op_Implicit` -> `DataTableView.DrawCell` -> `HeadlessHost.Frame`); a later case aborted natively. It also failed after enough combined 1k frame trials. Those failures have no successful median. The first 10k Drawing=false diagnostic took 4683.8598 ms, but that single sample is not represented as a median. Original 1k frame medians were obtained in fresh processes per case; current frame medians all completed in a single process.

The owner supplied a list clipper, view caches, batched schema-child lookup and a many-values command. This card changes no library source. Full-column 10k fill was deliberately deferred while that replacement was pending; no old 10k-fill number was invented.

| Rows | Scope | Before median ms | Current median ms |
| ---: | --- | ---: | ---: |
| 1,000 | fill | 3533.2580 | 401.8040 |
| 1,000 | idle-false | 231.7486 | 0.5104 |
| 1,000 | idle-true | 383.0810 | 152.1224 |
| 10,000 | columns | 69.4659 | 35.8049 |
| 10,000 | rows-all | 4284.4678 | 1029.9168 |
| 10,000 | filter | 4415.6878 | 1357.6075 |
| 10,000 | sort | 4286.9322 | 1189.8498 |
| 10,000 | paste | 2354.5432 | 729.1455 |

The source actually measured is identified by SHA-256, without depending on a branch name:

| File | Before SHA-256 | Current SHA-256 |
| --- | --- | --- |
| `src/Brutal.Authoring.Data/DataDocument.cs` | `2afc195c874defa95f4cb6339203c298d90010bad27b8dad460dab2bab63c2e2` | `112e4ff371a526c61935b579cf8ad62b578e17e6b86feb93b5c4027481af56d9` |
| `src/Brutal.Authoring.Data/JsonSchemas.cs` | `142bf6fab5b17be398a57fa76354bb18beb9d41e54b7ec957cd4042b7c488ff4` | `950813458bc84483ef5f162d48d2d60e92c372600082161dbcd99af397853252` |
| `src/Brutal.Authoring.Editor/DataEditing.cs` | `1b8d40329cd6eb771aaefd362d9ffdb1c007a74041240432111f434c3146cc70` | `0fb5ecf36534babf104ea8017570b4fcfb259fe875e3eefed2d2acfae667ba02` |
| `src/Brutal.Authoring.Editor.ImGui/DataTableView.cs` | `67d3a7449868343877e63df66b963c3e81db17d24cc87db27d75b5a494b045e1` | `78bf79caca47a71f95bf1348e074f7684aeb68e82a99ca00e44a0d9ca5a5c892` |
| `src/Brutal.Authoring.Editor.ImGui/HeadlessHost.cs` | `3ce08df748af78cbe4f932a52118a58026b9b30dcb4bdea42b4c69f853245a1c` | `3ce08df748af78cbe4f932a52118a58026b9b30dcb4bdea42b4c69f853245a1c` |
| `src/Brutal.Authoring/DocumentSession.cs` | `67245649c5ea4d972324473a2810aa554d17693d465a701d7e821c1479d0f7da` | `67245649c5ea4d972324473a2810aa554d17693d465a701d7e821c1479d0f7da` |

## Determinism and independent fixture check

Seed `330031` produced identical hashes in two independent temporary directories at each size. The generated data sizes are 650,890 / 6,518,993 bytes; no generated data is checked in.

| Output | SHA-256 |
| --- | --- |
| schema.json (both sizes) | `58f8b07b3e3e0ec1b91972c613e091f3da3a862e72959674926b31f806f4fd03` |
| rows-1000.json | `88c9b59e419c5eff92125f9f3bf6546973466aadfc8d6ba7820b5a774373b636` |
| rows-10000.json | `d698d30124f5996ee07d610eeb6aabcd8067dae82ca4da964c5d9e35c59a4623` |

Complete files pass the already-installed `jsonschema.Draft202012Validator`; a bad enum and an out-of-range referenced integer are independently rejected. This author-time cross-check adds no runtime dependency. Seeds `331033` / `340033` selected 20 rows at 1k / 10k: 20/20 passed at each size for scalar count, two tags, reverse numeric key, zero-padded ID, enum and score range.

Numerical self-check seed **330035**: **20/20** sampled measurement records agree with six verified trials, the third sorted measured value, the README and fixed code ceiling. Successful samples are logged only after their complete correctness oracle passes. A finding's record matching is not a passing budget.

```text
10000:fill: 6/6 verified trials; third(sorted five)=2667.7235 ms; frozen ceiling=26678 ms; table/code agree (latency finding, not budget PASS)
10000:save: 6/6 verified trials; third(sorted five)=128.4695 ms; frozen ceiling=1285 ms; table/code agree
10000:filter: 6/6 verified trials; third(sorted five)=1357.6075 ms; frozen ceiling=13577 ms; table/code agree
10000:replay: 6/6 verified trials; third(sorted five)=1394.3418 ms; frozen ceiling=13944 ms; table/code agree
10000:read: 6/6 verified trials; third(sorted five)=24.4993 ms; frozen ceiling=245 ms; table/code agree
10000:first-true: 6/6 verified trials; third(sorted five)=191.2270 ms; frozen ceiling=1913 ms; table/code agree
1000:validate: 6/6 verified trials; third(sorted five)=64.5993 ms; frozen ceiling=646 ms; table/code agree
1000:first-false: 6/6 verified trials; third(sorted five)=36.6964 ms; frozen ceiling=367 ms; table/code agree
1000:rows20: 6/6 verified trials; third(sorted five)=1.3776 ms; frozen ceiling=100 ms; table/code agree
10000:validate: 6/6 verified trials; third(sorted five)=640.1610 ms; frozen ceiling=6402 ms; table/code agree
10000:rows20: 6/6 verified trials; third(sorted five)=1.3699 ms; frozen ceiling=100 ms; table/code agree
1000:undo: 6/6 verified trials; third(sorted five)=0.1153 ms; frozen ceiling=100 ms; table/code agree
1000:sort: 6/6 verified trials; third(sorted five)=82.0013 ms; frozen ceiling=821 ms; table/code agree
1000:columns: 6/6 verified trials; third(sorted five)=34.4593 ms; frozen ceiling=345 ms; table/code agree
1000:idle-true: 6/6 verified trials; third(sorted five)=152.1224 ms; frozen ceiling=1522 ms; table/code agree
1000:fill: 6/6 verified trials; third(sorted five)=401.8040 ms; frozen ceiling=4019 ms; table/code agree
10000:columns: 6/6 verified trials; third(sorted five)=35.8049 ms; frozen ceiling=359 ms; table/code agree
10000:paste: 6/6 verified trials; third(sorted five)=729.1455 ms; frozen ceiling=7292 ms; table/code agree
1000:replay: 6/6 verified trials; third(sorted five)=138.5884 ms; frozen ceiling=1386 ms; table/code agree
10000:idle-true: 6/6 verified trials; third(sorted five)=153.1525 ms; frozen ceiling=1532 ms; table/code agree
```

After calibration, fill and paste at both sizes were run again with the R2 bulk-command oracle: **4/4 cases, 24/24 verified trials**, with the extra skipped finding fact reported separately. Public journal batches were recursively flattened, then exactly one `do` leaf named `data.set_many` was required. These checks execute after the stopwatch and leave the calibration scopes unchanged. Reverting to many `data.set` leaves fails this structural guard even if a generous timing ceiling is not exceeded.
