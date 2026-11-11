using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
namespace Bed;

/// <summary>Pure bounded expansion of accepted snapshots; never reads a file or an asset catalog.</summary>
public static class Scene2Resolver
{
    private static DocumentFormatException Bad(string at,string reason)=>Scene2Read.Bad(at,reason);
    /// <summary>Validates and resolves the complete scene, or throws DocumentFormatException without returning partial output.</summary>
    public static Scene2Resolution Resolve(Scene2 scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try{return ResolveCore(scene);}
        catch(Exception error)when(error is InvalidOperationException or System.Text.EncoderFallbackException or JsonException)
        {throw Bad("","invalid detached scene value: "+error.Message);}
    }
    private static Scene2Resolution ResolveCore(Scene2 scene)
    {
        if(!Enum.IsDefined(scene.Space))throw Bad("/space","unknown coordinate space");
        if(scene.Objects.IsDefault||scene.Bases.IsDefault||scene.Instances.IsDefault||scene.Objects.Length>50000||scene.Bases.Length>512||scene.Instances.Length>10000)throw Bad("","scene count budget exceeded or uninitialized collection");
        ScenePhysicsRules.CheckPhysics(scene.Physics,scene.Space);
        var bodyCount=0;
        var bases=new Dictionary<string,(PrefabBase Base,int Index)>(StringComparer.Ordinal);
        var canonical=new Dictionary<(string,string),byte[]>();
        for(var i=0;i<scene.Bases.Length;i++)
        {
            var b=scene.Bases[i];var at="/bases/"+i;Scene2Read.Id(b.Id,at+"/id");
            if(!bases.TryAdd(b.Id,(b,i)))throw Bad(at+"/id","duplicate base id");
            if(b.Definition.Nodes.IsDefault||b.Definition.Nodes.Length is <1 or >10000)throw Bad(at+"/definition/nodes","definition node budget exceeded");
        }
        long total=scene.Objects.Length;
        for(var index=0;index<scene.Instances.Length;index++)
        {var instance=scene.Instances[index];if(!bases.TryGetValue(instance.Base,out var b))throw Bad("/instances/"+index+"/base","unknown accepted base "+instance.Base);total=checked(total+1+b.Base.Definition.Nodes.Length);if(total>50000)throw Bad("/instances/"+index,"expanded object budget exceeds50000");}
        // Expanded counts are admitted before definition graph/hash work or effective-object allocation.
        foreach(var entry in bases.Values)
        {
            var b=entry.Base;var at="/bases/"+entry.Index;CheckDefinition(b.Definition,at+"/definition");var bytes=Scene2Jcs.Canonicalize(b.Definition.Original,256*1024*1024);
            var revision="sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes));if(b.Revision!=revision)throw Bad(at+"/revision","accepted definition hash does not match its supplied JSON");
            var identity=(b.Definition.Guid,b.Revision);if(canonical.TryGetValue(identity,out var previous)&&!bytes.AsSpan().SequenceEqual(previous))throw Bad(at,"same source/revision has different canonical content");canonical[identity]=bytes;
        }
        var objects=new List<Scene2Object>((int)total);var origins=ImmutableSortedDictionary.CreateBuilder<string,Scene2Origin>(StringComparer.Ordinal);
        var used=new HashSet<string>(StringComparer.Ordinal);var instanceKeys=new HashSet<string>(StringComparer.Ordinal);var boundTargets=new List<(string Id,string Path)>();
        void Add(Scene2Object item,Scene2Origin origin)
        {if(item.Body is not null&&++bodyCount>4096)throw Bad(origin.SourcePointer+"/body","resolved body count exceeds 4096");var idPath=origin.FieldOwners["/id"];Scene2Read.Id(item.Object.Id,idPath);CheckObject(item,scene.Space,origin.SourcePointer);if(origins.ContainsKey(item.Object.Id))throw Bad(idPath,"duplicate expanded id "+item.Object.Id);objects.Add(item);origins.Add(item.Object.Id,origin);}
        for(var i=0;i<scene.Objects.Length;i++){var at="/objects/"+i;Add(scene.Objects[i],new(Scene2OriginKind.Ordinary,null,null,at,Fields(scene.Objects[i],at)));}
        for(var i=0;i<scene.Instances.Length;i++)
        {
            var instance=scene.Instances[i];var at="/instances/"+i;Scene2Read.Key(instance.Key,at+"/key");Scene2Read.Key(instance.Source,at+"/source");
            if(!instanceKeys.Add(instance.Key))throw Bad(at+"/key","duplicate instance key");var b=bases[instance.Base];used.Add(instance.Base);var definition=b.Base.Definition;
            if(instance.Source!=definition.Guid)throw Bad(at+"/source","source GUID differs from accepted snapshot");
            if(definition.Space!=scene.Space)throw Bad(at,"definition space differs from scene space");
            var acceptedKeys=definition.Nodes.Select(n=>n.Key).ToHashSet(StringComparer.Ordinal);
            if(instance.NodeIds.Count!=definition.Nodes.Length||definition.Nodes.Any(n=>!instance.NodeIds.ContainsKey(n.Key))||instance.NodeIds.Keys.Any(key=>!acceptedKeys.Contains(key)))throw Bad(at+"/nodeIds","node map must exactly cover the accepted nodes");
            var slotMap=definition.Slots.ToDictionary(s=>s.Key,StringComparer.Ordinal);
            foreach(var binding in instance.Bindings)
            {if(!slotMap.ContainsKey(binding.Key))throw Bad(at+"/bindings","unknown exposed slot");Scene2Read.Id(binding.Value,at+"/bindings/"+binding.Key);boundTargets.Add((binding.Value,at+"/bindings/"+binding.Key));}
            foreach(var slot in definition.Slots)if(slot.Required&&!instance.Bindings.ContainsKey(slot.Key))throw Bad(at+"/bindings/"+slot.Key,"required slot is unbound");
            var placement=instance.Placement;
            if(placement.Asset is not null||placement.Properties.Count!=0||placement.References.Count!=0)throw Bad(at+"/placement","placement must be an empty transform wrapper");
            var wrapper=new Scene2Object(placement,null);Add(wrapper,new(Scene2OriginKind.Placement,instance.Key,null,at+"/placement",Fields(wrapper,at+"/placement")));
            if(instance.Overrides.IsDefault||instance.Overrides.Length>10000)throw Bad(at+"/overrides","override budget exceeded");
            var nodes=definition.Nodes.ToDictionary(n=>n.Key,StringComparer.Ordinal);var groups=new Dictionary<string,List<(PrefabOverride Item,int Index)>>(StringComparer.Ordinal);var addresses=new HashSet<(string,string)>();var wholeBodies=new HashSet<string>(StringComparer.Ordinal);var bodyChildren=new HashSet<string>(StringComparer.Ordinal);
            for(var j=0;j<instance.Overrides.Length;j++)
            {
                var edit=instance.Overrides[j];if(!nodes.ContainsKey(edit.Node))throw Bad(at+"/overrides/"+j+"/node","override names an unknown node");
                if(edit.Field=="/body"&&bodyChildren.Contains(edit.Node)||edit.Field.StartsWith("/body/",StringComparison.Ordinal)&&wholeBodies.Contains(edit.Node))throw Bad(at+"/overrides/"+j,"whole-body and child-body overrides overlap");
                if(edit.Field=="/body")wholeBodies.Add(edit.Node);else if(edit.Field.StartsWith("/body/",StringComparison.Ordinal))bodyChildren.Add(edit.Node);
                if(!addresses.Add((edit.Node,edit.Field)))throw Bad(at+"/overrides/"+j,"duplicate or overlapping override address");
                if(!groups.TryGetValue(edit.Node,out var group))groups.Add(edit.Node,group=[]);group.Add((edit,j));
            }
            for(var j=0;j<definition.Nodes.Length;j++)
            {
                var node=definition.Nodes[j];var source="/bases/"+b.Index+"/definition/nodes/"+j;
                var refs=ImmutableSortedDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
                foreach(var (name,target) in node.References)
                    if(target.Node is {} targetNode)refs.Add(name,instance.NodeIds[targetNode]);
                    else if(instance.Bindings.TryGetValue(target.Slot!,out var targetId))refs.Add(name,targetId);
                var content=new SceneObject(instance.NodeIds[node.Key],node.Asset,node.Position,node.Rotation,node.Scale,node.ParentKey is {} parent?instance.NodeIds[parent]:placement.Id,node.Properties,refs.ToImmutable());
                var item=new Scene2Object(content,node.Sprite){Body=node.Body};var owners=Fields(item,source).SetItem("/id",at+"/nodeIds/"+node.Key).SetItem("/parent",node.ParentKey is null?at+"/placement/id":source+"/parentKey");
                if(groups.TryGetValue(node.Key,out var edits))foreach(var (edit,index) in edits)
                {var editAt=at+"/overrides/"+index;item=Apply(item,edit,editAt);owners=owners.SetItem(edit.Field,editAt+(edit.Operation==PrefabOverrideOperation.Set?"/value":"/op"));if(edit.Field=="/body")foreach(var field in new[]{"motion","shape","massKg","friction"})owners=owners.SetItem("/body/"+field,editAt+(edit.Operation==PrefabOverrideOperation.Set?"/value/"+field:"/op"));}
                Add(item,new(Scene2OriginKind.PrefabNode,instance.Key,node.Key,source,owners));
            }
        }
        foreach(var b in scene.Bases)if(!used.Contains(b.Id))throw Bad("/bases","unused accepted base "+b.Id);
        var byId=objects.ToDictionary(x=>x.Object.Id,StringComparer.Ordinal);
        foreach(var target in boundTargets)if(!byId.ContainsKey(target.Id))throw Bad(target.Path,"bound slot target does not exist");
        foreach(var item in objects)
        {
            var o=item.Object;var origin=origins[o.Id];
            if(o.Parent is {} parent)
            {
                if(!byId.ContainsKey(parent))throw Bad(origin.SourcePointer+"/parent","parent target does not exist");
                if(origin.Kind!=Scene2OriginKind.PrefabNode&&origins[parent].Kind==Scene2OriginKind.PrefabNode)throw Bad(origin.SourcePointer+"/parent","external objects cannot parent into instance internals; use its placement wrapper");
            }
            foreach(var (name,target) in o.References)if(!byId.ContainsKey(target))throw Bad(origin.FieldOwners["/references/"+Scene2Read.Escape(name)],"reference target does not exist");
        }
        CheckParents(byId.ToDictionary(p=>p.Key,p=>p.Value.Object.Parent,StringComparer.Ordinal),"/objects");
        if(bodyCount>0)
        {
            if(scene.Physics is null)throw Bad("/physics","explicit world physics is required when a resolved body exists");
            Scene2BodyTransforms.Check(byId.ToDictionary(p=>p.Key,p=>p.Value.Object,StringComparer.Ordinal),objects.Where(o=>o.Body is not null).Select(o=>o.Object.Id).ToHashSet(StringComparer.Ordinal),(id,field)=>origins[id].FieldOwners.GetValueOrDefault(field,origins[id].SourcePointer+field));
        }
        if(scene.ObjectOrder is {} order)
        {
            if(order.IsDefault||order.Length!=objects.Count||order.Distinct(StringComparer.Ordinal).Count()!=objects.Count||order.Any(id=>!byId.ContainsKey(id)))throw Bad("/objectOrder","expected an exact permutation of all expanded IDs");
            objects=order.Select(id=>byId[id]).ToList();
        }
        return new(objects.ToImmutableArray(),origins.ToImmutable());
    }
    internal static void CheckDefinition(PrefabDefinition definition,string at="")
    {
        Scene2Read.Key(definition.Guid,at+"/guid");Scene2Read.Key(definition.Root,at+"/root");
        if(!Enum.IsDefined(definition.Space)||definition.Nodes.IsDefault||definition.Nodes.Length is <1 or >10000||definition.Slots.IsDefault||definition.Slots.Length>256)throw Bad(at,"invalid definition space/counts");
        var nodes=new Dictionary<string,PrefabNode>(StringComparer.Ordinal);var slots=new HashSet<string>(StringComparer.Ordinal);
        foreach(var slot in definition.Slots)
        {Scene2Read.Key(slot.Key,at+"/slots");CheckText(slot.Label,1,256,at+"/slots");if(!slots.Add(slot.Key))throw Bad(at+"/slots","duplicate slot key");}
        for(var i=0;i<definition.Nodes.Length;i++)
        {
            var n=definition.Nodes[i];var source=at+"/nodes/"+i;Scene2Read.Key(n.Key,source+"/key");CheckText(n.Label,1,256,source+"/label");
            if(!nodes.TryAdd(n.Key,n))throw Bad(source+"/key","duplicate node key");
            // Node keys have their own namespace; use a valid temporary scene id solely for content checks.
            var o=new SceneObject("node",n.Asset,n.Position,n.Rotation,n.Scale,null,n.Properties,ImmutableSortedDictionary<string,string>.Empty);
            CheckObject(new(o,n.Sprite){Body=n.Body},definition.Space,source);
            if(n.References.Count>1024)throw Bad(source+"/references","reference budget exceeded");
        }
        if(!nodes.TryGetValue(definition.Root,out var root)||root.ParentKey is not null)throw Bad(at+"/root","definition root must exist and have no parent");
        foreach(var node in nodes.Values)
        {
            if(node.Key!=definition.Root&&(node.ParentKey is null||!nodes.ContainsKey(node.ParentKey)))throw Bad(at+"/nodes","every non-root node needs an existing parent");
            foreach(var (name,target) in node.References)
            {
                Scene2Read.Name(name,at+"/references");
                if((target.Node is null)==(target.Slot is null)||target.Node is {} key&&!nodes.ContainsKey(key)||target.Slot is {} slot&&!slots.Contains(slot))throw Bad(at+"/references/"+Scene2Read.Escape(name),"unknown or ambiguous prefab reference");
            }
        }
        CheckParents(nodes.ToDictionary(p=>p.Key,p=>p.Value.ParentKey,StringComparer.Ordinal),at+"/nodes");
        if(definition.Nodes.Any(n=>n.Body is not null))
        {
            var nodeIndices=definition.Nodes.Select((node,index)=>(node.Key,index)).ToDictionary(p=>p.Key,p=>p.index,StringComparer.Ordinal);
            var poses=definition.Nodes.ToDictionary(n=>"node_"+n.Key,n=>new SceneObject("node_"+n.Key,n.Asset,n.Position,n.Rotation,n.Scale,n.ParentKey is {} parent?"node_"+parent:null,n.Properties,ImmutableSortedDictionary<string,string>.Empty),StringComparer.Ordinal);
            Scene2BodyTransforms.Check(poses,definition.Nodes.Where(n=>n.Body is not null).Select(n=>"node_"+n.Key).ToHashSet(StringComparer.Ordinal),(id,field)=>at+"/nodes/"+nodeIndices[id[5..]]+field);
        }
    }
    private static void CheckParents(Dictionary<string,string?> parents,string at)
    {
        var colors=new Dictionary<string,byte>(StringComparer.Ordinal);
        foreach(var id in parents.Keys)
        {
            var chain=new List<string>();string? current=id;
            while(current is not null)
            {if(colors.TryGetValue(current,out var color)){if(color==1)throw Bad(at,"parent cycle");break;}colors[current]=1;chain.Add(current);current=parents[current];}
            foreach(var key in chain)colors[key]=2;
        }
    }
    private static void CheckText(string value,int min,int max,string at)
    {
        try{_=Scene2Read.Utf8.GetByteCount(value);}catch(System.Text.EncoderFallbackException){throw Bad(at,"invalid Unicode");}
        if(value.EnumerateRunes().Count()<min||value.EnumerateRunes().Count()>max)throw Bad(at,"text length outside allowed bounds");
    }
    private static void CheckObject(Scene2Object item,SceneSpace space,string at)
    {
        var o=item.Object;Scene2Read.Id(o.Id,at+"/id");if(o.Parent is {} parent)Scene2Read.Id(parent,at+"/parent");if(o.Asset is {} asset)CheckText(asset,1,2048,at+"/asset");
        foreach(var n in new[]{o.Position.X,o.Position.Y,o.Position.Z,o.Rotation.X,o.Rotation.Y,o.Rotation.Z,o.Rotation.W,o.Scale.X,o.Scale.Y,o.Scale.Z})if(!float.IsFinite(n))throw Bad(at,"transform must be finite float32");
        if(o.Properties.Count>1024||o.References.Count>1024)throw Bad(at,"property/reference budget exceeded");
        foreach(var (name,value) in o.Properties)
        {Scene2Read.Name(name,at+"/properties");var r=new Scene2Read(value,at+"/properties/"+Scene2Read.Escape(name));r.Scalar();if(value.ValueKind==JsonValueKind.String)CheckText(value.GetString()!,0,65536,r.Path);}
        foreach(var (name,target) in o.References){Scene2Read.Name(name,at+"/references");Scene2Read.Id(target,at+"/references/"+Scene2Read.Escape(name));}
        if(item.Body is {} body)
        {
            ScenePhysicsRules.CheckBody(body,at+"/body");
            if(body.Shape is SceneMeshBoundsShape&&(o.Asset is null||item.Sprite is not null))throw Bad(at+"/body/shape","mesh-bounds requires a model asset, not a sprite or empty marker");
        }
        if(space==SceneSpace.TwoDXY)
        {
            if(o.Position.Z!=0||o.Rotation.X!=0||o.Rotation.Y!=0||o.Scale.Z!=1||o.Scale.X==0||o.Scale.Y==0||Math.Abs((double)o.Rotation.Z*o.Rotation.Z+(double)o.Rotation.W*o.Rotation.W-1)>Scene2Numeric.PlanarRotationTolerance)throw Bad(at,"planar transform must stay in XY with unit Z-rotation and nonzero XY scale");
            if((o.Asset is null)!=(item.Sprite is null))throw Bad(at,"planar objects require both image asset and sprite, or neither");
        }
        else if(item.Sprite is not null)throw Bad(at+"/sprite","sprites are unsupported in 3d space");
        if(item.Sprite is {} sprite)
        {
            Scene2Read.Key(o.Asset!,at+"/asset");
            if(!float.IsFinite(sprite.Size.X)||!float.IsFinite(sprite.Size.Y)||sprite.Size.X<=0||sprite.Size.Y<=0||!float.IsFinite(sprite.Pivot.X)||!float.IsFinite(sprite.Pivot.Y)||sprite.Pivot.X is <0 or >1||sprite.Pivot.Y is <0 or >1||!Enum.IsDefined(sprite.Filter))throw Bad(at+"/sprite","invalid sprite dimensions, pivot or filter");
        }
    }
    private static ImmutableSortedDictionary<string,string> Fields(Scene2Object item,string at)
    {
        var fields=ImmutableSortedDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
        foreach(var field in new[]{"/id","/asset","/position","/rotation","/scale","/parent"})fields.Add(field,at+field);
        foreach(var key in item.Object.Properties.Keys)fields.Add("/properties/"+Scene2Read.Escape(key),at+"/properties/"+Scene2Read.Escape(key));
        foreach(var key in item.Object.References.Keys)fields.Add("/references/"+Scene2Read.Escape(key),at+"/references/"+Scene2Read.Escape(key));
        fields.Add("/body",at+"/body");if(item.Body is not null)foreach(var field in new[]{"motion","shape","massKg","friction"})fields.Add("/body/"+field,at+"/body/"+field);
        if(item.Sprite is not null)foreach(var field in new[]{"size","pivot","order","filter"})fields.Add("/sprite/"+field,at+"/sprite/"+field);
        return fields.ToImmutable();
    }
    private static string Segment(string text,string at)
    {
        var result=new System.Text.StringBuilder();for(var i=0;i<text.Length;i++)
        {if(text[i]!='~'){result.Append(text[i]);continue;}if(++i>=text.Length||text[i] is not ('0' or '1'))throw Bad(at,"invalid JSON Pointer escape");result.Append(text[i]=='0'?'~':'/');}
        return Scene2Read.Name(result.ToString(),at);
    }
    private static Scene2Object Apply(Scene2Object item,PrefabOverride edit,string at)
    {
        if(!Enum.IsDefined(edit.Operation)||(edit.Value is null)!=(edit.Operation==PrefabOverrideOperation.Unset))throw Bad(at,"set requires a value; unset must omit it");
        var unset=edit.Operation==PrefabOverrideOperation.Unset;var o=item.Object;var sprite=item.Sprite;var body=item.Body;var path=edit.Field.Split('/');
        var value=edit.Value is {} v?new Scene2Read(v,at+"/value"):null;
        switch(edit.Field)
        {
            case "/body":body=unset?null:ScenePhysicsRules.ReadBody(value!);break;
            case "/position":if(unset)throw Bad(at,"transform cannot be unset");o=o with{Position=value!.Vec3()};break;
            case "/rotation":if(unset)throw Bad(at,"transform cannot be unset");o=o with{Rotation=value!.Quat()};break;
            case "/scale":if(unset)throw Bad(at,"transform cannot be unset");o=o with{Scale=value!.Vec3()};break;
            case "/asset":o=o with{Asset=unset?null:value!.Text(1,2048)};break;
            default:
                if(path.Length!=3||path[0]!="")throw Bad(at+"/field","unsupported atomic override field");
                var name=Segment(path[2],at+"/field");
                if(path[1]=="properties")o=o with{Properties=unset?o.Properties.Remove(name):o.Properties.SetItem(name,value!.Scalar())};
                else if(path[1]=="references")
                {if(!unset)value!.Only("object");o=o with{References=unset?o.References.Remove(name):o.References.SetItem(name,value!.Get("object").Id())};}
                else if(path[1]=="body"&&body is not null)
                {
                    if(unset&&name!="massKg")throw Bad(at,"only a whole body or its massKg can be unset");
                    body=name switch{"motion"=>body with{Motion=ScenePhysicsRules.Motion(value!)},"shape"=>body with{Shape=ScenePhysicsRules.ReadShape(value!)},"massKg"=>body with{MassKg=unset?null:ScenePhysicsRules.Number(value!)},"friction"=>body with{Friction=ScenePhysicsRules.Number(value!)},_=>throw Bad(at,"unsupported body override")};
                }
                else if(path[1]=="sprite"&&sprite is not null&&!unset)
                    sprite=name switch
                    {"size"=>sprite with{Size=value!.Vec2()},"pivot"=>sprite with{Pivot=value!.Vec2()},"order"=>sprite with{Order=value!.Integer()},"filter"=>sprite with{Filter=value!.Text() switch{"nearest"=>SceneSpriteFilter.Nearest,"linear"=>SceneSpriteFilter.Linear,_=>throw Bad(at,"invalid sprite filter")}},_=>throw Bad(at,"unsupported sprite override")};
                else throw Bad(at+"/field","unsupported field, unset operation or missing inherited body/sprite");break;
        }
        return new(o,sprite){Body=body};
    }
}
