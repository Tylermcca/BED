using System.Text;
using System.Text.RegularExpressions;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

public sealed class AgentIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "bed-agent-index-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string Initialize()
    {
        var data = Path.Combine(root, "data"); var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
        var server = new McpServer(new AgentSession(DataProject.Open(data, schemas)));
        var response = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""")!;
        return response["result"]!["instructions"]!.GetValue<string>();
    }
    [Fact]
    public void Connection_gets_a_bounded_framework_index_without_writing_files()
    {
        var text = Initialize();
        Assert.Equal(AgentIndex.Text, text);
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, AgentIndex.MaxBytes);
        Assert.InRange(text.Split('\n').Length, 1, AgentIndex.MaxLines);
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.Contains("change data and levels only through BED's commands", text);
        Assert.Contains("if a name isn't in the reference or source, it doesn't exist", text);
        Assert.Contains("dotnet build", text); Assert.Contains("dotnet run", text);
        Assert.Contains("bed-cli --data data --schemas schemas problems", text);
        Assert.Contains("BED --probe-play . --timeout 30", text);
        Assert.Contains("there is no game build/run target yet", text);
        Assert.Equal(10, Regex.Matches(text, @"(?m)^\d+\. T\d+").Count);
        foreach (var file in new[] { "core-libs", "sdl", "opengl", "glfw-imgui", "imgui-sdl", "vulkan", "box2d", "jolt", "assets", "audio", "misc", "shipping", "verification", "traps", "export-gaps" })
            Assert.Contains("references/" + file + ".md", text);
    }
    [Fact]
    public void Personal_bundled_scene_example_also_gets_the_same_instruction_files()
    {
        var installed = Path.Combine(root, "scene-starter");
        var files = new[] { "data/level.json", "schemas/scene.schema.json", "reference_map.json",
            "assets/cube.obj", "assets/cube.obj.asset.json", "assets/pillar.obj", "assets/pillar.obj.asset.json",
            "assets/textured-crate.obj", "assets/textured-crate.obj.asset.json", "assets/textured-crate.mtl",
            "assets/textured-crate.png", "assets/textured-crate.png.asset.json", "game/SceneStarter.exe", "game/LICENSE.txt", "game/NOTICE.txt" };
        foreach (var file in files)
        {
            var path = Path.Combine(installed, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Self-authored copy fixture; never executed.");
        }
        var made = new ProjectCreator(Path.Combine(root, "recipes")).TryExampleAt(Path.Combine(root, "projects"));
        Assert.Equal(ProjectCreationStatus.Created, made.Status);
        Assert.Equal(AgentIndex.Text, File.ReadAllText(Path.Combine(made.Folder!, "AGENTS.md")));
        Assert.Equal(AgentIndex.ClaudeInstructions, File.ReadAllText(Path.Combine(made.Folder!, "CLAUDE.md")));
        Assert.All(files, file => Assert.Equal(File.ReadAllBytes(Path.Combine(installed, file)), File.ReadAllBytes(Path.Combine(made.Folder!, file))));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("recipes")]
    public void New_project_has_the_same_index_and_one_line_Claude_pointer(string template)
    {
        var connected = Initialize(); var recipes = Path.Combine(root, "installed-recipes");
        foreach (var file in ProjectTemplates.All.Single(t => t.Id == "recipes").Files.Where(f => f is not ("AGENTS.md" or "CLAUDE.md")))
        {
            var path = Path.Combine(recipes, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "Self-authored fixture: " + file);
        }
        var creator = new ProjectCreator(recipes); var location = Path.Combine(root, "projects");
        var preview = creator.Preview(template, location, "Example");
        Assert.True(preview.CanCreate, preview.Reason);
        Assert.Contains("AGENTS.md", preview.Files); Assert.Contains("CLAUDE.md", preview.Files);
        Assert.False(Directory.Exists(location));
        var made = creator.Create(template, location, "Example");
        Assert.Equal(ProjectCreationStatus.Created, made.Status);
        Assert.Equal(connected, File.ReadAllText(Path.Combine(made.Folder!, "AGENTS.md")));
        var claude = File.ReadAllText(Path.Combine(made.Folder!, "CLAUDE.md"));
        Assert.Equal(AgentIndex.ClaudeInstructions, claude);
        Assert.Single(claude.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
