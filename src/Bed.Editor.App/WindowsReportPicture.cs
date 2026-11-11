using System.Runtime.InteropServices;
using Bed.Editor.ImGui;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.App;
// Called only from the explicit picture checkbox. Never creates or shows a window.
internal static unsafe class WindowsReportPicture
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Info{internal uint Size;internal int Width,Height;internal ushort Planes,Bits;internal uint Compression,ImageSize;internal int XPels,YPels;internal uint Used,Important,Color;}
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]private static extern nint CreateDIBSection(nint dc,ref Info info,uint use,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint dc);
    internal static byte[]? Capture(ProblemReportContext context)
    {
        if(!OperatingSystem.IsWindows())return null;
        var view=Im.GetWindowViewport();var handle=view.PlatformHandleRaw;
        if(handle==0||!GetClientRect(handle,out var rect))return null;
        var width=rect.Right-rect.Left;var height=rect.Bottom-rect.Top;
        if(width<=0||height<=0||width>8192||height>8192||(long)width*height>16*1024*1024)return null;
        var dc=CreateCompatibleDC(0);if(dc==0)return null;
        nint bitmap=0,previous=0;
        try
        {
            var info=new Info{Size=40,Width=width,Height=-height,Planes=1,Bits=32};bitmap=CreateDIBSection(dc,ref info,0,out var data,0,0);
            if(bitmap==0||data==0)return null;previous=SelectObject(dc,bitmap);
            if(!PrintWindow(handle,dc,3))return null;
            var pixels=new byte[checked(width*height*4)];Marshal.Copy(data,pixels,0,pixels.Length);
            for(var i=0;i<pixels.Length;i+=4){(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);pixels[i+3]=255;}
            var viewport=view.ID==Im.GetMainViewport().ID?0u:view.ID.Value;
            if(!RedactPixels(pixels,width,height,Ui.Snapshot(),viewport,new(view.Pos.X,view.Pos.Y,view.Size.X,view.Size.Y),context))return null;
            return Png.Encode(width,height,MemoryMarshal.Cast<byte,uint>(pixels));
        }
        finally{if(previous!=0)SelectObject(dc,previous);if(bitmap!=0)DeleteObject(bitmap);DeleteDC(dc);}
    }
    // Separated from native capture so privacy coverage runs without creating any HWND.
    internal static bool RedactPixels(byte[] pixels,int width,int height,UiSnapshot snapshot,uint viewport,UiBounds view,ProblemReportContext context)
    {
        if(!float.IsFinite(view.Width)||!float.IsFinite(view.Height)||view.Width<=0||view.Height<=0)return false;
        var sx=width/view.Width;var sy=height/view.Height;
        foreach(var node in snapshot.ForViewport(viewport).Nodes)
        {
            var text=node.Label+"\n"+node.Value;if(ProblemReport.Redact(text,context.Root)==text)continue;
            var b=node.Bounds;
            if(!float.IsFinite(b.X)||!float.IsFinite(b.Y)||!float.IsFinite(b.Width)||!float.IsFinite(b.Height)||b.Width<0||b.Height<0)return false;
            var left=Math.Clamp((int)((b.X-view.X)*sx)-2,0,width);var right=Math.Clamp((int)MathF.Ceiling((b.X+b.Width-view.X)*sx)+2,0,width);
            var top=Math.Clamp((int)((b.Y-view.Y)*sy)-2,0,height);var bottom=Math.Clamp((int)MathF.Ceiling((b.Y+b.Height-view.Y)*sy)+2,0,height);
            for(var y=top;y<bottom;y++)for(var x=left;x<right;x++){var i=(y*width+x)*4;pixels[i]=pixels[i+1]=pixels[i+2]=32;pixels[i+3]=255;}
        }
        return true;
    }
}