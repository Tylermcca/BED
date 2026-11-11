using Bed.Editor.ImGui;

namespace Bed.Editor.App;

/// <summary>Declared adapter Play over the existing owned-process host; no executable discovery, build or automatic save.
/// Construction starts nothing. Start is reached only after the editor's saved-files decision.</summary>
public sealed class LinkedPlayHost : ILinkedPlayHost
{
    /// <summary>Creates the one saved-input preview with explicit host policy: fifteen seconds to Ready, five to stop.</summary>
    public LinkedPlayPreview Start(GameLaunchSpec spec,PlaySnapshot snapshot)
    {
        var id=Guid.NewGuid();
        var logs=new PlayLogBuffer(snapshot.Correlate(id),500,4096,1024*1024,[snapshot.DocumentId]);
        var process=new GameProcessHost(spec,16*1024*1024,64*1024,256,logs);
        var session=new LinkedPlaySession(process,snapshot,id,TimeProvider.System,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(5));
        try {return new(session,process.Pump,()=>process.Completion is null || process.Completion.IsCompleted,new ConsolePanel(logs));}
        catch {session.Dispose();process.Dispose();throw;}
    }
}
