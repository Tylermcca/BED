using System.Text.Json;
using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Toy;
using ImGui = Brutal.ImGuiApi.ImGui;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// Prepared only. No snippets, clipboard, app configuration or URLs are executed by these formatter tests.
[Collection(ImGuiCollection.Name)]
public sealed class AiSetupPanelA388Tests
{
    [Fact] public void Project_map_adapter_and_spaces_survive_json_and_PowerShell_literals()
    {
        const string folder = @"C:\Acme Tools\BED";
        var project = new ProjectPaths(@"C:\Acme Data\O'Brien $x` folder", @"C:\Acme Data\schemas", @"C:\Acme Data\links.json")
            { AdapterPath = @"C:\Acme Tools\AcmeAdapter.dll" };
        var snippets = AiSetupSnippets.Create(folder, project);
        using var doc = JsonDocument.Parse(snippets.DesktopJson);
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("bed");
        Assert.Equal(Path.Combine(folder, "bed-cli.exe"), server.GetProperty("command").GetString());
        Assert.Equal(new[] { "--data", project.Data, "--schemas", project.Schemas, "--map", project.Map,
            "--adapter", project.AdapterPath, "mcp" }, server.GetProperty("args").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.StartsWith("& 'C:\\Acme Tools\\BED\\bed-cli.exe' ".Replace("\\\\", "\\"), snippets.Command);
        Assert.Contains("'C:\\Acme Data\\O''Brien $x` folder'".Replace("\\\\", "\\"), snippets.Command);
        Assert.EndsWith("'describe'", snippets.Command);
        Assert.StartsWith("claude mcp add bed -- ", snippets.ClaudeCode);
        Assert.EndsWith("'mcp'", snippets.ClaudeCode);
    }
    [Fact] public void Sample_paths_match_packaged_recipes_and_guide_layout()
    {
        const string folder = @"C:\Acme Bundle";
        var snippets = AiSetupSnippets.Create(folder);
        using var doc = JsonDocument.Parse(snippets.DesktopJson);
        var args = doc.RootElement.GetProperty("mcpServers").GetProperty("bed").GetProperty("args").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(Path.Combine(folder, "samples", "recipes", "data"), args[1]);
        Assert.Equal(Path.Combine(folder, "samples", "recipes", "schemas"), args[3]);
        Assert.Equal(Path.Combine(folder, "samples", "recipes", "reference_map.json"), args[5]);
        Assert.Equal(Path.Combine(folder, "guides", "using_bed_with_ai.md"), snippets.GuidePath);
        Assert.DoesNotContain("--adapter", args);
    }
    [Fact] public void Plain_read_only_is_supported_but_adapter_combination_is_refused()
    {
        var project = new ProjectPaths(@"C:\Acme\data", @"C:\Acme\schemas");
        Assert.Contains("'--read-only'", AiSetupSnippets.Create(@"C:\Acme Bundle", project, true).Command);
        Assert.Throws<ArgumentException>(() => AiSetupSnippets.Create(@"C:\Acme Bundle",
            project with { AdapterPath = @"C:\Acme\AcmeAdapter.dll" }, true));
    }
    [Theory]
    [InlineData("a'b", "'a''b'")]
    [InlineData("$value; $(test) `x", "'$value; $(test) `x'")]
    public void PowerShell_literals_do_not_interpolate(string input, string expected) => Assert.Equal(expected, AiSetupSnippets.Quote(input));

    [Fact] public void Fileless_adapter_withholds_every_runnable_snippet()
    {
        var panel = new AiSetupPanel(@"C:\Acme Bundle", new ProjectPaths(@"C:\Acme\data", @"C:\Acme\schemas"),
            AiSetupPanel.BuiltInReason);
        Assert.Null(panel.Snippets);
        Assert.Equal("this project's adapter is built into BED and can't be passed to bed-cli", panel.Unavailable);
        Assert.EndsWith(Path.Combine("guides", "using_bed_with_ai.md"), panel.GuidePath);
    }

    [Fact] public void Bundled_adapter_path_is_included_without_recent_filtering()
    {
        var path = @"C:\Acme Bundle\adapters\Acme\AcmeAdapter.dll";
        var panel = new AiSetupPanel(@"C:\Acme Bundle",
            new ProjectPaths(@"C:\Acme\data", @"C:\Acme\schemas") { AdapterPath = path });
        Assert.Null(panel.Unavailable);
        using var json = JsonDocument.Parse(panel.Snippets!.DesktopJson);
        Assert.Contains(path, json.RootElement.GetProperty("mcpServers").GetProperty("bed").GetProperty("args")
            .EnumerateArray().Select(e => e.GetString()));
    }

    private static EditorModel<Board> Model() => new(BoardKit.Session(new Board([])),
        new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));

    [Fact] public void Existing_palette_lists_one_closed_AI_panel_and_opens_it_without_editing()
    {
        var model = Model(); model.Add(new AiSetupPanel(@"C:\Acme Bundle").AsPanel());
        var window = new EditorWindow<Board>(model);
        var entry = Assert.Single(window.PaletteEntries("Use with an AI assistant"));
        Assert.Equal("editor.panel.bed.ai-setup", entry.Id);
        Assert.Null(entry.Unavailable);
        Assert.False(model.IsOpen(AiSetupPanel.Id));
        var revision = model.Session.Revision;
        Assert.True(window.Run(entry.Id));
        Assert.True(model.IsOpen(AiSetupPanel.Id));
        Assert.Equal(revision, model.Session.Revision);
        Assert.False(model.Session.Dirty);
    }

    [Fact] public void Prepared_keyboard_proof_reaches_panel_without_copy_or_link_activation()
    {
        using var host = new HeadlessHost(1600, 1000) { Drawing = false };
        var model = Model(); model.Add(new AiSetupPanel(@"C:\Acme Bundle").AsPanel());
        var keys = new Shortcuts().Default(AiSetupPanel.ActionId, "F8");
        var window = new EditorWindow<Board>(model, shortcuts: keys);
        EditorWindow<Board>.Configure(ImGui.GetIO());
        host.Frames(window.Draw, 3);
        host.Press(window.Draw, ImGuiKey.F8);
        Assert.True(model.IsOpen(AiSetupPanel.Id));
        host.Press(window.Draw, ImGuiKey.Tab);
        var context = ImGui.GetCurrentContext();
        Assert.NotEqual(0u, context.NavId.Value);
        Assert.False(context.NavWindow.IsNull());
        Assert.Contains("panel.bed.ai-setup", Marshal.PtrToStringUTF8((IntPtr)context.NavWindow.Name)!);
        Assert.False(model.Session.Dirty);
    }

    // Entire fixture is self-authored; the adapter uses nested data/schemas to distinguish actual opened paths.
    private sealed class NestedAdapter(string data, string schemas) : IProjectAdapter
    {
        public string Name => "Acme";
        public DataProject Open(ProjectPaths _) => DataProject.Open(data, schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }

    [Fact] public void Launcher_panel_captures_actual_adapter_opened_folders_and_resolved_map()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-setup-synthetic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = Path.Combine(root, "data"); var schemas = Path.Combine(root, "schemas");
            var nestedData = Path.Combine(root, "nested", "data"); var nestedSchemas = Path.Combine(root, "nested", "schemas");
            foreach (var folder in new[] { data, schemas, nestedData, nestedSchemas }) Directory.CreateDirectory(folder);
            foreach (var folder in new[] { schemas, nestedSchemas })
                File.WriteAllText(Path.Combine(folder, "items.schema.json"), """{"type":"object"}""");
            File.WriteAllText(Path.Combine(nestedData, "items.json"), """{"value":"synthetic"}""");
            var map = Path.Combine(root, "reference_map.json");
            File.WriteAllText(map, """{"game":"Acme","labels":[],"references":[]}""");
            using var host = new HeadlessHost(1200, 800) { Drawing = false };
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "settings", "recent.json")))
                { Adapter = new NestedAdapter(nestedData, nestedSchemas) };
            Assert.True(launcher.Open(new(data, schemas, map)), launcher.Said);
            var window = Assert.IsType<EditorWindow<DataDocument>>(launcher.Workspace!.Window);
            var registered = Assert.Single(window.Editor.Panels, p => p.Id == AiSetupPanel.Id);
            var panel = Assert.IsType<AiSetupPanel>(registered.Draw.Target);
            using var json = JsonDocument.Parse(panel.Snippets!.DesktopJson);
            var args = json.RootElement.GetProperty("mcpServers").GetProperty("bed").GetProperty("args")
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(nestedData, args[1]); Assert.Equal(nestedSchemas, args[3]); Assert.Equal(map, args[5]);
            Assert.Equal("--adapter", args[6]);
            Assert.Equal(Path.GetFullPath(typeof(NestedAdapter).Assembly.Location), args[7]);
            Assert.Contains(window.PaletteEntries(""), e => e.Id == AiSetupPanel.ActionId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
