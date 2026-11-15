namespace Bed.Editor.ImGui;
// Private, optional sample capability. Each target comes from the explicit adapter opt-in.
internal interface ILivePlayHost
{
    IProjectPlaySession StartLive(PlayLaunch launch,PlayApprovals approvals,LiveValueTarget target);
}
internal interface ILivePlaySession
{
    LiveValueTarget Target {get;}
    LiveValuesSnapshot? Values {get;}
    bool Busy {get;}
    bool TryValue(string field,float value);
    bool TryKey(string key);
}
