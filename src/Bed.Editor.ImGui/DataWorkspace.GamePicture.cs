using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using Im=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private bool requestingPicture,pendingPicture,focusPicture;
    private readonly PictureTexture gamePictureTexture=new();
    private GamePictureFrame? shownPicture;
    private long gamePictureVersion;
    internal bool RequestGamePicture()
    {
        if(projectPlayHost is not IGamePictureHost){Said="This BED installation cannot request game pictures.";return false;}
        requestingPicture=true;
        try{var started=RequestProjectPlay();focusPicture=started;return started;}finally{requestingPicture=false;}
    }
    internal void DrawGamePicture()
    {
        PictureTexture.Tick();
        Ui.TextWrapped("Take a picture from a game that shares pictures with BED. Start a separate run with no game window; stop ordinary Play first."u8);
        if(projectPlay is IGamePictureSession picture&&projectPlay.HasCompleted==false)
        {
            Ui.BeginDisabled(picture.Busy,"Wait for the picture request to finish.");
            if(focusPicture&&!picture.Busy){if(Im.IsWindowFocused())Im.SetKeyboardFocusHere();focusPicture=false;}
            if(Ui.Button("Take picture###picture-request"u8)){picture.TakePicture();focusPicture=true;}Ui.EndDisabled();
            if(Ui.Button("Stop picture run"u8)){StopProjectPlay();focusPicture=true;}
        }
        else
        {
            Ui.BeginDisabled(projectPlay is {HasCompleted:false},"Stop the running program first.");
            if(focusPicture){if(Im.IsWindowFocused())Im.SetKeyboardFocusHere();focusPicture=false;}
            if(Ui.Button("Start picture run###picture-request"u8))RequestGamePicture();Ui.EndDisabled();
        }
        Ui.TextWrapped(projectPlay is IGamePictureSession?projectPlay.Status:said.Showing?Said:"");
        if(projectPlay is not IGamePictureSession{Picture:{} frame})return;
        Ui.TextWrapped($"Game picture: {frame.Width} by {frame.Height} pixels, game step {frame.Frame}.");
        Ui.TextWrapped(frame.Description);
        if(!ReferenceEquals(shownPicture,frame)){shownPicture=frame;gamePictureVersion++;}
        gamePictureTexture.Update(frame.Width,frame.Height,frame.Rgba,gamePictureVersion);
        var room=Im.GetContentRegionAvail();var scale=MathF.Min(room.X/frame.Width,room.Y/frame.Height);
        if(scale>0)Im.Image(gamePictureTexture.Ref,new float2(frame.Width*scale,frame.Height*scale));
    }
}
