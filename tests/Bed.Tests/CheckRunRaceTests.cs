using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>A check that finishes at once can't trip over its own clean-up (found by a full run: ObjectDisposedException in Start).</summary>
public sealed class CheckRunRaceTests
{
    private sealed class Instant(string id) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, id, id, TimeSpan.FromSeconds(30));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) =>
            Task.FromResult(new CheckReport(true, [], "done"));
    }

    [Fact]
    public async Task A_check_that_finishes_at_once_can_be_started_again_and_again()
    {
        var root = Directory.CreateTempSubdirectory("check-race-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "schemas"));
            var project = DataProject.Open(Path.Combine(root, "data"), Path.Combine(root, "schemas"));
            var checks = new Checks().Add(new Instant("instant"));
            for (var i = 0; i < 2000; i++)
            {
                var run = checks.Start("instant", project, out var why);
                Assert.True(run is not null, why);
                await run!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(CheckState.Completed, run.State);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
