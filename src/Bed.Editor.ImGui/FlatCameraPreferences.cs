using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Bed.Editor.ImGui;

// Personal presentation only. ImGui cannot depend on the App-owned docking/INI store.
internal sealed class FlatCameraPreferences
{
    internal string FileName{get;}
    internal string Note{get;private set;}="";
    private (Vector2 Centre,float Zoom) remembered=(Vector2.Zero,64);
    internal FlatCameraPreferences(string project,string level,string preferences)
    {
        var path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));if(OperatingSystem.IsWindows()){path=path.ToUpperInvariant();level=level.ToUpperInvariant();}
        static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        FileName=Path.Combine(Path.GetFullPath(preferences),"flat-views",Hash(path),Hash(level)+".json");
    }
    internal (Vector2 Centre,float Zoom) Read()
    {
        remembered=(Vector2.Zero,64);Note="";
        try
        {
            if(!File.Exists(FileName))return remembered;
            using var input=new FileStream(FileName,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(input.Length is <1 or >4096)throw new JsonException("Camera preferences exceed 4 KiB or are empty.");
            var bytes=new byte[(int)input.Length];input.ReadExactly(bytes);
            using var json=JsonDocument.Parse(bytes,new(){MaxDepth=4});var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw new JsonException("Camera preferences need an object.");
            var fields=root.EnumerateObject().ToArray();if(fields.Length!=3||fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=3||fields.Any(p=>p.Name is not("Version" or "Centre" or "PixelsPerMetre")))throw new JsonException("Camera preference fields changed.");
            var version=root.GetProperty("Version"u8);var xy=root.GetProperty("Centre"u8);var zoom=root.GetProperty("PixelsPerMetre"u8);
            if(version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var v)||v!=1||xy.ValueKind!=JsonValueKind.Array||xy.GetArrayLength()!=2||xy[0].ValueKind!=JsonValueKind.Number||xy[1].ValueKind!=JsonValueKind.Number||zoom.ValueKind!=JsonValueKind.Number)throw new JsonException("Invalid camera preference shape.");
            var centre=new Vector2(xy[0].GetSingle(),xy[1].GetSingle());var pixels=zoom.GetSingle();if(!Valid(centre,pixels))throw new JsonException("Invalid camera preference values.");
            return remembered=(centre,pixels);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or FormatException or OverflowException or ArgumentException)
        {Note="Could not read the saved flat camera; using the default view. "+e.Message;return remembered;}
    }
    private static bool Valid(Vector2 centre,float zoom)=>float.IsFinite(centre.X)&&float.IsFinite(centre.Y)&&float.IsFinite(zoom)&&zoom is >=2 and <=2048;
    internal void Remember(Vector2 centre,float zoom)
    {
        if(remembered==(centre,zoom))return;
        // A failed save is reported once per changed camera, never retried every draw frame.
        remembered=(centre,zoom);
        string? temporary=null;
        try
        {
            if(!Valid(centre,zoom))throw new ArgumentException("The flat camera values are outside their finite bounds.");
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Centre=new[]{centre.X,centre.Y},PixelsPerMetre=zoom});
            if(bytes.Length>4096)throw new IOException("Camera preferences exceed 4 KiB.");
            Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);temporary=FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes);output.Flush(true);}
            File.Move(temporary,FileName,true);temporary=null;remembered=(centre,zoom);Note="";
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException)
        {Note="Could not remember this flat camera for next time. "+e.Message;}
        finally{if(temporary is not null)try{File.Delete(temporary);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
    }
}
