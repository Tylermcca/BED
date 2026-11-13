using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace Bed;

internal sealed class Scene2Read(JsonElement value,string path)
{
    internal static readonly UTF8Encoding Utf8=new(false,true);
    internal JsonElement Value=>value;
    internal string Path=>path;
    internal static DocumentFormatException Bad(string path,string message)=>new(path.Length==0?"$":path,message);
    internal static JsonElement Parse(string text,int bytes)
    {
        try
        {
            var utf8=Utf8;
            if(text.Length>bytes||utf8.GetByteCount(text)>bytes)throw Bad("","UTF-8 byte budget exceeded");
            using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=64});
            Scan(doc.RootElement,"");return doc.RootElement.Clone();
        }
        catch(Exception e)when(e is JsonException or EncoderFallbackException or InvalidOperationException)
        {throw Bad("","invalid JSON or Unicode: "+e.Message);}
    }
    private static void Scan(JsonElement e,string at)
    {
        switch(e.ValueKind)
        {
            case JsonValueKind.Object:
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var p in e.EnumerateObject())
                {CheckUnicode(p.Name,at);if(!seen.Add(p.Name))throw Bad(at,"duplicate member "+p.Name);Scan(p.Value,at+"/"+Escape(p.Name));}break;
            case JsonValueKind.Array:var i=0;foreach(var a in e.EnumerateArray())Scan(a,at+"/"+i++);break;
            case JsonValueKind.String:CheckUnicode(e.GetString()!,at);break;
            case JsonValueKind.Number:if(!e.TryGetDouble(out var d)||!double.IsFinite(d))throw Bad(at,"expected a finite IEEE754 double");break;
        }
    }
    private static void CheckUnicode(string text,string at)
    {
        try{_=Utf8.GetByteCount(text);}
        catch(EncoderFallbackException){throw Bad(at,"invalid Unicode");}
        if(text.EnumerateRunes().Count()>65536)throw Bad(at,"string exceeds 65536 characters");
    }
    internal static string Escape(string text)=>text.Replace("~","~0").Replace("/","~1");
    internal void Only(params string[] keys)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"expected an object");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
        {if(!seen.Add(property.Name))throw Bad(path+"/"+Escape(property.Name),"duplicate field");if(!keys.Contains(property.Name,StringComparer.Ordinal))throw Bad(path+"/"+Escape(property.Name),"unknown field");}
    }
    internal bool Has(string name)=>value.TryGetProperty(name,out _);
    internal Scene2Read Get(string name)=>value.TryGetProperty(name,out var v)?new(v,path+"/"+Escape(name)):throw Bad(path+"/"+Escape(name),"required field missing");
    internal string Text(int min=0,int max=65536)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"expected a string");
        var text=value.GetString()!;var count=text.EnumerateRunes().Count();if(count<min||count>max)throw Bad(path,$"expected {min}..{max} characters");return text;
    }
    internal bool Boolean()=>value.ValueKind is JsonValueKind.True or JsonValueKind.False?value.GetBoolean():throw Bad(path,"expected a boolean");
    internal int Integer()
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var n)||!double.IsFinite(n)||n!=Math.Truncate(n)||n<int.MinValue||n>int.MaxValue)throw Bad(path,"expected an int32");
        return (int)n;
    }
    internal ImmutableArray<Scene2Read> Array(int max,bool requiredNonempty=false)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>max||requiredNonempty&&value.GetArrayLength()==0)throw Bad(path,$"expected an array of {(requiredNonempty?1:0)}..{max} items");
        var i=0;return value.EnumerateArray().Select(e=>new Scene2Read(e,path+"/"+i++)).ToImmutableArray();
    }
    internal ImmutableSortedDictionary<string,T> Map<T>(int max,Func<string,string,string> key,Func<Scene2Read,T> item)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"expected an object map");
        var result=ImmutableSortedDictionary.CreateBuilder<string,T>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())
        {if(result.Count>=max)throw Bad(path,"map budget exceeded");var at=path+"/"+Escape(p.Name);result.Add(key(p.Name,at),item(new(p.Value,at)));}return result.ToImmutable();
    }
    internal float[] Floats(int count)
    {
        var items=Array(count);if(items.Length!=count)throw Bad(path,$"expected exactly {count} components");var values=new float[count];
        for(var i=0;i<count;i++)if(items[i].Value.ValueKind!=JsonValueKind.Number||!items[i].Value.TryGetSingle(out values[i])||!float.IsFinite(values[i]))throw Bad(items[i].Path,"expected a finite float32");
        return values;
    }
    internal Vector3 Vec3(){var a=Floats(3);return new(a[0],a[1],a[2]);}
    internal Vector2 Vec2(){var a=Floats(2);return new(a[0],a[1]);}
    internal Quaternion Quat(){var a=Floats(4);return new(a[0],a[1],a[2],a[3]);}
    internal static string Id(string name,string at)
    {
        if(name.Length is <1 or >128||name[0] is <'a' or >'z'||name.Any(c=>c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))throw Bad(at,"expected lowercase readable scene id (max128)");return name;
    }
    internal static string Key(string name,string at)
    {if(name.Length!=32||name.Any(c=>c is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')))throw Bad(at,"expected 32 lowercase hexadecimal characters");return name;}
    internal static string Name(string name,string at)
    {try{_=Utf8.GetByteCount(name);}catch(EncoderFallbackException){throw Bad(at,"invalid Unicode name");}if(name.EnumerateRunes().Count() is <1 or >128)throw Bad(at,"expected a name of 1..128 characters");return name;}
    internal string Id()=>Id(Text(),path);
    internal string Key()=>Key(Text(),path);
    internal JsonElement Scalar()
    {
        if(value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))throw Bad(path,"expected scalar text, finite number or boolean");
        if(value.ValueKind==JsonValueKind.Number&&(!value.TryGetDouble(out var n)||!double.IsFinite(n)))throw Bad(path,"expected finite number");
        return value.Clone();
    }
    internal SceneSpace Space()=>Text() switch{"3d"=>SceneSpace.ThreeD,"2d-xy"=>SceneSpace.TwoDXY,_=>throw Bad(path,"unsupported coordinate space")};
    internal SceneSprite Sprite()
    {
        Only("size","pivot","order","filter");var size=Get("size").Vec2();var pivot=Get("pivot").Vec2();
        if(size.X<=0||size.Y<=0)throw Bad(path+"/size","sprite size must remain positive in float32");
        if(pivot.X is <0 or >1||pivot.Y is <0 or >1)throw Bad(path+"/pivot","pivot must lie in 0..1");
        var filter=Get("filter").Text() switch{"nearest"=>SceneSpriteFilter.Nearest,"linear"=>SceneSpriteFilter.Linear,_=>throw Bad(path+"/filter","unknown sprite filter")};
        return new(size,pivot,Get("order").Integer(),filter);
    }
    internal SceneObject Content(string id,string? parent)
    {
        var props=Has("properties")?Get("properties").Map(1024,Name,x=>x.Scalar()):ImmutableSortedDictionary.Create<string,JsonElement>(StringComparer.Ordinal);
        var refs=Has("references")?Get("references").Map(1024,Name,x=>x.Id()):ImmutableSortedDictionary.Create<string,string>(StringComparer.Ordinal);
        return new(id,Has("asset")?Get("asset").Text(1,2048):null,Has("position")?Get("position").Vec3():Vector3.Zero,
            Has("rotation")?Get("rotation").Quat():Quaternion.Identity,Has("scale")?Get("scale").Vec3():Vector3.One,parent,props,refs);
    }
    internal Scene2Object Object(bool placement=false)
    {
        if(placement)Only("id","position","rotation","scale","parent","label");
        else Only("id","position","rotation","scale","parent","asset","properties","references","sprite","body");
        var content=Content(Get("id").Id(),Has("parent")?Get("parent").Id():null);
        if(placement&&Has("label"))content=content with{Properties=content.Properties.SetItem("name",JsonSerializer.SerializeToElement(Get("label").Text(1,65536)))};
        return new(content,Has("sprite")?Get("sprite").Sprite():null){Body=Has("body")?ScenePhysicsRules.ReadBody(Get("body")):null};
    }
    internal PrefabDefinition Definition()
    {
        Only("kind","version","guid","space","root","nodes","slots");
        if(Get("kind").Text()!="prefab-definition"||Get("version").Integer()!=1)throw Bad(path,"expected prefab-definition version1");
        var nodes=Get("nodes").Array(10000,true).Select(n=>
        {
            n.Only("key","label","parentKey","asset","position","rotation","scale","properties","references","sprite","body");
            var refs=n.Has("references")?n.Get("references").Map(1024,Name,r=>
            {r.Only("node","slot");if(r.Has("node")==r.Has("slot"))throw Bad(r.Path,"expected exactly one node or slot target");return new PrefabReference(r.Has("node")?r.Get("node").Key():null,r.Has("slot")?r.Get("slot").Key():null);}):ImmutableSortedDictionary.Create<string,PrefabReference>(StringComparer.Ordinal);
            var props=n.Has("properties")?n.Get("properties").Map(1024,Name,r=>r.Scalar()):ImmutableSortedDictionary.Create<string,JsonElement>(StringComparer.Ordinal);
            return new PrefabNode(n.Get("key").Key(),n.Get("label").Text(1,256),n.Has("parentKey")?n.Get("parentKey").Key():null,
                n.Has("asset")?n.Get("asset").Text(1,2048):null,n.Has("position")?n.Get("position").Vec3():Vector3.Zero,n.Has("rotation")?n.Get("rotation").Quat():Quaternion.Identity,
                n.Has("scale")?n.Get("scale").Vec3():Vector3.One,props,refs,n.Has("sprite")?n.Get("sprite").Sprite():null){Body=n.Has("body")?ScenePhysicsRules.ReadBody(n.Get("body")):null};
        }).ToImmutableArray();
        var slots=Get("slots").Array(256).Select(r=>{r.Only("key","label","required");return new PrefabSlot(r.Get("key").Key(),r.Get("label").Text(1,256),r.Get("required").Boolean());}).ToImmutableArray();
        return new(Get("guid").Key(),Has("space")?Get("space").Space():SceneSpace.ThreeD,Get("root").Key(),nodes,slots,value);
    }
    private PrefabOverride Override()
    {
        Only("node","field","op","value");var op=Get("op").Text() switch{"set"=>PrefabOverrideOperation.Set,"unset"=>PrefabOverrideOperation.Unset,_=>throw Bad(path+"/op","expected set or unset")};
        if(Has("value")!=(op==PrefabOverrideOperation.Set))throw Bad(path,"set requires value; unset must omit value");
        return new(Get("node").Key(),Get("field").Text(1,512),op,Has("value")?Get("value").Value.Clone():null);
    }
    internal Scene2 Scene()
    {
        Only("kind","version","space","objects","bases","instances","objectOrder","physics");
        if(Get("kind").Text()!="scene"||Get("version").Integer()!=2)throw Bad(path,"expected scene version2");
        var objects=Get("objects").Array(50000).Select(r=>r.Object()).ToImmutableArray();
        var bases=Has("bases")?Get("bases").Array(512).Select(r=>{r.Only("id","revision","definition");return new PrefabBase(r.Get("id").Id(),r.Get("revision").Text(71,71),r.Get("definition").Definition());}).ToImmutableArray():[];
        var instances=Has("instances")?Get("instances").Array(10000).Select(r=>
        {
            r.Only("key","source","base","placement","nodeIds","bindings","overrides");
            return new PrefabInstance(r.Get("key").Key(),r.Get("source").Key(),r.Get("base").Id(),r.Get("placement").Object(true).Object,
                r.Get("nodeIds").Map(10000,Key,x=>x.Id()),r.Get("bindings").Map(256,Key,x=>x.Id()),r.Get("overrides").Array(10000).Select(x=>x.Override()).ToImmutableArray());
        }).ToImmutableArray():[];
        return new(Has("space")?Get("space").Space():SceneSpace.ThreeD,objects,bases,instances,Has("objectOrder")?Get("objectOrder").Array(50000).Select(r=>r.Id()).ToImmutableArray():null){Physics=Has("physics")?ScenePhysicsRules.ReadPhysics(Get("physics")):null};
    }
}

// Version selection is internal until the single-authority host integration card. No v1 reader changes.
internal static class SceneVersionDispatch
{
    internal static object Read(string json)
    {
        try
        {
            // Peek without applying any v2 byte/count policy to a v1/custom-v1 document.
            var bytes=Scene2Read.Utf8.GetBytes(json);var reader=new Utf8JsonReader(bytes,new JsonReaderOptions{MaxDepth=64});
            string? kind=null;int? version=null;
            while((kind is null||version is null)&&reader.Read())
            {
                if(reader.TokenType!=JsonTokenType.PropertyName||reader.CurrentDepth!=1)continue;
                var name=reader.GetString();if(!reader.Read())throw Scene2Read.Bad("","missing header value");
                if(name=="kind")kind=reader.TokenType==JsonTokenType.String?reader.GetString():throw Scene2Read.Bad("/kind","expected a string");
                else if(name=="version")
                {
                    if(reader.TokenType!=JsonTokenType.Number||!reader.TryGetDouble(out var n)||n!=Math.Truncate(n)||n<int.MinValue||n>int.MaxValue)throw Scene2Read.Bad("/version","expected integer version");
                    version=(int)n;
                }
                else reader.Skip();
            }
            return (kind,version) switch
            {({},1)=>new SceneFormat(kind).Read(json),("scene",2)=>new Scene2Format().Read(json),_=>throw Scene2Read.Bad("/version","unsupported kind/version")};
        }
        catch(Exception error)when(error is JsonException or EncoderFallbackException or InvalidOperationException)
        {throw Scene2Read.Bad("","invalid scene header: "+error.Message);}
    }
}
