using System.Text.Json;
using Brutal.Authoring.Agent.Cli;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiSnapshotScriptTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"click\":\"A\",\"key\":\"S\"}]")]
    [InlineData("[{\"launch\":\"A\"}]")]
    [InlineData("[{\"select\":-1}]")]
    [InlineData("[{\"key\":\"Ctrl+Ctrl+S\"}]")]
    [InlineData("[{\"key\":\"123\"}]")]
    public void Invalid_complete_scripts_refuse_before_host_or_output(string script)
    {
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(2, Program.Run(["ui", "--script", script, "--json"], new StringReader(""), output, errors));
        Assert.Empty(output.ToString()); Assert.Contains("Invalid ui script", errors.ToString());
    }

    [Fact]
    public void Cap_is_exactly32_and_empty_script_keeps_snapshot_shape()
    {
        UiScript.Validate("[" + string.Join(',', Enumerable.Repeat("{\"key\":\"Escape\"}", 32)) + "]");
        Assert.Throws<ArgumentException>(() => UiScript.Validate("[" + string.Join(',', Enumerable.Repeat("{\"key\":\"Escape\"}", 33)) + "]"));
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(0, Program.Run(["ui", "--script", "[]", "--json"], new StringReader(""), output, errors));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("frame").GetInt64() > 0);
    }

    [Fact]
    public void Cli_prints_initial_and_each_step_as_separate_json_lines()
    {
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(0, Program.Run(["ui", "--script", "[{\"key\":\"Escape\"},{\"key\":\"Tab\"}]", "--json"], new StringReader(""), output, errors));
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        long previous = 0;
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            var frame = json.RootElement.GetProperty("frame").GetInt64();
            Assert.True(frame > previous); previous = frame;
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("nodes").ValueKind);
        }
    }

    [Fact]
    public void Click_type_key_select_use_actual_local_input_and_emit_each_completed_step()
    {
        using var host = new HeadlessHost { Drawing = false };
        var input = new ImInputString(100);
        var clicks = 0; var selected = -1; var snapshots = new List<UiSnapshot>();
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(600, 500));
            Ui.Begin("Script fixture"u8);
            if (Ui.Button("Press"u8)) clicks++;
            Ui.InputText("Name"u8, input);
            for (var i = 0; i < 3; i++) if (Ui.Selectable($"Row {i}", selected == i)) selected = i;
            Ui.End();
        }
        host.Frames(Draw, 4);
        UiScript.Run("""[{"click":"Press"},{"click":"Name"},{"type":"Invented"},{"key":"Tab"},{"select":1}]""", host, Draw, snapshots.Add);
        Assert.Equal(1, clicks); Assert.Equal(1, selected); Assert.Equal(5, snapshots.Count);
        Assert.Contains(snapshots[2].Nodes, n => n.Role == UiRole.TextField && n.Value == "Invented");
        Assert.True(snapshots.Zip(snapshots.Skip(1)).All(p => p.First.Frame < p.Second.Frame));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Disabled")]
    [InlineData("Duplicate")]
    public void Missing_disabled_or_ambiguous_click_refuses_without_clicking(string label)
    {
        using var host = new HeadlessHost { Drawing = false };
        var clicks = 0;
        void Draw()
        {
            Ui.Begin("Refusal fixture"u8);
            Ui.BeginDisabled(true); if (Ui.Button("Disabled"u8)) clicks++; Ui.EndDisabled();
            if (Ui.Button("Duplicate##one"u8)) clicks++;
            if (Ui.Button("Duplicate##two"u8)) clicks++;
            Ui.End();
        }
        host.Frames(Draw, 4);
        Assert.Throws<ArgumentException>(() => UiScript.Run(JsonSerializer.Serialize(new[] { new { click = label } }), host, Draw, _ => Assert.Fail("No step should complete")));
        Assert.Equal(0, clicks);
        Assert.Throws<ArgumentException>(() => UiScript.Run("[{\"type\":\"not focused\"}]", host, Draw, _ => Assert.Fail()));
    }
}

