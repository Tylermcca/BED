namespace Bed.Editor;

/// <summary>One game-declared number. Both bounds enable trying it; no bounds makes it read-only.
/// SaveField is an optional existing scene-v2 object field for checked Keep, never a file path.</summary>
public sealed class LiveValue
{
    /// <summary>Copies bounded labels and a finite, ordered trial range.</summary>
    public LiveValue(string id,string label,string unit="",float? minimum=null,float? maximum=null,string? saveField=null,string? trialLabel=null)
    {
        Id=Text(id,64);Label=Text(label,128);Unit=Text(unit,32,true);
        if(minimum.HasValue!=maximum.HasValue||minimum is {} min&&(!float.IsFinite(min)||!float.IsFinite(maximum!.Value)||min>=maximum.Value))throw new ArgumentException("Set a minimum and maximum number, with the minimum first.");
        if(saveField is not null&&(!minimum.HasValue||saveField.Length>256||saveField.Any(char.IsControl)||!(saveField.StartsWith("/properties/",StringComparison.Ordinal)||saveField.StartsWith("/body/",StringComparison.Ordinal))))throw new ArgumentException("Choose an existing saved object setting for Keep this value.");
        Minimum=minimum;Maximum=maximum;SaveField=saveField;TrialLabel=trialLabel is null?null:Text(trialLabel,128);
    }
    internal static string Text(string text,int max,bool empty=false)
    {ArgumentNullException.ThrowIfNull(text);if((!empty&&text.Length==0)||text.Length>max||text.Any(char.IsControl))throw new ArgumentException("Use a short name with no line breaks or control characters.");return text;}
    /// <summary>Stable game field key.</summary>
    public string Id{get;}
    /// <summary>Person-facing label.</summary>
    public string Label{get;}
    /// <summary>Units shown beside the number.</summary>
    public string Unit{get;}
    /// <summary>Inclusive minimum, or absent for read-only.</summary>
    public float? Minimum{get;}
    /// <summary>Inclusive maximum, or absent for read-only.</summary>
    public float? Maximum{get;}
    /// <summary>Optional scene object field used only by an explicit checked Keep.</summary>
    public string? SaveField{get;}
    /// <summary>Optional exact trial-control wording.</summary>
    public string? TrialLabel{get;}
    /// <summary>Whether this value accepts a bounded trial.</summary>
    public bool CanTry=>Minimum.HasValue;
    /// <summary>Whether a finite value is inside the declared trial range.</summary>
    public bool Accepts(float value)=>CanTry&&float.IsFinite(value)&&value>=Minimum&&value<=Maximum;
}
/// <summary>An immutable adapter opt-in for one named object in an owned Play process.
/// Runtime replies use Values order; optional game inputs are literal identifiers, never desktop keys.</summary>
public sealed class LiveValueTarget
{
    /// <summary>Copies at most sixteen unique values and eight unique input names. Construction starts nothing.</summary>
    public LiveValueTarget(string objectId,string title,IEnumerable<LiveValue> values,IEnumerable<string>? inputs=null)
    {
        ArgumentNullException.ThrowIfNull(values);ObjectId=LiveValue.Text(objectId,256);Title=LiveValue.Text(title,128);
        var owned=values.Take(17).ToArray();var keys=(inputs??[]).Take(9).Select(s=>LiveValue.Text(s,64)).ToArray();
        if(owned.Length is <1 or >16||owned.Any(v=>v is null)||owned.Select(v=>v.Id).Distinct(StringComparer.Ordinal).Count()!=owned.Length||keys.Length>8||keys.Distinct(StringComparer.Ordinal).Count()!=keys.Length)throw new ArgumentException("Give each live object its own short name and a small list of values.");
        Values=Array.AsReadOnly(owned);Inputs=Array.AsReadOnly(keys);
    }
    /// <summary>Exact game object identity, also supplied to the owned child.</summary>
    public string ObjectId{get;}
    /// <summary>Person-facing object name.</summary>
    public string Title{get;}
    /// <summary>Readout/trial declarations in runtime reply order.</summary>
    public IReadOnlyList<LiveValue> Values{get;}
    /// <summary>Optional short game inputs, each tried for a quarter simulation second.</summary>
    public IReadOnlyList<string> Inputs{get;}
}
internal sealed record LiveValuesSnapshot(long Frame,IReadOnlyList<float> Numbers);
