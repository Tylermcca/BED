namespace Bed.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    private ProjectPaths? pendingRecentOpen;
    private string recentMenuNote="";
    // A remembered root may recover cached data paths through its original provider.
    private static string? RecentOpenRefusal(ProjectPaths project) => project.Folder is { } root
        ? Directory.Exists(root) ? null : "That project folder was not found."
        : !Directory.Exists(project.Data) ? "The project's data folder was not found."
        : !Directory.Exists(project.Schemas) ? "The project's schema folder was not found." : null;

    private void DrawRecentMenu()
    {
        if(recentMenuNote.Length>0)Ui.TextWrapped(recentMenuNote,semanticId:"recent.projects.note");
        var projects=Recent.Items.Take(8).ToArray();
        if(projects.Length==0)Ui.TextWrapped("No recent projects."u8,semanticId:"recent.projects.empty");
        for(var i=0;i<projects.Length;i++)
        {
            var project=projects[i];var folder=project.Root??project.Folder??project.Data;
            var refusal=RecentOpenRefusal(project);
            if(Ui.MenuItem($"{i+1}. {Name(project)}{(Recent.IsPinned(project)?" [Pinned]":"")}{(refusal is null?"":" (missing)")} - {folder}",""u8,false,refusal is null,refusal))pendingRecentOpen=project;
            if(refusal is not null)
            {
                Brutal.ImGuiApi.ImGui.PushID(System.Text.Json.JsonSerializer.Serialize(project));
                if(Ui.MenuItem($"Remove {Name(project)} from list"))
                {Recent.Remove(project);recentMenuNote="Removed from Recent projects. No project files were deleted.";}
                Brutal.ImGuiApi.ImGui.PopID();
            }
        }
    }
}
