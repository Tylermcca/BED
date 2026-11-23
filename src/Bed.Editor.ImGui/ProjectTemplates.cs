using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Bed.Editor.ImGui;

/// <summary>A built-in project recipe. Files are relative to the new project, not executable commands.</summary>
public sealed record ProjectTemplate(string Id, string Name, string Description, IReadOnlyList<string> Files);

/// <summary>The self-authored project recipes offered by BED. Does not create or edit projects.</summary>
public static class ProjectTemplates
{
    // MIXED: approved start-window choices, existing packaged recipes and standard data/schemas layout.
    // Kept in source so the installed/single-file app needs no additional content-copy rule.
    private const string Manifest = """
        {
          "version": 1,
          "templates": [
            {
              "id": "empty",
              "name": "Empty project",
              "description": "An editable starter file and its rules file, with notes.",
              "files": [
                "README.md",
                "AGENTS.md",
                "CLAUDE.md",
                "data/settings.json",
                "schemas/settings.schema.json"
              ]
            },
            {
              "id": "recipes",
              "name": "Recipes example",
              "description": "Linked wares and recipes, copied to your own folder.",
              "files": [
                "README.md",
                "AGENTS.md",
                "CLAUDE.md",
                "reference_map.json",
                "data/recipes.json",
                "data/wares.json",
                "schemas/recipes.schema.json",
                "schemas/wares.schema.json"
              ]
            },
            {
              "id": "game",
              "name": "New game",
              "description": "A small playable game, two Gum screens and source you can build.",
              "files": [
                "README.md",
                "AGENTS.md",
                "CLAUDE.md",
                "LICENSE.txt",
                "FIRST_GAME.md",
                ".bed/project.json",
                "reference_map.json",
                "data/settings.json",
                "schemas/settings.schema.json",
                "ui/Game.gumj",
                "ui/Screens/Menu.gusj",
                "ui/Screens/Play.gusj",
                "ui/Standards/Text.gutj",
                "ui/Standards/ColoredRectangle.gutj",
                "game/StarterGame.exe",
                "game/THIRD-PARTY-NOTICES.txt",
                "source/StarterGame.csproj",
                "source/Program.cs",
                "source/LiveValuesHost.cs",
                "source/Game.cs",
                "source/GameScreen.cs",
                "source/GameWindow.cs"
              ]
            }
          ]
        }
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
    internal static IReadOnlyDictionary<string, byte[]> Contents(ProjectTemplate template, string recipes, bool readBytes = true)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var relative in template.Files)
        {
            if (relative is "AGENTS.md" or "CLAUDE.md")
                files.Add(relative, Encoding.UTF8.GetBytes(relative == "AGENTS.md" ? AgentIndex.Text+(template.Id=="game"?GameInstructions:"") : AgentIndex.ClaudeInstructions));
            else if (template.Id == "empty") files.Add(relative, Encoding.UTF8.GetBytes(Empty(relative)));
            else
            {
                var source = Path.Combine(template.Id=="game"?Path.Combine(Path.GetDirectoryName(recipes)!,"new-game"):recipes, relative.Replace('/', Path.DirectorySeparatorChar));
                if(readBytes) files.Add(relative, File.ReadAllBytes(source));
                else { using var input=File.OpenRead(source);files.Add(relative,Array.Empty<byte>()); }
            }
        }
        return new ReadOnlyDictionary<string, byte[]>(files);
    }

    internal static void AddInstructions(IDictionary<string, byte[]> files)
    {
        files["AGENTS.md"] = Encoding.UTF8.GetBytes(AgentIndex.Text);
        files["CLAUDE.md"] = Encoding.UTF8.GetBytes(AgentIndex.ClaudeInstructions);
    }

    private const string GameInstructions="""


        ## This starter game
        Read README.md for the game, controls and exact paths. Game data is data/settings.json.
        Change it through bed-cli or the BED connection, with preview, checks and explicit save.
        Keep each data edit one undoable command; never replace the data file by hand.
        Edit Menu and Play in ui/Game.gumj with Gum. Saving a screen updates the running game.
        Follow Gum's own AI instructions for screen work: https://github.com/vchelaru/Gum/blob/main/CLAUDE.md
        Game code is source/. Keep it separate from BED's editor and AI connection.
        Build: dotnet publish source/StarterGame.csproj -c Release --self-contained true -o game
        Headless check: game/StarterGame.exe --project . --headless --script start,collect,back
        Run only checks for what changed; no run-any-code editor tools, weakened checks or package upgrades.
        """+"\n";

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
            Add your own data and matching schemas as the project grows. No game add-on is required.
            """ + "\n",
        _ => throw new InvalidOperationException("The starter template names a file that doesn't exist.")
    };
}
