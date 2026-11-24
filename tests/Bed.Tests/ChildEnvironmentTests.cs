using System.Collections.Concurrent;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class ChildEnvironmentTests
{
    // Under the dotnet test runner this process carries the dotnet command's MSBuild variables (MSBuildSDKsPath and friends).
    // A game build BED starts must not inherit them, or a child dotnet that picks another SDK mixes two SDKs' parts.
    [Fact]
    public async Task Programs_BED_starts_get_its_environment_without_the_dotnet_commands_MSBuild_leftovers()
    {
        var lines = new ConcurrentQueue<string>();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var child = new SupervisedProcess(new(cmd, Path.GetTempPath(), ["/d", "/q"]), 64, 65536, (text, _) => lines.Enqueue(text), _ => { },
            new Dictionary<string, string> { ["BED_CHILD_ENVIRONMENT_CHECK"] = "1" }, plainOutput: true);
        await child.Send("set"); await child.Send("exit 0"); child.CloseInput();
        var code = await child.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(code == 0, string.Join(Environment.NewLine, lines));
        var names = lines.Select(line => line.Split('=', 2)[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PATH", names);
        Assert.Contains("BED_CHILD_ENVIRONMENT_CHECK", names);
        foreach (var leftover in ChildEnvironment.LeftByDotnet) Assert.DoesNotContain(leftover, names);
    }

    [Fact]
    public void Start_requests_lose_the_same_leftovers_and_keep_everything_else()
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet");
        start.Environment["MSBuildSDKsPath"] = "elsewhere"; start.Environment["DOTNET_HOST_PATH"] = "elsewhere"; start.Environment["BED_KEEP"] = "1";
        ChildEnvironment.Clean(start);
        Assert.False(start.Environment.ContainsKey("MSBuildSDKsPath"));
        Assert.False(start.Environment.ContainsKey("DOTNET_HOST_PATH"));
        Assert.Equal("1", start.Environment["BED_KEEP"]);
        Assert.True(start.Environment.ContainsKey("PATH"));
    }
}
