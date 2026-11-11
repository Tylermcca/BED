using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

/// <summary>Strict scene version-two reader/writer. Existing SceneFormat remains a separate version-one contract.</summary>
public sealed class Scene2Format:IDocumentFormat<Scene2>
{
    /// <inheritdoc />
    public string Kind=>"scene";
    /// <inheritdoc />
    public int Version=>2;
    /// <inheritdoc />
    public Scene2 Read(string text)
    {var result=new Scene2Read(Scene2Read.Parse(text,32*1024*1024),"").Scene();_=Scene2Resolver.Resolve(result);return result;}
    /// <inheritdoc />
    public string Write(Scene2 document)
    {_=Scene2Resolver.Resolve(document);return Scene2Write.Text(Scene2Write.Scene(document),32*1024*1024);}
}

/// <summary>Strict standalone prefab-definition version-one reader/writer, capped at four MiB.</summary>
public sealed class PrefabDefinitionFormat:IDocumentFormat<PrefabDefinition>
{
    /// <inheritdoc />
    public string Kind=>"prefab-definition";
    /// <inheritdoc />
    public int Version=>1;
    /// <inheritdoc />
    public PrefabDefinition Read(string text)
    {var result=new Scene2Read(Scene2Read.Parse(text,4*1024*1024),"").Definition();Scene2Resolver.CheckDefinition(result);return result;}
    /// <inheritdoc />
    public string Write(PrefabDefinition document)
    {Scene2Resolver.CheckDefinition(document);return Scene2Write.Text(Scene2Write.Definition(document),4*1024*1024);}
}

/// <summary>Complete semantic validation of an immutable scene2 candidate, using the same pure resolver as reading.</summary>
public sealed class Scene2Validator:IValidator<Scene2>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(Scene2 document)
    {try{_=Scene2Resolver.Resolve(document);return [];}catch(DocumentFormatException e){return [Issue.Error(e.Message,path:e.Path)];}}
}

/// <summary>Rooted graph, slot and coordinate-profile validation of a standalone immutable definition.</summary>
public sealed class PrefabDefinitionValidator:IValidator<PrefabDefinition>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(PrefabDefinition document)
    {try{Scene2Resolver.CheckDefinition(document);return [];}catch(DocumentFormatException e){return [Issue.Error(e.Message,path:e.Path)];}}
}

internal static class Scene2Write
{
    private static JsonArray Vector(params float[] values)=>new(values.Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray());
    private static JsonObject Properties(ImmutableSortedDictionary<string,JsonElement> values)
    {var result=new JsonObject();foreach(var p in values)result.Add(p.Key,JsonNode.Parse(p.Value.GetRawText()));return result;}
    private static JsonObject Map(ImmutableSortedDictionary<string,string> values)
    {var result=new JsonObject();foreach(var p in values)result.Add(p.Key,p.Value);return result;}
    private static JsonObject Object(Scene2Object item,bool placement=false)
    {
        var o=item.Object;var result=new JsonObject{{"id",o.Id},{"position",Vector(o.Position.X,o.Position.Y,o.Position.Z)},
            {"rotation",Vector(o.Rotation.X,o.Rotation.Y,o.Rotation.Z,o.Rotation.W)},{"scale",Vector(o.Scale.X,o.Scale.Y,o.Scale.Z)}};
        if(o.Parent is not null)result.Add("parent",o.Parent);
        if(!placement)
        {
            if(o.Asset is not null)result.Add("asset",o.Asset);result.Add("properties",Properties(o.Properties));result.Add("references",Map(o.References));
            if(item.Body is {} body)result.Add("body",ScenePhysicsRules.Body(body));
            if(item.Sprite is {} s)result.Add("sprite",new JsonObject{{"size",Vector(s.Size.X,s.Size.Y)},{"pivot",Vector(s.Pivot.X,s.Pivot.Y)},{"order",s.Order},{"filter",s.Filter==SceneSpriteFilter.Nearest?"nearest":"linear"}});
        }
        return result;
    }
    internal static JsonNode Definition(PrefabDefinition definition)=>JsonNode.Parse(Scene2Jcs.Canonicalize(definition.Original,256*1024*1024))!;
    internal static JsonNode Scene(Scene2 scene)
    {
        var root=new JsonObject{{"kind","scene"},{"version",2},{"space",scene.Space==SceneSpace.ThreeD?"3d":"2d-xy"},
            {"objects",new JsonArray(scene.Objects.Select(o=>(JsonNode)Object(o)).ToArray())}};
        if(scene.Physics is {} physics)root.Add("physics",ScenePhysicsRules.Physics(physics));
        if(scene.Bases.Length>0)root.Add("bases",new JsonArray(scene.Bases.Select(b=>(JsonNode)new JsonObject{{"id",b.Id},{"revision",b.Revision},{"definition",Definition(b.Definition)}}).ToArray()));
        if(scene.Instances.Length>0)root.Add("instances",new JsonArray(scene.Instances.Select(i=>
        {
            var edits=new JsonArray();foreach(var e in i.Overrides)
            {var edit=new JsonObject{{"node",e.Node},{"field",e.Field},{"op",e.Operation==PrefabOverrideOperation.Set?"set":"unset"}};if(e.Value is {} v)edit.Add("value",JsonNode.Parse(v.GetRawText()));edits.Add(edit);}
            return (JsonNode)new JsonObject{{"key",i.Key},{"source",i.Source},{"base",i.Base},{"placement",Object(new(i.Placement,null),true)},
                {"nodeIds",Map(i.NodeIds)},{"bindings",Map(i.Bindings)},{"overrides",edits}};
        }).ToArray()));
        if(scene.ObjectOrder is {} order)root.Add("objectOrder",new JsonArray(order.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray()));return root;
    }
    private sealed class BudgetStream(int maximum):MemoryStream
    {
        private void Admit(int count){if(count>maximum-Length)throw Scene2Read.Bad("","written UTF-8 byte budget exceeded");}
        public override void Write(byte[] buffer,int offset,int count){Admit(count);base.Write(buffer,offset,count);}
        public override void Write(ReadOnlySpan<byte> buffer){Admit(buffer.Length);base.Write(buffer);}
        public override void WriteByte(byte value){Admit(1);base.WriteByte(value);}
    }
    internal static string Text(JsonNode node,int maximum)
    {
        using var stream=new BudgetStream(maximum);
        using(var writer=new Utf8JsonWriter(stream,Canonical.WriterOptions))node.WriteTo(writer);
        stream.WriteByte((byte)'\n');return Encoding.UTF8.GetString(stream.ToArray());
    }
}
