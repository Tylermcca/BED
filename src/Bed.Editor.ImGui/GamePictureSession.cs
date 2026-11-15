namespace Bed.Editor.ImGui;

// Optional internal bridge over the existing owned headless-picture connection.
internal interface IGamePictureHost
{
    IProjectPlaySession StartPictures(PlayLaunch launch,PlayApprovals approvals);
}
internal sealed record GamePictureFrame(int Width,int Height,byte[] Rgba,long Frame,string Description);
internal interface IGamePictureSession
{
    GamePictureFrame? Picture {get;}
    bool Busy {get;}
    bool TakePicture();
}
