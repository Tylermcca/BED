using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Bed;

/// <summary>The person's display-name style for copies. Object identities are allocated separately.</summary>
public enum CopyNameStyle
{
    /// <summary>Cube (1).</summary>
    Parentheses,
    /// <summary>Cube.001.</summary>
    Dot,
    /// <summary>Cube_1.</summary>
    Underscore,
    /// <summary>Cube2.</summary>
    Number,
    /// <summary>Cube copy.</summary>
    Copy,
}

/// <summary>Shared copy display names for windows, agents and command-line hosts. Never allocates identities.</summary>
public static class CopyNames
{
    private static readonly Regex Suffix=new(@"^(.*?)(?: \(([1-9][0-9]*)\)|\.([0-9]+)|_([1-9][0-9]*)| copy(?: ([1-9][0-9]*))?|([0-9]+))$",RegexOptions.CultureInvariant);
    /// <summary>The current personal choice, initially read from BED's standard personal settings.</summary>
    public static CopyNameStyle Style { get; set; }=ReadStyle(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"BED","settings.json"));
    /// <summary>Reads the saved choice without changing settings; old, missing or invalid choices use parentheses.</summary>
    public static CopyNameStyle ReadStyle(string settingsFile)
    {
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(settingsFile));
            if(json.RootElement.ValueKind==JsonValueKind.Object&&json.RootElement.TryGetProperty("copyNames",out var value)&&value.ValueKind==JsonValueKind.String
                &&Enum.TryParse<CopyNameStyle>(value.GetString(),out var style)&&Enum.IsDefined(style))return style;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){}
        return CopyNameStyle.Parentheses;
    }
    /// <summary>The example used to choose a style.</summary>
    public static string Example(CopyNameStyle style)=>Format("Cube",style==CopyNameStyle.Number?2:1,style);
    /// <summary>The next unused copy name, recognising existing suffixes in every supported style.</summary>
    public static string Next(string name,IEnumerable<string> existing,CopyNameStyle? style=null)
    {
        static (string Root,int Number) Split(string text)
        {
            var match=Suffix.Match(text);if(!match.Success||match.Groups[1].Value.Length==0)return(text,0);
            var number=1;
            foreach(Group group in match.Groups.Cast<Group>().Skip(2))if(group.Success&&int.TryParse(group.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed))number=parsed;
            return(match.Groups[1].Value,number);
        }
        var root=name;
        while(Split(root) is var split&&split.Root!=root)root=split.Root;
        var taken=new HashSet<int>();var names=existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var other in names)
        {var split=Split(other);if(string.Equals(split.Root,root,StringComparison.OrdinalIgnoreCase)&&split.Number>0)taken.Add(split.Number);}
        var choice=style??Style;
        for(var n=choice==CopyNameStyle.Number?2:1;n<int.MaxValue;n++)
        {var candidate=Format(root,n,choice);if(!taken.Contains(n)&&!names.Contains(candidate))return candidate;}
        throw new InvalidOperationException("There are too many copies with this name.");
    }
    private static string Format(string root,int number,CopyNameStyle style)=>style switch
    {
        CopyNameStyle.Dot=>$"{root}.{number.ToString("D3",CultureInfo.InvariantCulture)}",
        CopyNameStyle.Underscore=>$"{root}_{number.ToString(CultureInfo.InvariantCulture)}",
        CopyNameStyle.Number=>$"{root}{number.ToString(CultureInfo.InvariantCulture)}",
        CopyNameStyle.Copy=>number==1?$"{root} copy":$"{root} copy {number.ToString(CultureInfo.InvariantCulture)}",
        _=>$"{root} ({number.ToString(CultureInfo.InvariantCulture)})",
    };
    /// <summary>The object's display name, falling back to its identity when no name is set.</summary>
    public static string NameOf(SceneObject item)=>item.Properties.TryGetValue("name",out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString() is {Length:>0} text?text:item.Id;
    /// <summary>The effective scene-v2 label, including an inherited prefab label when no local name is set.</summary>
    public static string NameOf(Scene2 scene,SceneObject item)
    {
        if(item.Properties.TryGetValue("name",out var name)&&name.ValueKind==JsonValueKind.String&&name.GetString() is {Length:>0})return NameOf(item);
        foreach(var instance in scene.Instances)
            foreach(var node in instance.NodeIds)
                if(node.Value==item.Id)return scene.Bases.Single(b=>b.Id==instance.Base).Definition.Nodes.Single(n=>n.Key==node.Key).Label;
        return item.Id;
    }

}
