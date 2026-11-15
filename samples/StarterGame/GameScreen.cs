using System.Diagnostics;
using Gum.Managers;
using Gum.Wireframe;
using GumRuntime;
using RenderingLibrary;
using SkiaSharp;
using GumService=Gum.GumService;

namespace StarterGame;

// MIXED: Gum's own Skia canvas API, the existing sample's pixel-window route and an owned file-save reload.
// This player references Gum directly. It carries no BED editor, adapter or AI connection.
public sealed class GameScreen:IDisposable
{
    public const int Width=800,Height=600;
    private readonly string folder,project;
    private readonly SKSurface surface=SKSurface.Create(new SKImageInfo(Width,Height,SKColorType.Bgra8888,SKAlphaType.Premul))!;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private GraphicalUiElement? shown;
    private string screen="",stamp="";
    public int Reloads {get;private set;}
    public byte[] Pixels {get;}=new byte[Width*Height*4];
    public GameScreen(string root){folder=Path.Combine(root,"ui");project=Path.Combine(folder,"Game.gumj");}
    private string SavedStamp()=>string.Join(";",Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p) is ".gumj" or ".gusj" or ".gutj").Order(StringComparer.Ordinal).Select(p=>p+":"+File.GetLastWriteTimeUtc(p).Ticks+":"+new FileInfo(p).Length));
    private void Open(string name,string saved)
    {
        var service=GumService.Default;if(service.IsInitialized)service.Uninitialize();
        service.Initialize(surface.Canvas,Width,Height,project);
        var loaded=ObjectFinder.Self.GumProjectSave??throw new InvalidDataException("Gum could not load ui/Game.gumj.");
        var description=loaded.Screens.Single(s=>s.Name==name);
        shown=description.ToGraphicalUiElement(SystemManagers.Default,addToManagers:false);shown.AddToRoot();
        screen=name;stamp=saved;Reloads++;
    }
    public void Frame(Game game)
    {
        var name=game.Playing?"Play":"Menu";var saved=SavedStamp();if(name!=screen||saved!=stamp)Open(name,saved);
        Set("Title.Text",game.Title);
        if(game.Playing)
        {
            Set("Score.Text",game.Won?"You caught every light! R starts again.":$"Lights: {game.Score} of {game.Goal}");
            Set("Target.Visible",!game.Won);Set("Target.X",game.Target.X);Set("Target.Y",game.Target.Y);
        }
        GumService.Default.Update(clock.Elapsed.TotalSeconds);surface.Canvas.Clear(new SKColor(20,28,42));GumService.Default.Draw(surface.Canvas);surface.Flush();
        using var pixels=surface.PeekPixels();pixels!.GetPixelSpan().CopyTo(Pixels);
    }
    private void Set(string path,object value)
    {var dot=path.LastIndexOf('.');var part=shown!.GetGraphicalUiElementByName(path[..dot].Split('.'));part?.SetProperty(path[(dot+1)..],value);}
    private bool Hit(string name,float x,float y)
    {var p=shown?.GetGraphicalUiElementByName(name);return p is {Visible:true}&&x>=p.AbsoluteLeft&&y>=p.AbsoluteTop&&x<p.AbsoluteLeft+p.AbsoluteWidth&&y<p.AbsoluteTop+p.AbsoluteHeight;}
    public void Click(Game game,float x,float y)
    {if(!game.Playing&&Hit("PlayButton",x,y))game.Start();else if(game.Playing&&Hit("BackButton",x,y))game.Back();else if(game.Playing&&Hit("Target",x,y))game.Collect();}
    internal byte[] Png(){using var image=surface.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();}
    public void Save(string path)=>File.WriteAllBytes(path,Png());
    public void Dispose(){if(GumService.Default.IsInitialized)GumService.Default.Uninitialize();surface.Dispose();}
}
