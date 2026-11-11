using System.Text.RegularExpressions;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

public sealed class KeyboardGuideDoc07Tests
{
    private static string Root()
    {
        for(var folder=new DirectoryInfo(AppContext.BaseDirectory);folder is not null;folder=folder.Parent)
            if(File.Exists(Path.Combine(folder.FullName,"Bed.slnx")))return folder.FullName;
        throw new InvalidOperationException("The keyboard guide check requires the source checkout.");
    }
    private static string Guide()=>File.ReadAllText(Path.Combine(Root(),"docs","guide","keyboard.md"));
    [Fact]
    public void Guide_covers_every_central_and_live_scene_workspace_binding_exactly()
    {
        var guide=Guide();var documented=Regex.Matches(guide,@"^\| `(?<id>(?:editor|scene)\.[^`]+)` \| `(?<key>[^`]+)` \|",RegexOptions.Multiline)
            .ToDictionary(match=>match.Groups["id"].Value,match=>match.Groups["key"].Value,StringComparer.Ordinal);
        var root=Directory.CreateTempSubdirectory("keyboard-guide-doc07-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            File.WriteAllText(Path.Combine(data,"level.json"),new SceneFormat().Write(Scene.Empty));
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);
            using var catalog=new AssetCatalog();Assert.True(catalog.Publish(catalog.BeginRefresh(),[],out _));Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets),catalog));
            Assert.True(workspace.Show("level.json"),workspace.Said);var actual=workspace.SceneWindow!.Shortcuts.InEffect();
            Assert.Equal(actual.Keys.Order(),documented.Keys.Order());
            foreach(var (id,chord) in actual)Assert.Equal(chord.ToString(),documented[id]);
            foreach(var (id,chord) in Shortcuts.EditorDefaults)Assert.Equal(Chord.Parse(chord).ToString(),documented[id]);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Fixed_key_inventory_and_all_direct_viewport_letter_bindings_are_documented()
    {
        var guide=Guide();foreach(var fixedKey in KeysPanel.FixedKeys)Assert.Contains(fixedKey,guide,StringComparison.Ordinal);
        var source=File.ReadAllText(Path.Combine(Root(),"src","Bed.Editor.ImGui","ViewportView.cs"));
        var keyboard=source.Split("private void Keyboard(bool wasFocused)",2)[1].Split("private void FrameSelected()",2)[0];
        var sourceKeys=Regex.Matches(keyboard,@"IsKeyPressed\(([^)]*)\)").SelectMany(call=>Regex.Matches(call.Groups[1].Value,@"ImGuiKey\.([A-Z])\b").Select(key=>key.Groups[1].Value)).Distinct().Order().ToArray();
        var documented=Regex.Matches(guide,@"^\| `([A-Z])` \|",RegexOptions.Multiline).Select(match=>match.Groups[1].Value).Order().ToArray();
        Assert.NotEmpty(sourceKeys);Assert.Equal(sourceKeys,documented);
        foreach(var text in new[]{"Ctrl+O","Ctrl+Shift+N","Shift+F10","Shift+Enter","Shift+Tab","Keypad Enter","Destination folder","Copy build to folder..."})Assert.Contains(text,guide);
    }
    [Fact]
    public void Rebinding_roundtrips_and_guide_covers_desktop_controls_and_local_persistence()
    {
        var guide=Guide();foreach(var control in new[]{"Rebind selected shortcut","Reset to default","Escape cancels","shortcuts.json","ReadOverrides","WriteOverrides","runs neither action"})Assert.Contains(control,guide);
        Assert.DoesNotContain("Keys panel is read-only",guide);Assert.DoesNotContain("desktop startup has no shortcut-override",guide);
        var keys=new Shortcuts();keys.Set("editor.play.project","F6");keys.Set("editor.stop",null);keys.ResetToDefault("editor.stop");
        Assert.Equal("F6",keys.For("editor.play.project")!.Value.ToString());Assert.Equal("Shift+F5",keys.For("editor.stop")!.Value.ToString());
        var loaded=new Shortcuts();Assert.Empty(loaded.ReadOverrides(keys.WriteOverrides()));Assert.Equal(keys.InEffect().OrderBy(pair=>pair.Key),loaded.InEffect().OrderBy(pair=>pair.Key));
        foreach(var call in new[]{"keys.Set(\"editor.play.project\", \"F6\")","keys.Set(\"editor.stop\", null)","keys.ResetToDefault(\"editor.stop\")","keys.WriteOverrides()","keys.ReadOverrides(text)"})Assert.Contains(call,guide);
    }
}
