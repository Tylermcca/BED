using System.Text;
using System.Text.Json;
namespace Bed.Runtime;

/// <summary>Optional game-owned, no-window live-values loop. All callbacks run on the calling game thread.
/// The game enforces its own field/input ranges; this helper never changes authoring files.</summary>
public static class LiveValuesHost
{
    /// <summary>Object requested by BED, or null for ordinary Play. Reading it starts nothing.</summary>
    public static string? Target
    {
        get {var value=Environment.GetEnvironmentVariable("BED_LIVE_TARGET");if(string.IsNullOrEmpty(value))return null;
            if(value.Length>256||value.Any(char.IsControl))throw new InvalidDataException("The selected live object could not be found.");return value;}
    }
    /// <summary>Answers bounded ordered requests with up to sixteen finite values in adapter declaration order.
    /// advance receives at most .25 simulation seconds; tryValue must refuse unknown fields and out-of-range values.
    /// EOF, stop or a refused request ends this game-owned loop. No window, process or filesystem is opened.</summary>
    public static int Run(string objectId,Func<long> frame,Func<IReadOnlyList<float>> read,Action<string,float> tryValue,
        Action<string?,double> advance,TextReader input,TextWriter output,TextWriter error)
    {
        try
        {
            if(objectId!=Target)throw new InvalidDataException("This game doesn't share live values for that object.");
            void Reply(long id,bool values)
            {
                var at=frame();if(at<0)throw new InvalidDataException("The game returned an out-of-date update number.");
                if(!values)output.WriteLine(JsonSerializer.Serialize(new{id,frame=at,description="Live values for "+objectId,png=(byte[]?)null,error=(string?)null,mode="headless; simulation pauses between requests"}));
                else
                {
                    var numbers=read().Take(17).ToArray();if(numbers.Length is <1 or >16||numbers.Any(n=>!float.IsFinite(n)))throw new InvalidDataException("The game returned live values BED cannot read.");
                    output.WriteLine(JsonSerializer.Serialize(new{id,frame=at,description="Live values for "+objectId,png=(byte[]?)null,error=(string?)null,mode="headless; simulation pauses between requests",values=numbers}));
                }
                output.Flush();
            }
            Reply(0,false);long last=0;
            while(Read(input) is {} line)
            {
                using var json=JsonDocument.Parse(line,new(){MaxDepth=4});var root=json.RootElement;
                var fields=root.EnumerateObject().Select(p=>p.Name).ToArray();
                if(fields.Length!=4||fields.Distinct().Count()!=4||fields.Except(new[]{"id","op","key","seconds"}).Any())throw new InvalidDataException("The live-value request contains missing or extra information.");
                var id=root.GetProperty("id"u8).GetInt64();if(id!=last+1)throw new InvalidDataException("The live-value request is out of date.");last=id;
                var op=root.GetProperty("op"u8).GetString();var key=root.GetProperty("key"u8).ValueKind==JsonValueKind.Null?null:root.GetProperty("key"u8).GetString();var seconds=root.GetProperty("seconds"u8).GetDouble();
                if(key is not null&&(key.Length is 0 or >64||key.Any(char.IsControl))||!double.IsFinite(seconds))throw new InvalidDataException("The requested control or value is not available.");
                if(op=="stop"&&key is null&&seconds==0){Reply(id,false);return 0;}
                if(op=="live"&&seconds>=0&&seconds<=.25)advance(key,seconds);
                else if(op=="tune"&&key is not null&&seconds>=-float.MaxValue&&seconds<=float.MaxValue)tryValue(key,(float)seconds);
                else throw new InvalidDataException("This game does not support that live-value action.");
                Reply(id,true);
            }
            return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine("Live Play stopped: "+e.Message);return 3;}
    }
    private static string? Read(TextReader input)
    {
        var line=new StringBuilder();while(true){var c=input.Read();if(c<0)return line.Length==0?null:throw new InvalidDataException("The live-value request is incomplete.");if(c=='\n')return line.ToString().TrimEnd('\r');if(line.Length>=512)throw new InvalidDataException("Keep a live-value request within 512 characters.");line.Append((char)c);}
    }
}
