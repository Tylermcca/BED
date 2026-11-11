using System.Runtime.CompilerServices;
using Bed.Editor.ImGui;

namespace Bed.Editor.Text;

/// <summary>
/// Plugs the Text panel (<see cref="JsonTextPane"/>) into a <see cref="DataWorkspace"/> through its per-file panel seam
/// (<see cref="DataWorkspace.FilePanels"/>), so every file shown afterwards gets one, and finds a file's pane again.
/// <code>
/// var workspace = new DataWorkspace(project).AddTextPanel();
/// workspace.Show("wares.json");
/// var text = workspace.TextOf("wares.json");
/// </code>
/// INVENTED: a host opts in, so the core editor carries no third-party code.
/// </summary>
public static class TextPanel
{
    private static readonly ConditionalWeakTable<DataWorkspace, Dictionary<string, JsonTextPane>> Panes = new();

    /// <summary>Adds the Text panel to every file <paramref name="workspace"/> shows from now on; a file whose editor
    /// already exists keeps the panels it has. Adding it again changes nothing. Returns the workspace.</summary>
    public static DataWorkspace AddTextPanel(this DataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (Panes.TryGetValue(workspace, out _)) return workspace;
        var panes = new Dictionary<string, JsonTextPane>(StringComparer.Ordinal);
        Panes.Add(workspace, panes);
        workspace.FilePanels.Add((file, editor) =>
        {
            var pane = new JsonTextPane(editor, () => file.ReadOnlyReason);
            panes[file.Name] = pane;
            return pane.AsPanel();
        });
        return workspace;
    }

    /// <summary>The Text panel of the file called <paramref name="name"/>, once it's been shown, or null: also when the
    /// Text panel wasn't added to <paramref name="workspace"/>, or the project no longer has that file (renamed or
    /// deleted).</summary>
    public static JsonTextPane? TextOf(this DataWorkspace workspace, string name) =>
        Panes.TryGetValue(workspace, out var panes) && workspace.Project.Find(name) is not null ? panes.GetValueOrDefault(name) : null;
}
