using System.Diagnostics;
using Bed.Agent.Cli;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AiShot02Tests
{
    [OpenGlFact]
    public void Island_returns_real_3D_frames_from_one_owned_process_and_keeps_live_physics_between_requests()
    {
        using var copy = new SpriteSampleTests.Copy(true); copy.OpenAndCompile();
        var source = Path.Combine(copy.Root, "data", "level.json"); var saved = File.ReadAllBytes(source);
        var files = Directory.GetFiles(copy.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var launch = new PlayLaunch(new(Path.Combine(copy.Root, "game", "PhysicsStarter.exe"), copy.Root, ["--project", "."]), null, copy.Root, []);
        using var observation = new AgentPlayObservation();
        var hello = observation.Start(launch, new(copy.Settings), false);
        var pid = hello["processId"]!.GetValue<int>();
        using var process = Process.GetProcessById(pid);
        Assert.Equal(0, process.MainWindowHandle); Assert.Contains("0.01 seconds each", hello["description"]!.GetValue<string>());
        var first = observation.Request("shot"); Assert.Equal(0, first["frame"]!.GetValue<long>());
        Assert.True(AgentPlayObservation.ValidatePng(first["imageData"]!.GetValue<string>()).Length > 10000);
        var moved = observation.Request("hold", "right", .5);
        Assert.Equal(pid, moved["processId"]!.GetValue<int>()); Assert.Equal(30, moved["frame"]!.GetValue<long>());
        Assert.Equal(30, moved["advancedFrames"]!.GetValue<long>()); Assert.Equal(.5, moved["simulatedSeconds"]!.GetValue<double>());
        Assert.Contains("physics updates: 50,", moved["description"]!.GetValue<string>());
        Assert.NotEqual(first["sha256"]!.GetValue<string>(), moved["sha256"]!.GetValue<string>());
        if (Environment.GetEnvironmentVariable("BED_AI_SHOT02_CAPTURE") == "1")
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Bed.slnx"))) repo = repo.Parent;
            var folder = Path.Combine(repo!.FullName, ".validation", "ai-shot02"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "island.png"), AgentPlayObservation.ValidatePng(moved["imageData"]!.GetValue<string>()));
        }
        var repeat = observation.Request("shot"); Assert.Equal(0, repeat["advancedFrames"]!.GetValue<long>());
        Assert.Equal(moved["sha256"]!.GetValue<string>(), repeat["sha256"]!.GetValue<string>());
        var jumped = observation.Request("hold", "jump", .25);
        Assert.Null(jumped["error"]); Assert.Equal(45, jumped["frame"]!.GetValue<long>());
        Assert.Contains("jump lifts the first saved crate", jumped["description"]!.GetValue<string>());
        Assert.NotEqual(moved["sha256"]!.GetValue<string>(), jumped["sha256"]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => observation.Request("hold", "fly", .5));
        var still = observation.Request("shot");
        Assert.Equal(45, still["frame"]!.GetValue<long>());
        Assert.Equal(jumped["sha256"]!.GetValue<string>(), still["sha256"]!.GetValue<string>());
        var back = observation.Request("hold", "left", .5); Assert.Equal(75, back["frame"]!.GetValue<long>());
        var reset = observation.Request("hold", "restart", 1.0 / 60);
        Assert.Equal(76, reset["frame"]!.GetValue<long>()); Assert.Contains("physics updates: 1,", reset["description"]!.GetValue<string>());
        Assert.Equal(reset["sha256"]!.GetValue<string>(), observation.Request("shot")["sha256"]!.GetValue<string>());
        process.Refresh(); Assert.Equal(0, process.MainWindowHandle);
        Assert.Equal(saved, File.ReadAllBytes(source)); Assert.Equal(files, Directory.GetFiles(copy.Root, "*", SearchOption.AllDirectories).Order());
        Assert.Contains("Stopped", observation.Request("stop")["status"]!.GetValue<string>());
        Assert.True(process.WaitForExit(5000)); Assert.Throws<InvalidOperationException>(() => observation.Request("shot"));
    }

    [OpenGlTheory]
    [InlineData("oversize")] [InlineData("sequence")] [InlineData("extra-field")]
    public async Task Island_refuses_malformed_private_requests_and_ends_only_its_owned_child(string mode)
    {
        using var copy = new SpriteSampleTests.Copy(true); copy.OpenAndCompile();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var child = new SupervisedProcess(new(Path.Combine(copy.Root, "game", "PhysicsStarter.exe"), copy.Root, []), 1024, 1500000,
            (_, error) => { if (!error) ready.TrySetResult(); }, _ => { },
            new Dictionary<string, string> { ["BED_AI_OBSERVE"] = "1", ["BED_PROJECT"] = copy.Root, ["BED_LIVE_TARGET"] = "" });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var request = mode switch
        {
            "oversize" => new string('x', 513),
            "sequence" => "{\"id\":2,\"op\":\"shot\",\"key\":null,\"seconds\":0}",
            _ => "{\"id\":1,\"op\":\"shot\",\"key\":null,\"seconds\":0,\"extra\":true}"
        };
        await child.Send(request); Assert.Equal(3, await child.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}