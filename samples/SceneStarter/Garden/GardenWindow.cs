using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace SceneStarter.Garden;

/// <summary>The game window: a plain Windows window that shows <see cref="GardenRenderer"/>'s pixels. It uses only
/// what Windows itself provides (user32/gdi32), so the game's runtime files stay the same small pure set that
/// export checks, and the exported copy plays too. Keys: arrows or A/D move, Space/Up/W jump, R restarts, Esc quits.</summary>
[SupportedOSPlatform("windows")]
public sealed class GardenWindow
{
    private const uint WsOverlappedWindow=0x00CF0000,WmDestroy=0x0002,WmSize=0x0005,WmKillFocus=0x0008,WmPaint=0x000F,
        WmEraseBackground=0x0014,WmKeyDown=0x0100,WmKeyUp=0x0101,WmSysKeyDown=0x0104,WmSysKeyUp=0x0105,WmQuit=0x0012;
    private static GardenWindow? current;
    private static readonly WndProc Procedure=Dispatch;   // kept alive for the native window class
    private readonly GardenGame game;
    private readonly GardenRenderer renderer;
    private readonly HashSet<int> held=[];
    private IntPtr window;

    private GardenWindow(GardenGame game,int width,int height){this.game=game;renderer=new GardenRenderer(game.Level,width,height);}

    /// <summary>Opens the window and plays until it is closed or Esc is pressed. With <paramref name="visible"/> false
    /// the same window is created hidden and runs <paramref name="frames"/> steps of <paramref name="script"/>; that is
    /// only a check that the native calls work, and never takes focus.</summary>
    public static int Play(GardenGame game,bool visible=true,GardenInput[]? script=null,int frames=0)
    {
        if(visible)try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        var screenW=GetSystemMetrics(0);var screenH=GetSystemMetrics(1);
        if(screenW<=0||screenH<=0){screenW=1600;screenH=900;}
        var (width,height)=GardenRenderer.Fit(game.Level.Bounds,Math.Min(1600,screenW*85/100),screenH*75/100);
        var host=new GardenWindow(game,width,height);current=host;
        try{return host.Run(visible,script,frames);}
        finally{current=null;}
    }

    private int Run(bool visible,GardenInput[]? script,int frames)
    {
        var instance=GetModuleHandleW(IntPtr.Zero);
        var name="BedFlagGarden"+Environment.ProcessId;
        var type=new WndClassEx{Size=(uint)Marshal.SizeOf<WndClassEx>(),Style=0x0003,Procedure=Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance=instance,Cursor=LoadCursorW(IntPtr.Zero,new IntPtr(32512)),ClassName=name};
        if(RegisterClassExW(ref type)==0)throw new InvalidOperationException("Windows refused the game window class.");
        timeBeginPeriod(1);
        try
        {
            var rect=new Rect{Right=renderer.Width,Bottom=renderer.Height};AdjustWindowRectEx(ref rect,WsOverlappedWindow,false,0);
            window=CreateWindowExW(0,name,"Flag Garden",WsOverlappedWindow,unchecked((int)0x80000000),unchecked((int)0x80000000),
                rect.Right-rect.Left,rect.Bottom-rect.Top,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
            if(window==IntPtr.Zero)throw new InvalidOperationException("Windows could not open the game window.");
            if(visible){ShowWindow(window,5);SetForegroundWindow(window);}
            var clock=Stopwatch.StartNew();double accumulated=0,last=0;var steps=0;
            while(true)
            {
                while(PeekMessageW(out var message,IntPtr.Zero,0,0,1))
                {
                    if(message.Message==WmQuit)return 0;
                    TranslateMessage(ref message);DispatchMessageW(ref message);
                }
                if(window==IntPtr.Zero)return 0;
                if(visible)
                {
                    var now=clock.Elapsed.TotalSeconds;accumulated=Math.Min(accumulated+now-last,GardenGame.Step*5);last=now;
                    while(accumulated>=GardenGame.Step){game.Update(Keys());accumulated-=GardenGame.Step;}
                }
                else
                {
                    if(steps>=frames){DestroyWindow(window);continue;}
                    game.Update(script is not null&&steps<script.Length?script[steps]:GardenInput.None);steps++;
                }
                renderer.Render(game);Present();
                if(visible&&GardenGame.Step-accumulated>0.002)Thread.Sleep(1);
            }
        }
        finally
        {
            if(window!=IntPtr.Zero)DestroyWindow(window);
            timeEndPeriod(1);UnregisterClassW(name,instance);
        }
    }

    private GardenInput Keys()=>new(held.Contains(0x25)||held.Contains('A'),held.Contains(0x27)||held.Contains('D'),
        held.Contains(0x20)||held.Contains(0x26)||held.Contains('W'),held.Contains('R'));

    private void Present()
    {
        var dc=GetDC(window);if(dc==IntPtr.Zero)return;
        try
        {
            var header=new BitmapInfoHeader{Size=(uint)Marshal.SizeOf<BitmapInfoHeader>(),Width=renderer.Width,Height=-renderer.Height,Planes=1,BitCount=32};
            StretchDIBits(dc,0,0,renderer.Width,renderer.Height,0,0,renderer.Width,renderer.Height,renderer.Pixels,ref header,0,0x00CC0020);
        }
        finally{ReleaseDC(window,dc);}
    }

    private static IntPtr Dispatch(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam)
    {
        var self=current;
        if(self is not null)
            switch(message)
            {
                case WmKeyDown or WmSysKeyDown:
                    var key=(int)wParam;
                    if(key==0x1B){DestroyWindow(hwnd);return IntPtr.Zero;}
                    self.held.Add(key);if(message==WmKeyDown)return IntPtr.Zero;break;   // system keys still reach Windows (Alt+F4)
                case WmKeyUp or WmSysKeyUp:self.held.Remove((int)wParam);if(message==WmKeyUp)return IntPtr.Zero;break;
                case WmKillFocus:self.held.Clear();break;
                case WmSize:
                    var size=(long)lParam;int w=(int)(size&0xFFFF),h=(int)((size>>16)&0xFFFF);
                    if(w>0&&h>0){self.renderer.Resize(w,h);self.renderer.Render(self.game);}
                    return IntPtr.Zero;
                case WmPaint:self.Present();ValidateRect(hwnd,IntPtr.Zero);return IntPtr.Zero;
                case WmEraseBackground:return new IntPtr(1);
                case WmDestroy:if(self.window==hwnd)self.window=IntPtr.Zero;PostQuitMessage(0);return IntPtr.Zero;
            }
        return DefWindowProcW(hwnd,message,wParam,lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size,Style;public IntPtr Procedure;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;
        public string? MenuName;public string ClassName;public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeMessage{public IntPtr Window;public uint Message;public IntPtr WParam,LParam;public uint Time;public int X,Y;public uint Private;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfoHeader
    {public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPelsPerMeter,YPelsPerMeter;public uint ClrUsed,ClrImportant;}

    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassExW(ref WndClassEx type);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClassW(string name,IntPtr instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWindowExW(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool AdjustWindowRectEx(ref Rect rect,uint style,bool menu,uint exStyle);
    [DllImport("user32.dll")]private static extern IntPtr DefWindowProcW(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern bool PeekMessageW(out NativeMessage message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")]private static extern IntPtr DispatchMessageW(ref NativeMessage message);
    [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]private static extern bool ValidateRect(IntPtr window,IntPtr rect);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("user32.dll")]private static extern IntPtr LoadCursorW(IntPtr instance,IntPtr name);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll")]private static extern IntPtr GetModuleHandleW(IntPtr name);
    [DllImport("gdi32.dll")]private static extern int StretchDIBits(IntPtr dc,int x,int y,int width,int height,int sourceX,int sourceY,int sourceWidth,int sourceHeight,uint[] bits,ref BitmapInfoHeader info,uint usage,uint operation);
    [DllImport("winmm.dll")]private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]private static extern uint timeEndPeriod(uint period);
}
