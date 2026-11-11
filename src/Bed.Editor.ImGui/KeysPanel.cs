using System.Runtime.CompilerServices;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Live shortcut inventory and keyboard rebinding, with explicitly fixed controls.</summary>
public static class KeysPanel
{
    /// <summary>The Keys panel's id.</summary>
    public const string Id="bed.keys";
    /// <summary>The command that opens the Keys panel.</summary>
    public const string ActionId="editor.panel."+Id;
    private sealed class Binding
    {
        public required Shortcuts Shortcuts;
        public required Func<IReadOnlyList<(string Id,string Label,string? Shortcut,string? Unavailable)>> Entries;
        public required Func<bool> IsOpen;
        public string? Selected;
        public bool Capturing;
    }
    private sealed class Preferences
    {
        public string? File;
        public string Status="";
        public Binding? Capture;
        public int SuppressFrame=-1;
    }
    private static readonly ConditionalWeakTable<object,Binding> bindings=new();
    private static readonly ConditionalWeakTable<Shortcuts,Preferences> preferences=new();
    /// <summary>Refreshes an editor's live sources without registering duplicate panels.</summary>
    public static void Attach<T>(EditorModel<T> editor,Shortcuts shortcuts,
        Func<IReadOnlyList<(string Id,string Label,string? Shortcut,string? Unavailable)>> entries) where T:class
    {
        var binding=bindings.GetValue(editor,_=>new Binding{Shortcuts=shortcuts,Entries=entries,IsOpen=()=>editor.IsOpen(Id)});
        binding.Shortcuts=shortcuts;binding.Entries=entries;
        if(!editor.Panels.Any(p=>p.Id==Id))editor.Add(new Panel(Id,"Keys",()=>Draw(binding),OpenAtStart:false,Width:600,Height:560));
    }
    internal static void LoadPreferences(Shortcuts shortcuts,string file)
    {
        var state=preferences.GetOrCreateValue(shortcuts);state.File=file;
        try
        {
            if(System.IO.File.Exists(file))
            {
                var problems=shortcuts.ReadOverrides(System.IO.File.ReadAllText(file));
                if(problems.Count>0)state.Status=string.Join("; ",problems.Select(p=>p.Message));
            }
        }
        catch(Exception error)when(error is not OutOfMemoryException){state.Status="Could not load shortcut preferences: "+error.Message+". Defaults are available; the file was left unchanged.";}
    }
    private static void Save(Binding binding,string message)
    {
        var state=preferences.GetOrCreateValue(binding.Shortcuts);state.Status=message;
        if(state.File is not {} file){state.Status+=" This host keeps changes for this session.";return;}
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);System.IO.File.WriteAllText(temporary,binding.Shortcuts.WriteOverrides());
            System.IO.File.Move(temporary,file,true);state.Status+=" Saved in your local shortcut preferences.";
        }
        catch(Exception error)when(error is not OutOfMemoryException){state.Status+=" Changed for this session; could not save shortcut preferences: "+error.Message;}
        finally{try{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    // Runs before command dispatch, including when another editor window shares these bindings.
    internal static bool Capture(object editor,Shortcuts shortcuts,IReadOnlyList<(ImGuiKey Key,string Name)> names)
    {
        var state=preferences.GetOrCreateValue(shortcuts);var frame=ImGui.GetFrameCount();
        if(state.SuppressFrame==frame)return true;
        if(state.Capture is not {Capturing:true} binding)return false;
        if(!bindings.TryGetValue(editor,out var active)||!ReferenceEquals(active,binding)||!binding.IsOpen()){binding.Capturing=false;state.Capture=null;return false;}
        var io=ImGui.GetIO();
        foreach(var (key,name) in names)
        {
            if(!ImGui.IsKeyPressed(key,false))continue;
            if(key==ImGuiKey.Escape){binding.Capturing=false;state.Capture=null;state.Status="Shortcut change cancelled.";state.SuppressFrame=frame;return true;}
            if(io.KeySuper){state.Status="Use Ctrl, Shift or Alt with a supported key.";return true;}
            var chord=new Chord((io.KeyCtrl?Mods.Ctrl:Mods.None)|(io.KeyShift?Mods.Shift:Mods.None)|(io.KeyAlt?Mods.Alt:Mods.None),name);
            shortcuts.Set(binding.Selected!,chord.ToString());binding.Capturing=false;state.Capture=null;state.SuppressFrame=frame;
            Save(binding,"Shortcut changed to "+chord+".");return true;
        }
        return true;
    }
    /// <summary>Every live binding, with palette label when present and the action id otherwise.</summary>
    public static IReadOnlyList<(string Id,string Label,string Chord)> Rows(Shortcuts shortcuts,
        IReadOnlyList<(string Id,string Label,string? Shortcut,string? Unavailable)> entries)=>
        shortcuts.InEffect().OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>
            (pair.Key,entries.FirstOrDefault(e=>e.Id==pair.Key).Label??pair.Key,pair.Value.ToString())).ToArray();
    /// <summary>Keys handled directly by tables and canvases, listed for reference; they can't be rebound.</summary>
    public static IReadOnlyList<string> FixedKeys{get;}=Array.AsReadOnly(new[]
    {
        "Table: Ctrl+C copies selected cells (not rebindable).",
        "Table: Up/Down/Left/Right moves the cell selection; Shift extends it (not rebindable).",
        "Canvas: Escape cancels a move (not rebindable).",
        "Canvas: 0 or keypad 0 resets zoom and pan (not rebindable).",
    });
    private static void Draw(Binding binding)
    {
        var state=preferences.GetOrCreateValue(binding.Shortcuts);var entries=binding.Entries();
        string Label(string id)=>entries.FirstOrDefault(e=>e.Id==id).Label??id;
        Ui.TextWrapped("Your shortcuts"u8);ImGui.SameLine();
        if(Ui.Button("Fixed keys"u8))ImGui.OpenPopup("##keys.fixed.help"u8);
        ImGui.SetNextWindowSize(new Brutal.Numerics.float2(MathF.Min(ImGui.GetMainViewport().Size.X-32,ImGui.GetFontSize()*35),0),ImGuiCond.Appearing);
        if(Ui.BeginPopup("##keys.fixed.help"u8))
        {
            foreach(var text in FixedKeys)Ui.TextWrapped(text);
            Ui.TextWrapped("Viewport: Tab/Shift+Tab cycles objects. Enter frames selection. Escape then Tab leaves object cycling."u8);
            Ui.EndPopup();
        }
        Ui.TextWrapped("Choose an action."u8);
        var available=ImGui.GetContentRegionAvail();var style=ImGui.GetStyle();
        var selectedText=binding.Selected is {} selected?"Selected action: "+Label(selected):"";
        var buttonsWidth=ImGui.CalcTextSize("Rebind selected shortcut"u8).X+ImGui.CalcTextSize("Reset to default"u8).X+4*style.FramePadding.X+style.ItemSpacing.X;
        var footer=(buttonsWidth>available.X?2:1)*ImGui.GetFrameHeightWithSpacing()+style.ItemSpacing.Y+(selectedText.Length>0?ImGui.CalcTextSize(selectedText,false,available.X).Y+style.ItemSpacing.Y:0);
        var minimum=ImGui.GetFrameHeight()+2*style.WindowPadding.Y+2;
        var listHeight=Math.Clamp(available.Y-footer,minimum,MathF.Max(210,minimum));
        if(Ui.BeginChild("shortcut.actions"u8,new Brutal.Numerics.float2(0,listHeight),ImGuiChildFlags.Borders))
            foreach(var id in binding.Shortcuts.InEffect().Keys.Concat(entries.Select(e=>e.Id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                if(Ui.Selectable($"{binding.Shortcuts.For(id)?.ToString()??"Unbound"}  {Label(id)}##key-{id}",binding.Selected==id))binding.Selected=id;
        Ui.EndChild();
        if(selectedText.Length>0)Ui.TextWrapped(selectedText,semanticId:"keys.selected");
        Ui.BeginDisabled(binding.Selected is null||binding.Capturing,"Select an action before rebinding or resetting it.");
        if(Ui.Button("Rebind selected shortcut"u8)){binding.Capturing=true;state.Capture=binding;state.Status="Press the new shortcut now. Escape cancels.";}
        if(buttonsWidth<=available.X)ImGui.SameLine();
        if(Ui.Button("Reset to default"u8)){binding.Shortcuts.ResetToDefault(binding.Selected!);Save(binding,"Default shortcut restored.");}
        Ui.EndDisabled();
        if(binding.Capturing&&Ui.Button("Cancel shortcut change"u8)){binding.Capturing=false;state.Capture=null;state.Status="Shortcut change cancelled.";}
        if(state.Status.Length>0)Ui.TextWrapped(state.Status,semanticId:"keys.status");
        foreach(var clash in binding.Shortcuts.InEffect().GroupBy(p=>p.Value).Where(g=>g.Count()>1))
            Ui.TextWrapped(clash.Key+" is used by "+string.Join(" and ",clash.Select(p=>Label(p.Key)))+". This shortcut runs neither action until one is changed.",semanticId:"keys.conflict."+clash.Key);

    }
}
