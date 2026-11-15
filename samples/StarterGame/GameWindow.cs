using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StarterGame;

// MIXED: the existing sample's small Windows pixel-window pattern, with this game's own Gum input and drawing.
[SupportedOSPlatform("windows")]
public sealed class GameWindow
{
    private static GameWindow? current;
    private static readonly WindowProc Procedure=Dispatch;
    private readonly Game game;
    private readonly GameScreen screen;
    private IntPtr window;
    private GameWindow(Game game,GameScreen screen){this.game=game;this.screen=screen;}
    public static void Play(Game game,GameScreen screen,string settings)
    {
        var host=new GameWindow(game,screen);current=host;
        var instance=GetModuleHandleW(null);var name="BedStarterGame"+Guid.NewGuid().ToString("N");
        var kind=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Procedure=Marshal.GetFunctionPointerForDelegate(Procedure),Instance=instance,Name=name,Cursor=LoadCursorW(IntPtr.Zero,new IntPtr(32512))};
        if(RegisterClassExW(ref kind)==0)throw new InvalidOperationException("Windows refused the game window.");
        try
        {
            var rect=new Rect{Right=800,Bottom=600};AdjustWindowRectEx(ref rect,0x00CF0000,false,0);
            host.window=CreateWindowExW(0,name,game.Title,0x00CF0000,100,100,rect.Right-rect.Left,rect.Bottom-rect.Top,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
            if(host.window==IntPtr.Zero)throw new InvalidOperationException("Windows could not create the game window.");
            ShowWindow(host.window,5);var saved=File.GetLastWriteTimeUtc(settings);
            while(host.window!=IntPtr.Zero)
            {
                while(PeekMessageW(out var message,IntPtr.Zero,0,0,1)){if(message.Id==0x0012)return;TranslateMessage(ref message);DispatchMessageW(ref message);}
                if(host.window==IntPtr.Zero)break;
                var changed=File.GetLastWriteTimeUtc(settings);if(changed!=saved){game.Load(settings);saved=changed;SetWindowTextW(host.window,game.Title);}
                screen.Frame(game);host.Present();Thread.Sleep(16);
            }
        }
        finally{if(host.window!=IntPtr.Zero)DestroyWindow(host.window);current=null;UnregisterClassW(name,instance);}
    }
    private void Present()
    {
        if(window==IntPtr.Zero)return;
        var dc=GetDC(window);if(dc==IntPtr.Zero)return;
        try{GetClientRect(window,out var client);var info=new BitmapInfo{Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=800,Height=-600,Planes=1,BitCount=32};StretchDIBits(dc,0,0,client.Right,client.Bottom,0,0,800,600,screen.Pixels,ref info,0,0x00CC0020);}
        finally{ReleaseDC(window,dc);}
    }
    private static IntPtr Dispatch(IntPtr hwnd,uint message,IntPtr key,IntPtr detail)
    {
        if(current is {} self)switch(message)
        {
            case 0x0100:
                if(((long)detail&(1L<<30))!=0)return IntPtr.Zero;
                if((int)key==27){if(self.game.Playing)self.game.Back();else DestroyWindow(hwnd);}
                else if((int)key==13&&!self.game.Playing)self.game.Start();
                else if((int)key==32)self.game.Collect();else if((int)key=='R'&&self.game.Playing)self.game.Start();
                return IntPtr.Zero;
            case 0x0202:
                GetClientRect(hwnd,out var client);if(client.Right>0&&client.Bottom>0)self.screen.Click(self.game,(short)((long)detail&65535)*800f/client.Right,(short)(((long)detail>>16)&65535)*600f/client.Bottom);return IntPtr.Zero;
            case 0x000F:self.Present();ValidateRect(hwnd,IntPtr.Zero);return IntPtr.Zero;
            case 0x0014:return new IntPtr(1);
            case 0x0002:self.window=IntPtr.Zero;PostQuitMessage(0);return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd,message,key,detail);
    }
    private delegate IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr key,IntPtr detail);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass{public uint Size,Style;public IntPtr Procedure;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string? Menu,Name;public IntPtr SmallIcon;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Message{public IntPtr Window;public uint Id;public IntPtr Key,Detail;public uint Time;public int X,Y;public uint Private;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPixels,YPixels;public uint Used,Important;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern ushort RegisterClassExW(ref WindowClass type);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClassW(string name,IntPtr instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateWindowExW(uint extended,string type,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool AdjustWindowRectEx(ref Rect rect,uint style,bool menu,uint extended);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]private static extern bool PeekMessageW(out Message message,IntPtr window,uint first,uint last,uint remove);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("user32.dll")]private static extern IntPtr DefWindowProcW(IntPtr window,uint message,IntPtr key,IntPtr detail);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("user32.dll")]private static extern bool ValidateRect(IntPtr window,IntPtr rect);
    [DllImport("user32.dll")]private static extern IntPtr LoadCursorW(IntPtr instance,IntPtr name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool SetWindowTextW(IntPtr window,string title);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("gdi32.dll")]private static extern int StretchDIBits(IntPtr dc,int x,int y,int width,int height,int sourceX,int sourceY,int sourceWidth,int sourceHeight,byte[] bits,ref BitmapInfo info,uint usage,uint operation);
}
