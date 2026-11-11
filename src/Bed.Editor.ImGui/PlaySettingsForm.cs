using System.Text.Json;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im = Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;

/// <summary>Workspace-owned project Play configuration. Saving settings never launches or approves a command.</summary>
public sealed class PlaySettingsForm
{
    private readonly string root;private readonly PlaySettings? defaults;private readonly PlayApprovals approvals;private readonly Func<IFileDialogs?> dialogs;
    private ImInputString program=new(8192,""u8),arguments=new(8192,"[]"u8),folder=new(8192,"."u8),build=new(8192,""u8),buildArgs=new(8192,"[]"u8),inputs=new(8192,"[]"u8);
    /// <summary>Loads project-owned fields and optional adapter defaults, using only host-selected local preferences for approval.</summary>
    public PlaySettingsForm(string projectRoot,PlaySettings? adapterDefaults,string userPreferencesFolder,Func<IFileDialogs?>? fileDialogs=null)
    {root=projectRoot;defaults=adapterDefaults;approvals=new(userPreferencesFolder);dialogs=fileDialogs??(()=>null);Reload();}
    /// <summary>Whether the settings window is open.</summary>
    public bool Open
    {
        get => open;
        set { if(value && !open) focusPending=true; open=value; }
    }
    private bool open,focusPending;
    private string? stamp;
    /// <summary>Last plain validation or save result.</summary>
    public string Status {get;private set;}="";
    /// <summary>The last successfully loaded or saved declaration.</summary>
    public PlaySettings? Settings {get;private set;}
    /// <summary>Reloads disk settings; malformed content remains untouched and reported.</summary>
    public void Reload()
    {
        try{Settings=PlaySettingsStore.Load(root,defaults);SetDraft(Settings??new());Status="";}
        catch(Exception e)when(e is not OutOfMemoryException){Status=e.Message;}
    }
    /// <summary>Sets editable fields without writing settings or authorizing execution.</summary>
    public void SetDraft(PlaySettings value)
    {
        stamp=value.Build?.Stamp;
        program=new(8192,value.Program);arguments=new(8192,JsonSerializer.Serialize(value.Arguments));folder=new(8192,value.WorkingFolder);
        build=new(8192,value.Build?.Command??"");buildArgs=new(8192,JsonSerializer.Serialize(value.Build?.Arguments??[]));inputs=new(8192,JsonSerializer.Serialize(value.Build?.Inputs??[]));
    }
    private PlaySettings Draft()=>new(program.ToString()??"",List(arguments,"Arguments"),folder.ToString()??".",
        new(build.ToString()??"",List(buildArgs,"Build arguments"),List(inputs,"Build inputs")){Stamp=stamp});
    private static string[] List(ImInputString field,string label)
    {
        try
        {
            var values=JsonSerializer.Deserialize<string[]>(field.ToString()??"");
            if(values is not null && values.All(v=>v is not null))return values;
        }
        catch(JsonException) { }
        throw new ArgumentException(label+" must be a JSON list of quoted strings. Use [] for no items.");
    }
    /// <summary>Validates and saves only configuration. External commands remain unapproved until a separate explicit action.</summary>
    public bool Save()
    {
        try
        {
            var value=Draft();
            if(!PlaySettingsResolver.TryResolve(root,value,out var launch,out var why)){Status=why!;return false;}
            if(!File.Exists(launch!.Run.Executable)&&launch.Build is null){Status="The program does not exist. Choose it or configure its build command.";return false;}
            PlaySettingsStore.Save(root,value);Settings=value;Status=approvals.NeedsApproval(launch,false)??"Play settings saved.";return true;
        }
        catch(Exception e)when(e is not OutOfMemoryException){Status=e.Message;return false;}
    }
    /// <summary>Resolves current saved settings without approving or launching them.</summary>
    public bool TryLaunch(out PlayLaunch? launch,out string? why)
    {
        launch=null;why=null;
        try{var value=PlaySettingsStore.Load(root,defaults);if(value is null){why="Choose a program in Play settings.";return false;}return PlaySettingsResolver.TryResolve(root,value,out launch,out why);}
        catch(Exception e)when(e is not OutOfMemoryException){why=e.Message;return false;}
    }
    /// <summary>Explains the exact approval needed for the selected saved action, without granting it.</summary>
    public string? ApprovalNeeded(PlayLaunch launch,bool forBuild)=>approvals.NeedsApproval(launch,forBuild);
    /// <summary>Records only the exact command the person has explicitly approved.</summary>
    public void Approve(PlayLaunch launch,bool forBuild)=>approvals.Approve(launch,forBuild);
    /// <summary>Draws settings and an explicit external-command confirmation. No process is started here.</summary>
    public void Draw()
    {
        if(!Open)return;
        if(focusPending)Im.SetNextWindowFocus();
        Im.SetNextWindowSize(new float2(700,0),ImGuiCond.FirstUseEver);
        if(Ui.Begin("Play settings###play.settings"u8))
        {
            if(focusPending){Im.SetKeyboardFocusHere();focusPending=false;}
            Ui.InputText("Program"u8,program);
            if(dialogs() is {} picker&&Ui.Button("Browse program..."u8)){var path=picker.OpenFile("Choose program",[new("Programs","*.exe")],root);if(path is not null)program=new(8192,Relative(path));}
            Ui.InputText("Arguments (JSON list)"u8,arguments);Ui.InputText("Working folder"u8,folder);
            if(dialogs() is {} folders&&Ui.Button("Browse working folder..."u8)){var path=folders.PickFolder("Choose working folder",root);if(path is not null)folder=new(8192,Relative(path));}
            Ui.InputText("Build command"u8,build);Ui.InputText("Build arguments (JSON list)"u8,buildArgs);Ui.InputText("Build inputs (JSON list of globs)"u8,inputs);
            Ui.TextWrapped("Arguments are literal items, for example [\"--background\"]. Nothing here runs a program."u8);
            if(Ui.Button("Save Play settings"u8))Save();
            if(TryLaunch(out var launch,out var why))
            {
                Ui.TextWrapped("Effective program: "+launch!.Run.Executable,"play.settings.effective.program");
                Ui.TextWrapped("Effective arguments: "+JsonSerializer.Serialize(launch.Run.Arguments),"play.settings.effective.arguments");
                Ui.TextWrapped("Effective working folder: "+launch.Run.WorkingDirectory,"play.settings.effective.folder");
                Ui.TextWrapped(launch.Build is {} command?"Effective build command: "+command.CommandLine():"Effective build command: Not configured.","play.settings.effective.build");
                if(launch.Build is {} buildCommand)Ui.TextWrapped("Effective build working folder: "+buildCommand.WorkingDirectory,"play.settings.effective.build.folder");
                Ui.TextWrapped("These are the settings Play will use. Save Play settings to use your edits."u8);
                if(ApprovalNeeded(launch,false) is {} question)
                {Ui.TextWrapped(question);if(Ui.Button("Allow this exact saved command"u8)){Approve(launch,false);Status="This saved command is allowed. Press Play to run it.";}}
            }
            else Ui.TextWrapped("Effective command unavailable: "+why,"play.settings.effective.problem");
            if(Status.Length>0)Ui.TextWrapped(Status);
            if(Ui.Button("Close settings"u8))Open=false;
            if(Im.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&Im.IsKeyPressed(ImGuiKey.Escape,false))Open=false;
        }
        Ui.End();
    }
    private string Relative(string path){var relative=Path.GetRelativePath(root,path);return relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)?path:relative;}
}

