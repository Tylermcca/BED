# Good first issues

Draft shortlist: twelve small, independent contributor tasks. These are proposals, not posted issues, assignments or approval to publish. Check current source and maintainer ownership before starting. The list was checked against selected working files when main identified 67dcf7637384f2eab7ce84a7b0c77fd5280bf4ee; it is not a clean-tree or compiled-result claim.

Sizes estimate authoring effort: XS = 1–2 hours; S = 2–4 hours, excluding review, unavailable tooling and execution approval. Acceptance tests below are future review criteria, not results from this document. Use original synthetic fixtures, keep existing assertions and preserve failure evidence. Do not install a dependency, activate CI, change a release decision, inspect excluded inputs or use a refused execution route to complete an item. Where an existing checker walks source trees, its example tests must use only a miniature self-authored tree.

This list improves delivered tools and guidance rather than rebuilding them. It does not include a new package product, compiler API baseline, runtime architecture, renderer, source-export approval or release workflow. Those require separate scope/decisions. No documentation/source check certifies compiled behavior, package contents, legal clearance, history cleanliness or permission to distribute.

Provenance: **MIXED**. Existing repository policies and APIs supply the contracts; these small task scopes and acceptance plans are original proposals. File names below are repository-relative; '(new)' means a proposed path, not an existing implementation. Source anchors are evidence locations, not hyperlinks or a promise that line numbers will never move.

## GFI-01: Replace a stale guide-hold explanation without lifting the hold

**Size:** XS, 1–2 hours.

**Files:** `tools/public_files.py`; `tools/test_public_files.py`.

**Current gap:** The selector still explains the guide hold as a stale delivery-reference problem, although the guide already points to the first-document tutorial.

**Evidence:** tools/public_files.py:41; docs/guide/api_overview.md:3.

**Small change:** Update that one reason to describe current maintainer review still needed. Keep needs-change until the maintainer separately approves the public boundary; do not turn the guide public to make a test pass.

**Acceptance test (future):** Add a small classifier fixture proving the reason no longer claims the missing tutorial, the guide is still withheld, and unknown/private/other held candidates retain their existing classification. No whole-checkout scan or export.

## GFI-02: Explain source-check results with a tiny neutral example

**Size:** S, 2–4 hours.

**Files:** `docs/contributing/source_check_results.md (new)`.

**Current gap:** The detailed operator explanation is tied to an internal census; a portable contributor-facing companion at the named new path is absent.

**Evidence:** tools/public_files.py:81; tools/public_files.py:277.

**Small change:** Document public/private/needs-change, excluded coverage, and exit 0/1/2 using an entirely self-authored miniature file tree and synthetic marker configuration. An explicit hold can remain even when --check returns 0. Never copy a private marker list or census.

**Acceptance test (future):** A reviewer can map all three exit outcomes to main/classify and reproduce the example only inside its own temporary fixture. Assert held files remain held and no matching private text is printed. State that no result certifies history, notices or permission to publish.

## GFI-03: Make the root entry useful to a new library contributor

**Size:** S, 2–4 hours.

**Files:** `README.md`.

**Current gap:** The root entry still leads with private integration context and local workflow notes rather than a neutral way into the existing guides.

**Evidence:** README.md:3; README.md:20; CONTRIBUTING.md:24.

**Small change:** Replace only the introductory/entry-route prose with the library purpose, links to CONTRIBUTING.md, samples/README.md and the existing first-document guide. Preserve licence/notices and explicitly retain working-name/unreleased status. Keep detailed source build instructions in the existing contributor guide.

**Acceptance test (future):** Relative links resolve to existing files; no private machine path or particular consumer name remains in the changed prose; no package ID/version/install command or universal support claim is invented. Existing sample index and package-first preparation are linked, not rewritten.

## GFI-04: Separate extension guidance from historical proof narration

**Size:** XS, 1–2 hours.

**Files:** `docs/api/extension_points.md`.

**Current gap:** The public introduction opens with internal assignment/snapshot narration and a private acceptance location.

**Evidence:** docs/api/extension_points.md:3; docs/api/extension_points.md:5; docs/api/extension_points.md:25.

**Small change:** Revise the introduction only: say what the examples require and what they demonstrate, distinguish historical example compilation from current behavior, and point to public source contracts. Keep all example code and honest verification limitations intact. This is editorial work, not API-reference generation.

**Acceptance test (future):** Byte-compare all existing example blocks before/after; they must be identical. Every new link resolves locally, Configure remains part of the adapter example, and no old compilation is presented as a fresh result or release guarantee.

## GFI-05: Add a worked header-report walkthrough

**Size:** XS, 1–2 hours.

**Files:** `docs/contributing/header_policy.md`.

**Current gap:** The policy gives a manifest and rules, but no small worked result showing why missing headers and incomplete coverage mean different things.

**Evidence:** docs/contributing/header_policy.md:13; tools/report_source_headers.py:46; tools/report_source_headers.py:66; tools/report_source_headers.py:81.

**Small change:** Add original example input/output for present, missing, unsupported and unreadable, with the exact coverage_complete and policy_enforced meanings. Use synthetic paths only. Explain that the reporter never rewrites files and findings do not impose a mandatory gate.

**Acceptance test (future):** Parse the example JSON and compare its fields/statuses to report. The example must keep policy_enforced false, allow coverage_complete true for present/missing-only lists and show false for unsupported/unreadable. No DCO adoption, bulk header edit or licence clearance.

## GFI-06: Show one complete compatibility-review entry

**Size:** XS, 1–2 hours.

**Files:** `docs/contributing/compatibility_review_example.md (new)`; `docs/api/compatibility_changes.md`.

**Current gap:** The ledger lists required fields but has no complete neutral worked review that a first contributor can follow.

**Evidence:** docs/api/compatibility_changes.md:10; docs/guide/api_support_policy.md:21; docs/guide/api_support_policy.md:43.

**Small change:** Write one explicitly hypothetical API rename example covering old/new declarations, source/binary/data impact, migration, unrun tests and reviewer decisions still pending. Add a link from the ledger. Do not change a real API, baseline, enum, version or support policy.

**Acceptance test (future):** Check the example against every required field in the ledger. It must not say a hypothetical change shipped, select a package/API version, or imply that the two-source tripwire is a complete compiled API baseline.

## GFI-07: Document what the boundary checker does not prove

**Size:** S, 2–4 hours.

**Files:** `docs/contributing/boundary_check_results.md (new)`.

**Current gap:** The source checker exposes explicit scope, metadata-only mode and incomplete coverage, but lacks a short contributor-facing worked guide at the named path.

**Evidence:** tools/check_generic_boundary.py:138; tools/check_generic_boundary.py:148; tools/check_generic_boundary.py:235; tools/check_generic_boundary.py:255.

**Small change:** Explain the version-1 scope, known identity roots, support projects and source exclusions using a tiny synthetic project graph. Distinguish metadata-only results, optional separately reviewed lexical input and actual compiled/package boundaries. Do not change the live scope or scan held sources.

**Acceptance test (future):** The examples map to load_scope/inspect_repo and CLI exits: metadata-only coverage stays incomplete, a synthetic forbidden reference is a finding, and neutral/unknown identities are not claimed universally safe. Use only a self-authored miniature tree, no live source census, assembly load or package check.

## GFI-08: Disclose unverified fragments in local Markdown links

**Size:** XS, 1–2 hours.

**Files:** `tools/check_docs_drift.py`; `tools/test_check_docs_drift.py`.

**Current gap:** A file-plus-fragment link is reduced to its file path; the existing-file check does not establish that the heading fragment exists. Same-page fragments already appear in notes.

**Evidence:** tools/check_docs_drift.py:356; tools/check_docs_drift.py:362.

**Small change:** Retain the existing file check and add an explicit fragment-unverified note for file#fragment links. Do not implement a new heading-slug dialect or silently certify the fragment. Preserve external-link no-fetch behavior and existing missing-file failures.

**Acceptance test (future):** On a miniature synthetic repository, existing.md#unknown records file existence plus the unverified fragment; missing.md#x still reports a missing file; an external URL is never fetched. Do not run the checker over an unrestricted live source tree.

## GFI-09: Count relative Markdown source links in the document gate

**Size:** XS, 1–2 hours.

**Files:** `tools/check_doc.py`; `tools/test_check_doc_links.py (new)`.

**Current gap:** The link count currently recognizes HTTP URLs and backticked file references, but not an ordinary relative Markdown file link.

**Evidence:** tools/check_doc.py:12; tools/check_doc.py:21.

**Small change:** Count syntactically valid relative Markdown file-link occurrences for min_links while preserving existing URL/code-reference behavior and section/mention checks. Be explicit that counting does not resolve files or validate headings. No network lookup.

**Acceptance test (future):** Pure problems(...) string tests: a relative guide.md link satisfies one required reference; plain prose does not; existing HTTP links are not newly double-counted; section/mention failures remain unchanged. Keep all existing assertions.

## GFI-10: Report conflicting notice destination names using synthetic inputs

**Size:** S, 2–4 hours.

**Files:** `tools/notice_name_collisions.py (new)`; `tools/test_notice_name_collisions.py (new)`.

**Current gap:** The current target deduplicates notice filenames. That does not demonstrate safety when different notice bytes map to the same destination; no actual-package collision is asserted.

**Evidence:** build/Notices.targets:19; build/Notices.targets:21; build/Notices.targets:25.

**Small change:** Provide a small report-only helper over an explicit list of caller-supplied destination keys and source bytes/hashes. Distinguish identical duplicates from differing-content collisions. Do not scan package caches, rewrite notices, wire a build target or perform a licence audit in this issue.

**Acceptance test (future):** Self-authored tests cover unique keys, identical duplicates and same-key/different-byte collision diagnostics. Preserve every input; report labels/hashes rather than notice text. Treat actual pack wiring and distribution-specific review as separate work, never as proved by the helper.

## GFI-11: Teach maintainers to read an incomplete package receipt

**Size:** XS, 1–2 hours.

**Files:** `docs/release/receipt_reading.md (new)`.

**Current gap:** The execution guide describes retention/timeouts but has no small annotated incomplete/timeout receipt walkthrough at the named path.

**Evidence:** tools/package_consumer_smoke.py:215; tools/package_consumer_smoke.py:229; tools/package_consumer_smoke.py:239; tools/package_consumer_smoke.py:316.

**Small change:** Show explicitly synthetic incomplete and failed/timed_out records using the actual commands/argv/cwd/timeout_seconds/elapsed_seconds/log fields. Explain that missing terminal evidence is unknown, and passed-build-only-smoke is compilation only, never consumer execution.

**Acceptance test (future):** Example JSON parses and matches field names in run/command. No example is called real evidence, no argv is executed, and timeout advice says preserve records and resolve owned lifecycle through the operator rather than blindly retry, discover/kill processes or delete outputs. Do not activate a workflow.

## GFI-12: Add an offline checklist for package-first input refusals

**Size:** XS, 1–2 hours.

**Files:** `docs/guide/package_first_use_inputs.md (new)`; `docs/release/package_verification.md`.

**Current gap:** The preparer has useful refusal messages, but there is no focused reader checklist at the named path for obtaining valid reviewed inputs without mistaking preparation for a release.

**Evidence:** tools/prepare_package_first_use.py:26; tools/prepare_package_first_use.py:34; tools/prepare_package_first_use.py:45; tools/prepare_package_first_use.py:96.

**Small change:** Map missing/placeholder review input, unknown Data-role ID, wrong source hash, unresolved README slot and unacceptable tutorial URL syntax to what the owner must supply. Link it from the execution guide. Never fill in a product name, package ID/version, feed or public URL on the owner's behalf.

**Acceptance test (future):** Quote/map the actual need/reviewed/tutorial_url checks. Preserve prepared-text-only, SDK_run/packages_read/consumer_compiled/consumer_run false and approval_inferred false. State that URL syntax checks neither fetch nor verify publication; no SDK/install/feed/CI action is part of this documentation issue.

## Review boundary

These scopes are deliberately small. A documentation example is not a new implementation or a captured successful run. Keep source-extraction and lexical limitations explicit, do not auto-accept changed compatibility output, and preserve every upstream notice. No item selects a public name, version, package set, support promise, contribution agreement, account or reporting destination. Verify any additional integration/owner decision separately; don't silently expand a task to make a demonstration pass.
