using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;

namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    private EditorThemePreference rememberedTheme = EditorThemePreference.System;
    private static readonly object preferencesGate = new();

    private void ReadPreferences()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("textScale"u8, out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetSingle(out var value))
                textScale = value;
            if (root.TryGetProperty("theme"u8, out var theme) && theme.ValueKind == JsonValueKind.String)
                rememberedTheme = theme.GetString() switch
                {
                    "Light" => EditorThemePreference.Light,
                    "Dark" => EditorThemePreference.Dark,
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
            if (textScale is { } wanted) EditorWindow<DataDocument>.SetTextScale(wanted);
            textScale = EditorWindow<DataDocument>.TextScale;
            EditorTheme.Preference = rememberedTheme;
            EditorTheme.Refresh(); // Colours only: retain DPI metrics and both text scales.
            return;
        }
        var size = EditorWindow<DataDocument>.TextScale;
        var theme = EditorTheme.Preference;
        var sizeChanged = size != textScale; var themeChanged = theme != rememberedTheme;
        if (!sizeChanged && !themeChanged) return;
        textScale = size; rememberedTheme = theme;
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
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, settings.ToJsonString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // Remembering is optional.
    }
}
