# ImCode, vendored

- **What:** ImCode, a Dear ImGui code-editor widget for BRUTAL's ImGui bindings, by aproposmath.
- **Upstream:** https://github.com/aproposmath/ImCode
- **Commit:** `98e69303054540c43fd67909b6a30975a19aca8b` (2026-10-15)
- **Vendored:** 2026-11-04, as a plain copy (no submodule), so an offline build works.
- **Licence:** MIT, `Copyright (c) 2026 aproposmath` (`LICENSE`, unchanged).
- **What's here, unchanged from that commit:** `LICENSE`, `README.md`, `Directory.Build.props`, `.editorconfig`,
  `docs/API.md`, and the two projects the editor uses: `src/ImCode.Text` (the text model, no UI) and `src/ImCode` (the
  widget). Left out: `src/ImCode.Workspace` (tabs, file dialogs and settings files, which read and write the disk), the
  demo, the tests and the screenshot.
- **Dependencies:** `Ahwoo.BRUTAL.Core` 2026.10.1 and `Ahwoo.BRUTAL.ImGui` 2026.10.2, the versions this repo already
  pins in `Directory.Packages.props`. Nothing else.
- **Checked before use (2026-11-04):** no file, network, process or native-interop code in the two projects (only
  `ImCode.Workspace`, not vendored, touches files); no telemetry. Its own `Directory.Build.props` keeps its build
  settings apart from this repo's.
- **Used by:** `Bed.Editor.Text`, the Text panel (`JsonTextPane`), and recorded in `THIRD-PARTY-NOTICES.md`.
  The core editor, `Bed.Editor.ImGui`, doesn't reference it (a test checks). A release carries this licence
  as `licenses/ImCode-LICENSE.txt` (`tools/package_release.py`).

## ImCode packaging

- **It ships inside `Bed.Editor.Text`, not on its own.** ImCode isn't on NuGet, so that package's project
  references it with `PrivateAssets="all"` and packs `ImCode.dll` and `ImCode.Text.dll` into its own `lib/net10.0/`,
  with this licence as `licenses/ImCode-LICENSE.txt` and a `THIRD-PARTY-NOTICES.txt` naming it. No ImCode package is
  made or published from here.
- **If ImCode's author publishes an official NuGet package, switch to depending on it** (the user plans to ask them):
  replace the two project references with that package, drop the DLLs and licence packed by hand, and stop vendoring
  this copy.

To update: copy the same paths from a newer upstream commit, unchanged, and change the commit and date above.
