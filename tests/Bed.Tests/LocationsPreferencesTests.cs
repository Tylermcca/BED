using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

// Preferences > Locations: Gum, the usual folder for new projects and a folder of extra adapters, kept with the person's
// other preferences and used by BED.
[Collection(ImGuiCollection.Name)]
public sealed class LocationsPreferencesTests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public Rig(string root)
        {
            EditorWindow<DataDocument>.Configure(Im.GetIO());
            Launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")));
            Host.Frames(Launcher.Draw,4);
        }
        public void Frames(int n=3)=>Host.Frames(Launcher.Draw,n);
        public void Press(string label)
        {
            for(var i=0;i<120;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Label==label)){Host.Press(Launcher.Draw,ImGuiKey.Enter);Frames();return;}
                Host.Press(Launcher.Draw,ImGuiKey.Tab);
            }
            Assert.Fail("The keyboard did not reach "+label);
        }
        public void Dispose(){Launcher.Dispose();Host.Dispose();}
    }

    [Fact]
    public void Gum_can_be_found_from_Preferences_and_put_back_to_normal_and_is_remembered()
    {
        var root=Directory.CreateTempSubdirectory("locations-").FullName;var places=Locations.GumPlaces;
        try
        {
            var gum=Path.Combine(root,"Tools","Gum","Gum.exe");Directory.CreateDirectory(Path.GetDirectoryName(gum)!);File.WriteAllText(gum,"");
            Locations.GumPlaces=()=>[gum];
            var settings=Path.Combine(root,"local","settings.json");
            using(var r=new Rig(root))
            {
                r.Press("Preferences");r.Press("Locations");r.Press("Find it for me");
                Assert.Equal(gum,Locations.Gum);Assert.Equal(gum,JsonNode.Parse(File.ReadAllText(settings))!["gumProgram"]!.GetValue<string>());
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label==$"Found Gum at {gum}.");
            }
            using(var r=new Rig(root))
            {
                r.Frames();Assert.Equal(gum,Locations.Gum);
                r.Press("Preferences");r.Press("Locations");r.Press("Back to normal");
                Assert.Equal("",Locations.Gum);Assert.Equal("",JsonNode.Parse(File.ReadAllText(settings))!["gumProgram"]!.GetValue<string>());
                Locations.GumPlaces=()=>[];r.Press("Find it for me");
                Assert.Equal("",Locations.Gum);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("BED couldn't find Gum on this computer.",StringComparison.Ordinal));
            }
        }
        finally{Locations.GumPlaces=places;Locations.Gum="";Directory.Delete(root,true);}
    }

    [Fact]
    public void New_projects_go_in_the_chosen_folder_and_extra_adapters_are_read_before_BED_starts()
    {
        var root=Directory.CreateTempSubdirectory("locations-").FullName;
        try
        {
            var mine=Path.Combine(root,"My games");var extras=Path.Combine(root,"More adapters");
            var settings=Path.Combine(root,"local","settings.json");Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            Assert.Null(ProjectLauncher.AdaptersFolderIn(settings));
            File.WriteAllText(settings,new JsonObject{["projectsFolder"]=mine,["adaptersFolder"]=extras}.ToJsonString());
            Assert.Equal(extras,ProjectLauncher.AdaptersFolderIn(settings));
            using(var r=new Rig(root))
            {
                Assert.Equal(mine,r.Launcher.NewProjectsFolder);Assert.Equal(extras,Locations.Adapters);
                Locations.Projects="";r.Frames();
                Assert.Equal(ProjectCreator.DefaultLocation??"",r.Launcher.NewProjectsFolder);
                Assert.Equal("",JsonNode.Parse(File.ReadAllText(settings))!["projectsFolder"]!.GetValue<string>());
                Assert.Equal(extras,JsonNode.Parse(File.ReadAllText(settings))!["adaptersFolder"]!.GetValue<string>());
            }
            // A host that names the folder itself (a trial, a test) keeps it, whatever Preferences says.
            File.WriteAllText(settings,new JsonObject{["projectsFolder"]=mine}.ToJsonString());
            using var given=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),projectsFolder:root);
            Assert.Equal(root,given.NewProjectsFolder);
        }
        finally{Locations.Projects="";Locations.Adapters="";Directory.Delete(root,true);}
    }
}
