namespace Bed.Editor;

/// <summary>The same small, local reference for an agent connection and a newly created project.</summary>
internal static class AgentIndex
{
    internal const int MaxBytes = 8192;
    internal const int MaxLines = 150;
    internal const string ClaudeInstructions = "Read AGENTS.md before changing this project.\n";
    internal const string Text = """
        # BED and BRUTAL: project index

        ## Start here
        BED is the editor for the project's data, assets and levels.
        The game is a separate program; BRUTAL supplies native bindings and helpers, not an engine.
        Call `describe` first. It lists this connection's files, schemas, edit calls and refusals.
        Read the project's README for its game, targets and controls. Do not infer them from a folder name.
        Call `checks` for the registered checks. A game adapter may add tools and checks.
        Use `tools/list` to see the actual MCP tools; a tool mentioned elsewhere may be unavailable here.

        ## House rules
        Rule: change data and levels only through BED's commands.
        Use bed-cli or the BED connection for game data; do not rewrite data files by hand.
        If a query cannot read an input, report "couldn't check: <missing input>"; never invent an empty answer.
        Report the check's findings and scope. The person decides whether the result suits their game.
        Block unrequested deletion, git reset --hard, git clean -fd, forced pushes, and publishing.
        Use only listed typed commands; no run-any-code tool.
        Rule: if a name isn't in the reference or source, it doesn't exist.
        Read the value and its schema before changing it. Keep existing ids and links intact.
        Preview edits, inspect the result, then apply and save when authorized by the person's request.
        A preview changes nothing. An applied edit is not a disk save; `save` is explicit.
        Read-only, outside-change and typed-document refusals are reasons to stop that edit and explain it.
        Use the scene tool for scenes; ordinary JSON edits cannot replace a typed scene operation.
        Use `refs` before renaming or removing an item. Do not bypass a failed check by changing its test.
        Hosted proposals are reviewed and accepted in BED; an agent cannot accept its own proposal remotely.
        Keep simulation separate from windowing/rendering so rules can be checked headlessly.
        Do not upgrade packages, add dependencies or change file formats just to make a guessed API compile.
        Run only the affected checks/tests. A build is not proof that a native call works.
        Start a visible game or editor only when the person asks; prefer existing headless checks.

        ## Commands
        MCP calls use names from `tools/list`: `describe`, `problems`, `checks`, `check`, `get`,
        `set`, `insert`, `remove`, `undo`, `save`; scene actions use `scene` when offered.
        `set`/`insert` values are JSON text; places are JSON Pointers, such as `/items/0/name`.
        Use `preview: true` first. For `check`, supply a registered id from `checks`.
        CLI examples below use the usual `data/` and `schemas/` layout, from the project folder.
        For another layout, substitute the actual folders used to start the connection.
        `bed-cli` and `BED` below are the installed programs; run from their folder or put it on PATH.

        ```powershell
        bed-cli --data data --schemas schemas describe
        bed-cli --data data --schemas schemas problems
        bed-cli --data data --schemas schemas checks
        bed-cli --data data --schemas schemas check links.all
        bed-cli --data data --schemas schemas mcp
        ```

        If the project has `reference_map.json`, add `--map reference_map.json` before the call.
        Keep the connection's explicit adapter/assets arguments when using the CLI.
        `check links.all` is the built-in link check; it does not replace the game's registered checks.
        Empty project and Recipes example create data and schemas only: there is no game build/run target yet.
        For game code, read its README and set `$gameProject` to the existing .csproj it names.
        The following build/run commands use that confirmed target, never an invented filename:

        ```powershell
        dotnet build "$gameProject" -c Release
        dotnet run --project "$gameProject" -c Release
        ```

        A normal run may open a window. Use it only on request; read the game's own headless flags first.
        For a saved-files game configured in BED (for example the bundled level example), use:

        ```powershell
        BED --probe-play . --timeout 30
        ```

        That probe runs the configured game headlessly and reports its actual result.
        It does not build a missing game or turn a data-only project into one.
        When working on BED itself, these commands are from the BED source checkout:

        ```powershell
        dotnet build Bed.slnx -c Release
        dotnet test tests/Bed.Tests -c Release --blame-hang-timeout 10m --filter "FullyQualifiedName~ProjectTemplateTests"
        ```

        Replace that test filter with the one or two affected classes. Do not run the whole suite for a small edit.

        ## Ten frequent BRUTAL traps
        These are condensed from the BRUTAL skill's trap index, not substitute API documentation.
        1. T8: old package notes mention private feeds. Ahwoo.BRUTAL packages restore from nuget.org;
           do not request a GitHub token for them.
        2. T10/T1: failure conventions differ. Check signatures: GLFW init returns bool, many SDL/Vulkan
           calls throw, some getters return null handles, and a failed Stb image has null Data.
        3. T12: extension methods need their own namespace. Find the declaration's namespace;
           pointer, Vulkan abstraction and ImGui extensions are separate from the base API.
        4. T13: BRUTALCORE0009/0010 mean managed memory cannot be used as that native pointer.
           Use stackalloc, native memory or a correctly scoped fixed block; keep the analyzers enabled.
        5. T14: use UTF-8 `"text"u8` literals when a native UTF-8 overload exists.
        6. T16: SDL/GLFW initialization, windows and event pumps belong on the main thread.
        7. T17: handle copies alias one object. Destroy owned handles once, in reverse creation order;
           do not destroy borrowed getters. Jolt's Factory uses DestroyInstance, not Dispose.
        8. T2: Vulkan struct constructors set sType. Use `new T()`; default/arrays/stackalloc do not set it.
        9. T33: vsync follows the display, not a fixed 60 Hz. Use a fixed-step accumulator and cap catch-up.
        10. T58: dynamic ImGui strings use a bounded shared arena. Use u8 literals for constants;
            follow the host's per-frame ImString.SharedStorage.Reset lifetime for dynamic text.

        ## Full reference by topic
        The `BRUTAL_HOME` environment variable names the reference clone; its skill is `.claude/skills/brutal/`.
        Public root: https://github.com/WafflBytes/brutal-skill/tree/main/.claude/skills/brutal
        Append the following paths to the skill root; the binding source is `BRUTAL/` in the reference clone.
        - Rules, lookup and templates: `SKILL.md`.
        - Every trap and symptoms: `references/traps.md`; native exports: `references/export-gaps.md`.
        - Memory, numerics, analyzers, logging/jobs: `references/core-libs.md`.
        - SDL window/input/2D/audio: `references/sdl.md`; OpenGL: `references/opengl.md`.
        - GLFW and ImGui: `references/glfw-imgui.md`; ImGui in SDL: `references/imgui-sdl.md`.
        - Vulkan, VMA and shaders: `references/vulkan.md`.
        - Physics: `references/box2d.md` and `references/jolt.md`.
        - Images, textures, meshes and glTF: `references/assets.md`.
        - Audio choices: `references/audio.md`; networking/dialogs: `references/misc.md`.
        - Publishing, logo, notices and native runtime: `references/shipping.md`.
        - Headless self-tests/snapshots and upgrades: `references/verification.md`.
        Confirm every unfamiliar API in the matching BRUTAL source and run each new native call once.
        Keep package versions pinned to the project; reference recipes may target a different version.
        Preserve the BRUTAL credit, opening logo and notices when adapting an existing starter.
        BED returns this local text to the connected client; it makes no AI service request or config change.
        """ + "\n";
}
