using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Data;
using Bed.Editor;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class ProjectLauncher
{
    private enum TourStep {OpenExample,SelectCrate,Move,Undo,OpenTable,EditPrice,RejectPrice,CorrectAndSave,Play,Stop}
    internal bool EnableOnboarding {get;set;}
    internal string TourStage=>tourActive?tourStep.ToString():tourFinished?"Complete":"Closed";
    private bool tourDismissed,tourActive,tourFinished,tourSaved;
    private TourStep tourStep;
    private DataWorkspace? tourOwner;
    private SceneDocumentEntry? tourScene;
    private Scene? tourBeforeMove;
    private DataFile? tourData;
    private string tourCrate="",tourInitialPrice="";
    private Vector3 tourOriginalPosition;
    private long tourMoveRevision,tourValidRevision,tourStopBaseline;
    private IProjectPlaySession? tourPlay;
    private Action<IReadOnlyList<DataFile>>? tourSaveWatcher;
    private string tourNotice="";
    private (float2 Pos,float2 Size)? welcomeArea,tourArea;

    /// <summary>Puts the next welcome or tour box in the bottom-right corner of the docked editor's centre view, so it
    /// never covers Files, Used by or the other docks. It moves again only when that area changes, so a box the
    /// person drags stays where they put it. False when no docked editor is showing (the start page).</summary>
    private static bool PlaceInCentre(ref (float2 Pos,float2 Size)? last,float minWidth)
    {
        if(!DockedCentre.TryGet(out var pos,out var size)){last=null;return false;}
        var margin=ImGui.GetFontSize();var moved=last!=(pos,size);last=(pos,size);
        var room=new float2(MathF.Max(1,size.X-2*margin),MathF.Max(1,size.Y-2*margin));
        ImGui.SetNextWindowPos(pos+size-new float2(margin,margin),moved?ImGuiCond.Always:ImGuiCond.Appearing,new float2(1,1));
        ImGui.SetNextWindowSizeConstraints(new float2(MathF.Min(minWidth,room.X),0),room);
        return true;
    }

    private void ReadTourPreference()
    {
        try
        {
            if(!File.Exists(SettingsFile))return;
            using var json=JsonDocument.Parse(File.ReadAllText(SettingsFile));
            tourDismissed=json.RootElement.ValueKind==JsonValueKind.Object&&json.RootElement.TryGetProperty("tourDismissed"u8,out var value)&&value.ValueKind==JsonValueKind.True;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){}
    }
    private void RememberTourDismissed()
    {
        if(!EnableOnboarding)return;
        tourDismissed=true;
        try
        {
            lock(preferencesGate)
            {
                var settings=new JsonObject();
                if(File.Exists(SettingsFile))try{settings=JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject??new();}catch(JsonException){}
                settings["tourDismissed"]=true;Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);File.WriteAllText(SettingsFile,settings.ToJsonString());
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){Said="The tour closed, but its preference could not be saved: "+error.Message;}
    }
    private void EndTour()
    {
        if(tourOwner is not null&&tourSaveWatcher is not null)tourOwner.Project.FilesSaved-=tourSaveWatcher;
        tourSaveWatcher=null;tourOwner=null;tourScene=null;tourData=null;tourBeforeMove=null;tourPlay=null;tourActive=false;
    }
    private void BeginTour()
    {
        if(!EnableOnboarding)return;
        EndTour();tourFinished=false;tourActive=true;tourStep=TourStep.OpenExample;tourNotice="";tourSaved=false;
        startPage=StartPage.Home;projectChooser=Workspace is not null;
    }
    private void SkipTour(){EndTour();tourFinished=false;RememberTourDismissed();}
    private void DrawTourOffer()
    {
        if(!EnableOnboarding||tourDismissed||tourActive||tourFinished)return;
        Ui.TextWrapped("Learn BED with a five-minute tour of your own example copy."u8);
        if(Ui.Button("Take the tour"u8))BeginTour();ImGui.SameLine();if(Ui.Button("Skip tour"u8))SkipTour();
    }
    private void AttachTourOwner()
    {
        if(Workspace is not {} owner||!owner.Documents.TryGetOpenScene("level.json",out var scene)||owner.Project.Find("wares.json") is not {} data)
        {tourNotice="The tour needs the complete bundled level and wares example.";return;}
        tourOwner=owner;tourScene=scene;tourData=data;
        var crate=scene!.Session.Current.Objects.FirstOrDefault(o=>o.Id.Contains("crate",StringComparison.OrdinalIgnoreCase));
        if(crate is null){tourNotice="The example's crate could not be found. Reinstall the complete BED package.";return;}
        tourCrate=crate.Id;tourOriginalPosition=crate.Position;tourBeforeMove=scene.Session.Current;
        tourSaveWatcher=files=>
        {
            if(tourActive&&tourStep==TourStep.CorrectAndSave&&files.Any(file=>ReferenceEquals(file,tourData))&&owner.TableOf("wares.json")?.CellError("/wares/0","price") is null)tourSaved=true;
        };
        owner.Project.FilesSaved+=tourSaveWatcher;tourStep=TourStep.SelectCrate;
    }
    private string TourInstruction=>tourStep switch
    {
        TourStep.OpenExample=>"Choose Try the example to make a copy you can change.",
        TourStep.SelectCrate=>"Select the crate in the Outliner.",
        TourStep.Move=>"Focus the viewport and press an arrow key to move the crate.",
        TourStep.Undo=>"Press Ctrl+Z to undo the move.",
        TourStep.OpenTable=>"Choose wares in Files to open its table.",
        TourStep.EditPrice=>"Change Bread's price in the table and press Enter.",
        TourStep.RejectPrice=>"Try a negative price and press Enter to see the explanation beside the field.",
        TourStep.CorrectAndSave=>"Enter a new price of 0 or more, press Enter, then Ctrl+S to save.",
        TourStep.Play=>"Press Play to run your saved example.",
        _=>"Press Stop to return to editing."
    };
    private void ObserveTour()
    {
        if(!tourActive||tourStep==TourStep.OpenExample)return;
        if(!ReferenceEquals(Workspace,tourOwner)||tourScene is null||tourData is null||!tourOwner!.Documents.TryGetOpenScene("level.json",out var current)||!ReferenceEquals(current,tourScene))
        {EndTour();Said="The tour stopped because its example closed or changed; reopen it from Help when you are ready.";return;}
        var scene=tourScene.Session;var data=tourData.Session;var table=tourOwner.TableOf("wares.json");
        string Price()=>data?.Current.At("/wares/0/price")?.GetRawText()??"";
        switch(tourStep)
        {
            case TourStep.SelectCrate when tourScene.Editor.Primary==tourCrate:
                tourBeforeMove=scene.Current;tourOriginalPosition=scene.Current.Find(tourCrate)!.Position;tourStep=TourStep.Move;break;
            case TourStep.Move when scene.Current.Find(tourCrate)?.Position!=tourOriginalPosition&&scene.History.CanUndo:
                tourMoveRevision=scene.Revision;tourStep=TourStep.Undo;break;
            case TourStep.Undo when ReferenceEquals(scene.Current,tourBeforeMove)&&scene.Revision>tourMoveRevision&&scene.History.CanRedo:
                tourStep=TourStep.OpenTable;break;
            case TourStep.OpenTable when ReferenceEquals(tourOwner.Active,tourData)&&data is not null:
                tourInitialPrice=Price();tourStep=TourStep.EditPrice;break;
            case TourStep.EditPrice when data is not null&&Price()!=tourInitialPrice&&table?.CellError("/wares/0","price") is null:
                tourValidRevision=data.Revision;tourStep=TourStep.RejectPrice;break;
            case TourStep.RejectPrice when data?.Revision==tourValidRevision&&table?.CellError("/wares/0","price") is not null:
                tourSaved=false;tourStep=TourStep.CorrectAndSave;break;
            case TourStep.CorrectAndSave when tourSaved&&data is not null&&!data.Dirty&&table?.CellError("/wares/0","price") is null:
                tourStep=TourStep.Play;break;
            case TourStep.Play when tourOwner.ProjectPlay is {HasCompleted:false} play:
                tourPlay=play;tourStopBaseline=tourOwner.TourStopRequests;tourStep=TourStep.Stop;break;
            case TourStep.Stop when tourPlay?.HasCompleted==true&&tourOwner.TourStopRequests>tourStopBaseline:
                EndTour();tourFinished=true;RememberTourDismissed();break;
            case TourStep.Stop when tourPlay?.HasCompleted==true:
                tourStep=TourStep.Play;tourNotice="The example already stopped; press Play and then Stop to finish.";break;
        }
    }
    private UiNode? TourTarget()
    {
        var nodes=Ui.Snapshot().Nodes;
        bool Matches(UiNode n)=>tourStep switch
        {
            TourStep.OpenExample=>n.Role==UiRole.Button&&n.Label=="Try the example",
            TourStep.SelectCrate=>n.Role==UiRole.ListItem&&n.Label.Contains(tourCrate,StringComparison.Ordinal),
            TourStep.Move=>n.Role==UiRole.Canvas&&n.Label.Contains("Viewport",StringComparison.OrdinalIgnoreCase),
            TourStep.Undo=>n.Role==UiRole.Button&&n.Label=="Undo",
            TourStep.OpenTable=>n.Role==UiRole.ListItem&&n.Label.Contains("wares.json",StringComparison.Ordinal),
            TourStep.EditPrice or TourStep.RejectPrice or TourStep.CorrectAndSave=>n.Role==UiRole.TextField&&n.Label=="Bread: price",
            TourStep.Play=>n.Role==UiRole.Button&&n.Label=="Play",
            _=>n.Role==UiRole.Button&&n.Label=="Stop"
        };
        foreach(var node in nodes)if(node.Enabled&&!node.Bounds.IsEmpty&&Matches(node))return node;
        return null;
    }
    private void DrawOnboarding()
    {
        if(!EnableOnboarding)return;
        ObserveTour();
        if(!tourActive&&!tourFinished)
        {
            if(Workspace is not null&&!tourDismissed)
            {
                var welcome=true;PlaceInCentre(ref welcomeArea,0);
                if(Ui.Begin("Welcome to BED###bed.tour.offer"u8,ref welcome,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoDocking))DrawTourOffer();
                Ui.End();if(!welcome)SkipTour();
            }
            return;
        }
        var target=tourActive?TourTarget():null;
        if(target is {} next&&next.TextEvidence is not {Truncated:true})
        {
            var bounds=next.Bounds;var viewport=next.ViewportId==0?ImGui.GetMainViewport():ImGui.FindViewportByID(new ImGuiID(next.ViewportId));
            var draw=ImGui.GetForegroundDrawList(viewport);var colour=ImGui.GetColorU32(ImGuiCol.NavCursor);
            draw.AddRect(new(bounds.X-3,bounds.Y-3),new(bounds.X+bounds.Width+3,bounds.Y+bounds.Height+3),colour,3,0,3);
        }
        if(!PlaceInCentre(ref tourArea,440))
        {
            var vp=ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(vp.Pos+vp.Size-new float2(16,48),ImGuiCond.Appearing,new float2(1,1));
            ImGui.SetNextWindowSizeConstraints(new float2(MathF.Min(440,vp.Size.X-32),0),new float2(vp.Size.X-32,vp.Size.Y-32));
        }
        var open=true;
        if(Ui.Begin("Take the tour###bed.tour"u8,ref open,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoFocusOnAppearing))
        {
            // Stay readable after the person focuses an editor panel without taking keyboard focus back.
            ImGui.Internal.BringWindowToDisplayFront(ImGui.Internal.GetCurrentWindow());
            if(tourFinished)
            {
                Ui.StatusWrapped("Tour complete: you opened a level, moved and undid, edited and checked data, saved, played and stopped."u8,"tour.instruction");
                if(Ui.Button("Close tour"u8))tourFinished=false;
            }
            else
            {
                Ui.Text($"Step {(int)tourStep+1} of 10");Ui.StatusWrapped(TourInstruction,"tour.instruction");
                if(tourNotice.Length>0)Ui.TextWrapped(tourNotice);
                Ui.BeginDisabled(target is null,"Open or scroll to the named panel to find the next control.");
                if(Ui.Button("Focus next action"u8)&&target is {} focus)
                {
                    if(focus.Role==UiRole.Canvas&&tourOwner?.SceneViewport is {} viewport)
                    {ImGui.SetWindowFocus("Viewport###panel.editor.viewport"u8);viewport.RequestKeyboardFocus();}
                    else Ui.RequestAction(focus.Id,UiActions.Focus);
                }
                Ui.EndDisabled();ImGui.SameLine();if(Ui.Button("Skip tour"u8))SkipTour();
            }
        }
        Ui.End();if(!open){if(tourActive)SkipTour();else tourFinished=false;}
    }
}
