namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    private ProjectPaths? pendingRecentOpen;
    private string recentMenuNote="";
    private void DrawRecentMenu()
    {
        var missing=Recent.Items.Where(p=>p.Folder is {} folder?!Directory.Exists(folder):!Directory.Exists(p.Data)||!Directory.Exists(p.Schemas)).ToArray();
        if(missing.Length>0)
        {
            foreach(var project in missing)Recent.Remove(project);
            recentMenuNote="Removed missing projects from Recent projects: "+string.Join("; ",missing.Select(p=>p.Folder??p.Data))+". No project files were deleted.";
        }
        if(recentMenuNote.Length>0)Ui.TextWrapped(recentMenuNote,semanticId:"recent.projects.note");
        var projects=Recent.Items.Take(8).ToArray();
        if(projects.Length==0)Ui.TextWrapped("No recent projects."u8,semanticId:"recent.projects.empty");
        for(var i=0;i<projects.Length;i++)
        {
            var project=projects[i];var folder=project.Root??project.Folder??project.Data;
            if(Ui.MenuItem($"{i+1}. {Name(project)} — {folder}"))pendingRecentOpen=project;
        }
    }
}
