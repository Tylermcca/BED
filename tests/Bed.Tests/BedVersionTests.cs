using System.Xml.Linq;
using Bed.Editor;
using Bed.Editor.ImGui;
using Toy;
using Xunit;
using BedProgram = Bed.Launcher.Program;
using CliProgram = Bed.Agent.Cli.Program;

namespace Bed.Tests;

// Prepared source/public-entry contracts; no actual executable launch or release publication.
[Collection(ImGuiCollection.Name)]
public sealed class BedVersionTests
{
    [Fact]
    public void Version_matches_the_single_shared_build_property()
    {
        var root = SourceRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var version = Assert.Single(props.Descendants("Version")).Value;
        Assert.NotEmpty(version);
        Assert.Equal(version, BuildVersion.Current);
        Assert.Equal("BED " + version, BedAbout.Heading);
    }

    [Fact]
    public void Bed_version_returns_without_opening_settings_or_a_project()
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(0, BedProgram.Run(["--version"], output, errors, "not-a-settings-folder\0"));
        Assert.Equal(BedAbout.Heading + Environment.NewLine, output.ToString());
        Assert.Equal("", errors.ToString());
    }

    [Fact]
    public void Cli_version_needs_no_data_schema_input_or_host()
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(0, CliProgram.Run(["--version"], TextReader.Null, output, errors));
        Assert.Equal(BedAbout.Heading + Environment.NewLine, output.ToString());
        Assert.Equal("", errors.ToString());
    }

    [Fact]
    public void Mixed_version_arguments_refuse_before_gui_or_mcp_startup()
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(2, BedProgram.Run(["--version", "--shot", "never.png"], output, errors, "invalid\0"));
        Assert.Equal("", output.ToString());
        Assert.Contains("must be used alone", errors.ToString());
        output.GetStringBuilder().Clear(); errors.GetStringBuilder().Clear();
        Assert.Equal(2, CliProgram.Run(["--version", "mcp"], TextReader.Null, output, errors));
        Assert.Equal("", output.ToString());
        Assert.Contains("must be used alone", errors.ToString());
    }

    [Fact]
    public void About_reuses_exact_credit_and_names_the_licences_folder()
    {
        Assert.Contains(BedAbout.Heading, BedAbout.Text);
        Assert.Contains("Copyright (c) 2026 Tyler McCauley. MIT licence.", BedAbout.Text);
        Assert.Contains(BrutalCredit.Copyright, BedAbout.Text);
        Assert.Contains("LICENSE.txt", BedAbout.Text);
        Assert.Contains("NOTICE.txt", BedAbout.Text);
        Assert.Contains(Path.Combine(AppContext.BaseDirectory, "licenses"), BedAbout.Text);
    }

    [Fact]
    public void About_is_a_single_existing_palette_panel_action()
    {
        var model = new EditorModel<Board>(BoardKit.Session(new Board([])),
            new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        var window = new EditorWindow<Board>(model);
        var entry = Assert.Single(window.PaletteEntries("About BED"));
        Assert.Equal("editor.panel.bed.about", entry.Id);
        Assert.Null(entry.Unavailable);
        Assert.False(model.IsOpen(BedAbout.Panel.Id));
        Assert.True(window.Run(entry.Id));
        Assert.True(model.IsOpen(BedAbout.Panel.Id));
        _ = new EditorWindow<Board>(model);
        Assert.Single(model.Panels, p => p.Id == BedAbout.Panel.Id);
    }

    private static string SourceRoot()
    {
        for (var p = new DirectoryInfo(AppContext.BaseDirectory); p is not null; p = p.Parent)
            if (File.Exists(Path.Combine(p.FullName, "Directory.Build.props")) && File.Exists(Path.Combine(p.FullName, "Bed.slnx"))) return p.FullName;
        throw new InvalidOperationException("Run this source-contract test in the repository build tree.");
    }
}
