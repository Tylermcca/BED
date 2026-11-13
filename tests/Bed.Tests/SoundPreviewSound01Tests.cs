using System.Reflection;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.Audio;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SoundPreviewSound01Tests
{
    private sealed class Fake : IAssetAudioPreview
    {
        public int Plays, Stops; public bool Refuse; public bool IsPlaying {get;private set;}
        public string Status => Refuse ? "Sound preview unavailable: damaged fixture." : IsPlaying ? "Playing sound." : "Sound ready.";
        public AudioPreviewInfo? Inspect(AssetCatalog.Lease content) => Refuse ? null : new("WAV PCM16",1,8000,800);
        public bool Play(AssetCatalog.Lease content) { Plays++; return IsPlaying=true; }
        public void Stop() { Stops++; IsPlaying=false; }
        public void Dispose()=>Stop();
    }
    private sealed class Rig : IDisposable
    {
        public string Root=Directory.CreateTempSubdirectory("sound01-").FullName;
        public HeadlessHost Host=new(900,800){Drawing=false};
        public ProjectAssetCatalog Assets;
        public AssetsPanel Panel;
        public Fake Audio=new();
        public string Path;
        public Rig()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Root,"data"));Directory.CreateDirectory(System.IO.Path.Combine(Root,"schemas"));Directory.CreateDirectory(System.IO.Path.Combine(Root,"assets"));
            Path=System.IO.Path.Combine(Root,"assets","tone.wav");
            using(var stream=File.Create(Path))using(var w=new BinaryWriter(stream))
            {w.Write("RIFF"u8);w.Write(36+1600);w.Write("WAVEfmt "u8);w.Write(16);w.Write((ushort)1);w.Write((ushort)1);w.Write(8000);w.Write(16000);w.Write((ushort)2);w.Write((ushort)16);w.Write("data"u8);w.Write(1600);w.Write(new byte[1600]);}
            var result=AssetImport.Import(Path,"wav",1,JsonSerializer.SerializeToElement(new{}),new("self","MIT","self-authored",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            Assert.True(result.Ok);Assets=new AudioAssetCatalogProvisioner().Open(new(System.IO.Path.Combine(Root,"data"),System.IO.Path.Combine(Root,"schemas")){Root=Root})!;
            Assert.Empty(Assets.Problems);Assert.NotNull(Assets.AudioPreview);
            Panel=(AssetsPanel)typeof(AssetsPanel).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke([Assets.Browser,Assets.Catalog,null]);
            typeof(AssetsPanel).GetProperty("AudioPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Panel,Audio);
            EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());Host.Frames(Draw,3);Click("tone.wav",UiRole.ListItem);
        }
        public void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(850,750));Ui.Begin("Sounds"u8);Panel.Draw();Ui.End();}
        public void Click(string label,UiRole role){var b=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role).Bounds;Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Draw,2);}
        public string Text=>string.Join("\n",Ui.Snapshot().Nodes.Select(n=>n.Label));
        public void Dispose(){Assets.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Admitted_sound_buttons_and_keyboard_use_fake_and_keep_source_and_rights_bytes()
    {
        using var r=new Rig();var source=File.ReadAllBytes(r.Path);var record=File.ReadAllText(AssetRecord.SidecarFor(r.Path));
        Assert.Contains("Licence: MIT",r.Text);Assert.Contains("0.100 seconds",r.Text);
        r.Click("Play sound",UiRole.Button);Assert.Equal(1,r.Audio.Plays);Assert.True(r.Audio.IsPlaying);
        r.Click("Stop sound",UiRole.Button);Assert.False(r.Audio.IsPlaying);
        r.Host.Press(r.Draw,ImGuiKey.F6);Assert.Equal(2,r.Audio.Plays);
        r.Host.Press(r.Draw,ImGuiKey.Escape);Assert.False(r.Audio.IsPlaying);
        r.Host.Press(r.Draw,ImGuiKey.F6);Assert.True(r.Audio.IsPlaying);
        r.Assets.Catalog.BeginRefresh();r.Host.Frames(r.Draw,2);Assert.False(r.Audio.IsPlaying);Assert.Contains("not available",r.Text);
        Assert.Equal(source,File.ReadAllBytes(r.Path));Assert.Equal(record,File.ReadAllText(AssetRecord.SidecarFor(r.Path)));
    }
    [Fact]
    public void Decoder_refusal_is_visible_and_disables_play_without_calling_backend()
    {
        using var r=new Rig();r.Audio.Refuse=true;r.Host.Frames(r.Draw,2);
        Assert.Contains("damaged fixture",r.Text);Assert.False(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Play sound").Enabled);Assert.Equal(0,r.Audio.Plays);
    }
    [Fact]
    public void Managed_ogg_decoder_reports_real_duration_without_opening_a_device()
    {
        var root=AppContext.BaseDirectory;
        while(!Directory.Exists(System.IO.Path.Combine(root,"tests","Bed.Tests","Fixtures","SOUND01")))root=Directory.GetParent(root)!.FullName;
        var bytes=File.ReadAllBytes(System.IO.Path.Combine(root,"tests","Bed.Tests","Fixtures","SOUND01","tone.ogg"));
        var type=typeof(OpenAlAssetAudioPreview);
        var result=type.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[bytes])!;
        Assert.Equal("OGG Vorbis",result.GetType().GetProperty("Format")!.GetValue(result));
        Assert.Equal(8000,result.GetType().GetProperty("SampleRate")!.GetValue(result));
        Assert.Equal(800L,result.GetType().GetProperty("Frames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(result));
        var bad=Assert.Throws<TargetInvocationException>(()=>type.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,["OggSbroken"u8.ToArray()]));
        Assert.IsAssignableFrom<ArgumentException>(bad.InnerException);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Actual_owner_reports_malformed_admitted_content_without_opening_a_device(bool ogg)
    {
        using var r=new Rig();var bytes=ogg?"OggSbroken"u8.ToArray():"RIFFbroken"u8.ToArray();
        var before=Assert.Single(r.Assets.Catalog.CurrentInputs()).ProducingRecord;
        var record=before with{SourceHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))};
        Assert.True(r.Assets.Catalog.Publish(r.Assets.Catalog.BeginRefresh(),[new(record,bytes,bytes,[],true,"sound-preview-v1",record.Write())],out _));
        var lease=r.Assets.Catalog.Resolve(record.Guid).Current!;var actual=r.Assets.AudioPreview!;
        Assert.Null(actual.Inspect(lease));Assert.StartsWith("Sound preview unavailable:",actual.Status);Assert.False(actual.Play(lease));Assert.False(actual.IsPlaying);
    }

    [Fact]
    public void Shared_window_host_has_no_OpenAl_dependency_but_BED_still_does()
    {
        var root=AppContext.BaseDirectory;
        while(!File.Exists(System.IO.Path.Combine(root,"Bed.slnx")))root=Directory.GetParent(root)!.FullName;
        string[] Packages(string project)
        {
            using var json=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root,"src",project,"obj","project.assets.json")));
            return json.RootElement.GetProperty("libraries").EnumerateObject().Select(p=>p.Name.Split('/')[0]).ToArray();
        }
        Assert.DoesNotContain("Ahwoo.Brutal.OpenAl",Packages("Bed.Editor.App"),StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("NVorbis",Packages("Bed.Editor.App"),StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Ahwoo.Brutal.OpenAl",Packages("Bed.Launcher"),StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NVorbis",Packages("Bed.Launcher"),StringComparer.OrdinalIgnoreCase);
        using var r=new Rig();
        using var plain=new AssetCatalogProvisioner().Open(new(System.IO.Path.Combine(r.Root,"data"),System.IO.Path.Combine(r.Root,"schemas")){Root=r.Root});
        Assert.NotNull(plain);Assert.Null(plain.AudioPreview);
    }

}
