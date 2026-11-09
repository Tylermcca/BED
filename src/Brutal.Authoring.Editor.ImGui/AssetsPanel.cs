using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Read-only selection and metadata over an existing asset browser; no importing or placement.</summary>
public sealed class AssetsPanel(AssetBrowser browser, Action<AssetEntry>? dragSource = null)
{
    private readonly ImInputString search = new(1024, ""u8);
    private bool problemsOnly;
    private (string Guid, string Path)? selected;

    /// <summary>The selected current entry, or null if it disappeared on refresh.</summary>
    public AssetEntry? Selected => selected is { } key
        ? browser.Entries.FirstOrDefault(e => e.Guid == key.Guid && e.RelativePath == key.Path) : null;

    /// <summary>The existing panel registration seam; hosts choose when to attach it.</summary>
    public Panel AsPanel() => new("editor.assets", "Assets", Draw, Width: 480, Height: 500);

    /// <summary>Draws searchable records and truthful model diagnostics without reading asset contents.</summary>
    public void Draw()
    {
        Ui.InputText("Search assets"u8, search);
        Ui.Checkbox("Problems only"u8, ref problemsOnly);
        var entries = browser.Search(search.ToString() ?? "", problemsOnly);
        Ui.TextDisabled($"{entries.Count} of {browser.Entries.Count} assets");
        if (entries.Count == 0) Ui.TextWrapped(browser.Entries.Count == 0 ? "No imported assets." : "No matching assets.");
        if (Ui.BeginChild("Asset list"u8, new float2(0, 160), ImGuiChildFlags.Borders))
        {
            foreach (var entry in entries)
            {
                var key = (entry.Guid, entry.RelativePath);
                if (Ui.Selectable($"{entry.RelativePath}{(entry.HasProblems ? " (needs attention)" : "")}##asset-{entry.Guid}-{entry.RelativePath}", selected == key))
                    selected = key;
                dragSource?.Invoke(entry);
            }
        }
        Ui.EndChild();
        if (Selected is { } current)
        {
            Ui.TextWrapped($"Path: {current.RelativePath}");
            Ui.TextWrapped($"ID: {current.Guid}");
            Ui.TextWrapped($"Importer: {current.Importer}");
            Ui.TextWrapped("Asset type: not provided by this browser."u8);
            Ui.TextWrapped($"Licence: {(string.IsNullOrWhiteSpace(current.Licence) ? "Not recorded" : current.Licence)}");
            Ui.TextWrapped($"AI use: {current.AiUse}");
            if (browser.Index.Resolve(current.Guid) is null)
                Ui.TextWrapped("Cannot resolve this asset: its source is missing or its identity is ambiguous. See the reported problems."u8);
            else
                Ui.TextWrapped("Source indexed. This does not prove current imported content or permission to export."u8);
            foreach (var issue in current.Issues) Ui.TextWrapped($"{issue.Severity}: {issue.Message}");
        }
        else Ui.TextWrapped(selected is null ? "Select an asset to see its details." : "The selected asset is no longer listed. Select another asset.");
        foreach (var issue in browser.General) Ui.TextWrapped($"{issue.Severity}: {issue.Message}");
        if (browser.NotImported.Count > 0)
        {
            Ui.TextWrapped("Not imported"u8);
            foreach (var path in browser.NotImported) Ui.TextWrapped(path);
        }
    }
}

