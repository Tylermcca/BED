using Bed.Data;
using Bed.Editor.Gum;
using Xunit;

namespace Bed.Tests;

public class GumFilesCheckTests
{
    [Fact]
    public async Task Check_panel_entry_reports_missing_files_in_plain_words_and_only_appears_for_projects_with_gum()
    {
        var deps = GumFixtures.Folder("Deps");
        Assert.True(GumFilesCheck.Applies(deps));
        Assert.False(GumFilesCheck.Applies(Directory.CreateTempSubdirectory("no gum ").FullName));
        var report = await new GumFilesCheck(deps).RunAsync(null!, new Progress<CheckProgress>(), CancellationToken.None);
        Assert.NotEmpty(report.Findings);
        Assert.Contains(report.Findings, f => f.Problem.Message.Contains("isn't in the project's folder"));
        Assert.Contains(report.Findings, f => f.Code == "GUM0018" && f.Help is not null);
        Assert.Contains("Gum screens", report.Summary);
    }

    [Fact]
    public async Task A_copied_screen_project_passes_with_the_unknown_text_route_note_retained()
    {
        var root=Directory.CreateTempSubdirectory("gum-copied-sample-").FullName;
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
        var from=Path.Combine(repo!.FullName,"samples","CheckersProject","ui");
        foreach(var file in Directory.GetFiles(from,"*",SearchOption.AllDirectories))
        {var to=Path.Combine(root,"ui",Path.GetRelativePath(from,file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);}
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var project=DataProject.Open(data,schemas);
            var run=new Checks().Add(new GumFilesCheck(root)).Start("gum.files",project,out var why);
            Assert.NotNull(run);await run!.Ended.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(CheckState.Completed,run.State);Assert.True(run.Verdict(project)=="clean",string.Join("; ",run.Report!.Findings.Select(f=>f.Problem.Message)));Assert.True(run.Clean(project));
            Assert.True(run.Report!.Complete);
            Assert.Contains(run.Report.Findings,f=>f.Problem.Severity==Severity.Info&&f.Problem.Message.Contains("can't tell how your game draws"));
            Assert.All(run.Report.Findings,f=>Assert.Equal(Severity.Info,f.Problem.Severity));
            Assert.Contains("All 2 Gum screens have everything they use.",run.Report.Summary);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task A_BRUTAL_game_gets_the_skia_font_findings_from_its_own_package_list()
    {
        var copy = GumFixtures.Copy("Files");
        try
        {
            File.WriteAllText(Path.Combine(copy, "Game.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Gum.Brutal.OGL\" Version=\"1.0.0\" /></ItemGroup></Project>");
            var report = await new GumFilesCheck(copy).RunAsync(null!, new Progress<CheckProgress>(), CancellationToken.None);
            Assert.Contains(report.Findings, f => f.Problem.Message.Contains("Skia can't draw a .fnt font"));
            Assert.DoesNotContain(report.Findings, f => f.Problem.Message.Contains("can't tell how your game draws"));
            Assert.DoesNotContain(report.Findings, f => f.Problem.Message.Contains("letters file"));
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void Agents_can_run_the_Gum_check_and_get_Gums_codes_and_help_pages()
    {
        var root = Directory.CreateTempSubdirectory("bed-gum-agent-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "schemas"));
            var session = new Bed.Agent.AgentSession(DataProject.Open(Path.Combine(root, "data"), Path.Combine(root, "schemas")));
            Assert.False(session.UseGum(root));   // no Gum project yet
            var copy = GumFixtures.Copy("Deps");
            Directory.Move(copy, Path.Combine(root, "ui"));
            Assert.True(session.UseGum(root));
            Assert.True(session.UseGum(root));   // asking twice adds it once
            var result = session.RunCheck("gum.files");
            var findings = result["findings"]!.AsArray();
            Assert.Contains(findings, f => f!["code"]?.GetValue<string>() == "GUM0018"
                && f["help"]!.GetValue<string>().StartsWith("https://docs.flatredball.com/gum/", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Gum.Brutal.Vulkan", GumTextRoute.Skia)]
    [InlineData("Gum.SkiaSharp", GumTextRoute.Skia)]
    [InlineData("Gum.MonoGame", GumTextRoute.FontCache)]
    [InlineData("Gum.raylib", GumTextRoute.FontCache)]
    [InlineData("Gum.sokol", GumTextRoute.FontCache)]
    [InlineData("Gum.MonoGame|KernSmith.MonoGameGum", GumTextRoute.MadeWhileRunning)]
    public void The_text_route_comes_from_the_games_Gum_packages(string packages, GumTextRoute expected) =>
        Assert.Equal([expected], GumTextRoutes.FromPackages(packages.Split('|')));

    [Fact]
    public void No_known_Gum_package_means_the_route_cant_be_told()
    {
        Assert.Empty(GumTextRoutes.FromPackages(["Gum.Stride", "SharpGLTF.Core"]));
        Assert.Empty(GumTextRoutes.Detect(GumFixtures.Folder("Deps")));
    }

    [Theory]
    [InlineData("not a project at all")]
    [InlineData("")]
    public async Task A_project_file_Gum_cant_read_is_reported_not_thrown(string text)
    {
        var folder = Directory.CreateTempSubdirectory("bed-gum-bad-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Bad.gumx"), text);
            var report = await new GumFilesCheck(folder).RunAsync(null!, new Progress<CheckProgress>(), CancellationToken.None);
            Assert.False(report.Complete);
            Assert.NotEmpty(report.Findings);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task A_readable_project_with_no_screens_is_still_checked()
    {
        var copy = GumFixtures.Copy("Files");
        try
        {
            var file = Path.Combine(copy, "Files.gumj");
            var text = File.ReadAllText(file);
            var start = text.IndexOf("\"ScreenReferences\"", StringComparison.Ordinal);
            var end = text.IndexOf("\"ComponentReferences\"", StringComparison.Ordinal);
            File.WriteAllText(file, text[..start] + "\"ScreenReferences\": [], " + text[end..]);
            var report = await new GumFilesCheck(copy).RunAsync(null!, new Progress<CheckProgress>(), CancellationToken.None);
            Assert.True(report.Complete);
            Assert.Contains(report.Findings, f => f.Code == "GUM0014");
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void A_central_package_version_alone_is_not_a_Gum_package_the_game_uses()
    {
        var folder = Directory.CreateTempSubdirectory("bed-gum-route-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageVersion Include=\"Gum.MonoGame\" Version=\"1.0.0\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(folder, "Game.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Update=\"Gum.Brutal.OGL\" /></ItemGroup></Project>");
            Assert.Equal([GumTextRoute.Skia], GumTextRoutes.Detect(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
