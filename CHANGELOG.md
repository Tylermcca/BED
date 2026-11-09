# Changelog

Notable changes to BED are recorded here, following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Prepared changes held for a later release; not included in the 0.2 release candidate.

### Added

- `bed-cli ai-setup` prints the same connection examples as the editor, with optional JSON output.
- The release check verifies the packaged AI guide and checks setup examples when the CLI advertises them.

### Changed

- A selected adapter's folder layout takes precedence over the standard layout; if the adapter returns no layout, the standard layout is used.

### Fixed

- Keep Help visible on narrow menu bars by shortening optional heading and status text.
- Escape closes the start screen's focused AI help window and returns focus to its Help button.

## [0.2.0]

Release candidate summary. No final release date or tag is announced here.

### Highlights

- Enter supported check options in the Checks panel, with invalid choices blocking Run and changed choices marking old results stale.
- Find assistant connection examples in Help with a data file open or on the start screen, with a guide to finding data, previewing edits and running checks.
- Open project folders using an adapter-recognised layout as well as the standard data and schema folders.
