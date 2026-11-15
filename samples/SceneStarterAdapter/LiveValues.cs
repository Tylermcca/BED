using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
namespace SceneStarterAdapter;
public sealed partial class SceneStarterAdapter
{
    /// <summary>Only the sample game's explicit runtime fields; other adapters opt in independently.</summary>
    public IEnumerable<LiveValueTarget> LiveValues(ProjectPaths project)
    {
        var root=ProjectRoot(project);if(SceneSettings(root) is not {} settings)return [];
        var path=Path.Combine(root,settings.Source);using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>32*1024*1024)throw new IOException("The sample level is too large to read.");
        using var reader=new StreamReader(file);var scene=new Scene2Format().Read(reader.ReadToEnd());var objects=Scene2Resolver.Resolve(scene).Objects;
        if(settings.SupportsPhysics)return objects.Where(o=>o.Body?.Motion==SceneBodyMotion.Dynamic).Select(o=>new LiveValueTarget(o.Object.Id,o.Object.Id.Replace('_',' '),[
            new("y","Y","m"),new("speedY","Speed Y","m/s"),new("gravityY","Gravity Y","m/s²",-30,0,trialLabel:"Try gravity (-30 to 0)"),new("massKg","Mass","kg",.1f,100,"/body/massKg","Try mass (0.1 to 100)")],["jump"])).ToArray();
        if(scene.Space!=SceneSpace.TwoDXY||!objects.Any(o=>o.Object.Id=="round_friend"&&o.Sprite is not null))return [];
        return [new("round_friend","Round friend",[
            new("x","X","m"),new("y","Y","m"),new("speedX","Speed X","m/s"),new("speedY","Speed Y","m/s"),
            new("jumpStrength","Jump strength","m/s",1,25,"/properties/jumpStrength","Try jump strength (1-25)")],["left","right","jump","restart"])];
    }
}
