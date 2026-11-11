using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

public sealed class AdapterPlayGuideDoc09Tests
{
    [Fact]
    public async Task Published_DefaultPlay_adapter_snippet_compiles_against_current_public_contract()
    {
        var checkout=new DirectoryInfo(AppContext.BaseDirectory);
        while(checkout is not null&&!File.Exists(Path.Combine(checkout.FullName,"Bed.slnx")))checkout=checkout.Parent;
        Assert.NotNull(checkout);
        var guide=File.ReadAllText(Path.Combine(checkout.FullName,"docs","guide","writing_an_adapter.md"));
        var match=Regex.Match(guide,@"<!-- DOC09 compiling Play adapter -->\s*```csharp\r?\n(?<code>[\s\S]*?)\r?\n```");
        Assert.True(match.Success,"The marked compiling Play adapter example is missing.");
        var directory=Directory.CreateTempSubdirectory("adapter-play-guide-doc09-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory,"Adapter.cs"),match.Groups["code"].Value);
            var assemblies=new[]{typeof(Bed.IValidator<>).Assembly,typeof(DataProject).Assembly,typeof(DataDocument).Assembly,typeof(EditorModel<>).Assembly,typeof(IProjectAdapter).Assembly}.Distinct();
            var references=string.Join("\n",assemblies.Select(a=>$"<Reference Include=\"{SecurityElement.Escape(a.GetName().Name)}\"><HintPath>{SecurityElement.Escape(a.Location)}</HintPath></Reference>"));
            File.WriteAllText(Path.Combine(directory,"Snippet.csproj"),$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
                  <ItemGroup>{references}</ItemGroup>
                </Project>
                """);
            // This SDK-only temporary project restores no packages and cannot contact package sources.
            File.WriteAllText(Path.Combine(directory,"NuGet.Config"),"<configuration><packageSources><clear /></packageSources></configuration>");
            var start=new ProcessStartInfo("dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"build","Snippet.csproj","--nologo","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));}
            finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
            Assert.True(child.ExitCode==0,$"Guide example failed to compile:\n{await output}\n{await error}");
            Assert.True(File.Exists(Path.Combine(directory,"bin","Debug","net10.0","Snippet.dll")));
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),directory,StringComparison.OrdinalIgnoreCase);
            Directory.Delete(directory,true);
        }
    }
}
