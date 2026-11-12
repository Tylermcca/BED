using Bed.Data;

namespace Bed.Editor.Gum;

/// <summary>A project check, shown in the Checks panel and offered to BED's agents, that runs Gum's own checker over
/// each Gum project in the folder, screen by screen, and says what it found in plain words with Gum's code and help
/// page. Reads only; never changes a file. MIXED: BED's existing checks with Gum's own checker (see
/// <see cref="GumDependencyCheck"/>).</summary>
public sealed class GumFilesCheck(string projectRoot) : ICheck
{
    /// <inheritdoc/>
    public CheckDefinition Definition { get; } = new("gum.files", "Gum screens",
        "Checks your Gum screens with Gum's own checker: every picture, font and part they use is in the project's folder, and nothing Gum would warn about.",
        TimeSpan.FromSeconds(30));

    /// <summary>True when <paramref name="projectRoot"/> holds a Gum project, so the check is worth offering.</summary>
    public static bool Applies(string projectRoot) => GumCatalog.FindProjects(projectRoot).Count > 0;

    /// <inheritdoc/>
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) => Task.Run(() =>
    {
        var findings = new List<CheckFinding>(); var complete = true; var screens = 0;
        var routes = GumTextRoutes.Detect(projectRoot);
        foreach (var file in GumCatalog.FindProjects(projectRoot))
        {
            cancel.ThrowIfCancellationRequested();
            progress.Report(new(null, $"Checking the Gum project {Path.GetFileNameWithoutExtension(file)}."));
            // One read of the project for both the screen list and the check.
            var (project, result) = GumDependencyCheck.ReadAndCheck(file, routes);
            findings.AddRange(project.Findings);
            if (result is null) { complete = false; continue; }   // Gum couldn't read the project: its finding says why
            findings.AddRange(result.Project.Findings); complete &= result.Project.Complete;
            foreach (var report in result.Screens.Values) { findings.AddRange(report.Findings); complete &= report.Complete; screens++; }
        }
        var problems = findings.Count(f => f.Problem.Severity != Severity.Info);
        var summary = problems == 0
            ? complete ? $"All {screens} Gum screens have everything they use." : $"BED found nothing wrong in {screens} Gum screens, but it couldn't check everything."
            : $"BED found {problems} things to look at in {screens} Gum screens.";
        return new CheckReport(complete, findings, summary);
    }, cancel);
}
