using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ExportBuildModelTests
{
    [Fact]
    public void Build_spec_lives_beside_launch_spec_without_an_App_dependency_or_duplicate_type()
    {
        Assert.Same(typeof(GameLaunchSpec).Assembly,typeof(GameBuildSpec).Assembly);
        Assert.Null(typeof(GameBuildHost).Assembly.GetType("Brutal.Authoring.Editor.App.GameBuildSpec"));
        Assert.DoesNotContain(typeof(GameBuildSpec).Assembly.GetReferencedAssemblies(),a=>a.Name=="Brutal.Authoring.Editor.App");
    }
    [Fact]
    public void Binding_owned_output_copies_literal_argv_and_preserves_the_reusable_template()
    {
        string[] argv=["--out","{output}","literal & value"];var spec=new GameBuildSpec(@"C:\admitted\builder.exe",@"C:\admitted",argv);argv[1]="changed";
        var bound=spec.BindOutput(@"C:\admitted\owned output");Assert.Equal(new[]{"--out",@"C:\admitted\owned output","literal & value"},bound.Arguments);
        Assert.Equal("{output}",spec.Arguments[1]);Assert.Equal(spec.Executable,bound.Executable);Assert.Equal(spec.WorkingDirectory,bound.WorkingDirectory);
        Assert.Throws<ArgumentException>(()=>spec.BindOutput("relative-output"));
    }
}
