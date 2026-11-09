using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Read-only setup examples. Copy and guide navigation require an explicit UI action.</summary>
public sealed class AiSetupPanel
{
    /// <summary>The existing per-file panel and palette identity.</summary>
    public const string Id = "bed.ai-setup";
    /// <summary>The normal panel command; no external command is run by it.</summary>
    public const string ActionId = "editor.panel." + Id;
    /// <summary>Why a fileless adapter cannot be represented by the CLI.</summary>
    public const string BuiltInReason = "this project's adapter is built into BED and can't be passed to bed-cli";

    /// <summary>Captures known paths once. Null project means the packaged recipes sample.</summary>
    public AiSetupPanel(string bedFolder, ProjectPaths? project = null, string? unavailable = null)
    {
        Unavailable = unavailable;
        GuidePath = Path.Combine(Path.GetFullPath(bedFolder), "guides", "using_bed_with_ai.md");
        if (unavailable is null) Snippets = AiSetupSnippets.Create(bedFolder, project);
    }

    /// <summary>The refusal, if runnable snippets would misrepresent the current adapter.</summary>
    public string? Unavailable { get; }
    /// <summary>Exact examples, or null when they must not be offered.</summary>
    public AiSetupSnippets? Snippets { get; }
    /// <summary>The packaged local guide, not a network URL.</summary>
    public string GuidePath { get; }
    /// <summary>An initially closed panel exposed through the normal keyboard palette.</summary>
    public Panel AsPanel() => new(Id, "Use with an AI assistant", Draw, OpenAtStart: false, Width: 720, Height: 560);

    /// <summary>Draws the same content in the project panel and start-screen Help window.</summary>
    public void Draw()
    {
        Ui.TextWrapped("BED does not contact an AI provider. Your external assistant may send tool inputs and results to its provider; check its privacy settings before connecting."u8);
        Ui.TextWrapped("These examples do not change any app's configuration. Value edits are drafts until save; file create, rename and delete happen immediately unless previewed. Ask the assistant to preview first."u8);
        if (Unavailable is { } reason) Ui.TextWrapped(reason);
        if (Snippets is { } snippets)
        {
            Ui.TextWrapped("PowerShell: inspect the project"u8);
            Copy("Copy bed-cli command", snippets.Command);
            Ui.TextWrapped("Claude Desktop configuration entry"u8);
            Copy("Copy Claude Desktop JSON", snippets.DesktopJson);
            Ui.TextWrapped("PowerShell: add to Claude Code"u8);
            Copy("Copy claude mcp add", snippets.ClaudeCode);
            Ui.TextWrapped("For a plain project, --read-only may be placed before describe or mcp. It cannot be combined with --adapter. Adapters are programs: use only ones you trust."u8);
        }
        ImGui.Separator();
        Ui.TextLinkOpenURL("Read the packaged AI guide"u8, new Uri(GuidePath).AbsoluteUri);
        Ui.TextWrapped(GuidePath);
    }

    private static void Copy(string label, string value)
    {
        if (Ui.Button(label)) ImGui.SetClipboardText(value);
        Ui.TextWrapped(value);
    }
}
