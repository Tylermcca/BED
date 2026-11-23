using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

// Construction captures paths only. Opening previews; only the labelled action writes.
internal sealed class AiConnectPanel(AiSetupSnippets? snippets, string? unavailable, AiConnectionLocations locations)
{
    internal const string Id = "bed.ai-connect";
    private readonly AiConnectionFiles? files = snippets is null ? null : new(snippets);
    private readonly IReadOnlyList<AiConnectionTarget> targets = locations.Targets;
    private int selected;
    private AiConnectionChange? change;
    private string said = "";
    internal Panel AsPanel() => new(Id, "Connect an AI app", Draw, OpenAtStart: false, Width: 760, Height: 650);
    internal void Draw()
    {
        Ui.TextWrapped("Connect this project to an AI app. BED does not contact an AI provider. Check the app's privacy settings before connecting; its assistant can change project files."u8);
        if (files is null) { Ui.TextWrapped(unavailable ?? "AI setup is unavailable."); return; }
        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (Ui.Button(targets[i].Name)) { selected = i; change = null; said = ""; }
        }
        change ??= files.Preview(targets[selected]);
        Ui.TextWrapped(change.Target.File);
        Ui.TextWrapped(change.Before is null ? "No settings file found here yet." : "This app already has a settings file here.");
        Ui.TextWrapped("This is the app's own settings file. Other profiles or project settings in the app may override it. Nothing changes until you press the button below."u8);
        if (change.Backup is { } backup) { Ui.TextWrapped("Backup made before writing:"u8); Ui.TextWrapped(backup); }
        else Ui.TextWrapped("A new settings file needs no backup. Remove connection below takes the added entry out again."u8);
        if (Ui.BeginChild("Change to the settings file###config-change"u8, new Brutal.Numerics.float2(0, 210), ImGuiChildFlags.Borders))
            Ui.TextWrapped(change.Refusal ?? change.Preview);
        Ui.EndChild();
        if (Ui.Button("Refresh preview"u8)) change = files.Preview(targets[selected]);
        ImGui.SameLine();
        Ui.BeginDisabled(!change.CanApply);
        if (Ui.Button(change.Removing ? "Remove connection###connection-action" : "Connect###connection-action"))
        {
            said = files.Apply(change).Message;
            change = files.Preview(targets[selected]);
        }
        Ui.EndDisabled();
        Ui.TextWrapped(said);
        Ui.TextWrapped("Restart the AI app after a change. Removal deletes only this project's connection entry; backup copies stay beside the settings file."u8);
    }
}
