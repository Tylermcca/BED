using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor.ImGui;

namespace Bed.Launcher;

// The normal launcher, workspace Saved capture, Export dialog and app-owned verification/copy path; no window.
internal static class ExportProbe
{
    internal static int Run(string folder,string destination,TimeSpan timeout,TextWriter output,TextWriter errors)
    {
        try
        {
            if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromMinutes(5))throw new ArgumentException("Use a timeout between 1 and 300 seconds.");
            var target=Path.GetFullPath(destination);
            if(File.Exists(target)||Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException("Choose a new or empty destination folder; existing files will not be replaced.");
            var watch=Stopwatch.StartNew();
            var local=Directory.CreateTempSubdirectory("bed-export-probe-").FullName;
            try
            {
                using var host=new HeadlessHost(1200,900){Drawing=false};
                EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
                using var launcher=Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(local,"recent.json"))));
                var (loaded,problems)=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));
                if(problems.Count>0)throw new InvalidOperationException(string.Join(Environment.NewLine,problems));
                launcher.Loaded=loaded;
                if(!launcher.OpenFolder(folder))throw new InvalidOperationException(launcher.Said);
                var workspace=launcher.Workspace??throw new InvalidOperationException("The project did not open.");
                var scenes=workspace.Project.Files.Where(f=>workspace.Documents.IsScene(f.Name)).ToArray();
                if(scenes.Length!=1)throw new InvalidOperationException("The export check needs exactly one level file the project can open.");
                var name=scenes[0].Name;
                if(!workspace.Show(name))throw new InvalidOperationException(workspace.Said);
                void Frame(int count=1)
                {
                    for(var i=0;i<count;i++)
                    {
                        if(watch.Elapsed>=timeout)throw new TimeoutException("Export probe timed out.");
                        host.Frame(workspace.Draw);
                    }
                }
                UiNode Control(UiRole role,string label)
                {
                    var snapshot=Ui.Snapshot();
                    bool Inside(UiNode node)
                    {
                        for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                            if(snapshot.Nodes[parent].Role==UiRole.Window&&snapshot.Nodes[parent].Label=="Export build")return true;
                        return false;
                    }
                    var matches=snapshot.Nodes.Where(n=>n.Role==role&&n.Label==label&&Inside(n)).ToArray();
                    return matches.Length==1?matches[0]:throw new InvalidOperationException("Export control is unavailable or ambiguous: "+label);
                }
                string Status()=>Ui.Snapshot().Nodes.Single(n=>n.Role==UiRole.Status&&n.Label.StartsWith("Export status: ",StringComparison.Ordinal)).Label;
                void Act(UiRole role,string label,UiActions action,string? value=null)
                {
                    var node=Control(role,label);
                    if(!node.Enabled)throw new InvalidOperationException(node.DisabledReason??"Export control is unavailable: "+label);
                    var request=Ui.RequestAction(node.Id,action,value);Frame(2);
                    if(request.Outcome!=UiActionOutcome.Applied)throw new InvalidOperationException("Export control did not accept the action: "+label);
                }
                Frame(3);
                var open=workspace.SceneWindow?.ExportBuildAction??workspace.Scene2Window?.ExportBuildAction??throw new InvalidOperationException("This project does not provide Export build.");
                open();Frame(3);Act(UiRole.Button,"Build saved files",UiActions.Invoke);
                while(!Control(UiRole.Button,"Copy build to folder...").Enabled)
                {
                    if(!Control(UiRole.Button,"Cancel export").Enabled)throw new InvalidOperationException(Status());
                    Frame();Thread.Sleep(5);
                }
                Act(UiRole.TextField,"Destination folder",UiActions.SetValue,target);
                Act(UiRole.Button,"Copy build to folder...",UiActions.Invoke);
                while(Status()=="Export status: Copying verified build..."){Frame();Thread.Sleep(1);}
                if(Status()!="Export status: Build copied to "+target)throw new InvalidOperationException(Status());
                var files=Directory.EnumerateFiles(target,"*",SearchOption.AllDirectories).Take(1025).Order(StringComparer.Ordinal).ToArray();
                if(files.Length is 0 or >1024)throw new IOException("Copied export inventory is empty or exceeds its bound.");
                long total=0;
                var proof=files.Select(path=>
                {
                    var info=new FileInfo(path);
                    if(info.Length>64L*1024*1024||(total+=info.Length)>320L*1024*1024)throw new IOException("Copied export exceeds its byte bound.");
                    return new{path=Path.GetRelativePath(target,path).Replace('\\','/'),length=info.Length,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))};
                }).ToArray();
                output.WriteLine(JsonSerializer.Serialize(new{status="passed",verified=true,copied=true,document=name,destination=target,fileCount=proof.Length,totalBytes=total,files=proof}));
                return 0;
            }
            finally{Directory.Delete(local,true);}
        }
        catch(Exception error){errors.WriteLine("Export probe failed: "+error.Message);return 1;}
    }
}
