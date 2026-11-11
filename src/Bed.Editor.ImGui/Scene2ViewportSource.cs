using System.Numerics;
using Bed.Editor;
namespace Bed.Editor.ImGui;

internal sealed class Scene2ViewportSource(Action changed):IViewportSource<Scene2>,IViewportTransformSource<Scene2>
{
    private readonly SceneCatalogSource source=new(changed);
    internal AssetCatalog? Catalog {get=>source.Catalog;set=>source.Catalog=value;}
    private static Scene Flat(Scene2 document)=>Scene2ViewProjection.For(document).Flat;
    public IEnumerable<ViewportItem> Items(Scene2 document)=>source.Items(Flat(document));
    internal static ICommand<Scene2> Wrap(ICommand<Scene> command)
    {var entry=SceneKit.Catalog().ToEntry(command);return Scene2Kit.Command("object."+entry.Command!["scene.".Length..],entry.Args!.Value);}
    public ICommand<Scene2>? MoveTo(Scene2 document,string id,Vector3 world)=>source.MoveTo(Flat(document),id,world) is {} command?Wrap(command):null;
    public ViewportTransformPlan<Scene2> Rotate(Scene2 document,IReadOnlyList<string> selected,Vector3 pivot,Quaternion basis,Vector3 axis,float radians,float snapStep)
    {var plan=source.Rotate(Flat(document),selected,pivot,basis,axis,radians,snapStep);return new(plan.Commands.Select(Wrap).ToArray(),plan.Roots,plan.Issues);}
    public ViewportTransformPlan<Scene2> Scale(Scene2 document,IReadOnlyList<string> selected,Vector3 factors)
    {var plan=source.Scale(Flat(document),selected,factors);return new(plan.Commands.Select(Wrap).ToArray(),plan.Roots,plan.Issues);}
}
