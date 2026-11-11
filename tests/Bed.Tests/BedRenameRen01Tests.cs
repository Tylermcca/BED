using System.Reflection;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

public sealed class BedRenameRen01Tests
{
    private static string Migrate(string root)=>(string)typeof(RecentProjects).Assembly.GetType("Bed.Editor.ImGui.PersonalSettingsMigration")!.GetMethod("Folder",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[root])!;
    [Fact]
    public void Readable_legacy_settings_are_copied_exactly_once_without_changing_either_existing_profile()
    {
        var root=Directory.CreateTempSubdirectory("bed-settings-migration-").FullName;
        try
        {
            var legacy=Directory.CreateDirectory(Path.Combine(root,"brutal-authoring")).FullName;Directory.CreateDirectory(Path.Combine(legacy,"nested"));byte[] recent=[0xef,0xbb,0xbf,13,10,0,255];File.WriteAllBytes(Path.Combine(legacy,"recent.json"),recent);File.WriteAllText(Path.Combine(legacy,"nested/shortcuts.json"),"{\"old\":true}");File.WriteAllText(Path.Combine(legacy,"settings.json"),"legacy settings");var target=Directory.CreateDirectory(Path.Combine(root,"BED")).FullName;Directory.CreateDirectory(Path.Combine(target,"nested"));File.WriteAllText(Path.Combine(target,"nested/keep.json"),"keep BED file");File.WriteAllText(Path.Combine(target,"settings.json"),"existing BED settings");
            Assert.Equal(target,Migrate(root));Assert.Equal(recent,File.ReadAllBytes(Path.Combine(target,"recent.json")));Assert.Equal("{\"old\":true}",File.ReadAllText(Path.Combine(target,"nested/shortcuts.json")));Assert.Equal("existing BED settings",File.ReadAllText(Path.Combine(target,"settings.json")));Assert.Equal(recent,File.ReadAllBytes(Path.Combine(legacy,"recent.json")));Assert.Equal("legacy settings",File.ReadAllText(Path.Combine(legacy,"settings.json")));
            File.WriteAllText(Path.Combine(legacy,"later.json"),"new legacy file");File.WriteAllText(Path.Combine(legacy,"recent.json"),"changed legacy file");Assert.Equal(target,Migrate(root));Assert.False(File.Exists(Path.Combine(target,"later.json")));Assert.Equal(recent,File.ReadAllBytes(Path.Combine(target,"recent.json")));Assert.Empty(Directory.GetFiles(target,"*.tmp",SearchOption.AllDirectories));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Locked_legacy_files_are_preserved_and_do_not_stop_readable_settings_or_reimport_on_restart()
    {
        var root=Directory.CreateTempSubdirectory("bed-settings-locked-").FullName;
        try
        {
            var legacy=Directory.CreateDirectory(Path.Combine(root,"brutal-authoring")).FullName;var locked=Path.Combine(legacy,"locked.json");File.WriteAllText(locked,"retained source");File.WriteAllText(Path.Combine(legacy,"readable.json"),"readable bytes");string target;using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None)){target=Migrate(root);Assert.False(File.Exists(Path.Combine(target,"locked.json")));Assert.Equal("readable bytes",File.ReadAllText(Path.Combine(target,"readable.json")));}Assert.Equal("retained source",File.ReadAllText(locked));Migrate(root);Assert.False(File.Exists(Path.Combine(target,"locked.json")));Assert.Empty(Directory.GetFiles(target,"*.tmp"));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Explicit_settings_and_information_commands_never_initialize_the_default_profile()
    {
        var method=typeof(Bed.Launcher.Program).GetMethod("InitialSettings",BindingFlags.Static|BindingFlags.NonPublic)!;var defaults=0;Func<string> folder=()=>{defaults++;return "default fixture folder";};
        foreach(var args in new[]{new[]{"--settings","owned scratch"},new[]{"--settings"},new[]{"--help"},new[]{"--version"},new[]{"--probe-play","fixture"}})method.Invoke(null,[args,folder]);Assert.Equal(0,defaults);Assert.Equal("owned scratch",method.Invoke(null,[new[]{"--settings","owned scratch"},folder]));Assert.Equal("default fixture folder",method.Invoke(null,[Array.Empty<string>(),folder]));Assert.Equal(1,defaults);
    }
    [Fact]
    public void Owned_namespaces_and_assembly_metadata_are_BED_while_saved_scene_bytes_are_unchanged()
    {
        var assemblies=new[]{typeof(Scene).Assembly,typeof(Bed.Data.DataProject).Assembly,typeof(Bed.Editor.DocumentHost).Assembly,typeof(RecentProjects).Assembly,typeof(Bed.Runtime.ProjectAssets).Assembly};
        foreach(var assembly in assemblies){Assert.StartsWith("Bed",assembly.GetName().Name);Assert.Equal("BED",assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);Assert.Equal("BED",assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);Assert.Equal("BED",assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);Assert.DoesNotContain(assembly.GetExportedTypes(),t=>t.Namespace?.StartsWith("Brutal.Authoring",StringComparison.Ordinal)==true);}
        Assert.Equal("Bed.Launcher",typeof(Bed.Launcher.Program).Assembly.GetName().Name);Assert.Equal("Bed.Agent.Cli",typeof(Bed.Agent.Cli.Program).Assembly.GetName().Name);
        const string old="{\"kind\":\"scene\",\"version\":1,\"objects\":[]}";var root=Directory.CreateTempSubdirectory("bed-old-scene-").FullName;
        try{var path=Path.Combine(root,"old.json");File.WriteAllText(path,old);var session=DocumentSession<Scene>.Open(path,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());session.Save();Assert.Equal(old,File.ReadAllText(path));}
        finally{Directory.Delete(root,true);}
    }
}
