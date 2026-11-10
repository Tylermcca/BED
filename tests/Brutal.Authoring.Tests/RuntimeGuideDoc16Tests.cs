using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Runtime;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class RuntimeGuideDoc16Tests
{
    [Fact]
    public async Task Published_runtime_snippet_compiles_with_only_runtime_and_core_and_reads_saved_multipart_textures_without_writing()
    {
        var checkout=new DirectoryInfo(AppContext.BaseDirectory);
        while(checkout is not null&&!File.Exists(Path.Combine(checkout.FullName,"Brutal.Authoring.slnx")))checkout=checkout.Parent;
        Assert.NotNull(checkout);
        var guide=File.ReadAllText(Path.Combine(checkout.FullName,"docs","guide","runtime.md"));
        var ticks=new string((char)96,3);
        var match=Regex.Match(guide,@"<!-- DOC16 compiling runtime example -->\s*"+ticks+@"csharp\r?\n(?<code>[\s\S]*?)\r?\n"+ticks);
        Assert.True(match.Success,"Marked runtime code is missing.");
        var temporary=Directory.CreateTempSubdirectory("runtime-guide-doc16-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(temporary,"Program.cs"),match.Groups["code"].Value);
            var references=string.Join("\n",new[]{typeof(SceneRuntimeReader).Assembly,typeof(ProjectAssets).Assembly}
                .Select(a=>$"<Reference Include=\"{SecurityElement.Escape(a.GetName().Name)}\"><HintPath>{SecurityElement.Escape(a.Location)}</HintPath></Reference>"));
            File.WriteAllText(Path.Combine(temporary,"Snippet.csproj"),$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
                  <ItemGroup>{references}</ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(temporary,"NuGet.Config"),"<configuration><packageSources><clear /></packageSources></configuration>");
            var build=await Run("build","Snippet.csproj","--nologo","--verbosity","quiet","--disable-build-servers");
            Assert.True(build.Exit==0,build.Output+build.Error);Assert.Equal("",build.Error);
            const string mesh="11111111111111111111111111111111",texture="22222222222222222222222222222222";
            var root=Directory.CreateDirectory(Path.Combine(temporary,"saved")).FullName;
            Directory.CreateDirectory(Path.Combine(root,"data"));
            var scene=new Scene([SceneObject.At("root",null,new(9,0,0)),SceneObject.At("placed",mesh,Vector3.UnitY) with{Parent="root"}]);
            var level=Path.Combine(root,"data","level.json");File.WriteAllText(level,new SceneFormat().Write(scene));
            var triangle=new MeshData([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2])
                {Uv0=[Vector2.Zero,Vector2.UnitX,Vector2.UnitY],Uv0Origin=MeshUv0Origin.ObjBottomLeft};
            var geometry=DerivedMeshCodec.Encode(new MeshPart[]{new(triangle,new(0,Vector4.One,Guid.ParseExact(texture,"N"))),new(triangle,null)});
            var pixels=new byte[20];"BEDTEX1\0"u8.CopyTo(pixels);BinaryPrimitives.WriteUInt32LittleEndian(pixels.AsSpan(8),1);BinaryPrimitives.WriteUInt32LittleEndian(pixels.AsSpan(12),1);pixels[16]=pixels[19]=255;
            using(var publisher=new AssetCatalogPublisher(root))
            {
                var prepared=publisher.Prepare("self-authored-doc16",new Dictionary<string,byte[]>{{mesh+".bin",geometry},{texture+".bin",pixels}});
                Assert.True(publisher.Publish(prepared,out var why),why);
            }
            Assert.False(Directory.Exists(Path.Combine(root,"assets")));
            var before=Hashes(root);
            var program=Path.Combine(temporary,"bin","Debug","net10.0","Snippet.dll");
            var read=await Run(program,root);
            Assert.True(read.Exit==0,read.Output+read.Error);Assert.Equal("",read.Error);
            Assert.Contains("Object root: parent=(none), asset=(none)",read.Output);
            Assert.Contains("Object placed: parent=root, asset="+mesh,read.Output);
            Assert.Contains("Mesh "+mesh+": 2 parts",read.Output);
            Assert.Equal(2,Regex.Matches(read.Output,Regex.Escape("Part: 3 vertices, 1 triangles, UV origin ObjBottomLeft")).Count);
            Assert.Contains("Texture "+texture+": 1 x 1, 4 RGBA bytes",read.Output);
            Assert.Equal(before.OrderBy(p=>p.Key),Hashes(root).OrderBy(p=>p.Key));
            var missing=Directory.CreateDirectory(Path.Combine(temporary,"missing")).FullName;
            var refused=await Run(program,missing);
            Assert.Equal(1,refused.Exit);Assert.Equal("",refused.Output);Assert.Contains(ProjectAssets.MissingGeneration,refused.Error);Assert.Empty(Directory.GetFileSystemEntries(missing));
            File.WriteAllText(level,"""{"kind":"scene","version":999,"objects":[]}""");
            var invalid=File.ReadAllBytes(level);refused=await Run(program,root);
            Assert.Equal(1,refused.Exit);Assert.Equal("",refused.Output);Assert.StartsWith("Cannot load saved level: ",refused.Error);
            Assert.Equal(invalid,File.ReadAllBytes(level));
            var arguments=await Run(program);
            Assert.Equal(2,arguments.Exit);Assert.Contains("Usage: SavedGame",arguments.Error);
            var sample=File.ReadAllText(Path.Combine(checkout.FullName,"samples","RuntimeInspect","Program.cs"));
            Assert.Contains("ProjectAssets.Open(root)",sample);Assert.Contains("SceneRuntimeReader.Read(",sample);Assert.Contains("assets.ReadMesh(",sample);Assert.Contains("assets.ReadTexture(",sample);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),temporary,StringComparison.OrdinalIgnoreCase);Directory.Delete(temporary,true);
        }
        async Task<(int Exit,string Output,string Error)> Run(params string[] arguments)
        {
            var start=new ProcessStartInfo("dotnet"){WorkingDirectory=temporary,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in arguments)start.ArgumentList.Add(argument);
            using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));}
            finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
            return(child.ExitCode,await output,await error);
        }
    }
    private static Dictionary<string,string> Hashes(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(p=>Path.GetRelativePath(root,p),p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
}
