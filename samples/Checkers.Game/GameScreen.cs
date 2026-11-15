using Gum.DataTypes;
using Gum.Managers;
using Gum.Wireframe;
using GumRuntime;
using RenderingLibrary;
using SkiaSharp;
using Checkers;
using GumService=Gum.GumService;

namespace CheckersPlay;

// MIXED: StarterGame's owned Gum/Skia surface and Checkers' existing board colours and move controls.
// No editor types, asset preparation or document host enters the player.
public sealed record ScreenPart(string Name,float X,float Y,float Width,float Height,string? Words);
public sealed class GameScreen : IDisposable
{
    public int Width=>720;
    public int Height=>800;
    public float Zoom {get;private set;}=1;
    public byte[] Pixels {get;}=new byte[720*800*4];
    public int Cursor {get;set;}
    public (float X,float Y) Pointer {get;set;}=(-1,-1);
    public bool PointerDown {get;set;}
    public string Problem {get;private set;}="";
    public (float X,float Y,float Cell) Board {get;private set;}
    public int Reloads {get;private set;}
    private const string Credit="BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";
    private const string Help="Arrows select; Enter chooses. N new; C computer; U take back.";
    private readonly bool plain;
    private readonly SKSurface surface=SKSurface.Create(new SKImageInfo(720,800,SKColorType.Bgra8888,SKAlphaType.Premul))!;
    private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    private GraphicalUiElement? shown;
    private GumProjectSave? loaded;
    private string screen="",stamp="";
    private List<ScreenPart> parts=[];
    public GameScreen(bool plain=false){this.plain=plain;}
    public static string? Project(){var path=Path.Combine(AppContext.BaseDirectory,"ui","CheckersUi.gumj");return File.Exists(path)?path:null;}
    public IReadOnlyList<ScreenPart> Parts()=>parts;
    private void Open(string name,string project,string saved,float scale)
    {
        var service=GumService.Default;if(service.IsInitialized)service.Uninitialize();
        service.Initialize(surface.Canvas,Width,name=="Title"?Height-(int)MathF.Ceiling(FooterHeight(scale)):Height,project);service.EnableExpandToWindow(scale);
        loaded=ObjectFinder.Self.GumProjectSave??throw new InvalidDataException("Gum could not load CheckersUi.gumj.");
        shown=loaded.Screens.Single(s=>s.Name==name).ToGraphicalUiElement(SystemManagers.Default,addToManagers:false);shown.AddToRoot();
        screen=name;stamp=saved;Zoom=scale;Reloads++;
    }
    private void Set(string path,object value)
    {var dot=path.LastIndexOf('.');shown?.GetGraphicalUiElementByName(path[..dot].Split('.'))?.SetProperty(path[(dot+1)..],value);}
    private static string? Words(GraphicalUiElement part)
    {
        if(part.RenderableComponent is RenderingLibrary.Graphics.IText {RawText:{Length:>0} words})return words;
        foreach(var child in part.Children.OfType<GraphicalUiElement>())if(Words(child) is {} found)return found;
        return null;
    }
    public void Frame(Play play)
    {
        var canvas=surface.Canvas;canvas.Clear(new SKColor(28,32,40));parts=[];var top=20f;
        Zoom=Math.Clamp(play.Scale,.5f,2f);
        if(!plain&&Project() is {} project)
        {
            try
            {
            var name=play.OnTitle?"Title":"MatchPanel";
            var saved=string.Join(';',Directory.EnumerateFiles(Path.GetDirectoryName(project)!,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p) is ".gumj" or ".gusj" or ".gutj").Order(StringComparer.Ordinal).Select(p=>p+File.GetLastWriteTimeUtc(p).Ticks+new FileInfo(p).Length));
            if(screen!=name||stamp!=saved||!ReferenceEquals(loaded,ObjectFinder.Self.GumProjectSave)||!GumService.Default.IsInitialized||lastScale!=Zoom)Open(name,project,saved,Zoom);
            Set("TurnText.Text",play.Match.Status);var(dark,light)=Rules.Count(play.Match.Current);Set("CountText.Text",$"Dark {dark}   Light {light}");
            GumService.Default.Update(clock.Elapsed.TotalSeconds);GumService.Default.Draw(canvas);
            void Walk(GraphicalUiElement parent)
            {
                foreach(var part in parent.Children.OfType<GraphicalUiElement>())
                {
                    if(!part.Visible||part.Name is null||part.Tag is not InstanceSave instance||instance.ParentContainer?.Name!=name)continue;
                    parts.Add(new(part.Name,part.AbsoluteLeft*Zoom,part.AbsoluteTop*Zoom,part.AbsoluteWidth*Zoom,part.AbsoluteHeight*Zoom,Words(part)));Walk(part);
                }
            }
            Walk(shown!);top=parts.Count==0?40:parts.Max(p=>p.Y+p.Height)+12;Problem="";
            }
            catch(Exception error)when(error is not OutOfMemoryException)
            {
                Problem="The Gum screen could not be read: "+error.Message;
                if(GumService.Default.IsInitialized)GumService.Default.Uninitialize();shown=null;parts=[];top=Plain(play);
            }
        }
        else top=Plain(play);
        if(!play.OnTitle)
        {
            var game=play.Match.Current;var size=MathF.Min(Width-32,Height-top-160*Zoom);var cell=MathF.Max(8,size/game.Size);Board=(16,top,cell);
            using var paint=new SKPaint{IsAntialias=true};
            for(var r=0;r<game.Size;r++)for(var c=0;c<game.Size;c++)
            {
                var x=16+c*cell;var y=top+r*cell;var square=game.Name(r,c);
                paint.Color=play.Match.Picked.Contains(square)?new(135,116,18):Game.Dark(game.Size,r,c)?new(55,72,62):new(230,220,195);canvas.DrawRect(x,y,cell,cell,paint);
                if(game.At(r,c) is var piece&&Game.Owner(piece) is {} side)
                {
                    paint.Color=new(245,235,210);canvas.DrawCircle(x+cell/2,y+cell/2,cell*.41f,paint);
                    paint.Color=side==Side.Dark?new(30,30,30):new(235,70,55);canvas.DrawCircle(x+cell/2,y+cell/2,cell*.36f,paint);
                    if(char.IsUpper(piece)){paint.Color=new(245,235,210);paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=MathF.Max(2.5f,cell*.05f);canvas.DrawCircle(x+cell/2,y+cell/2,cell*.2f,paint);paint.Style=SKPaintStyle.Fill;}
                }
                if(r*game.Size+c==Cursor){paint.Color=SKColors.Yellow;paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=2;canvas.DrawRect(x+2,y+2,cell-4,cell-4,paint);paint.Style=SKPaintStyle.Fill;}
            }
            var footer=top+cell*game.Size+28*Zoom;
            DrawText("Computer move (C)",20,footer,16*Zoom);DrawText("Take back (U)",Width*.58f,footer,16*Zoom,play.Match.CanTakeBack?SKColors.White:SKColors.Gray);
            parts.Add(new("ComputerMove",16,footer-22*Zoom,Width*.54f,30*Zoom,"Computer move"));
            if(play.Match.CanTakeBack)parts.Add(new("TakeBack",Width*.57f,footer-22*Zoom,Width*.4f,30*Zoom,"Take back"));
            DrawText(play.Match.Said,20,footer+28*Zoom,14*Zoom);
        }
        var bottom=Height-10f;
        Wrapped("Help",Help,12*Zoom,ref bottom);bottom-=8;
        Wrapped("Copyright",Credit,11*Zoom,ref bottom);
        foreach(var part in parts.Where(p=>p.Name.EndsWith("Button",StringComparison.Ordinal)||p.Name is "ComputerMove" or "TakeBack"))
        {
            var hover=Pointer.X>=part.X&&Pointer.Y>=part.Y&&Pointer.X<part.X+part.Width&&Pointer.Y<part.Y+part.Height;
            if(play.Focus!=part.Words&&!hover)continue;
            using var ring=new SKPaint{Color=play.Focus==part.Words?SKColors.Yellow:PointerDown?SKColors.White:SKColors.LightGray,Style=SKPaintStyle.Stroke,StrokeWidth=3};
            canvas.DrawRect(part.X+2,part.Y+2,part.Width-4,part.Height-4,ring);
        }
        lastScale=Zoom;surface.Flush();using var pixels=surface.PeekPixels();pixels!.GetPixelSpan().CopyTo(Pixels);
    }
    private float Plain(Play play)
    {
        DrawText("New game (N)",20,30*Zoom,18*Zoom);DrawText(play.Match.Status,20,60*Zoom,18*Zoom);
        parts.Add(new("NewGameButton",16,4,MathF.Min(Width-32,240*Zoom),36*Zoom,"New game"));
        parts.Add(new("Status",16,40*Zoom,Width-32,24*Zoom,play.Match.Status));
        if(Problem.Length>0)DrawText("The Gum screen could not be read. N starts a match.",20,80*Zoom,14*Zoom);
        return (Problem.Length>0?100:80)*Zoom;
    }
    private float lastScale;
    private void DrawText(string text,float x,float y,float size,SKColor? color=null)
    {using var font=new SKFont(SKTypeface.Default,size);using var paint=new SKPaint{Color=color??SKColors.White,IsAntialias=true};surface.Canvas.DrawText(text,x,y,SKTextAlign.Left,font,paint);}
    private List<string> WrappedLines(string text,float size)
    {
        using var font=new SKFont(SKTypeface.Default,size);var lines=new List<string>();var line="";
        foreach(var word in text.Split(' ')){var next=line.Length==0?word:line+" "+word;if(font.MeasureText(next)>Width-32&&line.Length>0){lines.Add(line);line=word;}else line=next;}
        if(line.Length>0)lines.Add(line);return lines;
    }
    private float FooterHeight(float scale)=>18+WrappedLines(Help,12*scale).Count*15*scale+WrappedLines(Credit,11*scale).Count*13.75f*scale;
    private void Wrapped(string name,string text,float size,ref float bottom)
    {
        var lines=WrappedLines(text,size);var height=size*1.25f;bottom-=height*lines.Count;
        for(var i=0;i<lines.Count;i++){DrawText(lines[i],16,bottom+(i+1)*height-size*.15f,size);parts.Add(new(name+"."+i,16,bottom+i*height,Width-32,height,lines[i]));}
    }
    public void Click(Play play,float x,float y)
    {
        foreach(var part in parts.Where(p=>p.Name.EndsWith("Button",StringComparison.Ordinal)||p.Name is "ComputerMove" or "TakeBack"))
            if(x>=part.X&&y>=part.Y&&x<part.X+part.Width&&y<part.Y+part.Height)
            {play.Focus=part.Words!;if(part.Name=="ComputerMove")play.Match.ComputerMove();else if(part.Name=="TakeBack")play.Match.TakeBack();else play.NewGame();return;}
        if(play.OnTitle)return;
        var(r,c)=((int)MathF.Floor((y-Board.Y)/Board.Cell),(int)MathF.Floor((x-Board.X)/Board.Cell));
        if(r>=0&&c>=0&&r<play.Match.Current.Size&&c<play.Match.Current.Size){play.Focus="Board";Cursor=r*play.Match.Current.Size+c;play.Match.Click(play.Match.Current.Name(r,c));}
    }
    public void Save(string path){using var image=surface.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);png.SaveTo(file);}
    public void Dispose(){if(ReferenceEquals(loaded,ObjectFinder.Self.GumProjectSave)&&GumService.Default.IsInitialized)GumService.Default.Uninitialize();surface.Dispose();}
}
