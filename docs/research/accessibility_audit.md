# Accessibility audit: the editor measured without a window

The default theme needs contrast fixes, and the current layout needs a usable text-size setting and better disclosure of clipped labels. Keyboard reaches the stock panes, but the real Inspector Reset button can receive entirely invisible focus; the isolated widget kinds themselves show focus cues. Those results do not establish screen-reader support or complete accessibility.

This is a measured audit of the existing editor, not a style change. **BORROWED:** WCAG criteria as design targets and the engines' documented accessibility approaches. **INVENTED:** the neutral relay fixture, measuring program, pixel decoder, seeded checks and proposed colour values. **MIXED:** recommendations combining those targets with the editor's existing document, descriptor and agent contracts. No private game content, licensed art pack, new package, native window or desktop capture was used.

## Baseline and method

The authoritative measurements are two fresh processes, run7 and run8, of the same frozen probe. They use the existing .NET 10 toolchain, Ahwoo.BRUTAL.ImGui 2026.10.2, runtime ImGui **1.92.2**, its default font, StyleColorsDark, and EditorWindow<DataDocument>.Configure(io). The shipped host calls the same theme setup in [EditorApp](../../src/Brutal.Authoring.Editor.App/EditorApp.cs); Configure enables docking, keyboard navigation and an always-visible navigation cursor. HeadlessHost alone does not apply those application settings.

Docked/table renders are **1280×720**; widget-kind renders are **760×650**. Only FontScaleMain changes: 1.0, 1.5 and 2.0 produce measured in-frame font sizes **13, 20 and 26 pixels**. Geometry and display size stay fixed. This tests font enlargement, not OS DPI changes or a full spacing-scale strategy. ImGui owns a real context; HeadlessHost supplies real key events and CPU rasterization. No mouse gesture was used. Intermediate rasterization is disabled during long key sequences, then enabled and a new frame drawn before every saved image.

The self-authored fixture supplies text, number, integer, checkbox, enum, reference, long labels, two lists, an override/reset, and a refused file beside a valid one. The probe draws the real DataWorkspace and DataTableView. A separate ordinary-ImGui control window records focus rectangles for eight widget kinds; it is a controlled test of the underlying widgets, not a claim that every application-specific dialog was audited.

Review evidence is retained under the card's allowed scratch directory: measurements/run7 and measurements/run8, style.json, keyboard-*.json, widgets-*.json, analysis.json and actual-widgets.json. PNG names below are relative to run7. The scratch files are review artifacts, not extra public assets to merge. The independent Python reader validates PNG CRCs and decodes pixels with the standard library; no image library was installed.

[WCAG 2.2](https://www.w3.org/TR/WCAG22/) supplies explicit thresholds for this native application's design audit. This is not a claim of web or desktop conformance. It is a bounded headless measurement, not a participant study, reader test, mixed-DPI test, IME test or every possible plug-in combination. The existing [usability profile](../spec/usability_profile.md) remains broader; A036's [item 12](editor_qol_2024_2026.md) still requires testing with real assistive tools.

## Contrast

### Rules and scope

Normal text uses **4.5:1** under [1.4.3](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html). Qualifying large text uses 3:1; this audit judges the default 13px font at 4.5:1. Required control/state-identifying graphics use **3:1** under [1.4.11](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html). The usability profile deliberately requires 4.5:1 for all informative text even when large; its stronger project target remains unmet by the blue fills at 2×. Inactive controls are exempt from the normative WCAG contrast criteria; informative muted help, limits and shortcut hints are not automatically inactive just because the implementation calls TextDisabled.

Not every border or fill needs 3:1. A labelled button may be identified by its text. An unchecked checkbox needs a distinguishable empty box when its label names the setting without stating the state. W3C's [component examples](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html#user-interface-component-examples) explicitly distinguish these cases, including a failing 2.7:1 checkbox boundary.

The inventory below includes **all 60 default style colour slots**, **52 text/background combinations** across 25 text-bearing background states, including input-frame selection cases, every remaining style slot against WindowBg, and **48 additional state-graphic/link contexts**: **158 pairs** in total. This is an explicit state/role matrix, not the meaningless assertion that every one of 60×60 colour permutations occurs in the UI. Some style slots are decorative, runtime-only, or unused by the stock data editor; their numeric target is recorded without inventing an AA failure.

Style swatches use the actual native style values, including alpha. They are rasterized on the HeadlessHost backdrop #202020, with one WindowBg base and then the named background and foreground; the WindowBg case is not double-layered. The two input-selection cases add FrameBg below TextSelectedBg before drawing text. Sample points are inside full-coverage flat areas, not antialiased glyph fringes. The captured bytes are treated as sRGB; a GPU output colour-space comparison remains unmeasured. [W3C's contrast guidance](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) distinguishes underlying colours from antialiasing.

The decoder uses sRGB breakpoint 0.04045 and luminance weights .2126/.7152/.0722, then (Llight+.05)/(Ldark+.05), as defined in [relative luminance](https://www.w3.org/TR/WCAG22/#dfn-relative-luminance). Threshold decisions use the unrounded result. Six-decimal table display is for reading only.

### Actual control results

The following pixels come from real keyboard-focused widgets, not just colour swatches. They corroborate the style-state findings.

| 1× actual widget | PNG and foreground/background coordinates | RGB pair | Ratio | AA design target |
|---|---|---|---:|---|
| text input | widgets-1.0-0.png: (108, 59) / (15, 57) | #FFFFFF / #1E2F49 | 13.478665 | PASS 4.5:1 |
| numeric input | widgets-1.0-1.png: (80, 82) / (15, 80) | #FFFFFF / #1E2F49 | 13.478665 | PASS 4.5:1 |
| checkbox | widgets-1.0-2.png: (58, 106) / (32, 103) | #FFFFFF / #101010 | 19.028111 | PASS 4.5:1 |
| combo | widgets-1.0-3.png: (54, 129) / (15, 126) | #FFFFFF / #24466E | 9.649693 | PASS 4.5:1 |
| selectable | widgets-1.0-4.png: (29, 149) / (11, 147) | #FFFFFF / #387BCB | 4.317706 | FAIL 4.5:1 |
| tree node | widgets-1.0-5.png: (34, 167) / (15, 166) | #FFFFFF / #387BCB | 4.317706 | FAIL 4.5:1 |
| small reset button | widgets-1.0-6.png: (18, 184) / (15, 183) | #FFFFFF / #4296FA | 3.010919 | FAIL 4.5:1 |
| button | widgets-1.0-7.png: (41, 37) / (15, 34) | #FFFFFF / #4296FA | 3.010919 | FAIL 4.5:1 |
| unchecked checkbox state boundary | widgets-1.0-baseline.png: (16, 104) / (8, 110) | #1E2F49 / #101010 | 1.411721 | FAIL 3:1 |

At 1× the normal button and small reset button fail text contrast at **3.010919:1**, and focused selectable/tree text fails at **4.317706:1**. The same RGB values remain at larger scales; qualifying 26px text can meet the large-text 3:1 threshold, which does not repair the failing default size. Input text and numbers pass comfortably.

The unchecked box is #1E2F49 against #101010, **1.411721:1**, without a separate contrasting boundary in this default render. Its setting label is readable but does not say “unchecked.” This is a required state/identification graphic below the 3:1 design target. The checked mark against the ordinary frame is stronger; evaluate hover/active backgrounds separately rather than averaging states.

White Text is #FFFFFF. The full matrix also finds ButtonActive below 4.5, and TabHovered/HeaderActive use the bright blue that fails normal text. Muted #808080 on WindowBg passes at **4.817927:1**; the same colour on MenuBarBg is **3.930408:1**. The latter matters for informative shortcut hints, while genuinely disabled Undo/Redo controls are a separate exempt case.

### Complete default-style inventory

RGBA values are the native style readings before compositing; alpha is retained. Pair pixels and coordinates follow in the complete matrix.

| Slot | Native RGBA | RGB before alpha |
|---|---|---|
| Text | 1.000000, 1.000000, 1.000000, 1.000000 | #FFFFFF |
| TextDisabled | 0.500000, 0.500000, 0.500000, 1.000000 | #808080 |
| WindowBg | 0.060000, 0.060000, 0.060000, 0.940000 | #0F0F0F |
| ChildBg | 0.000000, 0.000000, 0.000000, 0.000000 | #000000 |
| PopupBg | 0.080000, 0.080000, 0.080000, 0.940000 | #141414 |
| Border | 0.430000, 0.430000, 0.500000, 0.500000 | #6E6E80 |
| BorderShadow | 0.000000, 0.000000, 0.000000, 0.000000 | #000000 |
| FrameBg | 0.160000, 0.290000, 0.480000, 0.540000 | #294A7A |
| FrameBgHovered | 0.260000, 0.590000, 0.980000, 0.400000 | #4296FA |
| FrameBgActive | 0.260000, 0.590000, 0.980000, 0.670000 | #4296FA |
| TitleBg | 0.040000, 0.040000, 0.040000, 1.000000 | #0A0A0A |
| TitleBgActive | 0.160000, 0.290000, 0.480000, 1.000000 | #294A7A |
| TitleBgCollapsed | 0.000000, 0.000000, 0.000000, 0.510000 | #000000 |
| MenuBarBg | 0.140000, 0.140000, 0.140000, 1.000000 | #242424 |
| ScrollbarBg | 0.020000, 0.020000, 0.020000, 0.530000 | #050505 |
| ScrollbarGrab | 0.310000, 0.310000, 0.310000, 1.000000 | #4F4F4F |
| ScrollbarGrabHovered | 0.410000, 0.410000, 0.410000, 1.000000 | #696969 |
| ScrollbarGrabActive | 0.510000, 0.510000, 0.510000, 1.000000 | #828282 |
| CheckMark | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| SliderGrab | 0.240000, 0.520000, 0.880000, 1.000000 | #3D85E0 |
| SliderGrabActive | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| Button | 0.260000, 0.590000, 0.980000, 0.400000 | #4296FA |
| ButtonHovered | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| ButtonActive | 0.060000, 0.530000, 0.980000, 1.000000 | #0F87FA |
| Header | 0.260000, 0.590000, 0.980000, 0.310000 | #4296FA |
| HeaderHovered | 0.260000, 0.590000, 0.980000, 0.800000 | #4296FA |
| HeaderActive | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| Separator | 0.430000, 0.430000, 0.500000, 0.500000 | #6E6E80 |
| SeparatorHovered | 0.100000, 0.400000, 0.750000, 0.780000 | #1A66BF |
| SeparatorActive | 0.100000, 0.400000, 0.750000, 1.000000 | #1A66BF |
| ResizeGrip | 0.260000, 0.590000, 0.980000, 0.200000 | #4296FA |
| ResizeGripHovered | 0.260000, 0.590000, 0.980000, 0.670000 | #4296FA |
| ResizeGripActive | 0.260000, 0.590000, 0.980000, 0.950000 | #4296FA |
| InputTextCursor | 1.000000, 1.000000, 1.000000, 1.000000 | #FFFFFF |
| TabHovered | 0.260000, 0.590000, 0.980000, 0.800000 | #4296FA |
| Tab | 0.180000, 0.350000, 0.580000, 0.862000 | #2E5994 |
| TabSelected | 0.200000, 0.410000, 0.680000, 1.000000 | #3369AD |
| TabSelectedOverline | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| TabDimmed | 0.068000, 0.102000, 0.148000, 0.972400 | #111A26 |
| TabDimmedSelected | 0.136000, 0.262000, 0.424000, 1.000000 | #23436C |
| TabDimmedSelectedOverline | 0.500000, 0.500000, 0.500000, 0.000000 | #808080 |
| DockingPreview | 0.260000, 0.590000, 0.980000, 0.700000 | #4296FA |
| DockingEmptyBg | 0.200000, 0.200000, 0.200000, 1.000000 | #333333 |
| PlotLines | 0.610000, 0.610000, 0.610000, 1.000000 | #9C9C9C |
| PlotLinesHovered | 1.000000, 0.430000, 0.350000, 1.000000 | #FF6E59 |
| PlotHistogram | 0.900000, 0.700000, 0.000000, 1.000000 | #E6B300 |
| PlotHistogramHovered | 1.000000, 0.600000, 0.000000, 1.000000 | #FF9900 |
| TableHeaderBg | 0.190000, 0.190000, 0.200000, 1.000000 | #303033 |
| TableBorderStrong | 0.310000, 0.310000, 0.350000, 1.000000 | #4F4F59 |
| TableBorderLight | 0.230000, 0.230000, 0.250000, 1.000000 | #3B3B40 |
| TableRowBg | 0.000000, 0.000000, 0.000000, 0.000000 | #000000 |
| TableRowBgAlt | 1.000000, 1.000000, 1.000000, 0.060000 | #FFFFFF |
| TextLink | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| TextSelectedBg | 0.260000, 0.590000, 0.980000, 0.350000 | #4296FA |
| TreeLines | 0.430000, 0.430000, 0.500000, 0.500000 | #6E6E80 |
| DragDropTarget | 1.000000, 1.000000, 0.000000, 0.900000 | #FFFF00 |
| NavCursor | 0.260000, 0.590000, 0.980000, 1.000000 | #4296FA |
| NavWindowingHighlight | 1.000000, 1.000000, 1.000000, 0.700000 | #FFFFFF |
| NavWindowingDimBg | 0.800000, 0.800000, 0.800000, 0.200000 | #CCCCCC |
| ModalWindowDimBg | 0.800000, 0.800000, 0.800000, 0.350000 | #CCCCCC |

### Complete rendered pair matrix

All coordinates are (x,y) in style-pairs.png, 1280×3000. “PASS/FAIL” in the numeric column compares the unrounded value with its stated target. For informative text the AA design-target judgment is direct. “Role-dependent” means a decorative/unused pair has no standalone normative verdict; apply 1.4.11 only when it supplies required identification/state information. This avoids declaring every subtle grid line inaccessible. The generic inventory comparator of 3 is not a text criterion: row 100 TextLink/WindowBg also passes its proper normal-text 4.5 threshold at 6.319701. The actual checkbox judgment above supplies the relevant role.

| Id / colours | Sample RGB fg / bg | Pixel fg / bg | Ratio / target | Numeric result / applicability |
|---|---|---|---:|---|
| 0 Text / WindowBg | #FFFFFF / #101010 | (52,37) / (152,37) | 19.028111 / 4.5 | PASS; Text |
| 1 Text / ChildBg | #FFFFFF / #101010 | (367,37) / (467,37) | 19.028111 / 4.5 | PASS; Text |
| 2 Text / PopupBg | #FFFFFF / #141414 | (682,37) / (782,37) | 18.422536 / 4.5 | PASS; Text |
| 3 Text / FrameBg | #FFFFFF / #1E2F49 | (997,37) / (1097,37) | 13.478665 / 4.5 | PASS; Text |
| 4 Text / FrameBgHovered | #FFFFFF / #24466E | (52,99) / (152,99) | 9.649693 / 4.5 | PASS; Text |
| 5 Text / FrameBgActive | #FFFFFF / #326AAD | (367,99) / (467,99) | 5.525354 / 4.5 | PASS; Text |
| 6 Text / Button | #FFFFFF / #24466E | (682,99) / (782,99) | 9.649693 / 4.5 | PASS; Text |
| 7 Text / ButtonHovered | #FFFFFF / #4296FA | (997,99) / (1097,99) | 3.010919 / 4.5 | FAIL; Text |
| 8 Text / ButtonActive | #FFFFFF / #0F87FA | (52,161) / (152,161) | 3.579753 / 4.5 | FAIL; Text |
| 9 Text / Header | #FFFFFF / #1F3A58 | (367,161) / (467,161) | 11.638219 / 4.5 | PASS; Text |
| 10 Text / HeaderHovered | #FFFFFF / #387BCB | (682,161) / (782,161) | 4.317706 / 4.5 | FAIL; Text |
| 11 Text / HeaderActive | #FFFFFF / #4296FA | (997,161) / (1097,161) | 3.010919 / 4.5 | FAIL; Text |
| 12 Text / TableHeaderBg | #FFFFFF / #303033 | (52,223) / (152,223) | 13.155752 / 4.5 | PASS; Text |
| 13 Text / TableRowBg | #FFFFFF / #101010 | (367,223) / (467,223) | 19.028111 / 4.5 | PASS; Text |
| 14 Text / TableRowBgAlt | #FFFFFF / #1E1E1E | (682,223) / (782,223) | 16.671157 / 4.5 | PASS; Text |
| 15 Text / Tab | #FFFFFF / #2A4F82 | (997,223) / (1097,223) | 8.270379 / 4.5 | PASS; Text |
| 16 Text / TabHovered | #FFFFFF / #387BCB | (52,285) / (152,285) | 4.317706 / 4.5 | FAIL; Text |
| 17 Text / TabSelected | #FFFFFF / #3369AD | (367,285) / (467,285) | 5.577959 / 4.5 | PASS; Text |
| 18 Text / TabDimmed | #FFFFFF / #111A25 | (682,285) / (782,285) | 17.524736 / 4.5 | PASS; Text |
| 19 Text / TabDimmedSelected | #FFFFFF / #23436C | (997,285) / (1097,285) | 10.043672 / 4.5 | PASS; Text |
| 20 Text / TitleBg | #FFFFFF / #0A0A0A | (52,347) / (152,347) | 19.798146 / 4.5 | PASS; Text |
| 21 Text / TitleBgActive | #FFFFFF / #294A7A | (367,347) / (467,347) | 8.917867 / 4.5 | PASS; Text |
| 22 Text / TitleBgCollapsed | #FFFFFF / #080808 | (682,347) / (782,347) | 20.027384 / 4.5 | PASS; Text |
| 23 Text / MenuBarBg | #FFFFFF / #242424 | (997,347) / (1097,347) | 15.522910 / 4.5 | PASS; Text |
| 24 Text / TextSelectedBg | #FFFFFF / #213F62 | (52,409) / (152,409) | 10.757989 / 4.5 | PASS; Text |
| 25 TextDisabled / WindowBg | #808080 / #101010 | (367,409) / (467,409) | 4.817927 / 4.5 | PASS; Informative text; inactive UI exempt |
| 26 TextDisabled / ChildBg | #808080 / #101010 | (682,409) / (782,409) | 4.817927 / 4.5 | PASS; Informative text; inactive UI exempt |
| 27 TextDisabled / PopupBg | #808080 / #141414 | (997,409) / (1097,409) | 4.664595 / 4.5 | PASS; Informative text; inactive UI exempt |
| 28 TextDisabled / FrameBg | #808080 / #1E2F49 | (52,471) / (152,471) | 3.412804 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 29 TextDisabled / FrameBgHovered | #808080 / #24466E | (367,471) / (467,471) | 2.443307 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 30 TextDisabled / FrameBgActive | #808080 / #326AAD | (682,471) / (782,471) | 1.399022 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 31 TextDisabled / Button | #808080 / #24466E | (997,471) / (1097,471) | 2.443307 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 32 TextDisabled / ButtonHovered | #808080 / #4296FA | (52,533) / (152,533) | 1.311706 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 33 TextDisabled / ButtonActive | #808080 / #0F87FA | (367,533) / (467,533) | 1.103272 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 34 TextDisabled / Header | #808080 / #1F3A58 | (682,533) / (782,533) | 2.946803 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 35 TextDisabled / HeaderHovered | #808080 / #387BCB | (997,533) / (1097,533) | 1.093245 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 36 TextDisabled / HeaderActive | #808080 / #4296FA | (52,595) / (152,595) | 1.311706 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 37 TextDisabled / TableHeaderBg | #808080 / #303033 | (367,595) / (467,595) | 3.331043 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 38 TextDisabled / TableRowBg | #808080 / #101010 | (682,595) / (782,595) | 4.817927 / 4.5 | PASS; Informative text; inactive UI exempt |
| 39 TextDisabled / TableRowBgAlt | #808080 / #1E1E1E | (997,595) / (1097,595) | 4.221145 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 40 TextDisabled / Tab | #808080 / #2A4F82 | (52,657) / (152,657) | 2.094064 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 41 TextDisabled / TabHovered | #808080 / #387BCB | (367,657) / (467,657) | 1.093245 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 42 TextDisabled / TabSelected | #808080 / #3369AD | (682,657) / (782,657) | 1.412342 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 43 TextDisabled / TabDimmed | #808080 / #111A25 | (997,657) / (1097,657) | 4.437271 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 44 TextDisabled / TabDimmedSelected | #808080 / #23436C | (52,719) / (152,719) | 2.543062 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 45 TextDisabled / TitleBg | #808080 / #0A0A0A | (367,719) / (467,719) | 5.012900 / 4.5 | PASS; Informative text; inactive UI exempt |
| 46 TextDisabled / TitleBgActive | #808080 / #294A7A | (682,719) / (782,719) | 2.258008 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 47 TextDisabled / TitleBgCollapsed | #808080 / #080808 | (997,719) / (1097,719) | 5.070943 / 4.5 | PASS; Informative text; inactive UI exempt |
| 48 TextDisabled / MenuBarBg | #808080 / #242424 | (52,781) / (152,781) | 3.930408 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 49 TextDisabled / TextSelectedBg | #808080 / #213F62 | (367,781) / (467,781) | 2.723928 / 4.5 | FAIL; Informative text; inactive UI exempt |
| 50 WindowBg / WindowBg | #0F0F0F / #101010 | (682,781) / (782,781) | 1.007386 / 3 | FAIL; Role-dependent / decorative inventory |
| 51 ChildBg / WindowBg | #101010 / #101010 | (997,781) / (1097,781) | 1.000000 / 3 | FAIL; Role-dependent / decorative inventory |
| 52 PopupBg / WindowBg | #141414 / #101010 | (52,843) / (152,843) | 1.032871 / 3 | FAIL; Role-dependent / decorative inventory |
| 53 Border / WindowBg | #3F3F48 / #101010 | (367,843) / (467,843) | 1.826636 / 3 | FAIL; Role-dependent / decorative inventory |
| 54 BorderShadow / WindowBg | #101010 / #101010 | (682,843) / (782,843) | 1.000000 / 3 | FAIL; Role-dependent / decorative inventory |
| 55 FrameBg / WindowBg | #1E2F49 / #101010 | (997,843) / (1097,843) | 1.411721 / 3 | FAIL; Role-dependent / decorative inventory |
| 56 FrameBgHovered / WindowBg | #24466E / #101010 | (52,905) / (152,905) | 1.971888 / 3 | FAIL; Role-dependent / decorative inventory |
| 57 FrameBgActive / WindowBg | #326AAD / #101010 | (367,905) / (467,905) | 3.443781 / 3 | PASS; Role-dependent / decorative inventory |
| 58 TitleBg / WindowBg | #0A0A0A / #101010 | (682,905) / (782,905) | 1.040468 / 3 | FAIL; Role-dependent / decorative inventory |
| 59 TitleBgActive / WindowBg | #294A7A / #101010 | (997,905) / (1097,905) | 2.133707 / 3 | FAIL; Role-dependent / decorative inventory |
| 60 TitleBgCollapsed / WindowBg | #080808 / #101010 | (52,967) / (152,967) | 1.052516 / 3 | FAIL; Role-dependent / decorative inventory |
| 61 MenuBarBg / WindowBg | #242424 / #101010 | (367,967) / (467,967) | 1.225808 / 3 | FAIL; Role-dependent / decorative inventory |
| 62 ScrollbarBg / WindowBg | #0A0A0A / #101010 | (682,967) / (782,967) | 1.040468 / 3 | FAIL; Role-dependent / decorative inventory |
| 63 ScrollbarGrab / WindowBg | #4F4F4F / #101010 | (997,967) / (1097,967) | 2.323014 / 3 | FAIL; Role-dependent / decorative inventory |
| 64 ScrollbarGrabHovered / WindowBg | #696969 / #101010 | (52,1029) / (152,1029) | 3.466075 / 3 | PASS; Role-dependent / decorative inventory |
| 65 ScrollbarGrabActive / WindowBg | #828282 / #101010 | (367,1029) / (467,1029) | 4.951440 / 3 | PASS; Role-dependent / decorative inventory |
| 66 CheckMark / WindowBg | #4296FA / #101010 | (682,1029) / (782,1029) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 67 SliderGrab / WindowBg | #3D85E0 / #101010 | (997,1029) / (1097,1029) | 5.101160 / 3 | PASS; Role-dependent / decorative inventory |
| 68 SliderGrabActive / WindowBg | #4296FA / #101010 | (52,1091) / (152,1091) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 69 Button / WindowBg | #24466E / #101010 | (367,1091) / (467,1091) | 1.971888 / 3 | FAIL; Role-dependent / decorative inventory |
| 70 ButtonHovered / WindowBg | #4296FA / #101010 | (682,1091) / (782,1091) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 71 ButtonActive / WindowBg | #0F87FA / #101010 | (997,1091) / (1097,1091) | 5.315481 / 3 | PASS; Role-dependent / decorative inventory |
| 72 Header / WindowBg | #1F3A58 / #101010 | (52,1153) / (152,1153) | 1.634968 / 3 | FAIL; Role-dependent / decorative inventory |
| 73 HeaderHovered / WindowBg | #387BCB / #101010 | (367,1153) / (467,1153) | 4.406995 / 3 | PASS; Role-dependent / decorative inventory |
| 74 HeaderActive / WindowBg | #4296FA / #101010 | (682,1153) / (782,1153) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 75 Separator / WindowBg | #3F3F48 / #101010 | (997,1153) / (1097,1153) | 1.826636 / 3 | FAIL; Role-dependent / decorative inventory |
| 76 SeparatorHovered / WindowBg | #185399 / #101010 | (52,1215) / (152,1215) | 2.479202 / 3 | FAIL; Role-dependent / decorative inventory |
| 77 SeparatorActive / WindowBg | #1A66BF / #101010 | (367,1215) / (467,1215) | 3.349662 / 3 | PASS; Role-dependent / decorative inventory |
| 78 ResizeGrip / WindowBg | #1A2B3F / #101010 | (682,1215) / (782,1215) | 1.324039 / 3 | FAIL; Role-dependent / decorative inventory |
| 79 ResizeGripHovered / WindowBg | #326AAD / #101010 | (997,1215) / (1097,1215) | 3.443781 / 3 | PASS; Role-dependent / decorative inventory |
| 80 ResizeGripActive / WindowBg | #3F8FEE / #101010 | (52,1277) / (152,1277) | 5.776342 / 3 | PASS; Role-dependent / decorative inventory |
| 81 InputTextCursor / WindowBg | #FFFFFF / #101010 | (367,1277) / (467,1277) | 19.028111 / 3 | PASS; Role-dependent / decorative inventory |
| 82 TabHovered / WindowBg | #387BCB / #101010 | (682,1277) / (782,1277) | 4.406995 / 3 | PASS; Role-dependent / decorative inventory |
| 83 Tab / WindowBg | #2A4F82 / #101010 | (997,1277) / (1097,1277) | 2.300754 / 3 | FAIL; Role-dependent / decorative inventory |
| 84 TabSelected / WindowBg | #3369AD / #101010 | (52,1339) / (152,1339) | 3.411303 / 3 | PASS; Role-dependent / decorative inventory |
| 85 TabSelectedOverline / WindowBg | #4296FA / #101010 | (367,1339) / (467,1339) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 86 TabDimmed / WindowBg | #111A25 / #101010 | (682,1339) / (782,1339) | 1.085786 / 3 | FAIL; Role-dependent / decorative inventory |
| 87 TabDimmedSelected / WindowBg | #23436C / #101010 | (997,1339) / (1097,1339) | 1.894537 / 3 | FAIL; Role-dependent / decorative inventory |
| 88 TabDimmedSelectedOverline / WindowBg | #101010 / #101010 | (52,1401) / (152,1401) | 1.000000 / 3 | FAIL; Role-dependent / decorative inventory |
| 89 DockingPreview / WindowBg | #336EB4 / #101010 | (367,1401) / (467,1401) | 3.651758 / 3 | PASS; Role-dependent / decorative inventory |
| 90 DockingEmptyBg / WindowBg | #333333 / #101010 | (682,1401) / (782,1401) | 1.506025 / 3 | FAIL; Role-dependent / decorative inventory |
| 91 PlotLines / WindowBg | #9C9C9C / #101010 | (997,1401) / (1097,1401) | 6.930791 / 3 | PASS; Role-dependent / decorative inventory |
| 92 PlotLinesHovered / WindowBg | #FF6E59 / #101010 | (52,1463) / (152,1463) | 6.910490 / 3 | PASS; Role-dependent / decorative inventory |
| 93 PlotHistogram / WindowBg | #E6B300 / #101010 | (367,1463) / (467,1463) | 9.797337 / 3 | PASS; Role-dependent / decorative inventory |
| 94 PlotHistogramHovered / WindowBg | #FF9900 / #101010 | (682,1463) / (782,1463) | 8.887481 / 3 | PASS; Role-dependent / decorative inventory |
| 95 TableHeaderBg / WindowBg | #303033 / #101010 | (997,1463) / (1097,1463) | 1.446372 / 3 | FAIL; Role-dependent / decorative inventory |
| 96 TableBorderStrong / WindowBg | #4F4F59 / #101010 | (52,1525) / (152,1525) | 2.351421 / 3 | FAIL; Role-dependent / decorative inventory |
| 97 TableBorderLight / WindowBg | #3B3B40 / #101010 | (367,1525) / (467,1525) | 1.708525 / 3 | FAIL; Role-dependent / decorative inventory |
| 98 TableRowBg / WindowBg | #101010 / #101010 | (682,1525) / (782,1525) | 1.000000 / 3 | FAIL; Role-dependent / decorative inventory |
| 99 TableRowBgAlt / WindowBg | #1E1E1E / #101010 | (997,1525) / (1097,1525) | 1.141379 / 3 | FAIL; Role-dependent / decorative inventory |
| 100 TextLink / WindowBg | #4296FA / #101010 | (52,1587) / (152,1587) | 6.319701 / 3 inventory; 4.5 text | PASS both; normal link text uses 4.5 |
| 101 TextSelectedBg / WindowBg | #213F62 / #101010 | (367,1587) / (467,1587) | 1.768742 / 3 | FAIL; Role-dependent / decorative inventory |
| 102 TreeLines / WindowBg | #3F3F48 / #101010 | (682,1587) / (782,1587) | 1.826636 / 3 | FAIL; Role-dependent / decorative inventory |
| 103 DragDropTarget / WindowBg | #E8E802 / #101010 | (997,1587) / (1097,1587) | 14.474668 / 3 | PASS; Role-dependent / decorative inventory |
| 104 NavCursor / WindowBg | #4296FA / #101010 | (52,1649) / (152,1649) | 6.319701 / 3 | PASS; Role-dependent / decorative inventory |
| 105 NavWindowingHighlight / WindowBg | #B8B8B8 / #101010 | (367,1649) / (467,1649) | 9.592346 / 3 | PASS; Role-dependent / decorative inventory |
| 106 NavWindowingDimBg / WindowBg | #363636 / #101010 | (682,1649) / (782,1649) | 1.574611 / 3 | FAIL; Role-dependent / decorative inventory |
| 107 ModalWindowDimBg / WindowBg | #525252 / #101010 | (997,1649) / (1097,1649) | 2.435167 / 3 | FAIL; Role-dependent / decorative inventory |
| 108 NavCursor / FrameBg | #4296FA / #1E2F49 | (52,1711) / (152,1711) | 4.476595 / 3 | PASS; Required state if used |
| 109 NavCursor / FrameBgHovered | #4296FA / #24466E | (367,1711) / (467,1711) | 3.204899 / 3 | PASS; Required state if used |
| 110 NavCursor / FrameBgActive | #4296FA / #326AAD | (682,1711) / (782,1711) | 1.835105 / 3 | FAIL; Required state if used |
| 111 NavCursor / Button | #4296FA / #24466E | (997,1711) / (1097,1711) | 3.204899 / 3 | PASS; Required state if used |
| 112 NavCursor / Header | #4296FA / #1F3A58 | (52,1773) / (152,1773) | 3.865337 / 3 | PASS; Required state if used |
| 113 NavCursor / TableHeaderBg | #4296FA / #303033 | (367,1773) / (467,1773) | 4.369347 / 3 | PASS; Required state if used |
| 114 CheckMark / FrameBg | #4296FA / #1E2F49 | (682,1773) / (782,1773) | 4.476595 / 3 | PASS; Required state if used |
| 115 CheckMark / FrameBgHovered | #4296FA / #24466E | (997,1773) / (1097,1773) | 3.204899 / 3 | PASS; Required state if used |
| 116 CheckMark / FrameBgActive | #4296FA / #326AAD | (52,1835) / (152,1835) | 1.835105 / 3 | FAIL; Required state if used |
| 117 CheckMark / Button | #4296FA / #24466E | (367,1835) / (467,1835) | 3.204899 / 3 | PASS; Required state if used |
| 118 CheckMark / Header | #4296FA / #1F3A58 | (682,1835) / (782,1835) | 3.865337 / 3 | PASS; Required state if used |
| 119 CheckMark / TableHeaderBg | #4296FA / #303033 | (997,1835) / (1097,1835) | 4.369347 / 3 | PASS; Required state if used |
| 120 SliderGrab / FrameBg | #3D85E0 / #1E2F49 | (52,1897) / (152,1897) | 3.613434 / 3 | PASS; Required state if used |
| 121 SliderGrab / FrameBgHovered | #3D85E0 / #24466E | (367,1897) / (467,1897) | 2.586943 / 3 | FAIL; Required state if used |
| 122 SliderGrab / FrameBgActive | #3D85E0 / #326AAD | (682,1897) / (782,1897) | 1.481267 / 3 | FAIL; Required state if used |
| 123 SliderGrab / Button | #3D85E0 / #24466E | (997,1897) / (1097,1897) | 2.586943 / 3 | FAIL; Required state if used |
| 124 SliderGrab / Header | #3D85E0 / #1F3A58 | (52,1959) / (152,1959) | 3.120038 / 3 | PASS; Required state if used |
| 125 SliderGrab / TableHeaderBg | #3D85E0 / #303033 | (367,1959) / (467,1959) | 3.526866 / 3 | PASS; Required state if used |
| 126 SliderGrabActive / FrameBg | #4296FA / #1E2F49 | (682,1959) / (782,1959) | 4.476595 / 3 | PASS; Required state if used |
| 127 SliderGrabActive / FrameBgHovered | #4296FA / #24466E | (997,1959) / (1097,1959) | 3.204899 / 3 | PASS; Required state if used |
| 128 SliderGrabActive / FrameBgActive | #4296FA / #326AAD | (52,2021) / (152,2021) | 1.835105 / 3 | FAIL; Required state if used |
| 129 SliderGrabActive / Button | #4296FA / #24466E | (367,2021) / (467,2021) | 3.204899 / 3 | PASS; Required state if used |
| 130 SliderGrabActive / Header | #4296FA / #1F3A58 | (682,2021) / (782,2021) | 3.865337 / 3 | PASS; Required state if used |
| 131 SliderGrabActive / TableHeaderBg | #4296FA / #303033 | (997,2021) / (1097,2021) | 4.369347 / 3 | PASS; Required state if used |
| 132 TextLink / FrameBg | #4296FA / #1E2F49 | (52,2083) / (152,2083) | 4.476595 / 4.5 | FAIL; Link text if used |
| 133 TextLink / FrameBgHovered | #4296FA / #24466E | (367,2083) / (467,2083) | 3.204899 / 4.5 | FAIL; Link text if used |
| 134 TextLink / FrameBgActive | #4296FA / #326AAD | (682,2083) / (782,2083) | 1.835105 / 4.5 | FAIL; Link text if used |
| 135 TextLink / Button | #4296FA / #24466E | (997,2083) / (1097,2083) | 3.204899 / 4.5 | FAIL; Link text if used |
| 136 TextLink / Header | #4296FA / #1F3A58 | (52,2145) / (152,2145) | 3.865337 / 4.5 | FAIL; Link text if used |
| 137 TextLink / TableHeaderBg | #4296FA / #303033 | (367,2145) / (467,2145) | 4.369347 / 4.5 | FAIL; Link text if used |
| 138 Separator / FrameBg | #464F65 / #1E2F49 | (682,2145) / (782,2145) | 1.647432 / 3 | FAIL; Required state if used |
| 139 Separator / FrameBgHovered | #495A77 / #24466E | (997,2145) / (1097,2145) | 1.384107 / 3 | FAIL; Required state if used |
| 140 Separator / FrameBgActive | #506C96 / #326AAD | (52,2207) / (152,2207) | 1.033115 / 3 | FAIL; Required state if used |
| 141 Separator / Button | #495A77 / #24466E | (367,2207) / (467,2207) | 1.384107 / 3 | FAIL; Required state if used |
| 142 Separator / Header | #47546C / #1F3A58 | (682,2207) / (782,2207) | 1.525488 / 3 | FAIL; Required state if used |
| 143 Separator / TableHeaderBg | #4F4F5A / #303033 | (997,2207) / (1097,2207) | 1.627857 / 3 | FAIL; Required state if used |
| 144 TableBorderStrong / FrameBg | #4F4F59 / #1E2F49 | (52,2269) / (152,2269) | 1.665642 / 3 | FAIL; Required state if used |
| 145 TableBorderStrong / FrameBgHovered | #4F4F59 / #24466E | (367,2269) / (467,2269) | 1.192472 / 3 | FAIL; Required state if used |
| 146 TableBorderStrong / FrameBgActive | #4F4F59 / #326AAD | (682,2269) / (782,2269) | 1.464553 / 3 | FAIL; Required state if used |
| 147 TableBorderStrong / Button | #4F4F59 / #24466E | (997,2269) / (1097,2269) | 1.192472 / 3 | FAIL; Required state if used |
| 148 TableBorderStrong / Header | #4F4F59 / #1F3A58 | (52,2331) / (152,2331) | 1.438207 / 3 | FAIL; Required state if used |
| 149 TableBorderStrong / TableHeaderBg | #4F4F59 / #303033 | (367,2331) / (467,2331) | 1.625737 / 3 | FAIL; Required state if used |
| 150 TableBorderLight / FrameBg | #3B3B40 / #1E2F49 | (682,2331) / (782,2331) | 1.210243 / 3 | FAIL; Required state if used |
| 151 TableBorderLight / FrameBgHovered | #3B3B40 / #24466E | (997,2331) / (1097,2331) | 1.154146 / 3 | FAIL; Required state if used |
| 152 TableBorderLight / FrameBgActive | #3B3B40 / #326AAD | (52,2393) / (152,2393) | 2.015646 / 3 | FAIL; Required state if used |
| 153 TableBorderLight / Button | #3B3B40 / #24466E | (367,2393) / (467,2393) | 1.154146 / 3 | FAIL; Required state if used |
| 154 TableBorderLight / Header | #3B3B40 / #1F3A58 | (682,2393) / (782,2393) | 1.044990 / 3 | FAIL; Required state if used |
| 155 TableBorderLight / TableHeaderBg | #3B3B40 / #303033 | (997,2393) / (1097,2393) | 1.181249 / 3 | FAIL; Required state if used |
| 156 Text / TextSelectedBg | #FFFFFF / #2B5387 | (52,2455) / (152,2455) | 7.807055 / 4.5 | PASS; Text; selection over FrameBg |
| 157 TextDisabled / TextSelectedBg | #808080 / #2B5387 | (367,2455) / (467,2455) | 1.976750 / 4.5 | FAIL; Informative text; inactive UI exempt; selection over FrameBg |

## Focus and keyboard

[2.1.1 Keyboard](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html) requires comparable functionality without particular keystroke timings. [2.4.7 Focus Visible](https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html) requires visible focus. [2.4.11 Focus Not Obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html) requires that the focused component not be entirely hidden. The stronger two-pixel-perimeter area and same-pixel 3:1 change requirement belongs to **2.4.13 AAA**, [Focus Appearance](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance), not to those two AA criteria.

At each font scale, holding Ctrl while issuing successive Tab events, then releasing Ctrl, reaches **Files, Outliner, Inspector, Table and the main toolbar**. Pressing and releasing Ctrl+Tab separately simply toggled recent panes in the exploratory harness; that was corrected before the final run. Each final trace contains 55 navigation observations, with native focused-window/item identities and rectangles. Palette search opens through Ctrl+Shift+P; typing its action id and Enter runs Files Refresh. The same route opens Save as; Escape cancels it. No document becomes dirty.

The controlled widget test reaches button, text input, numeric input, checkbox, combo, selectable, tree node and small reset button using Tab only. All **24 kind/scale observations** have a visible changed focus region inside the view. Compared with the unfocused baseline, their expanded item rectangles contain **312–3080 newly blue navigation pixels**. This is pixel evidence of a focus cue, not a screen-reader result or proof of the AAA perimeter-area rule.

| Scale / kind | Focused PNG | Item rectangle | Newly blue pixels |
|---|---|---|---:|
| 1.0 text input | widgets-1.0-0.png | (13, 55, 532, 74) | 1551 |
| 1.0 numeric input | widgets-1.0-1.png | (13, 78, 546, 97) | 1557 |
| 1.0 checkbox | widgets-1.0-2.png | (13, 101, 85, 120) | 312 |
| 1.0 combo | widgets-1.0-3.png | (13, 124, 546, 143) | 1867 |
| 1.0 selectable | widgets-1.0-4.png | (9, 145, 751, 162) | 3028 |
| 1.0 tree node | widgets-1.0-5.png | (13, 164, 106, 177) | 412 |
| 1.0 small reset button | widgets-1.0-6.png | (13, 181, 56, 194) | 688 |
| 1.0 button | widgets-1.0-7.png | (13, 32, 63, 51) | 871 |
| 1.5 text input | widgets-1.5-0.png | (13, 69, 548, 95) | 1572 |
| 1.5 numeric input | widgets-1.5-1.png | (13, 99, 570, 125) | 1578 |
| 1.5 checkbox | widgets-1.5-2.png | (13, 129, 120, 155) | 438 |
| 1.5 combo | widgets-1.5-3.png | (13, 159, 570, 185) | 2158 |
| 1.5 selectable | widgets-1.5-4.png | (9, 187, 751, 211) | 3056 |
| 1.5 tree node | widgets-1.5-5.png | (13, 213, 145, 233) | 582 |
| 1.5 small reset button | widgets-1.5-6.png | (13, 237, 76, 257) | 1204 |
| 1.5 button | widgets-1.5-7.png | (13, 39, 87, 65) | 1577 |
| 2.0 text input | widgets-2.0-0.png | (13, 81, 560, 113) | 1590 |
| 2.0 numeric input | widgets-2.0-1.png | (13, 117, 588, 149) | 1596 |
| 2.0 checkbox | widgets-2.0-2.png | (13, 153, 147, 185) | 537 |
| 2.0 combo | widgets-2.0-3.png | (13, 189, 588, 221) | 2458 |
| 2.0 selectable | widgets-2.0-4.png | (9, 223, 751, 253) | 3080 |
| 2.0 tree node | widgets-2.0-5.png | (13, 255, 175, 281) | 714 |
| 2.0 small reset button | widgets-2.0-6.png | (13, 285, 91, 311) | 2067 |
| 2.0 button | widgets-2.0-7.png | (13, 45, 105, 77) | 2628 |

The isolated widget tests have no entirely offscreen focused item. The real docked Inspector does: its Reset button, native item 3660806787, is entirely to the right of the visible clip at every scale. An independent fresh keyboard-only ResetProbe reaches that item by Ctrl+Tab and Tab, saves its focused frame, and presses Enter. The amount changes from 30 to its default 15 in memory, proving which control had focus; nothing is saved. This is an invisible-focus defect in the current layout, not a missing command implementation.

| Scale | Reset focus rectangle (x0,y0,x1,y1) | Visible right edge | Result |
|---|---|---:|---|
| 1.0 | (1289,105,1332,118) | 1280 | Entirely clipped; Enter resets 30→15. |
| 1.5 | (1349,167,1412,187) | 1280 | Entirely clipped; Enter resets 30→15. |
| 2.0 | (1394,235,1472,261) | 1280 | Entirely clipped; Enter resets 30→15. |

Evidence: reset-check/reset-check.json and reset-focus-*.png in the scratch output. The profile's visible/unobscured-focus requirement is not met for this fixture; the AA focus criteria are the design benchmark. Toolbar trace rectangles from navigation layer 0 can be zero because menu navigation uses another layer; do not classify those zeros as offscreen controls. ResetProbe reads the actual current navigation layer. No complete 2.4.11 conformance claim is made.

### Routes and remaining gaps

| Surface | Evidence and keyboard route | Limit |
|---|---|---|
| Toolbar, Files, Outliner, Inspector, Table | Fresh keyboard traces visit each at every scale. Standard Tab/Shift+Tab, Enter/Space and window cycling operate their ordinary widgets. | Traversal is not exhaustive activation coverage. Reset is specifically verified activatable but entirely invisible in the Inspector. |
| Palette | Fresh actual key events open it, search action IDs, execute Refresh and open/cancel Save as. | No explicit focus-restoration state is apparent; invoker restoration after every popup path needs a targeted test. |
| Table editing | Accepted A037 review proves 36 unchanged tests: cell movement, Enter edits, Escape cancellation, text/document undo isolation, enum choice, checkbox Space, filter, header Enter sorting, fill/paste preview/apply/cancel, reference Follow/Back, copying and ranges. | Its test rig is 1600×900 with Drawing=false; those results are not visual-focus or 200%-layout results. |
| Panel commands | Files offers save-all/refresh; Table offers fill/paste/copy. Actions are named in the palette and unavailable reasons are provided. | File selection, named Repair, Follow/Back and field Reset have no dedicated palette command; ordinary keyboard widgets can still provide routes. Palette absence alone is not keyboard failure. |
| Long help and status | Inspector displays the first help sentence; full help is attached to the following nonfocusable TextWrapped item. Status/help are not themselves general keyboard-navigation items. | Add an explicit keyboard-reachable full-help/details route. Do not assume all SetItemTooltip calls are mouse-only: ImGui has HoverFlagsForTooltipNav. File-row tooltip dwell paths need their own verification. |
| Dock rearrangement and reset | Docked panes are keyboard reachable in the initial layout. | No application-owned keyboard rearrangement/reset route was found. Do not infer accessible rearrangement from pointer docking. |
| Scale/theme/accessibility settings | No shipped editor scale control or scale command found. | Programmatic FontScaleMain in this audit is not an end-user feature. |

Source locators: [EditorWindow](../../src/Brutal.Authoring.Editor.ImGui/EditorWindow.cs) Configure92–96, palette252–272 and659–718, toolbar460–490, inspector532–635, help554–562; [DataTableView](../../src/Brutal.Authoring.Editor.ImGui/DataTableView.cs) panel commands343–351, controls356–467 and567–629; [DataWorkspace](../../src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs) Files panel136–142 and178–210; [accepted A037 review](../../handoff/reviews/A037.md) and [keyboard tests](../../tests/Brutal.Authoring.Tests/DataTableKeyboardTests.cs). Installed ImGui XML's SetItemTooltip documentation explicitly mentions mouse/nav tooltip settings; the nonfocusable help-item attachment is a separate application issue.

## Text size

[1.4.4 Resize Text](https://www.w3.org/WAI/WCAG22/Understanding/resize-text.html) targets enlargement to 200% without lost content or functionality. These tests demonstrate the enlarged font is rendered; they do not establish that a user can choose it, that all information remains available, or that OS display scaling works.

| FontScaleMain / measured font | Docked view | Full-width Table | Finding |
|---|---|---|---|
| 1.0 / 13px | Long names already truncate in the Outliner and right Inspector; field-label text can run past x1279. | Long row names and headers truncate; reference arrows are visible. | Baseline disclosure issue, not only a 200% regression. |
| 1.5 / 20px | Wider text consumes the unchanged pane widths; more labels truncate. | Fixed-width enum cells clip “Normal” while the combo arrow remains. | Font enlargement works; fixed geometry does not grow with it. |
| 2.0 / 26px | Inspector name at roughly x983–1279,y78–99 is clipped. Labels beginning around x1183 run off the screen. Files status wraps, preserving it; Repair remains visible near x155–246,y190–219. | Enum values truncate to “Quie”, “Norm”, “Brig” around x647–739,y146–247; Follow arrows remain visible around x871–908. | Information truncates and the Inspector Reset focus is entirely clipped. Follow and Repair remain present. |

The docked Table's rightmost link column starts beyond its initial panel edge near x972, with a horizontal scrollbar at y701–709. That is scroll-reachable clipping pending a scaled keyboard-scroll test, not proof of a lost Follow action. In the full-width table render the link values/arrows are present at every scale. Combo popup disclosure may expose full choices, but that popup was not exhaustively exercised at each scale.

Authoritative images: docked-1.0/1.5/2.0.png and table-1.0/1.5/2.0.png. The two clean runs reproduce them byte for byte. Fixed initial dock ratios and table widths explain the pressure: [EditorWindow](../../src/Brutal.Authoring.Editor.ImGui/EditorWindow.cs) uses a 20% left split then 30% of the remainder on the right; [DataTableView](../../src/Brutal.Authoring.Editor.ImGui/DataTableView.cs) has fixed 180/200/140/120 controls and 160/90/80 columns. Those source facts explain the renders; they do not replace measuring them.

No localized long strings, dynamic text-size persistence, different monitors, real assistive zoom or IME were tested. Keep those in the profile's later acceptance matrix. Do not mark U12–U14 complete from these three font-scale renders.

## Screen readers

No OS screen reader, UI Automation inspector or accessibility backend was launched. CPU images and keyboard focus cannot reveal a semantic accessibility tree. The current source offers no demonstrated general bridge with names, roles, values, relations, bounds, focus changes and action requests.

Dear ImGui's [repository](https://github.com/ocornut/imgui) lists accessibility among unsupported areas. [Issue #4122](https://github.com/ocornut/imgui/issues/4122) separates navigation from the semantics readers need; its old 2021 tab limitation should not be presented as current behavior. The January 2024 maintainer update reports full tab cycling since 1.89.4. [Issue #8022](https://github.com/ocornut/imgui/issues/8022) is still open; on 28 April 2026 the maintainer calls the Xbox narration sample a “proof of concept.” Separately rendered labels, advanced application structures and coverage remain work. Do not turn that example into a claim that this editor supports Narrator or NVDA.

### Other engines: borrow the right layer

| Engine/versioned source | What it establishes | What it does not establish |
|---|---|---|
| [Godot 4.5 introduction](https://godotengine.org/releases/4.5/), [4.7 GUI changes](https://godotengine.org/releases/4.7/#gui), [4.7 EditorSettings](https://docs.godotengine.org/en/4.7/classes/class_editorsettings.html) | AccessKit-backed Control support began in 4.5; 4.7 adds landmark context navigation for readers and searchable long menus. Current settings provide accessibility modes, property descriptions for readers, and automatic/custom editor scaling under interface/editor/appearance. | The 4.5 incomplete-editor warning is historical, not a measurement of 4.7. Release/settings documentation does not prove complete editor coverage. [Application guidance](https://docs.godotengine.org/en/stable/tutorials/ui/creating_applications.html#screen-reader-integration) still requires labels, reading order and real-reader testing. |
| [Unity 6000.6 Accessibility module](https://docs.unity.com/en-us/engine/6000.6/manual/accessibility/module-intro), [AccessibilityHierarchy](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/accessibility/accessibilityhierarchy) | Runtime/application APIs cover supported mobile and desktop readers, including Windows Narrator/macOS VoiceOver; the developer supplies and synchronizes a semantic hierarchy, exposed for the main application window. Historical [2021.1 editor preferences](https://docs.unity3d.com/es/2021.1/Manual/Preferences.html) separately document editor text/icon scaling. | Neither runtime hierarchy APIs nor historical scaling docs prove whole Unity Editor reader support. Avoid the outdated claim that Unity reader APIs are mobile-only. |
| [Unreal screen-reader support](https://dev.epicgames.com/documentation/en-us/unreal-engine/supporting-screen-readers-in-unreal-engine), [5.4 notes](https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-engine-5.4-release-notes?application_version=5.4) | Experimental screen-reader documentation covers common UMG/game widgets and custom Slate adapters. 5.4 introduced an initially Experimental Editor Preferences Application Scale. [UMG DPI scaling](https://dev.epicgames.com/documentation/en-us/unreal-engine/dpi-scaling-in-unreal-engine) is a separate runtime feature. | UMG accessibility is not whole-editor accessibility; historical 5.4 status is not a claim about a later release's maturity. |

### Realistic options

Primary sources below were checked on 2026-11-01; engine feature statements are versioned. Costs below are **INVENTED engineering estimates**, not vendor guarantees.

1. **MIXED — semantic accessibility-tree bridge; high effort.** Borrow [AccessKit's tree/action model](https://accesskit.dev/how-it-works/) and combine it with stable editor object/property/command IDs. Start with one Windows form and a table: names, roles, values, enabled/read-only/selected states, bounds, parent/child order and stable lifetime. Route focus/invoke/text actions through the editor's normal command and refusal boundaries. Add text ranges, validation announcements and scrolling; then test the real native host with Narrator, NVDA and UI Automation inspection. This needs native-host integration, not a PNG hook.
2. **MIXED — accessible text/HTML companion over the existing agent API; medium effort for a useful first slice.** Reuse describe/find/get/problems/refs, preview/edit/undo/save and game-provided rules with native or semantic HTML controls. Expose location, full labels, pending edits and refusal reasons. This can make structured-data work accessible sooner; it does not make the existing viewport or ImGui widget tree accessible. AgentSession JSON alone is not a screen-reader interface.
3. **BORROWED — targeted self-voicing from the ImGui narration discussion; low–medium initial effort.** Announce focus, errors and completion. Treat it as supplemental feedback: reader-driven exploration, braille and rich text interaction do not follow automatically.
4. **MIXED — retained/native shell over existing document/session APIs; high migration cost.** Reuse validated commands and persistence under controls with an established accessibility implementation. Plan viewport alternatives and real-user testing rather than assuming a framework solves the workflow.

AccessKit is a candidate, not an approved or installed dependency. Its [C-bindings README](https://github.com/AccessKit/accesskit-c) identifies Apache-2.0 OR MIT library licensing, BSD-style Chromium-derived portions, and LGPL-2.1 for the separate Meson build system. Review the exact chosen distribution and notices before adoption; this audit changes no package or licence decision.

## Smallest fixes

All after values below are predictions/calculations for a proposed change, **not edited-theme measurements**. Claude owns implementation. Re-render and repeat the same probes after any fix.

| Order / provenance | Measured before | Smallest intervention | Expected after / acceptance |
|---|---|---|---|
| 1 MIXED: WCAG text target plus INVENTED colours | Normal focused-button text 3.010919; selectable/tree 4.317706. Matrix also exposes active/hovered tab/header states. | Darken applicable hover/active fills while retaining white text. Candidate opaque hover #2467A3 and active #285986. Check every state, not only idle. | Calculated white contrast 5.925539 and 7.333886 respectively, ≥4.5. Preserve distinct hover/active cues and remeasure all affected contexts. |
| 2 MIXED: WCAG state identification plus INVENTED boundary | Unchecked box/window 1.411721. | Add a clearly contrasting checkbox boundary; candidate opaque #8CA3BD, without relying on its readable label to state “unchecked.” | Calculated 7.329258 against #101010 and 5.191720 against #1E2F49. Test both checked and unchecked, hover/active/disabled and focus. |
| 3 INVENTED responsive layout over the existing widget seams | Inspector Reset is entirely clipped already at 1× (x1289–1332); labels/enum values truncate further at2×; no user scale setting. | Add a persisted, keyboard-reachable text-size control; let field labels wrap above inputs or adapt widths. Provide a full-value/details route instead of requiring pointer resize/hover. | User chooses 100–200%; all labels/current values/actions and their focus cues remain visible or explicitly obtainable by keyboard at 1280×720. Repeat U12/U13, long strings and scaled scrolling; do not mark complete from nominal font size. |
| 4 MIXED: existing palette plus keyboard criterion | Long help is attached to nonfocusable text; no named full-help/layout-reset action. | Add palette/help-details access, clear focus restoration, and keyboard reset/rearrangement alternatives. Test file tooltip navigation with dwell before declaring it missing. | Full help reachable without pointer, Escape returns to useful focus, layout can be recovered. Validate with a finite named-action inventory. |
| 5 MIXED: real accessibility-tree systems plus current command API | No demonstrated reader bridge; PNGs give pixels, not semantics. | Approve one semantic prototype or companion frontend, with stable roles/IDs/actions and actual reader tests. | Named Narrator/NVDA workflows pass reading, editing, validation, navigation, undo and explicit save, with game rules/read-only policies retained. No general reader-support claim before this. |

Informative muted shortcut/help colours need role-specific review: do not “fix” disabled controls merely to satisfy a threshold from which they are exempt, and do not exempt useful instructions just because TextDisabled draws them. Colour alone must not convey the only state/error; preserve words, shapes and explicit reasons.

## Self-check

The final population is the 158 indexed rendered pairs in run7/style.json. random.Random(580059).sample(range(158),20) selected 20 distinct pairs for the standing output check. random.Random(580006).sample(range(158),6) independently selected the card's six-pair clean rerun. The draws do not overlap: **26 checks over 26 distinct pairs**.

For each selected pair, the independent Python decoder re-read the saved foreground/background coordinates, calculated contrast, and compared a fresh-process run8 pixel pair and metadata. **20/20 standing checks and 6/6 card checks match exactly.** This is a measurement-consistency pass; several selected theme pairs correctly remain below their accessibility targets.

All 158 independently decoded ratios agree with the C# calculations within 1e-12. All 12 canonical docked/table/palette/Save-as images match byte-for-byte between fresh processes. The 24 focus-kind observations have changed visible pixels and are inside the display; all three scale traces visit every stock pane. No frozen probe or library source changed between final runs.

| Draw / index | Pair | Recomputed ratio | Fresh pixels / source judgment |
|---|---|---:|---|
| Standing20 / seed580059 / 67 | SliderGrab / WindowBg | 5.101160 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 91 | PlotLines / WindowBg | 6.930791 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 4 | Text / FrameBgHovered | 9.649693 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 153 | TableBorderLight / Button | 1.154146 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 33 | TextDisabled / ButtonActive | 1.103272 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 50 | WindowBg / WindowBg | 1.007386 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 118 | CheckMark / Header | 3.865337 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 157 | TextDisabled / TextSelectedBg | 1.976750 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 25 | TextDisabled / WindowBg | 4.817927 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 121 | SliderGrab / FrameBgHovered | 2.586943 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 74 | HeaderActive / WindowBg | 6.319701 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 123 | SliderGrab / Button | 2.586943 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 129 | SliderGrabActive / Button | 3.204899 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 61 | MenuBarBg / WindowBg | 1.225808 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 34 | TextDisabled / Header | 2.946803 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 51 | ChildBg / WindowBg | 1.000000 | MATCH; unrounded numeric FAIL correctly retained |
| Standing20 / seed580059 / 38 | TextDisabled / TableRowBg | 4.817927 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 23 | Text / MenuBarBg | 15.522910 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 45 | TextDisabled / TitleBg | 5.012900 | MATCH; unrounded numeric PASS correctly retained |
| Standing20 / seed580059 / 89 | DockingPreview / WindowBg | 3.651758 | MATCH; unrounded numeric PASS correctly retained |
| Card6 / seed580006 / 111 | NavCursor / Button | 3.204899 | MATCH; unrounded numeric PASS correctly retained |
| Card6 / seed580006 / 99 | TableRowBgAlt / WindowBg | 1.141379 | MATCH; unrounded numeric FAIL correctly retained |
| Card6 / seed580006 / 30 | TextDisabled / FrameBgActive | 1.399022 | MATCH; unrounded numeric FAIL correctly retained |
| Card6 / seed580006 / 46 | TextDisabled / TitleBgActive | 2.258008 | MATCH; unrounded numeric FAIL correctly retained |
| Card6 / seed580006 / 9 | Text / Header | 11.638219 | MATCH; unrounded numeric PASS correctly retained |
| Card6 / seed580006 / 42 | TextDisabled / TabSelected | 1.412342 | MATCH; unrounded numeric FAIL correctly retained |

Exploratory runs1–6 are retained but are not the authoritative measurements: instrumentation was corrected to avoid layering WindowBg twice, decode native window names correctly, read font size inside a frame, clear prior palette queries with Ctrl+A, hold Ctrl during multi-window cycling, include all text-bearing default states, and measure the eight widget kinds. These corrections changed measurement coverage/setup, not the library, theme or expected accessibility thresholds.

The document gate and exact build/run commands, hashes and complete retained-file inventory are in REPORT.md. No unrun native reader/DPI/user study is counted as a pass.
