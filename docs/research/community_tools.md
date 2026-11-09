# Community tools for BRUTAL (surveyed 2026-11-04)

The user, 2026-11-04: "see if anyone's made additional tools for Brutal that you can incorporate". A read-only survey of Ahwoo's forums (Tools and Libraries, General, Guides and Help), GitHub repository search, and NuGet. BRUTAL went open source on 15 October 2026, so there's little yet. GitHub *code* search needs a login, so that was not covered.

| Tool | What it is | Licence | Fit for brutal-ed |
|---|---|---|---|
| **ImCode**, [github.com/aproposmath/ImCode](https://github.com/aproposmath/ImCode) ([forum](https://forums.ahwoo.com/forums/brutal/tools-and-libraries/imcode-code-editor-component-for-brutal.1395/)) | A Dear ImGui code editor: tabs, split view, find and replace, highlighting, completion, hover, error squiggles, undo, themes, Vim mode | MIT | **The one to try.** A proper text pane for raw JSON and schema files, with check findings as squiggles. Needs a JSON highlighter (custom languages are supported). One commit and no releases, so pin a commit. |
| **Brutal-MCP**, [nuget.org/packages/com.IvanMurzak.Brutal.MCP](https://www.nuget.org/packages/com.IvanMurzak.Brutal.MCP) | An MCP server *inside a running game*: query and change live state, tail logs, run C# in Debug builds | Apache-2.0, but it depends on `com.IvanMurzak.Rql` under **PolyForm Shield 1.0.0** | Covers the live game, where ours covers data. **Don't bundle it** (Rql's licence, a cloud link, its source repo returned 404). Worth naming to users as a companion. |
| **brutal-skill**, [github.com/WafflBytes/brutal-skill](https://github.com/WafflBytes/brutal-skill) | A Claude Code skill of checked BRUTAL API notes, plus a `dotnet new` game template | MIT | Likely the same project as our local `brutal` skill. Its template could start a sample game for the launcher. |
| **EmpireEngine**, [github.com/ardenarrison/EmpireEngine](https://github.com/ardenarrison/EmpireEngine) | A full engine on BRUTAL: an editor, glTF, FBX and OBJ import, a PBR Vulkan renderer | Apache-2.0 (name and logo are trademarks) | Early (0.2.8), with natives for Apple Silicon only. At most, a reference for adding FBX import. |
| **VkFeatureChain**, [forum thread 1406](https://forums.ahwoo.com/forums/brutal/tools-and-libraries/vkphysicaldevice-feature-chaining-helper.1406/) | A Vulkan 1.2 to 1.4 feature-struct chaining helper, posted as code | none stated | Not relevant unless we render with Vulkan ourselves. |

**Found on GitHub directly (repository search, 2026-11-04; the anonymous rate limit cut the search short, and code search needs a login):**

| Tool | What it is | Licence | Fit |
|---|---|---|---|
| **Brutal.ImGuizmo**, [github.com/Liteboxnz/Brutal.ImGuizmo](https://github.com/Liteboxnz/Brutal.ImGuizmo) | An ImGuizmo port for Brutal.ImGui: move, rotate and scale handles in a 3D view | MIT | **Useful for brutal-ed's viewport:** dragging kit markers (S13), parts and pivots by hand instead of typing numbers. New (last push 1 Nov); not affiliated with Ahwoo. |
| **Brutal.ImGui.GameEngine.Editor.Studies**, [github.com/Liteboxnz/Brutal.ImGui.GameEngine.Editor.Studies](https://github.com/Liteboxnz/Brutal.ImGui.GameEngine.Editor.Studies) | Studies of Unity's and Unreal's editor layouts rebuilt in Brutal.ImGui: docking, outliner, details, content browser, output log | none stated | **A reference** for brutal-ed's layout and docking, not code to take (no licence). |
| **brutal-web**, [github.com/finalbuildgames-com/brutal-web](https://github.com/finalbuildgames-com/brutal-web) | One C# BRUTAL game running on Linux desktop and in Chrome, through a raylib host | MIT | **Interesting for browser play:** a browser build path, where players join from a phone or browser. Raylib-based, not BRUTAL's own renderer, so it's a reference to study, not a drop-in. |
| **brutal-imgui-standalone**, [github.com/meow-sci/brutal-imgui-standalone](https://github.com/meow-sci/brutal-imgui-standalone) | Runs Brutal.ImGui standalone | MIT | Already covered by our own host. |

Also on GitHub: about 26 Kitten Space Agency mods (game mods, not tools), and an Ahwoo CLI setup action ([actions-marketplace-validations/ahwootech_setup-ahwoo-cli](https://github.com/actions-marketplace-validations/ahwootech_setup-ahwoo-cli)), for CI later.

Also seen: Ahwoo's official [samples branch](https://github.com/ahwootech/BRUTAL/tree/samples) (a reference); a planet and star generator announced as coming soon (forum thread 1456). Not yet checked: the [Discord](https://discord.gg/MEuNTYbAh8) and the [procgen thread](https://forums.ahwoo.com/forums/brutal/in-progress/procgen-in-brutal.1490/).

**Recommendation:** try ImCode as a raw-text pane behind a game-neutral seam, pinned to a commit with its licence recorded, after the current performance and code-health work. Nothing found replaces what's built.
