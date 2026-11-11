using Bed.Data;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// PREPARED ONLY. No filesystem, C# or native test has been run by this worker.
public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "project-template-" + Guid.NewGuid().ToString("N"));
    private string At(string name) => Path.Combine(root, name);
    private string Installed => At("installed-recipes");
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    private void InstallInventedExample()
    {
        foreach (var relative in ProjectTemplates.All.Single(t => t.Id == "recipes").Files)
        {
            var path = Path.Combine(Installed, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("Self-authored fixture: " + relative + "\r\n"));
        }
    }
    private Dictionary<string, byte[]> Snapshot(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(path, f), File.ReadAllBytes);
    private static void Same(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]);
    }

    [Fact] public void Manifest_has_only_the_two_approved_templates_and_safe_unique_paths()
    {
        Assert.Equal(new[] { "empty", "recipes" }, ProjectTemplates.All.Select(t => t.Id));
        foreach (var t in ProjectTemplates.All)
        {
            Assert.NotEmpty(t.Name); Assert.NotEmpty(t.Description); Assert.NotEmpty(t.Files);
            Assert.Equal(t.Files.Count, t.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(t.Files, f => { Assert.False(Path.IsPathRooted(f)); Assert.DoesNotContain("..", f); });
        }
    }
    [Fact] public void Preview_lists_exact_files_and_does_not_make_even_the_location()
    {
        var location = At("not-created"); var creator = new ProjectCreator(Installed);
        var preview = creator.Preview("empty", location, "Acme");
        Assert.True(preview.CanCreate, preview.Reason);
        Assert.Equal(Path.Combine(location, "Acme"), preview.Folder);
        Assert.Equal(ProjectTemplates.All[0].Files, preview.Files);
        Assert.False(Directory.Exists(root));
    }
    [Fact] public void Empty_project_opens_and_its_starter_file_passes_the_existing_schema_reader()
    {
        var result = new ProjectCreator(Installed).Create("empty", At("projects"), "Acme");
        Assert.Equal(ProjectCreationStatus.Created, result.Status); Assert.NotNull(result.Paths);
        var paths = ProjectPaths.In(result.Folder!); Assert.NotNull(paths);
        var data = DataProject.Open(paths.Data, paths.Schemas);
        var file = Assert.Single(data.Files); Assert.Equal("settings.json", file.Name);
        Assert.True(file.Open(), string.Join("; ", file.Problems));
        Assert.Contains("Start here", File.ReadAllText(Path.Combine(paths.Schemas, "settings.schema.json")));
        Assert.Empty(result.Leftovers);
    }
    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData(".")] [InlineData("..")]
    [InlineData("../Acme")] [InlineData("Acme/Other")] [InlineData("Acme\\Other")]
    [InlineData("CON")] [InlineData("nul.txt")] [InlineData("COM1.json")] [InlineData("LPT9")]
    [InlineData("Acme.")] [InlineData("Acme ")] [InlineData(" Acme")] [InlineData("A:B")]
    [InlineData("A?B")] [InlineData("A*B")] [InlineData("A|B")]
    public void Invalid_names_refuse_before_any_location_or_staging_write(string name)
    {
        var creator = new ProjectCreator(Installed);
        Assert.False(creator.Preview("empty", At("projects"), name).CanCreate);
        var result = creator.Create("empty", At("projects"), name);
        Assert.Equal(ProjectCreationStatus.Refused, result.Status); Assert.NotEmpty(result.Reason);
        Assert.False(Directory.Exists(root));
    }
    [Fact] public void Nonempty_destination_is_unchanged_and_no_staging_folder_is_left()
    {
        var destination = At("projects/Acme"); Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "Do not touch"); var before = Snapshot(root);
        var result = new ProjectCreator(Installed).Create("empty", At("projects"), "Acme");
        Assert.Equal(ProjectCreationStatus.DestinationExists, result.Status); Same(before, Snapshot(root));
        Assert.Equal(new[] { "Acme" }, Directory.GetDirectories(At("projects")).Select(Path.GetFileName));
    }
    [Fact] public void Even_an_empty_destination_refuses_with_the_approved_plain_reason()
    {
        Directory.CreateDirectory(At("projects/Acme"));
        var creator = new ProjectCreator(Installed);
        Assert.False(creator.Preview("empty", At("projects"), "Acme").CanCreate);
        var result = creator.Create("empty", At("projects"), "Acme");
        Assert.Equal(ProjectCreationStatus.DestinationExists, result.Status);
        Assert.Equal("A folder called Acme is already there. Choose another name.", result.Reason);
        Assert.Empty(Directory.GetFileSystemEntries(At("projects/Acme")));
        Assert.Single(Directory.GetDirectories(At("projects")));
    }
    [Fact] public void Overlong_name_and_relative_location_refuse_before_any_write()
    {
        var creator = new ProjectCreator(Installed);
        Assert.Equal(ProjectCreationStatus.Refused, creator.Create("empty", At("projects"), new string('a', 256)).Status);
        Assert.Equal(ProjectCreationStatus.Refused, creator.Create("empty", "relative", "Acme").Status);
        Assert.False(Directory.Exists(root));
    }
    [Fact] public void A_file_in_the_location_chain_refuses_without_changing_it()
    {
        Directory.CreateDirectory(root); File.WriteAllText(At("projects"), "keep"); var before = Snapshot(root);
        var result = new ProjectCreator(Installed).Create("empty", At("projects/child"), "Acme");
        Assert.Equal(ProjectCreationStatus.Refused, result.Status); Same(before, Snapshot(root));
    }
    [Fact] public void Unknown_template_and_incomplete_installed_example_refuse_before_writing()
    {
        var creator = new ProjectCreator(Installed);
        Assert.Equal(ProjectCreationStatus.Refused, creator.Create("other", At("projects"), "Acme").Status);
        Assert.Equal(ProjectCreationStatus.Refused, creator.Create("recipes", At("projects"), "Acme").Status);
        Assert.False(Directory.Exists(root));
    }
    [Fact] public void Example_copies_every_declared_byte_and_never_changes_its_installed_source()
    {
        InstallInventedExample(); var before = Snapshot(Installed);
        var result = new ProjectCreator(Installed).TryExampleAt(At("projects"));
        Assert.Equal(ProjectCreationStatus.Created, result.Status);
        Assert.Equal("Example", Path.GetFileName(result.Folder)); Same(before, Snapshot(result.Folder!));
        Same(before, Snapshot(Installed));
    }
    [Fact] public void Numbering_skips_existing_directories_and_files_without_overwriting_them()
    {
        InstallInventedExample(); var location = At("projects"); Directory.CreateDirectory(Path.Combine(location, "Example"));
        File.WriteAllText(Path.Combine(location, "Example 2"), "keep");
        var result = new ProjectCreator(Installed).TryExampleAt(location);
        Assert.Equal(ProjectCreationStatus.Created, result.Status); Assert.Equal("Example 3", Path.GetFileName(result.Folder));
        Assert.True(Directory.Exists(Path.Combine(location, "Example")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(location, "Example 2")));
    }
    [Fact] public void A_destination_created_at_publish_wins_and_the_operation_never_deletes_it()
    {
        InstallInventedExample(); var location = At("projects");
        var creator = new ProjectCreator(Installed, null, (stage, destination) =>
        {
            Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "keep.txt"), "winner");
            Directory.Move(stage, destination);
        });
        var result = creator.Create("empty", location, "Acme");
        Assert.Equal(ProjectCreationStatus.DestinationExists, result.Status);
        Assert.Equal("winner", File.ReadAllText(Path.Combine(location, "Acme/keep.txt")));
        Assert.Single(Directory.GetDirectories(location));
        Assert.Equal(new[] { location }, result.Leftovers);
        Assert.Contains("Left the folder " + location, result.Reason);
        Assert.DoesNotContain("Left the empty folder " + location, result.Reason);
    }
    [Fact] public void Numbered_example_retries_a_known_publish_collision_with_the_next_name()
    {
        InstallInventedExample(); var calls = 0;
        var creator = new ProjectCreator(Installed, null, (stage, destination) =>
        {
            if (calls++ == 0) { Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "keep"), "winner"); }
            Directory.Move(stage, destination);
        });
        var result = creator.TryExampleAt(At("projects"));
        Assert.Equal(ProjectCreationStatus.Created, result.Status); Assert.Equal("Example 2", Path.GetFileName(result.Folder));
        Assert.Equal("winner", File.ReadAllText(At("projects/Example/keep"))); Assert.Equal(2, calls);
    }
    [Fact] public void Partial_write_failure_cleans_staging_but_retains_and_reports_new_location_parents()
    {
        InstallInventedExample(); var before = Snapshot(Installed); var writes = 0;
        var creator = new ProjectCreator(Installed, (stream, bytes) =>
        {
            stream.Write(bytes.Span); if (++writes == 2) throw new IOException("Invented copy failure");
        }, null);
        var result = creator.Create("recipes", At("new/location"), "Acme");
        Assert.Equal(ProjectCreationStatus.Failed, result.Status); Assert.Contains("Invented copy failure", result.Reason);
        var location = Path.GetFullPath(At("new/location"));
        Assert.True(Directory.Exists(location)); Assert.Empty(Directory.GetFileSystemEntries(location));
        Assert.Equal(new[] { location, At("new") }, result.Leftovers);
        Assert.Contains("Left the empty folder " + location, result.Reason);
        Assert.Contains("Left the folder " + At("new"), result.Reason);
        Assert.DoesNotContain("Left the empty folder " + At("new") + ".", result.Reason);
        Same(before, Snapshot(Installed));
    }
    [Fact] public void Copy_failure_keeps_a_preexisting_empty_location_and_unrelated_sibling()
    {
        var location = At("projects"); Directory.CreateDirectory(location);
        File.WriteAllText(At("keep.txt"), "keep");
        var creator = new ProjectCreator(Installed, (_, _) => throw new IOException("stop"), null);
        var result = creator.Create("empty", location, "Acme");
        Assert.Equal(ProjectCreationStatus.Failed, result.Status); Assert.Empty(result.Leftovers);
        Assert.True(Directory.Exists(location)); Assert.Empty(Directory.GetFileSystemEntries(location));
        Assert.Equal("keep", File.ReadAllText(At("keep.txt")));
    }
    [Fact] public void An_outside_file_in_a_new_shared_location_survives_failure_and_is_not_called_empty()
    {
        InstallInventedExample(); var location = At("projects"); var writes = 0;
        var creator = new ProjectCreator(Installed, (stream, bytes) =>
        {
            stream.Write(bytes.Span);
            if (++writes == 2)
            {
                File.WriteAllText(Path.Combine(location, "keep.txt"), "outside writer");
                throw new IOException("stop");
            }
        }, null);
        var result = creator.Create("recipes", location, "Acme");
        Assert.Equal(ProjectCreationStatus.Failed, result.Status);
        Assert.Equal("outside writer", File.ReadAllText(Path.Combine(location, "keep.txt")));
        Assert.Equal(new[] { location }, result.Leftovers);
        Assert.Contains("Left the folder " + location, result.Reason);
        Assert.DoesNotContain("Left the empty folder " + location, result.Reason);
        Assert.Equal(new[] { "keep.txt" }, Directory.GetFileSystemEntries(location).Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(location, "Acme")));
    }
    [Fact] public void Unowned_file_in_failed_staging_is_retained_and_reported_instead_of_recursive_deletion()
    {
        InstallInventedExample(); string? foreign = null;
        var creator = new ProjectCreator(Installed, null, (stage, _) =>
        {
            foreign = Path.Combine(stage, "keep.txt"); File.WriteAllText(foreign, "outside writer"); throw new IOException("stop");
        });
        var result = creator.Create("empty", At("projects"), "Acme");
        Assert.Equal(ProjectCreationStatus.Failed, result.Status); Assert.NotEmpty(result.Leftovers);
        Assert.NotNull(foreign); Assert.Equal("outside writer", File.ReadAllText(foreign));
        Assert.False(Directory.Exists(At("projects/Acme"))); Assert.Contains("left", result.Reason);
    }
    [Fact] public void Installed_example_and_its_children_cannot_be_used_as_destinations()
    {
        InstallInventedExample(); var before = Snapshot(Installed);
        var result = new ProjectCreator(Installed).Create("empty", Installed, "Acme");
        Assert.Equal(ProjectCreationStatus.Refused, result.Status); Same(before, Snapshot(Installed));
    }
    [Fact] public void Default_location_uses_the_OS_documents_folder_without_creating_it()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.Equal(string.IsNullOrWhiteSpace(documents) ? null : Path.Combine(documents, "BED Projects"), ProjectCreator.DefaultLocation);
    }
}
