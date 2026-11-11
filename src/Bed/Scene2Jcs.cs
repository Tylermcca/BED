using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Bed;

// BORROWED contract: RFC8785 §§3.2.2–3.2.4 and Appendix B; original implementation.
// Kept separate from v1's human-readable Canonical writer. No external process/package.
internal static class Scene2Jcs
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static byte[] Canonicalize(string json,int maxBytes=32*1024*1024)
    {
        try
        {
            if(maxBytes<1||Utf8.GetByteCount(json)>maxBytes)throw new DocumentFormatException("$","JSON byte budget exceeded");
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});
            return Canonicalize(document.RootElement,maxBytes);
        }
        catch(Exception error)when(error is JsonException or EncoderFallbackException or InvalidOperationException)
        {throw new DocumentFormatException("$","invalid canonical JSON: "+error.Message);}
    }
    internal static byte[] Canonicalize(JsonElement value,int maxBytes=32*1024*1024)
    {
        try
        {
            if(maxBytes<1)throw new DocumentFormatException("$","JSON byte budget exceeded");
            var output=new Output(maxBytes);Write(value,output,"$",0);return Utf8.GetBytes(output.Text.ToString());
        }
        catch(Exception error)when(error is EncoderFallbackException or InvalidOperationException)
        {throw new DocumentFormatException("$","invalid canonical JSON: "+error.Message);}
    }
    private sealed class Output(int maxBytes)
    {
        internal readonly StringBuilder Text=new();private int count;
        internal void Add(string text)
        {
            var added=Utf8.GetByteCount(text);
            if(added>maxBytes-count)throw new DocumentFormatException("$","canonical JSON byte budget exceeded");
            count+=added;Text.Append(text);
        }
    }
    private static void Write(JsonElement value,Output output,string path,int depth)
    {
        if(depth>64)throw new DocumentFormatException(path,"JSON depth budget exceeded");
        switch(value.ValueKind)
        {
            case JsonValueKind.Object:
                var members=value.EnumerateObject().ToArray();var names=new HashSet<string>(StringComparer.Ordinal);
                foreach(var member in members)if(!names.Add(member.Name))throw new DocumentFormatException(path,"duplicate member "+member.Name);
                output.Add("{");var first=true;
                foreach(var member in members.OrderBy(p=>p.Name,StringComparer.Ordinal))
                {if(!first)output.Add(",");first=false;String(member.Name,output);output.Add(":");Write(member.Value,output,path+"/"+member.Name.Replace("~","~0").Replace("/","~1"),depth+1);}
                output.Add("}");break;
            case JsonValueKind.Array:
                output.Add("[");var index=0;
                foreach(var item in value.EnumerateArray()){if(index>0)output.Add(",");Write(item,output,path+"/"+index++,depth+1);}output.Add("]");break;
            case JsonValueKind.String:String(value.GetString()!,output);break;
            case JsonValueKind.Number:
                if(!value.TryGetDouble(out var number)||!double.IsFinite(number))throw new DocumentFormatException(path,"expected a finite IEEE754 double");
                output.Add(Number(number));break;
            case JsonValueKind.True:output.Add("true");break;
            case JsonValueKind.False:output.Add("false");break;
            case JsonValueKind.Null:output.Add("null");break;
            default:throw new DocumentFormatException(path,"undefined JSON value");
        }
    }
    private static void String(string value,Output output)
    {
        _=Utf8.GetByteCount(value); // Refuse lone surrogates instead of replacing them.
        output.Add("\"");
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            output.Add(c switch
            {
                '"'=>"\\\"",'\\'=>"\\\\",'\b'=>"\\b",'\t'=>"\\t",'\n'=>"\\n",'\f'=>"\\f",'\r'=>"\\r",
                <' '=>"\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture),
                _ when char.IsHighSurrogate(c)=>new string([c,value[++i]]),
                _=>c.ToString()
            });
        }
        output.Add("\"");
    }
    internal static string Number(double value)
    {
        if(!double.IsFinite(value))throw new DocumentFormatException("$","expected a finite IEEE754 double");
        if(value==0)return "0";
        var sign=value<0?"-":"";var text=Math.Abs(value).ToString("R",CultureInfo.InvariantCulture);
        var e=text.IndexOf('E');var exponent=e<0?0:int.Parse(text[(e+1)..],CultureInfo.InvariantCulture);
        var mantissa=e<0?text:text[..e];var point=mantissa.IndexOf('.');if(point<0)point=mantissa.Length;
        var digits=mantissa.Replace(".","");var leading=0;while(digits[leading]=='0')leading++;
        var n=point+exponent-leading;digits=digits[leading..].TrimEnd('0');var k=digits.Length;
        if(n>0&&n<=21)return sign+(k<=n?digits+new string('0',n-k):digits[..n]+"."+digits[n..]);
        if(n<=0&&n>-6)return sign+"0."+new string('0',-n)+digits;
        var power=n-1;
        return sign+digits[0]+(k>1?"."+digits[1..]:"")+"e"+(power>=0?"+":"")+power.ToString(CultureInfo.InvariantCulture);
    }
}
