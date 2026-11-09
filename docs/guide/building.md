# Review a build before exporting it

BED's build and export components are host APIs. They do not yet provide a universal one-click Build command for any folder. Your project's adapter must declare the build command, target, inputs and output mapping. BED does not guess these from filenames or install a toolchain for you.

## Before a build

- Save the version you intend to build. The build host refuses unsaved input.
- Confirm that the adapter declares the right project and target.
- Run the project's required checks for the exact captured inputs. Old receipts are not made current by a recent timestamp.
- Complete origin review and identify the required notices. A clean technical check does not grant distribution rights.

Keep your last known-good export until the replacement has been reviewed. A staging success is not publication and must not be presented as one.

## Read the outcome

The supervised host uses a fresh owned output folder and a bounded log. A nonzero exit, deadline, cancellation or unexpected output must be reported as a failure, not treated as a successful partial package. Missing or changed outputs must not be waved through merely because an executable exists.

The export staging layer checks each declared artifact against its pinned hash and length, including required notices. These byte checks cannot prove that the game starts, that a dependency declaration is complete, or that the notice text is legally sufficient. Those remain separate review responsibilities.

## Review the unpacked result

Before sharing a build, its owner should verify the complete inventory and notices, then perform the project's documented startup/smoke check on the unpacked result. Record what was actually built and tested. A synthetic helper, an in-process test or a successful compile is not an exported-game smoke result.

The public SceneTool sample demonstrates scene tooling; it is not a general exported game. Sample export and linked-game integrations are still being connected. This guide does not claim an owner smoke run, a finished release workflow, or approval to publish a package.

See [content rights](content_rights.md), [Play](playing.md) and the project's own build instructions. Never include private source, credentials or unrelated files just to make a package run.

## Source reference

- [Build profile declarations](../../src/Brutal.Authoring.Editor/GameBuildProfile.cs)
- [Required-check gate](../../src/Brutal.Authoring.Editor/ExportGate.cs)
- [Origin review](../../src/Brutal.Authoring.Editor/ExportOriginReview.cs)
- [Pinned staging checks](../../src/Brutal.Authoring.Editor/ExportStaging.cs)
