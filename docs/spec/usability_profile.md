# Usability profile for the first editor window

A008 revision 1 proposed acceptance profile, 2026-10-31. This is the acceptance contract for the future ImGui host against `docs/goals.md` goals 1 and 12. No participant study, host accessibility session or visual test has been run. Results remain blank until observed.

**Provenance:** the study protocol is INVENTED operational detail for the existing goal. Keyboard discovery is MIXED: Blender operators and this repository's catalogue. Scale, contrast, errors and localisation are MIXED: the cited guidance and existing session contract. These choices extend `docs/research/editor_architecture.md`; they introduce no public interface or document-schema change.

**Version baseline:** installed `Ahwoo.BRUTAL.ImGui` **2026.10.2**, from `ahwoo.brutal.imgui/2026.10.2/ahwoo.brutal.imgui.nuspec` in the local NuGet cache. Its `lib/net10.0/Brutal.ImGui.xml` links to [v1.92.2-docking-brutal](https://github.com/RocketWerkz/imgui/blob/v1.92.2-docking-brutal/imgui.h) [S01]. The Windows native DLL also contains `1.92.2`; this corroborates the XML, not a runtime test. This profile uses **1.92.2** APIs. The eventual host test record must include package version, `ImGui.GetVersion()`, build, OS/backend, fonts, theme, input method and scale. A007 owns the overall version-set decision.

WCAG supplies measurable desktop design targets here. This native application does not gain WCAG conformance by adopting a few criteria. Stronger project rules are identified below. Source editors' accessibility support does not transfer to ImGui.

## First-run study

**INVENTED protocol implementing goal 1.** Recruit five prospective content authors who have never used or watched a demonstration of this tool and have not built/tested its UI. Record prior computer/editor experience and input/access needs under anonymous participant codes. Do not exclude people using assistive technology. Necessary personal access setup may precede timing, without demonstrating task controls; record accommodations and comparability limits. Five formative observations do not establish population-wide usability or accessibility.

Use the same build, normal first-window defaults, fresh user layout and a new self-authored empty Toy board per person. No coaching, docs or configuration prerequisite. Ordinary in-product menus, labels, palette and feedback remain available. Show this exact task script without pointing at controls:

> You have a new empty board. Place one object named Lamp at X 1, Y 1. Move that same object to X 4, Y 2. Rename it Desk Lamp. Save the board as study-board.json in the study folder. Then undo the rename so that the object is called Lamp again. Tell me when you are finished. Please work without documentation or help; you may stop whenever you wish.

The script specifies outcomes, not gestures. Palette/inspector solutions count. The observer may repeat it verbatim. Start after the script when the ready board becomes interactive. Stop on the participant's declaration, at 600 seconds, on withdrawal, or on a crash preventing continuation. Success means all five outcomes correct **before** 600 seconds, with no task help or docs. The proposed threshold is **4 of 5** successful participants. Keep crashes, withdrawals and failures; do not silently replace them. Record environmental invalidations and any replacement separately. A changed-build retest uses five fresh participants with a separate result set.

After stopping, verify exactly one object with unchanged id, current label Lamp, X 4/Y 2; the saved file has Desk Lamp at X 4/Y 2; undo leaves the session dirty. Save is not a document edit to undo. Typing the old label instead of undo is not success. The participant need not understand ids or JSON.

A help request includes asking any person, assistant or search engine where a control is, what it means or how to complete a step; opening docs also counts. Log unanswered requests. Repetition of the script and reporting discomfort do not count as task help. Reply only, 'Please use your best judgment; I cannot explain the controls during this task.' Any hint supplied, even unsolicited, contaminates unaided success. Do not require think-aloud narration during timing.

Record timestamps, milestones, wrong selections, repeated attempts, rejected edits, lost focus, errors, recovery, exact help requests and interventions. Record the screen only with consent; otherwise use contemporaneous notes. Debrief after timing about confusing controls and expectations. Store only neutral sample content and participant codes.

| Participant | Experience / input setup | Place / move / rename / save / undo times | Stop time/reason | All five outcomes correct? | Help/docs/interventions | Unaided success? | Failures/recovery |
|---|---|---|---|---|---|---|---|
| P1 | | | | | | | |
| P2 | | | | | | | |
| P3 | | | | | | | |
| P4 | | | | | | | |
| P5 | | | | | | | |

Attach build/environment, starting state, observer, consent method, successes/5, threshold result and defects. This is a blank results template, not an executed study.

## Keyboard and focus

**MIXED: ImGui navigation, Blender discovery, WCAG keyboard principle.** Every exposed action needs a keyboard route, including canvas placement, dragging, selection, settings and file prompts. Numeric coordinates and an object list are valid alternatives; custom drawing alone supplies no navigation semantics. Sources: [1.92.2 FAQ](https://github.com/ocornut/imgui/blob/v1.92.2/docs/FAQ.md) [S02], [WCAG Keyboard](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html) [S05].

Required settings/members, confirmed in the installed XML:

- Include `ImGuiConfigFlags.NavEnableKeyboard` in `io.ConfigFlags` (upstream `ImGuiConfigFlags_NavEnableKeyboard`). Optional `NavEnableGamepad` requires backend support and proves no keyboard coverage.
- Set `io.ConfigNavCursorVisibleAlways = true` and `io.ConfigNavEscapeClearFocusItem = false`; explicitly route cancellation below. Theme `ImGuiCol.NavCursor` and check the rendered outline. Flags alone do not prove visible focus.
- Use `SetItemDefaultFocus` / `SetKeyboardFocusHere` when opening a palette/dialog and restore its invoker on close. Do not call these unconditionally every frame and steal input.
- Always feed backend events to ImGui. Respect capture and key ownership to prevent document actions while text/popup controls own input. `WantCaptureKeyboard` is not permission to drop ImGui's events. Text-field Undo must not also undo the document.
- Use stable object/command/property identities with `PushID` or an invariant `###id` suffix. Translated, renamed and duplicate labels must not collide.

Existing bindings in `src/Brutal.Authoring.Editor/Shortcuts.cs` are authoritative: Ctrl+Shift+P palette, Ctrl+S save, Ctrl+Z undo, Ctrl+Y redo, F focus in, Escape focus out. Effective user overrides/removals win; menus show effective bindings; palette routes survive shortcut clashes. This profile changes no shortcut schema. **BORROWED lesson:** Blender's [menu search](https://docs.blender.org/manual/en/2.91/interface/controls/templates/operator_search.html) [S12] provides typed search and keyboard activation; borrow discovery rather than replacing existing bindings with F3.

| Action/surface | Required route and focus behavior |
|---|---|
| Menus, toolbar, panels | Tab/Shift+Tab, arrows, Enter/Space; Ctrl+Tab for ImGui windows. Predictable sequence with a visible label at every stop. |
| Place/move/rename/remove/select | Object list plus palette/inspector expose all operations without pointer use; exact X/Y entry works. Selection is distinguishable from keyboard focus. |
| Save/undo/redo | Menu/palette and effective shortcuts; unavailable actions explain why in keyboard-reachable text. |
| Palette | Open from menu/shortcut; search gains focus; type label/alias/id; arrows and Enter activate once; Escape cancels and restores focus. Allowed commands discoverable, unavailable reasons shown, internal commands excluded. |
| Workspace/breadcrumb/focus | Reach selector and breadcrumb; use existing F/Escape only when no text/popup owns the key. View navigation never dirties the document. |
| Issues/settings/file prompts | Reach/read/activate issues; edit locale, scale and shortcuts; type path and confirm/cancel without a pointer. |
| Modal, text and deletion recovery | Escape cancels composition/text or closes the innermost popup first; only a later press may navigate out of document focus. Modal dismissal is reachable. Restore invoker, or nearest surviving item/list if deleted. |

**INVENTED coverage rule:** enumerate every shipped action/control and attach its tested keyboard route, including controls added after this list. One missing route fails. Exercise forward/backward traversal, scroll areas, empty/full lists, failed edits, reopen panels and shortcut conflicts. Focus must remain visible and unobscured, a stronger project rule than [WCAG's minimum not-entirely-hidden criterion](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) [S17].

**MIXED docking policy:** start with useful default panes requiring no dragging. [Docking documentation](https://github.com/ocornut/imgui/wiki/Docking) [S04] describes `DockingEnable`; it does not guarantee keyboard docking. Docking and additional OS viewports are distinct choices. If rearrangement is exposed, provide a keyboard equivalent or keyboard layout presets/reset with equivalent access. Otherwise use fixed panes for this host. No internal DockBuilder dependency or additional OS window is required.

## Scale and contrast

**MIXED:** adjustable sizing from [Godot EditorSettings](https://docs.godotengine.org/en/stable/classes/class_editorsettings.html) [S11], [Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/allow-interfaces-to-be-resized/) [S10] and [WCAG Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html) [S09]. Borrow desktop text/control resizing, not gameplay HUD behavior. Provide keyboard-reachable 100%, 125%, 150%, 175%, 200% user scale. Persist it per user, outside documents/history/journals.

**INVENTED environment:** at a recorded 1280x720 device-independent-pixel client area, test both locales at all five scales. Actions/values/full errors remain reachable without overlap or inaccessible clipping. Wrapping, scrolling and pane switching are acceptable; essential controls disappearing are not. Truncated list labels disclose full text on focus/activation, not only mouse hover. Repeat 100 -> 200 -> 125 -> 100 to detect cumulative drift. If multi-monitor support is advertised, move between 100%/150% OS DPI and back with fields/popups open; otherwise record that support untested.

In 1.92.2, `style.FontScaleMain` is user scale and `style.FontScaleDpi` monitor scale. Do not apply OS DPI twice. Recreate padding/spacing/thickness from an unscaled theme using `ScaleAllSizes(totalScale)`; it does not scale fonts, and repeated rescaling is lossy (installed XML). Layout values must follow font/control metrics. Experimental `ConfigDpiScaleFonts` scales fonts without padding and alone cannot pass layout checks. Matching [font docs](https://github.com/ocornut/imgui/blob/v1.92.2/docs/FONTS.md) [S03] require checking renderer texture support before relying on dynamic fonts.

Theme targets, **MIXED from WCAG with stronger project defaults**:

- Informative text, including unavailable-command explanations: >=4.5:1 against effective background. [WCAG Contrast Minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) [S06] allows 3:1 for qualifying large text; this project deliberately uses 4.5:1 for all informative text.
- Active control/state cues and focus outlines: >=3:1 against adjacent backgrounds, following [Non-text Contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html) [S07].
- Adopt [Focus Appearance](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance.html) [S08], an AAA target, as a project requirement: indicator area at least a 2-logical-pixel perimeter, and >=3:1 between the same pixels focused/unfocused. CSS pixels are not framebuffer pixels: this native adaptation uses device-independent units and records physical scale. Do not describe it as an AA requirement.

For every theme and normal/hover/focus/selected/error/warning/modal/unavailable state, record foreground, composited background, alpha and ratio. After compositing, linearize sRGB c with c/12.92 for c<=.04045, otherwise ((c+.055)/1.055)^2.4. L=.2126R+.7152G+.0722B; contrast=(Llighter+.05)/(Ldarker+.05). Do not round a failing ratio upward. Headless theme calculations verify ratios; offscreen renders verify actual outline area, text output and clipping at each scale. Neither replaces the other. Stock dark styling is not assumed to pass.

## Errors

**MIXED:** the existing `Issue` severity/object/path contract plus [WCAG Error Suggestion](https://www.w3.org/WAI/WCAG22/Understanding/error-suggestion.html) [S18]. Use persistent, keyboard-reachable issues and field feedback. Severity has words/icon markers; color reinforces it, following [redundant signifiers](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-a-fixed-colour-alone/) [S19] for diagnostic readability.

Every issue says what failed, which object (label plus stable id) or file/path, and what to do next. INVENTED example: 'Could not rename Lamp [item_1]: the name is empty. Enter 1 to 80 characters, then try again.' Preserve the entered text for correction and explicit Cancel; do not claim a rejected value was saved. Technical paths may appear in expandable details. Stack traces alone, 'invalid input', color-only badges and disappearing toasts fail.

**BORROWED:** [Unity Debug.Log context](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/debug/log) [S13] locates associated objects; [Unreal FActorToken](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Core/Logging?application_version=5.5) [S14] selects its actor on activation. **MIXED adaptation:** activating an issue selects its current stable-id target, reveals it and its field/inspector, changing no document/history/journal. An outside-focus target needs explanation and an explicit action to navigate out; never silently unlock edits. A deleted target says unavailable, never binds to a different object with the same name. File errors show file/path details without fake object links.

Exercise empty-name refusal, invalid number, malformed file, failed save and outside-edit conflict using neutral fixtures. Future host tests need deterministic failure seams rather than damaging real projects. Refused edits preserve committed state/history; failed saves preserve old files and dirty work; conflicts preserve outside changes. Refresh issues after correction without stealing focus. If a field disappears, retain accessible object/issue context and explain why.

## Localisation

**MIXED:** stable catalogue ids and explicit editor language as in Godot [S11]. English fallback; mandatory non-English locale **de-DE** for the full workflow: menus, fields, aliases, shortcuts, unavailable reasons, errors and dialogs. A fluent German reviewer checks first-milestone wording. Missing resources use readable English and logged defects rather than blank/raw-id UI.

Keep `toy.add`, `toy.move`, `toy.rename`, `toy.remove` and editor action ids invariant. Translate display labels/aliases, not journal ids/keys. Search each action by German label/alias and stable id, respecting internal-command hiding. Language changes preserve selection and never dirty documents. If the existing translation seam is insufficient, report a host finding; do not alter command interfaces to fit.

**INVENTED corpus:** ASCII ids `item_1` etc.; names `Grüße`, `Straße`, `Café`, decomposed `Cafe\u0301` (actual e plus U+0301), `Łódź`, `東京`, `灯台`. Create visually similar distinct labels; edit one without affecting the other. Copy/paste/rename/undo/save/reload/replay retain exact codepoint sequences without silent normalization or transliteration. No Unicode-id requirement is added. Preserve existing Toy label validation (1-80 characters); test a maximum valid 80-ASCII-character label and 81-character refusal without inventing a new Unicode length definition.

Expand translated UI resources to 200% English length in a stress-test resource set, separately from document-label limits. Test palette, menus, inspector, breadcrumb, issues and dialogs at every scale. Wrapping, focus disclosure and resizing expose full text without losing actions. Stable widget ids prevent translation/duplicate-label focus collisions.

**BORROWED font/backend requirement [S03]:** use fonts with verified tested-script coverage; record file/version/licence before bundling. This card installs no font/dependency. UTF-8 encoding and font coverage are separate. Dynamic glyph loading requires a renderer actually implementing `ImGuiBackendFlags.RendererHasTextures`. Otherwise use persistent glyph ranges for resources and editable names, build/update atlas textures safely between frames. Default Latin ranges cannot cover Japanese just because the input is UTF-8. Introduce previously unseen pasted characters after frame one and after scale changes; missing-glyph squares fail.

IME requires an OS/backend test: compose `東京` with Japanese IME, select candidates, commit once, undo, and cancel another composition with Escape without leaving document focus. Verify candidate placement at 100%/200%. The matching FAQ describes Windows `PlatformHandleRaw` and `GetPlatformIO().Platform_SetImeDataFn`; installed bindings expose `ImGuiPlatformIO.Platform_SetImeDataFn`. Injecting UTF-8 headlessly proves neither OS composition nor candidate placement. A separately authorized host test session must record the OS/input method; do not launch a GUI during this research card.

**INVENTED numeric presentation rule:** show decimal dots in both locales with a German hint; accept `1.5`, reject ambiguous `1,5` clearly rather than silently reading 15. This avoids assuming ImGui numeric parsing follows .NET culture. Canonical JSON/journal numbers retain invariant dots, no grouping, fixed ordering/newlines under `docs/spec/first_slice.md`. Saving the same unchanged document under en-US/de-DE must yield identical bytes. Locale changes are presentation only.

## Limits

As of 2026-10-31, navigation, larger fonts and contrast are not evidence of an OS accessibility tree. Upstream [accessibility issue #8022](https://github.com/ocornut/imgui/issues/8022) [S15] remains open; its April 2026 maintainer discussion describes narration prototypes, not a finished cross-platform backend. This card has demonstrated no BRUTAL screen-reader integration. Mark first-host screen-reader support unverified/unsupported until the test below passes, regardless of other checklist results.

**MIXED future workaround:** [AccessKit](https://accesskit.dev/) [S16] supplies accessibility infrastructure for custom-drawn toolkits. A later adapter could expose roles, names, values, focus, selections, editable text and actions to native APIs. It is a candidate, not an approved dependency or working ImGui integration. Self-voicing may assist carefully designed workflows but does not establish screen-reader/Braille/voice-control interoperability. Existing CLI commands and text diagnostics offer alternative workflows, not proof of accessible UI.

Before claiming support, record OS/reader/version/backend and demonstrate names/roles/values, focus announcements, object selection, text/caret/selection editing, error discovery/object navigation, modal/menu behavior and all five study tasks without sighted help. Retain unsupported items individually; keyboard tests/screenshots cannot close them. Do not generalize across readers/platforms.

Unicode rendering does not prove shaping/bidi editing. [Open issue #4227](https://github.com/ocornut/imgui/issues/4227) [S20] documents separate complex-text work. German/Japanese tests establish no Arabic/Hebrew/Indic support. Record those gaps until tested. Keyboard docking, mixed DPI, IME and dynamic font updates also need backend-specific evidence.

## Checklist

These are future host tests, not research results. Record PASS/FAIL/BLOCKED/NOT RUN with evidence; unrun/blocked never count as pass. Conditional rows are mandatory for advertised features. All requirements except the explicitly disclosed assistive-technology gap must pass for this scoped first-host profile; this is not a claim of universal accessibility.

| Id | How to run | Pass condition |
|---|---|---|
| U01 Baseline | Record version/build/backend/OS/fonts/licences/theme/scale/locale. | Reproducible baseline, no assumed API/backend support. |
| U02 Defaults | Fresh settings, self-authored empty board. | No setup prerequisite or advanced controls obstructing tasks. |
| U03 Study | Exact five-person script; retain all records. | 4 of 5 correct unaided completions before 600 seconds. |
| U04 Study state | Inspect current state and saved file. | One unchanged id; current Lamp and saved Desk Lamp at (4,2); undo leaves dirty. |
| U05 Coverage | Inventory every exposed action; execute keyboard routes. | Every operation works without pointer, including canvas alternatives. |
| U06 Focus | Traverse forward/backward, windows, scroll areas, empty/full lists and reopened panels. | Predictable visible/unobscured focus; no theft. |
| U07 Palette | Search labels/aliases/ids; activate/cancel/unavailable. | Correct discovery/reasons; internal hidden; focus restored. |
| U08 Ownership | F/Delete/Escape/undo inside and outside text/popups. | One intended action; no background edit or focus-out leakage. |
| U09 Shortcuts | Remap/remove/conflict; restart. | User choices persist, effective labels correct, clashes fire nothing with reason. |
| U10 Recovery | Dismiss modal and delete invoking item. | No trap/data loss; useful surviving control focused. |
| U11 Layout | If rearrangement shipped, keyboard rearrange/reset. | Equivalent content/action access without dragging. |
| U12 Scale | Both locales, five scales, 1280x720 logical area. | No lost action/value, overlap or inaccessible clipping. |
| U13 Persistence | Repeated scale sequence and restart. | No drift; user choice restored; document/history unchanged. |
| U14 DPI | If advertised, cross monitors with open popup/input. | Correct single scaling, font/hit-target/caret/padding alignment. |
| U15 Text contrast | Calculate every theme/state's composited colors. | Informative text >=4.5:1 without upward rounding. |
| U16 Focus/control contrast | Ratios plus offscreen bounds/outline renders. | Adjacent >=3:1; focus >=2-logical-pixel-perimeter area and >=3:1 same-pixel change. |
| U17 Color | Read statuses/issues without color. | Words/symbols preserve essential meaning. |
| U18 Error wording | Neutral invalid-name/number/file/save/conflict fixtures. | Persistent keyboard feedback says what/which/remedy; correctable text retained. |
| U19 Issue navigation | Current/outside-focus/deleted/file targets. | Stable-id reveal or honest unavailable explanation; no silent unlock/edit/rebinding. |
| U20 Safe failure | Compare state/history/file around refusal/save/conflict. | Existing session safety contract preserved. |
| U21 German | Full workflow and errors plus fluent review. | Readable translated workflow, logged fallback defects, no blank/raw ids. |
| U22 Identity | Locale switch and duplicate/renamed labels. | Stable command/object/widget identity and selection; no document dirtying. |
| U23 Unicode | Corpus through input/undo/save/reload/replay. | Exact codepoints, distinct names, no missing glyphs/normalization. |
| U24 Long text | 200% UI resources; valid 80/invalid 81 ASCII labels at every scale. | Full info keyboard accessible; existing validation retained. |
| U25 Fonts | New glyph after first frame/scale; record backend/range path. | Correct glyph/texture updates and documented font rights. |
| U26 IME | Separate authorized Japanese composition session at 100/200%. | Commit once, isolated cancellation, candidate follows caret; environment recorded. |
| U27 Numbers/files | Input 1.5/reject 1,5; same document under en-US/de-DE. | Clear numeric intent and identical canonical bytes. |
| U28 Assistive tech | Disclose limits; before any support claim run named reader workflow. | Honest gap, or complete interoperability evidence per platform. |
| U29 Other limits | Compare promised locales/platforms with test matrix. | No hidden shaping/bidi, DPI, docking or IME assumptions. |

## Self-check

Population: 20 numbered external sources S01-S20, in the list order below. Selection: `random.Random(8001).sample(range(1, 21), 8)` gives `[19, 10, 7, 14, 15, 12, 4, 16]`. Reopened 2026-10-31. Short source excerpts show the judgment; none is a host result.

| Source | Claim and source evidence | Result |
|---|---|---|
| S19 | Recommends 'text or a symbol, pattern or shape'; supports redundant diagnostic meaning. | CONFIRMED. |
| S10 | Says 'manually resizable interfaces are also useful'; explanation covers vision and fine motor needs. | CONFIRMED. |
| S07 | Requires 'at least 3:1 against adjacent color(s)' for applicable active cues. | CONFIRMED. |
| S14 | FActorToken defaults to 'select an actor in the opened level'. Supports linked activation, not our full policy. | CONFIRMED via readable official Logging index; en-us/class variants returned no readable content. |
| S15 | Open issue; April 28 2026 maintainer calls sample 'a proof of concept'. Does not establish a finished adapter. | CONFIRMED. |
| S12 | Initial official indexed text gives 'F3' search and 'Return' activation. Direct rereads failed with fetch 402; source mirror 403. | LIMITED: narrow primary indexed support, full reread unresolved. |
| S04 | Guide specifies 'ImGuiConfigFlags_DockingEnable'; DockBuilder remains internal. No keyboard guarantee. | CONFIRMED. |
| S16 | Helps 'toolkits that render their own user interface elements'. Supports future adapter candidate, not current ImGui support. | CONFIRMED. |

**7/8 full-source reopen checks confirmed.** S12 still has primary indexed evidence but unresolved direct fetching. The direct-source verification gap is reported as blocked in REPORT.md; the document gate is recorded there separately. No accepted A008 result is claimed.

Sources accessed 2026-10-31:

1. S01 [Pinned BRUTAL ImGui fork](https://github.com/RocketWerkz/imgui/blob/v1.92.2-docking-brutal/imgui.h); package/XML evidence above.
2. S02 [1.92.2 FAQ](https://github.com/ocornut/imgui/blob/v1.92.2/docs/FAQ.md).
3. S03 [1.92.2 Fonts](https://github.com/ocornut/imgui/blob/v1.92.2/docs/FONTS.md).
4. S04 [Docking](https://github.com/ocornut/imgui/wiki/Docking).
5. S05 [Keyboard](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html).
6. S06 [Contrast Minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).
7. S07 [Non-text Contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html).
8. S08 [Focus Appearance](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance.html).
9. S09 [Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html).
10. S10 [Resize interfaces](https://gameaccessibilityguidelines.com/allow-interfaces-to-be-resized/).
11. S11 [Godot EditorSettings](https://docs.godotengine.org/en/stable/classes/class_editorsettings.html).
12. S12 [Blender search](https://docs.blender.org/manual/en/2.91/interface/controls/templates/operator_search.html); historical borrowing, current-version equivalence not claimed.
13. S13 [Unity Debug.Log](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/debug/log).
14. S14 [Unreal Logging/FActorToken](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Core/Logging?application_version=5.5).
15. S15 [ImGui accessibility #8022](https://github.com/ocornut/imgui/issues/8022).
16. S16 [AccessKit](https://accesskit.dev/).
17. S17 [Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html).
18. S18 [Error Suggestion](https://www.w3.org/WAI/WCAG22/Understanding/error-suggestion.html).
19. S19 [Redundant signifiers](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-a-fixed-colour-alone/).
20. S20 [Shaping/bidi #4227](https://github.com/ocornut/imgui/issues/4227).
