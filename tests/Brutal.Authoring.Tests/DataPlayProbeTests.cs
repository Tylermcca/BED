using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Launcher;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DataPlayProbeTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("data-play-probe-").FullName;
        public string Project=>Path.Combine(Root,"data project");
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Project,"data"));
            Directory.CreateDirectory(Path.Combine(Project,"schemas"));
            File.WriteAllText(Path.Combine(Project,"data","values.json"),"""{"value":42}""");
            File.WriteAllText(Path.Combine(Project,"schemas","values.schema.json"),"""{"type":"object","properties":{"value":{"type":"integer"}},"required":["value"]}""");
            var game=Directory.CreateDirectory(Path.Combine(Project,"game")).FullName;
            foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(game,"ProjectPlayTestGame"+suffix));
            PlaySettingsStore.Save(Project,new("game/ProjectPlayTestGame.exe",[],"."));
        }
        // Normal startup may prepare its derived asset cache; authored files and settings must stay exact.
        public Dictionary<string,string> Hashes()=>Directory.EnumerateFiles(Project,"*",SearchOption.AllDirectories)
            .Where(path=>!Path.GetRelativePath(Project,path).StartsWith(".bed"+Path.DirectorySeparatorChar+"cache"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path=>Path.GetRelativePath(Project,path),path=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        public void Dispose()=>Directory.Delete(Root,true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Data_project_uses_real_saved_files_Play_and_preserves_every_input(bool unreadableFirst)
    {
        using var f=new Fixture();
        if(unreadableFirst)
        {
            File.WriteAllText(Path.Combine(f.Project,"data","0-broken.json"),"not json");
            File.WriteAllText(Path.Combine(f.Project,"schemas","0-broken.schema.json"),"{}");
        }
        var before=f.Hashes();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(0,Program.Run(["--probe-play",f.Project,"--timeout","10"],output,errors,"unused-probe-settings"));
        Assert.Equal("",errors.ToString());
        using var result=JsonDocument.Parse(output.ToString());var json=result.RootElement;
        Assert.Equal("passed",json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("completed").GetBoolean());Assert.Equal(0,json.GetProperty("exitCode").GetInt32());
        Assert.Equal("values.json",json.GetProperty("document").GetString());
        var lines=json.GetProperty("output").EnumerateArray().ToArray();
        Assert.Contains(lines,line=>!line.GetProperty("error").GetBoolean()&&line.GetProperty("text").GetString()=="plain stdout");
        Assert.Contains(lines,line=>line.GetProperty("error").GetBoolean()&&line.GetProperty("text").GetString()=="plain stderr");
        Assert.Contains(lines,line=>line.GetProperty("text").GetString()=="BED_PROJECT="+f.Project);
        Assert.Equal(before.OrderBy(p=>p.Key),f.Hashes().OrderBy(p=>p.Key));
    }

    [Fact]
    public void Data_project_probe_arguments_preserve_failure_exit_and_saved_settings()
    {
        using var f=new Fixture();var before=f.Hashes();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",f.Project,"--probe-args","crash","--timeout","10"],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("code 7",errors.ToString());
        Assert.Equal(before.OrderBy(p=>p.Key),f.Hashes().OrderBy(p=>p.Key));
    }

    [Fact]
    public void Data_project_does_not_bypass_external_program_approval()
    {
        using var f=new Fixture();
        PlaySettingsStore.Save(f.Project,new(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame.exe")));
        var before=f.Hashes();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",f.Project,"--probe-args","ok"],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("Needs your OK",errors.ToString());
        Assert.Equal(before.OrderBy(p=>p.Key),f.Hashes().OrderBy(p=>p.Key));
    }

    [Fact]
    public void Unreadable_data_is_not_repaired_or_played()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Project,"data","values.json"),"not json");
        var before=f.Hashes();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",f.Project],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("No admitted data document could be opened",errors.ToString());
        Assert.Equal(before.OrderBy(p=>p.Key),f.Hashes().OrderBy(p=>p.Key));
    }
    [Fact]
    public void Missing_data_project_settings_are_not_created_by_the_probe()
    {
        using var f=new Fixture();File.Delete(Path.Combine(f.Project,".bed","project.json"));
        var before=f.Hashes();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",f.Project],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("Set the program in Tools > Play settings",errors.ToString());
        Assert.False(File.Exists(Path.Combine(f.Project,".bed","project.json")));
        Assert.Equal(before.OrderBy(p=>p.Key),f.Hashes().OrderBy(p=>p.Key));
    }

}
