using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Bed.Editor.ImGui;

/// <summary>A built-in project recipe. Files are relative to the new project, not executable commands.</summary>
public sealed record ProjectTemplate(string Id, string Name, string Description, IReadOnlyList<string> Files);

/// <summary>The two self-authored project recipes offered by BED. Does not create or edit projects.</summary>
public static class ProjectTemplates
{
    // MIXED: approved start-window choices, existing packaged recipes and standard data/schemas layout.
    // Kept in source so the installed/single-file app needs no additional content-copy rule.
    private const string Manifest = """
        { "version": 1, "templates": [
          { "id": "empty", "name": "Empty project", "description": "An editable starter file and its rules file, with notes.",
            "files": ["README.md", "data/settings.json", "schemas/settings.schema.json"] },
          { "id": "recipes", "name": "Recipes example", "description": "Linked wares and recipes, copied to your own folder.",
            "files": ["README.md", "reference_map.json", "data/recipes.json", "data/wares.json", "schemas/recipes.schema.json", "schemas/wares.schema.json"] }
        ] }
        """;

    /// <summary>The built-in templates, in their display order. No file system work is performed.</summary>
    public static IReadOnlyList<ProjectTemplate> All { get; } = ReadManifest();

    private static IReadOnlyList<ProjectTemplate> ReadManifest()
    {
        using var json = JsonDocument.Parse(Manifest);
        return Array.AsReadOnly(json.RootElement.GetProperty("templates"u8).EnumerateArray().Select(t =>
            new ProjectTemplate(t.GetProperty("id"u8).GetString()!, t.GetProperty("name"u8).GetString()!,
                t.GetProperty("description"u8).GetString()!, Array.AsReadOnly(t.GetProperty("files"u8)
                    .EnumerateArray().Select(f => f.GetString()!).ToArray()))).ToArray());
    }

    internal static ProjectTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    // Only these declared text files are copied. Never scan an installation or accept a user-supplied template path.
    internal static IReadOnlyDictionary<string, byte[]> Contents(ProjectTemplate template, string recipes)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var relative in template.Files)
        {
            if (template.Id == "empty") files.Add(relative, Encoding.UTF8.GetBytes(Empty(relative)));
            else
            {
                var source = Path.Combine(recipes, relative.Replace('/', Path.DirectorySeparatorChar));
                files.Add(relative, File.ReadAllBytes(source));
            }
        }
        return new ReadOnlyDictionary<string, byte[]>(files);
    }

    private static string Empty(string relative) => relative switch
    {
        "data/settings.json" => """
            { "title": "Untitled", "enabled": true }
            """ + "\n",
        "schemas/settings.schema.json" => """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "Project settings", "type": "object",
              "description": "Start here: edit a field in settings.json, or add fields to this schema.",
              "properties": {
                "title": { "type": "string", "description": "A name to display in your own data." },
                "enabled": { "type": "boolean", "description": "An example on/off setting." }
              },
              "required": ["title", "enabled"], "additionalProperties": false
            }
            """ + "\n",
        "README.md" => """
            # Your project

            Open this folder in BED. Open data/settings.json and edit its fields, then save.
            The matching schemas/settings.schema.json describes those fields and their allowed values.
            Add your own data and matching schemas as the project grows. No game connection is required.
            """ + "\n",
        _ => throw new InvalidOperationException("The starter template names a file that doesn't exist.")
    };
}
