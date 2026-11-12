using Brutal.ImGuiApi;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;

namespace Bed.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    private EditorThemePreference rememberedTheme = EditorThemePreference.System;
    private bool rememberedWer;
    private string rememberedGum = "", rememberedProjects = "", rememberedAdapters = "";
    private string keymapNote = "Using the default viewport keymap; choose a keymap to remember it.";
    private static readonly object preferencesGate = new();

    /// <summary>The extra adapters folder remembered in <paramref name="settingsFile"/> (Preferences > Locations), or
    /// null when none is set. Read before the launcher exists, because adapters load as BED starts.</summary>
    public static string? AdaptersFolderIn(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsFile));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("adaptersFolder"u8, out var folder)
                && folder.ValueKind == JsonValueKind.String && folder.GetString() is { Length: > 0 } path ? path : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private void ReadPreferences()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("viewportKeymap"u8, out var keymap))
            {
                var choice = keymap.ValueKind == JsonValueKind.String ? keymap.GetString() : null;
                rememberedWer = choice == "engine";
                keymapNote = choice is "bed" or "engine" ? "" : "Unrecognized viewport keymap; using BED defaults. Choose a keymap to replace it.";
            }
            if (root.TryGetProperty("gumProgram"u8, out var gum) && gum.ValueKind == JsonValueKind.String) rememberedGum = gum.GetString() ?? "";
            if (root.TryGetProperty("projectsFolder"u8, out var projects) && projects.ValueKind == JsonValueKind.String) rememberedProjects = projects.GetString() ?? "";
            if (root.TryGetProperty("adaptersFolder"u8, out var adapters) && adapters.ValueKind == JsonValueKind.String) rememberedAdapters = adapters.GetString() ?? "";
            if (root.TryGetProperty("textScale"u8, out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetSingle(out var value))
                textScale = value;
            if (root.TryGetProperty("theme"u8, out var theme) && theme.ValueKind == JsonValueKind.String)
                rememberedTheme = theme.GetString() switch
                {
                    "Light" => EditorThemePreference.Light,
                    "Dark" => EditorThemePreference.Dark,
                    "HighContrast" => EditorThemePreference.HighContrast,
                    _ => EditorThemePreference.System,
                };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }

    private void KeepPreferences()
    {
        if (!textApplied)
        {
            textApplied = true;
            ViewportControls.Wer = rememberedWer;
            ViewportControls.Note = keymapNote;
            ViewportControls.Changed = false;
            if (textScale is { } wanted) EditorWindow<DataDocument>.SetTextScale(wanted);
            textScale = EditorWindow<DataDocument>.TextScale;
            EditorTheme.Preference = rememberedTheme;
            Locations.Gum = rememberedGum; Locations.Projects = rememberedProjects; Locations.Adapters = rememberedAdapters;
            EditorTheme.Refresh(); // Colours only: retain DPI metrics and both text scales.
            return;
        }
        var size = EditorWindow<DataDocument>.TextScale;
        var theme = EditorTheme.Preference;
        var sizeChanged = size != textScale; var themeChanged = theme != rememberedTheme;
        var keymapChanged = ViewportControls.Changed || ViewportControls.Wer != rememberedWer;
        var gumChanged = Locations.Gum != rememberedGum;
        var projectsChanged = Locations.Projects != rememberedProjects; var adaptersChanged = Locations.Adapters != rememberedAdapters;
        if (!sizeChanged && !themeChanged && !keymapChanged && !gumChanged && !projectsChanged && !adaptersChanged) return;
        rememberedGum = Locations.Gum; rememberedProjects = Locations.Projects; rememberedAdapters = Locations.Adapters;
        if (projectsChanged) projectLocation = new ImInputString(2048, personalProjects);
        textScale = size; rememberedTheme = theme; rememberedWer = ViewportControls.Wer;
        if (keymapChanged) { ViewportControls.Note = ""; ViewportControls.Changed = false; }
        try
        {
            lock (preferencesGate)
            {
                JsonObject settings = new();
                if (File.Exists(SettingsFile))
                {
                    try { settings = JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject ?? new(); }
                    catch (JsonException) { } // Retain the existing recovery behavior for malformed settings.
                }
                // Merge only changed owned keys into the latest file, not the constructor's cached settings.
                if (sizeChanged) settings["textScale"] = size;
                if (themeChanged) settings["theme"] = theme.ToString();
                if (gumChanged) settings["gumProgram"] = rememberedGum;
                if (projectsChanged) settings["projectsFolder"] = rememberedProjects;
                if (adaptersChanged) settings["adaptersFolder"] = rememberedAdapters;
                if (keymapChanged) settings["viewportKeymap"] = rememberedWer ? "engine" : "bed";
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, settings.ToJsonString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // Remembering is optional.
    }
}
