using System.Diagnostics;
using System.Security.Cryptography;
using Brutal.Authoring.Runtime;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RuntimeInspectRt01Tests(ITestOutputHelper evidence)
{
    private static Dictionary<string,string> Hashes(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(path=>Path.GetRelativePath(root,path),path=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Actual_console_process_reads_the_prepared_sample_without_editor_or_source_art_and_writes_nothing(bool removeSource)
    {
        using var temporary=new CapturedExportTests.Owned();var project=ReleasePlayProbeTests.CopyProject(temporary.Root);
        using(var prepared=new StringWriter())using(var why=new StringWriter())
            Assert.Equal(0,Brutal.Authoring.Launcher.Program.Run(["--probe-play",project,"--probe-args","--seconds 0","--timeout","10"],prepared,why,Path.Combine(temporary.Root,"local")));
        if(removeSource)Directory.Move(Path.Combine(project,"assets"),Path.Combine(temporary.Root,"unused-source-assets"));
        var before=Hashes(project);
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(typeof(RuntimeInspect.Program).Assembly.Location);start.ArgumentList.Add(project);
        using var process=Process.Start(start)!;var text=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10_000)){process.Kill(entireProcessTree:true);Assert.Fail("RuntimeInspect did not finish within 10 seconds.");}
        Assert.Equal(0,process.ExitCode);Assert.Equal("",await error);var printed=await text;
        Assert.Contains("Object root: parent=(none), position=(2, 3, 4), asset=(none)",printed);
        Assert.Contains("Object child: parent=root, position=(1, 0, 0)",printed);
        Assert.Contains("Object textured_crate:",printed);Assert.Contains("Mesh c8a7e000000000000000000000000001: 1 parts",printed);
        Assert.Contains("Part 0:",printed);Assert.Contains("triangles, UV origin ObjBottomLeft",printed);
        Assert.Contains("Texture c8a7e000000000000000000000000002: 8 x 8 RGBA8",printed);
        Assert.Contains("Loaded 3 objects, 1 meshes, 1 textures.",printed);
        Assert.Equal(before.OrderBy(p=>p.Key),Hashes(project).OrderBy(p=>p.Key));
        evidence.WriteLine(removeSource?"Prepared sample, source art removed:":"Prepared sample:");evidence.WriteLine(printed);
    }
    [Fact] public void Missing_generation_and_bad_arguments_refuse_plainly_without_writing_anything()
    {
        var root=Directory.CreateTempSubdirectory("runtime-inspect-rt01-").FullName;
        try
        {
            using var output=new StringWriter();using var error=new StringWriter();
            Assert.Equal(2,RuntimeInspect.Program.Run([],output,error));Assert.Contains("Usage:",error.ToString());error.GetStringBuilder().Clear();
            Assert.Equal(1,RuntimeInspect.Program.Run([root],output,error));Assert.Contains(ProjectAssets.MissingGeneration,error.ToString());
            Assert.Equal("",output.ToString());Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
