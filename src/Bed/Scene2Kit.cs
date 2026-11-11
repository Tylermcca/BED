using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

/// <summary>The version-two command catalog and strict shared command factory. Pure commands do no file discovery or saving; the host checks admission and captured source freshness.</summary>
public static class Scene2Kit
{
    /// <summary>Flattens an accepted snapshot to ordinary version-two objects in authoritative order, retaining all effective local transforms and metadata.</summary>
    /// <param name="snapshot">The immutable authoring snapshot. The caller owns saved-file capture and admission.</param>
    /// <returns>A scene with explicit object order, the same coordinate space, and no prefab bases or instances.</returns>
    /// <exception cref="DocumentFormatException">The snapshot violates the scene-v2 semantic or expansion limits.</exception>
    /// <remarks>Pure transformation: never reads definitions/assets, publishes output, or changes the authoring document.</remarks>
    public static Scene2 Flatten(Scene2 snapshot)=>Scene2Runtime.Flatten(snapshot);

    /// <summary>Returns a fresh catalog with distinct scene2.* payload-version-one entries. Existing v1 catalogs and journals are unchanged.</summary>
    public static CommandCatalog<Scene2> Catalog()=>Scene2Commands.Catalog();
    /// <summary>Builds an immutable command from a documented action and strict JSON arguments. IDs and source snapshots are explicit inputs; duplicate's optional generated map is resolved into the recorded command.</summary>
    /// <param name="action">set, revert, break_link, instantiate, duplicate, update, group, reorder, remove_instance, add_sprite, set_sprite, paste, set_physics, edit_bodies, or a compatible object action prefixed by object. (for example object.move).</param>
    /// <param name="args">The action's argument object, detached by the command.</param>
    /// <returns>A registered command usable by a scene-v2 session and checked plan.</returns>
    /// <exception cref="DocumentFormatException">The action or arguments are unsupported.</exception>
    public static ICommand<Scene2> Command(string action,JsonElement args)
    {
        if(args.ValueKind!=JsonValueKind.Object)throw new DocumentFormatException("$.args","expected an argument object");
        _=Scene2Jcs.Canonicalize(args); // Reject duplicate keys, invalid Unicode and nonfinite JSON before deserialization.
        if(action.StartsWith("object.",StringComparison.Ordinal))
        {
            var name=action[7..];
            // Validate the compatible command's exact argument shape before constructing its wrapper.
            _=SceneKit.Catalog().FromEntry(JournalEntry.Do("scene."+name,1,args));
            return new Scene2Compatible(name,args);
        }
        return Catalog().FromEntry(JournalEntry.Do("scene2."+action,1,args));
    }
}

internal sealed record Scene2AddSprite(JsonElement Object):ICommand<Scene2>
{
    public JsonElement Object {get;}=Object.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        if(scene.Space!=SceneSpace.TwoDXY)Scene2Commands.Refuse("Sprites require a planar XY scene.");
        var item=JsonNode.Parse(Object.GetRawText())!.AsObject();if(item["sprite"] is null)Scene2Commands.Refuse("Add sprite requires explicit sprite metadata.");
        json["objects"]!.AsArray().Add(item);if(json["objectOrder"] is JsonArray order)order.Add(Scene2Commands.Text(item,"id"));
    });
}
internal sealed record Scene2SetSprite(string Id,JsonElement Sprite):ICommand<Scene2>
{
    public JsonElement Sprite {get;}=Sprite.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        if(Sprite.ValueKind!=JsonValueKind.Object)Scene2Commands.Refuse("Sprite must be an object.");
        var fields=Sprite.EnumerateObject().ToArray();
        if(fields.Length!=4||fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=4||fields.Any(p=>p.Name is not("size" or "pivot" or "order" or "filter")))Scene2Commands.Refuse("Sprite requires exactly size, pivot, order and filter.");
        foreach(var field in fields)Scene2Commands.Set(json,resolved,Id,"/sprite/"+field.Name,field.Value);
    });
}
