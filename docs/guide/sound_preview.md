# Preview a sound

## Prepare the import

Use a WAV or OGG/Vorbis sound in the project's asset folder. It needs an import record with its GUID, origin, licence/source where required, and AI-use declaration, just like a model or image. An import declaration does not grant distribution rights; see [content rights](content_rights.md).

For example, from a project root with `data/`, `schemas/` and `assets/`, import a sound you created yourself:

```text
bed-cli --data data --schemas schemas --assets assets import chirp.wav ours --ai forbidden
```

This accepted import can write its record immediately. Use your real declaration and the needed licence/source fields for other origins; see the [command-line guide](command_line.md). Reopen the project to admit a new or changed import. The Assets panel browses imported records; selecting a raw, unimported file does not make it playable.

## Listen in Assets

1. Open **Assets**, search for the imported sound and select its row.
2. Read **Sound:** for its decoded format, channel count, sample rate and duration in seconds. Selection alone does not start playback.
3. Choose **Play sound** to listen. With Assets focused, **F6** also starts playback; press it again to stop. The shortcut does not take over while typing in a field or using Ctrl, Alt or the system modifier.
4. Choose **Stop sound**, or press **Escape** with Assets focused, to stop.

Changing the selection, losing admission or closing the project stops the preview. Play replaces any previous preview. This listens to admitted asset bytes without changing the sound, its import record or your project data. It does not start the game's Play program. See [Assets](assets.md) for browsing models and images.

## Supported audio and limits

| Item | Supported preview |
| --- | --- |
| WAV | PCM 8-bit or 16-bit, or IEEE float32. |
| OGG | A single Vorbis stream. Opus and chained streams are unsupported. |
| Channels | Mono or stereo. |
| Sample rate | 1 to 192000 Hz. |
| Encoded file | At most 32 MiB. |
| Decoded audio | At most 64 MiB as PCM16. A small compressed file can still exceed this limit. |

## If playback is unavailable

Read the status beside the controls. A decoding failure or missing audio device reports its reason; a failed decode leaves Play sound disabled. An unadmitted asset or a host without audio preview shows **Sound preview unavailable**. Open the project again after repairing an import. A decode limit or unsupported format needs a suitable source file; renaming its extension does not convert it.

The desktop app supplies audio playback. A custom host can provide the optional `IAssetAudioPreview` service; without it the rest of the editor remains usable. Automated UI tests use a fake service, and the native audio check uses a silent driver; those checks do not establish how a particular physical output device sounds.
