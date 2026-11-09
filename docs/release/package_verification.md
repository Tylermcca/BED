# Manual local package verification

Status: preparation, not an activated workflow or a release recipe approved for this product. No manifest is supplied. Public name, package IDs/scope, version, repository identity, source export/history and licence decisions remain with the owner. The current source properties do not adopt the packaging proposal. This route deliberately refuses missing or placeholder inputs rather than silently using that proposal.

MIXED design: local-folder-feed and clean consumer isolation follow `docs/release/packaging_dry_run.md`; the required reviewed manifest, exact archive allowlist and receipts are original. Role separation follows `docs/research/editor_architecture.md`. Historical successful packaging is not a current pass.

## What this verifies

After separate execution authorization, the script takes an explicit source-file closure and dependency-feed file list, hashes them before use, copies only those files into a new outside-repository work folder, and uses one exact installed SDK. It restores/builds only selected package projects, executes only named test filters, packs those projects, checks each resulting ordinary nupkg, and compiles a fresh consumer with exact PackageReference versions for every selected package. Its assets must contain packages only, with all produced IDs at the exact chosen version. It never launches the consumer, a UI or a game.

The archive gate requires the exact reviewed entry set, actual DLL/XML documentation, nonempty README and authors/description, exact ID/version/repository URL/revision, licence metadata and byte-pinned notices. No identity or notice list is inferred. It records hashes and sizes of every entry. A licence expression is not a legal compatibility determination; the manifest reviewer must select every applicable notice, including upstream obligations. Omitting a required notice from the reviewed contract is not detectable by a generic checker.

A pass means **local build-only package smoke**, not runtime/API behavior, pixel rendering, public source/history cleanliness, legal clearance, signing, reproducible binary identity, or permission to publish. No remote lookup, package upload, release job or token is included.

## Required reviewed manifest, format 1

Supply UTF-8 JSON with exactly these fields. The script validates structure; a review string is an audit reference, not proof of approval. Only an authorized maintainer supplies the manifest after reviewing its entire contents. Do not insert the synthetic test fixture as a real manifest.

- `format`: integer 1.
- `review`: nonempty reference to the actual approval/review record, without placeholder words.
- `source_revision`: exact 40-character lowercase revision. Local execution treats it as a supplied assertion plus the pinned file closure; it does not run git or prove that the checkout is that commit. The workflow separately requires equality to GITHUB_SHA.
- `sdk`: exact installed release SDK, such as the three-number format, with no roll-forward. No SDK or workload is installed. Missing framework/runtime/workload packs fail.
- `repository_url`: approved public HTTPS repository URL, with no credentials/query/fragment. No default URL is chosen.
- `version`: approved exact SemVer, optionally prerelease. No version is chosen. Trial 0.0.0-local values and placeholder strings are refused.
- `source_files`: map of POSIX checkout-relative path to lowercase SHA256 for every required source, project, build configuration, fixture, schema, notice and original consumer input. Include complete project-reference/import/build-resource closure. Only these named files are read/copied. Excluded directory segments include art, reference, .git, bin, obj, codex and node_modules; binary source inputs are refused. Do not include private or licensed content without separate authorization. A missing closure file fails later rather than searching other checkouts.
- `dependency_files`: nonempty map of flat local `.nupkg` filenames to SHA256 for the complete already-available dependency closure, including test tooling. There are no feeds/versions installed or fetched by this card. The future operator supplies a separately authorized local folder; it must not contain a produced-package filename. The script copies only named, verified files, never the user's cache wholesale.
- `packages`: nonempty list of objects, each with exactly `id`, `project`, `entries`, `notices`. `id` is the approved exact package ID; `project` is a pinned `.csproj` path; `entries` is the exact list of ordinary nupkg file entries, including NuGet bookkeeping; `notices` maps all required licence/notice entry paths to expected SHA256. Names are relative, case-unambiguous and traversal-free. No wildcard or broad package discovery. The existing source metadata must already produce this ID/version/repository; this script does not override or adopt metadata. Required README and DLL/XML entries belong in `entries`. Symbol companions are outside this first route: if pack emits extra archives, the gate refuses. Enabling symbol verification needs a separate reviewed extension, not silently discarding them.
- `tests`: nonempty list of objects with exactly `project` and `filter`. Each project is a pinned `.csproj`; each filter names the specific affected FullyQualifiedName classes/cases. This is not a permission to select broad suites. The owner must review discovery side effects and scope. No full-run escape marker is inserted.
- `consumer`: exactly `framework` (managed `netN.N`) and `files` (nonempty list of pinned original `.cs` sources). They are copied to Input0.cs, Input1.cs etc. The generated executable-shaped project has explicit Compile items and exact bracketed PackageReferences only, no ProjectReference. It is compiled, not launched; supply a valid Main/top-level entry point and useful API usage. No default game or adapter is chosen.

The manifest has no arbitrary shell-command field. Build inputs are nevertheless trusted executable MSBuild/test code, not untrusted data. Hash pinning prevents accidental drift, not malicious build logic. Source targets can themselves access files or network; this tool is **not an OS sandbox**. Use a reviewed source closure and an isolated, policy-constrained runner. Do not use the script to override an outstanding SDK, source-read, installation or execution refusal.

## Local usage, only when separately authorized

Structure-only preparation (no SDK, source/package reads, work creation or smoke claim):

    python -B tools/package_consumer_smoke.py --manifest REVIEWED_MANIFEST.json

Future execution, not run for this delivery:

    python -B tools/package_consumer_smoke.py --manifest REVIEWED_MANIFEST.json --repo SOURCE_CHECKOUT --feed REVIEWED_LOCAL_FEED --work NEW_OUTSIDE_WORK_FOLDER --execute

The work folder must not exist, must have an existing parent, and must be outside both checkout and dependency feed. No cleanup or overwrite is performed. Logs, source copy, feed copy, obj/bin, caches, temp, CLI home and receipts stay below it. Root empty Directory.Build.props/targets/Directory.Packages.props stop outer inheritance; source's reviewed settings remain intact. The source copy's global.json is deliberately replaced with the manifest SDK and rollForward=disable; this is the sole generated source-configuration override and is recorded in this contract, not an unreported change to original bytes.

NuGet.Config clears sources/fallback folders; every restore explicitly selects the local copied feed and private package cache with audit disabled. Consumer restore starts with a separate empty cache. Certificates/telemetry/first-time setup notifications are suppressed; environment inheritance is restricted to platform executable lookup basics. No secret is requested. These flags are not proof that arbitrary reviewed build targets cannot contact a remote service.

Each restore/build/pack has a 600-second limit; SDK identity 30 seconds; each filtered test invocation 720 seconds and --blame-hang-timeout 10m. Commands and exit codes/output paths are recorded before/after execution in receipt.json. On failure or timeout, stop and retain the workspace. Python's subprocess timeout terminates its direct owned process, not a proven entire build-process tree; do not blindly retry, delete output or probe/kill machine-wide processes. The operator must resolve owned-process state under their approved runner policy. No shared compiler or reusable MSBuild node is intentionally requested.

The script does not claim deterministic byte-for-byte pack reproduction or enforce transitive dependency version policy beyond the hashed feed and recorded consumer resolution. Review the receipt's exact resolved package IDs/versions and archive entries. No remote vulnerability audit is performed.

## Workflow preparation and activation prerequisites

The prepared workflow, parked at `docs/release/package-verification.workflow.yml.txt` until a runner is reviewed, is manual-only, with read-only contents permission, pinned checkout action, credentials not persisted, no submodules/LFS, no artifact upload and no publication step. It runs only for the default branch. It is delivered as an outbox patch, not installed or activated.

Before accepting or enabling it, the owner must approve the repository export, manifest, runner and execution. Provision a dedicated **ephemeral isolated Windows x64 runner** with the labels shown in the workflow, installed Python/exact SDK/packs and the reviewed local feed. The label alone does not create or prove isolation; never add it to a personal always-on machine. Configure the `package-verification` environment's approval/protection rules separately; merely naming it in YAML does not create those protections. No runner, account, token, credential, network policy or environment setting is provisioned here.

A hosted runner without the local feed/SDK prerequisites will not magically work. There is deliberately no setup/install/download workaround. The manual workflow requires manifest and local-feed inputs. Inputs are passed through environment variables, not interpolated into shell commands. It first runs only the script's standard-library synthetic tests, then checks manifest revision against GITHUB_SHA and invokes explicit execution. Missing prerequisites fail instead of reporting a skipped package stage as green. A non-default-branch job is marked skipped and is not a verification pass. A 45-minute job cap bounds the overall route; choose a small reviewed package/test scope that fits it.

Receipts remain in the runner's work directory. Preserve them before disposal through a separately approved retention mechanism. This card adds no upload, including private package archives/logs. PR validation and any privileged publishing workflow remain separate future decisions, not hidden side effects of this manual job.

## Checks for this preparation

    python -B tools/test_package_consumer_smoke_a289.py

The tests use only original synthetic XML/ZIP/JSON in temporary directories and mock the no-execute subprocess boundary. They do not run an SDK, restore/build/test/pack, compile, launch a consumer, read real dependencies or exercise GitHub Actions. In the worker delivery they run with TEMP/TMP under the assigned outbox. Actual SDK command compatibility, YAML action/runner integration, native discovery, package contents and end-to-end consumption remain UNRUN until separately permitted.
