# Content rights: declared origins and protected names

The editor can't tell whether a picture, model, sound or font is someone else's work. No tool can. What it does is make every asset's origin declared and traceable, and refuse input that plainly says it belongs to someone else. Think of it as a seatbelt, not a detector.

**INVENTED:** the three declarations, the refusal rules and the protected-names match. **BORROWED:** where each format keeps its rights tags (the PNG, EXIF, XMP, glTF and OpenType specifications). **MIXED:** the declaration lives in the asset's existing sidecar record, and the check uses the existing registered-check system.

## What it does

**Every import declares an origin.** An asset is imported with one of three declarations:

| Declaration | Needs | Meaning |
|---|---|---|
| `ours` | nothing more | Made by this project. |
| `licensed` | the licence's name and the source | Someone else's work, used under a licence. |
| `publicDomain` | the source | Public domain, or dedicated to it (such as CC0). |

With no declaration, or a declaration missing what it needs, the import is refused and nothing is written. The declaration is stored in the asset's sidecar (`<file>.asset.json`), beside its GUID. It can be changed later only through the `asset.declare_origin` command, which can be undone like any other edit.

**The file's own rights tags are read and judged.** The import reads these tags:

| Format | Tags read |
|---|---|
| PNG | `tEXt`, `zTXt` and `iTXt` chunks named `Copyright` or `Author`, and XMP `dc:rights` |
| JPEG | EXIF Copyright (0x8298) and Artist (0x013B), and XMP `dc:rights` |
| glTF (`.gltf`, `.glb`) | `asset.copyright` |
| TrueType / OpenType (`.ttf`, `.otf`, `.ttc`) | `name` table IDs 0 (copyright), 13 (licence) and 14 (licence URL) |

A copyright or licence tag that has text in it, or any tag that says "©", "(c)", "copyright" or "all rights reserved", claims rights for someone. If such a tag is found and the asset is declared `ours` or `publicDomain`, the import is refused, and the refusal quotes the tags:

```text
declared ours (made by this project), but the file's own rights tags say otherwise: png tEXt Copyright "© 2026 Another Studio. All rights reserved.". If it's someone else's work, declare it licensed with the licence's name and where it came from; ...
```

A `licensed` asset is never refused for its tags. They're kept in its record and shown beside the declaration, so the licence can be checked against them.

Two kinds of tag don't count against a declaration:
- **Public domain:** a tag on a `publicDomain` asset that is only a plain public-domain or CC0 statement, such as "CC0 1.0 Universal public domain dedication". A tag that says anything else, such as "not public domain" or "Copyright 2020 Someone; public domain elsewhere", still refuses.
- **Your own name:** a tag on an `ours` asset whose only words, apart from "©", "copyright", "all rights reserved", years and the like, are one of the project's own rights holders. Each name must be at least three letters and matches whole words only, so "© 2026 Our Studio" clears for "Our Studio" but "© Big Co, ripped by Our Studio" doesn't. An adapter supplies these names through `IProjectAdapter.OwnNames`, and the command line through `--own-name` (repeatable).

Re-importing a changed file (`AssetRecord.Reimport`) reads its tags again. If the new tags contradict the declaration, the re-import is refused and nothing is written.

The tag readers are bounded. They cap the file size (64 MiB) and decompression (1 MiB per chunk, 4 MiB and 32 compressed chunks per file, and only the text chunks they read are ever decompressed). They also cap XML, refuse XML DTDs, and never follow a chain that could loop. A malformed file never causes a crash or a hang.

**An incomplete scan refuses ours and public domain.** The scan is incomplete whenever part of the file couldn't be read: a broken compressed chunk, bad XML, an EXIF or font record pointing outside the file, or text in an encoding the reader can't decode. It's also incomplete when a cap was reached: the decompression budget, more than 32 compressed chunks, more than 64 rights tags, a tag longer than 16,384 characters, or a font collection of more than 64 fonts. An unread tag might say the file is someone else's, so an incomplete scan refuses `ours` and `publicDomain` and says what couldn't be read. For `licensed` it's a warning, and the reason is kept in the record (`rightsUnread`). Judging always reads a tag's whole text; only the display is shortened.

**The content rights check** (`rights.assets`) lists every asset with its origin, licence, source and the tags its file carries now. It flags:
- assets with no declared origin (imported before this feature existed);
- files that look like assets but were never imported;
- declarations missing what they need;
- tags that contradict a declaration, including a file swapped out after it was imported.

Old projects still open. An undeclared asset is flagged by the check, never refused on opening.

**Protected names** are names or phrases a game must never use, such as a trademark it doesn't own or a real person's name. A text value or object key that contains one is refused at its field, with the reason, on every edit. Matching ignores case, accents, punctuation and extra spaces, and works on whole words. So the name "Mega Mouse" matches "the MEGA-mouse returns!" but not "megamouse".

## The same rules for people and agents

The command line and MCP import through `AssetImport.Import`. Protected names are checked through the project's rules, so the editor, the command line and MCP refuse them alike. A refusal reads the same everywhere.

The editor has no import screen yet, and doesn't register the content rights check by itself; an adapter adds it (see below). A host that imports assets should call `AssetImport.Import`.

Asset records are written only when the project isn't read-only, and only after the game's `DataProject.FileCheck` agrees. The check is asked with `FileChangeKind.Replace`, the record's name and the text it would hold.

```powershell
bed-cli --data data --schemas schemas --assets assets import art/tree.png licensed --licence CC-BY-4.0 --source https://example.invalid/tree
bed-cli --data data --schemas schemas --assets assets declare art/tree.png ours --save
bed-cli --data data --schemas schemas --assets assets check rights.assets
```

Over MCP, the `import` and `declare` tools are listed only when the server has an asset folder.

## For adapter authors

- **Protected names:** return them from `IProjectAdapter.ProtectedNames`. The editor and the command line add them to the project the adapter opens (`Adapters.ApplyProtectedNames`). Without an adapter, set `DataProject.ProtectedNames` before files are opened.
- **Own names:** return the project's own rights holders from `IProjectAdapter.OwnNames`. The command line uses them with `--assets`.
- **The check:** register `new ContentRightsCheck(assetFolder)` from `IProjectAdapter.Checks`, with your asset extensions and your own rights holders' names if needed. `AgentSession.UseAssets(folder)` registers it for agents (the command line's `--assets`). The editor shows it only when your adapter adds it.
- Keep your game's list in your adapter, not in this library.

## What it doesn't catch

- Copying with no tags. Most files carry no rights metadata, and tags are easy to strip, so a clean scan proves nothing.
- A false declaration. Someone who declares a stolen file `ours` and strips its tags gets it in. The declaration is on record, which is the point: it's traceable.
- Sounds, OBJ and MTL files. Their comments are free text, and audio tags aren't read yet, so these depend on the declaration alone.
- Names that aren't on your list, look-alike spellings ("Meega Mouse"), and names inside images.
- Whether a licence permits your use. The record holds the licence's name; reading its terms is still your job, and none of this is legal advice.
- `AssetRecord.Create` is the low-level record writer, kept for existing callers and marked obsolete. It takes no declaration, so use `AssetImport.Import` for imports.
