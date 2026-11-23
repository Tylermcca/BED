using System.Diagnostics;
using Bed.Editor.Gum;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// GUM-PICTURE: Gum draws its own screens to pixels in BED's process (no window). Fixtures as GumCatalogTests.
[Collection(ImGuiCollection.Name)]   // Gum's drawing service is one per process, shared with the panel tests
public sealed class GumScreenPictureTests(ITestOutputHelper output)
{
    private static string Original => Path.Combine(GumFixtures.Folder("Original"), "Original.gumj");

    private static (byte R, byte G, byte B, byte A) At(GumScreenPicture picture, int x, int y)
    {
        var i = (y * picture.Width + x) * 4;
        var p = picture.Pixels;
        return (p[i], p[i + 1], p[i + 2], p[i + 3]);
    }

    private static string CopyOf(string fixture)
    {
        var to = Path.Combine(Path.GetTempPath(), "bed-gum-picture-" + Guid.NewGuid().ToString("N"));
        var from = GumFixtures.Folder(fixture);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return to;
    }

    [Theory]
    [InlineData("Original", "Original.gumj")]
    [InlineData("OriginalXml", "Original.gumx")]
    public void The_fixture_screen_draws_at_the_projects_size_with_its_panel_colour(string folder, string file)
    {
        using var picture = new GumScreenPicture(Path.Combine(GumFixtures.Folder(folder), file));
        Assert.True(picture.Show("Preview"), picture.Problem);
        Assert.Equal("", picture.Problem);
        Assert.True(picture.Frame(0));
        Assert.Equal((800, 600), (picture.Width, picture.Height));
        Assert.Equal(800 * 600 * 4, picture.Pixels.Length);
        // The fixture's Panel: a 260 x 120 rectangle at (20, 20), coloured (20, 60, 90).
        Assert.Equal(((byte)20, (byte)60, (byte)90, (byte)255), At(picture, 30, 30));
        // Outside it, the background shows.
        Assert.Equal(((byte)0x30, (byte)0x30, (byte)0x30, (byte)255), At(picture, 700, 500));
        Assert.False(picture.Frame(1 / 60.0));   // nothing changed, so no new picture
        Assert.Equal(1, picture.Version);
    }

    [Fact]
    public void Update_only_frames_reload_and_expose_current_parts_then_paint_fresh()
    {
        var folder=CopyOf("Original");
        try
        {
            using var picture=new GumScreenPicture(Path.Combine(folder,"Original.gumj"));
            Assert.True(picture.Show("Preview"),picture.Problem);
            Assert.False(picture.Frame(0,false));Assert.True(picture.Drawn);Assert.Equal(0,picture.Version);
            Assert.True(picture.Frame(.01));var before=picture.Pixels.ToArray();var version=picture.Version;
            var path=Path.Combine(folder,"Screens","Preview.gusj");
            var root=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            foreach(var v in root["States"]![0]!["Variables"]!.AsArray())
            {
                if(v!["Name"]!.GetValue<string>()=="Panel.X")v["ValueAsFloat"]=80;
                if(v["Name"]!.GetValue<string>()=="Panel.Red")v["ValueAsInt"]=200;
            }
            File.WriteAllText(path,root.ToJsonString());
            var clock=Stopwatch.StartNew();var t=.01;
            while((picture.Saves==0||!picture.Parts().Any(p=>p.Name=="Panel"&&p.X==80))&&clock.Elapsed<TimeSpan.FromSeconds(10))
            {Thread.Sleep(50);Assert.False(picture.Frame(t+=.05,false));}
            Assert.True(picture.Saves>0);Assert.Equal(80,Assert.Single(picture.Parts(),p=>p.Name=="Panel").X);
            // Another caller takes the shared service; an update-only frame must restore this screen too.
            using(var other=new GumScreenPicture(Original))Assert.True(other.Show("Preview"),other.Problem);
            Assert.False(picture.Frame(t+=.05,false));Assert.True(picture.Drawn);
            Assert.Equal(80,Assert.Single(picture.Parts(),p=>p.Name=="Panel").X);
            Assert.Equal(version,picture.Version);Assert.True(before.AsSpan().SequenceEqual(picture.Pixels));
            Assert.True(picture.Frame(t+.05,true));Assert.Equal(version+1,picture.Version);
            Assert.Equal(((byte)200,(byte)60,(byte)90,(byte)255),At(picture,90,30));
            Assert.Equal(((byte)0x30,(byte)0x30,(byte)0x30,(byte)255),At(picture,30,30));
        }
        finally { try { Directory.Delete(folder,true); } catch(IOException) { } }
    }

    [Fact]
    public void Resizing_keeps_the_live_screen_and_matches_a_fresh_layout_and_paint()
    {
        using var picture = new GumScreenPicture(Original);
        Assert.True(picture.Show("Preview"), picture.Problem);
        picture.Set("Panel.X", 35f); picture.Frame(0);
        foreach (var (width, height, zoom) in new[] { (480, 320, .5f), (900, 675, 1.5f), (800, 600, 1f) })
        {
            var live = Gum.GumService.Default.Root.Children.Single();
            picture.Resize(width, height, zoom);
            Assert.False(picture.Frame(.1, false));
            Assert.True(picture.Drawn, picture.Problem);
            Assert.Same(live, Gum.GumService.Default.Root.Children.Single());
            var parts = picture.Parts().ToArray();
            picture.Frame(.2);
            var pixels = picture.Pixels.ToArray();
            using (var fresh = new GumScreenPicture(Original, width, height))
            {
                fresh.Resize(width, height, zoom); Assert.True(fresh.Show("Preview"), fresh.Problem);
                fresh.Set("Panel.X", 35f); fresh.Frame(.2);
                Assert.Equal(parts, fresh.Parts());
                Assert.True(pixels.AsSpan().SequenceEqual(fresh.Pixels), $"Resize differed from a fresh screen at {width}x{height}, zoom {zoom}.");
            }
            // An unrelated owner still uses the full restore path, including our retained values.
            picture.Frame(.3); Assert.Equal(parts, picture.Parts());
            Assert.True(pixels.AsSpan().SequenceEqual(picture.Pixels));
        }
    }

    [Fact]
    public void Same_project_switches_keep_the_service_but_rebuild_exact_screens_and_follow_saves()
    {
        var folder=CopyOf("Original");
        try
        {
            var project=Path.Combine(folder,"Original.gumj");
            var second=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"Screens","Preview.gusj")))!;second["Name"]="Second";
            foreach(var v in second["States"]![0]!["Variables"]!.AsArray())if(v!["Name"]!.GetValue<string>()=="Panel.Y")v["ValueAsFloat"]=50;
            File.WriteAllText(Path.Combine(folder,"Screens","Second.gusj"),second.ToJsonString());
            var definition=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(project))!;
            definition["ScreenReferences"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject{["Name"]="Second",["ElementType"]=0,["LinkType"]=0});File.WriteAllText(project,definition.ToJsonString());
            using var a=new GumScreenPicture(project,480,320);a.Resize(480,320,.5f);Assert.True(a.Show("Preview"));a.Set("Panel.Red",40);a.Frame(0);
            var aParts=a.Parts().ToArray();var aPixels=a.Pixels.ToArray();
            using var b=new GumScreenPicture(project,900,675);b.Resize(900,675,1.5f);Assert.True(b.Show("Second"));b.Set("Panel.Red",200);b.Frame(0);
            var bParts=b.Parts().ToArray();var bPixels=b.Pixels.ToArray();var live=Gum.GumService.Default.Root;
            for(var i=0;i<6;i++)
            {
                var picture=i%2==0?a:b;var parts=i%2==0?aParts:bParts;var pixels=i%2==0?aPixels:bPixels;
                picture.Set("Panel.Red",i%2==0?40:200);Assert.Empty(picture.Parts());
                Assert.False(picture.Frame(.1+i*.05,false));Assert.Same(live,Gum.GumService.Default.Root);Assert.Single(live.Children);
                Assert.Equal(parts,picture.Parts());picture.Frame(.11+i*.05);
                Assert.True(pixels.AsSpan().SequenceEqual(picture.Pixels),"Switching differed from the original cold picture.");
            }
            Assert.Equal(2,GumCatalog.Read(project).Screens.Count);a.Frame(.5,false);
            Assert.NotSame(live,Gum.GumService.Default.Root);Assert.Equal(aParts,a.Parts());
            var path=Path.Combine(folder,"Screens","Second.gusj");var root=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            foreach(var v in root["States"]![0]!["Variables"]!.AsArray())if(v!["Name"]!.GetValue<string>()=="Panel.Y")v["ValueAsFloat"]=80;
            File.WriteAllText(path,root.ToJsonString());var clock=Stopwatch.StartNew();var t=.5;
            while(a.Saves+b.Saves==0&&clock.Elapsed<TimeSpan.FromSeconds(10)){Thread.Sleep(50);a.Frame(t+=.05,false);b.Frame(t+=.05,false);}
            Assert.True(a.Saves+b.Saves>0);a.Frame(t+=.05);Assert.Single(Gum.GumService.Default.Root.Children);Assert.Equal(10,Assert.Single(a.Parts(),p=>p.Name=="Panel").Y);
            b.Frame(t+=.05);Assert.Single(Gum.GumService.Default.Root.Children);Assert.Equal(120,Assert.Single(b.Parts(),p=>p.Name=="Panel").Y);
            Assert.Equal((byte)40,At(a,20,15).R);Assert.Equal((byte)200,At(b,40,135).R);
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public void A_new_picture_of_the_project_Gum_has_open_joins_it_without_reading_it_again_and_draws_as_a_cold_one()
    {
        // A card game, 2026-11-21: each of a game's screens shown for the first time took Gum down and read the whole project again, a
        // frame of half a second or more. A new picture now joins the open project as a known one does.
        var folder=CopyOf("Original");
        try
        {
            var project=Path.Combine(folder,"Original.gumj");
            var second=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"Screens","Preview.gusj")))!;second["Name"]="Second";
            foreach(var v in second["States"]![0]!["Variables"]!.AsArray())if(v!["Name"]!.GetValue<string>()=="Panel.Y")v["ValueAsFloat"]=50;
            File.WriteAllText(Path.Combine(folder,"Screens","Second.gusj"),second.ToJsonString());
            var definition=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(project))!;
            definition["ScreenReferences"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject{["Name"]="Second",["ElementType"]=0,["LinkType"]=0});File.WriteAllText(project,definition.ToJsonString());
            byte[] cold;GumPart[] coldParts;
            using(var alone=new GumScreenPicture(project,900,675)){alone.Resize(900,675,1.5f);Assert.True(alone.Show("Second"));alone.Set("Panel.Red",200);alone.Frame(0);cold=alone.Pixels.ToArray();coldParts=[..alone.Parts()];}
            using var a=new GumScreenPicture(project,480,320);a.Resize(480,320,.5f);Assert.True(a.Show("Preview"));a.Frame(0);
            var live=Gum.GumService.Default.Root;
            using var b=new GumScreenPicture(project,900,675);b.Resize(900,675,1.5f);Assert.True(b.Show("Second"));
            Assert.Same(live,Gum.GumService.Default.Root);Assert.Equal("",b.Problem);
            b.Set("Panel.Red",200);b.Frame(0);
            Assert.Same(live,Gum.GumService.Default.Root);
            Assert.Equal(coldParts,b.Parts());
            Assert.True(cold.AsSpan().SequenceEqual(b.Pixels),"The joined picture differed from a cold one.");
            a.Frame(.1);Assert.Same(live,Gum.GumService.Default.Root);Assert.Single(live.Children);
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public void A_picture_switched_back_to_puts_up_the_screen_it_built_and_draws_as_a_cold_one()
    {
        // A card game, 2026-11-21: switching between its table's words and its score page built the screen afresh, and checked the whole
        // project for loops, every time: 25-400 ms a switch. A picture switched back to puts up the screen it built before.
        var folder=CopyOf("Original");
        try
        {
            var project=Path.Combine(folder,"Original.gumj");
            byte[] cold;
            using(var alone=new GumScreenPicture(project,480,320)){Assert.True(alone.Show("Preview"));alone.Set("Panel.Red",120);alone.Frame(0);cold=alone.Pixels.ToArray();}
            using var a=new GumScreenPicture(project,480,320);Assert.True(a.Show("Preview"));a.Frame(0);
            var built=Assert.Single(Gum.GumService.Default.Root.Children);
            using var b=new GumScreenPicture(project,900,675);Assert.True(b.Show("Preview"));b.Frame(0);
            Assert.NotSame(built,Assert.Single(Gum.GumService.Default.Root.Children));
            a.Set("Panel.Red",120);a.Frame(.1);   // set while b had Gum: applied as a comes back
            Assert.Same(built,Assert.Single(Gum.GumService.Default.Root.Children));
            Assert.Equal("",a.Problem);
            Assert.True(cold.AsSpan().SequenceEqual(a.Pixels),"The picture switched back to differed from a cold one.");
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public void A_project_with_a_missing_screen_file_still_shows_its_other_screens_and_says_what_is_missing()
    {
        using var picture = new GumScreenPicture(Path.Combine(GumFixtures.Folder("Deps"), "Deps.gumj"));
        Assert.True(picture.Show("Menu"), picture.Problem);
        Assert.Contains("Gone", picture.Problem);
        Assert.Contains("missing", picture.Problem);
        Assert.True(picture.Frame(0));
        Assert.True(picture.Pixels.Length > 0);

        Assert.False(picture.Show("Gone"));
        Assert.Contains("isn't in the Gum project's folder", picture.Problem);
        Assert.False(picture.Frame(0));

        Assert.False(picture.Show("Nowhere"));
        Assert.Contains("no screen called Nowhere", picture.Problem);
    }

    [Fact]
    public void A_screen_with_looks_lists_them_and_shows_the_chosen_one()
    {
        var folder = CopyOf("Original");
        try
        {
            var screen = Path.Combine(folder, "Screens", "Preview.gusj");
            File.WriteAllText(screen, File.ReadAllText(screen).Replace("\"Categories\": []",
                """
                "Categories": [ { "Name": "Mood", "States": [
                  { "Name": "Calm", "Variables": [], "VariableLists": [] },
                  { "Name": "Warm", "Variables": [ { "Name": "Panel.Red", "Type": "int", "SetsValue": true, "ValueAsInt": 250 } ], "VariableLists": [] } ] } ]
                """));
            using var picture = new GumScreenPicture(Path.Combine(folder, "Original.gumj"));
            Assert.True(picture.Show("Preview", new GumLook("Mood", "Warm")), picture.Problem);
            Assert.Equal(["Mood: Calm", "Mood: Warm"], picture.Looks.Select(l => l.Label));
            Assert.Equal(new GumLook("Mood", "Warm"), picture.Look);
            picture.Frame(0);
            Assert.Equal((byte)250, At(picture, 30, 30).R);
            Assert.True(picture.Show("Preview"));
            Assert.Null(picture.Look);
            picture.Frame(0);
            Assert.Equal((byte)20, At(picture, 30, 30).R);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_picture_file_named_relative_to_the_project_draws_when_another_file_is_missing_too()
    {
        // Gum's own start (InitializeCore) gives up on a project with a missing file; BED's restatement of it (Ready)
        // carries on, and must still set Gum's folder to the project's so a picture named "Images/red.png" is found.
        var folder = CopyOf("Original");
        try
        {
            File.Copy(Path.Combine(GumFixtures.Folder("Deps"), "Standards", "Sprite.gutj"), Path.Combine(folder, "Standards", "Sprite.gutj"));
            Directory.CreateDirectory(Path.Combine(folder, "Images"));
            using (var red = new SkiaSharp.SKBitmap(64, 64))
            {
                red.Erase(new SkiaSharp.SKColor(255, 0, 0));
                using var png = red.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(folder, "Images", "red.png"), png.ToArray());
            }
            var project = Path.Combine(folder, "Original.gumj");
            File.WriteAllText(project, File.ReadAllText(project).Replace("\"StandardElementReferences\": [",
                "\"StandardElementReferences\": [ { \"Name\": \"Sprite\", \"ElementType\": 2, \"LinkType\": 0 },"));
            var screen = Path.Combine(folder, "Screens", "Preview.gusj");
            File.WriteAllText(screen, File.ReadAllText(screen)
                .Replace("\"Instances\": [", "\"Instances\": [ { \"Name\": \"Pic\", \"BaseType\": \"Sprite\" },")
                .Replace("\"Variables\": [", """
                    "Variables": [
                      { "Name": "Pic.SourceFile", "Type": "string", "SetsValue": true, "IsFile": true, "ValueAsString": "Images/red.png" },
                      { "Name": "Pic.X", "Type": "float", "SetsValue": true, "ValueAsFloat": 400 },
                      { "Name": "Pic.Y", "Type": "float", "SetsValue": true, "ValueAsFloat": 300 },
                      { "Name": "Pic.Width", "Type": "float", "SetsValue": true, "ValueAsFloat": 64 },
                      { "Name": "Pic.Height", "Type": "float", "SetsValue": true, "ValueAsFloat": 64 },
                    """));

            byte[] whole;
            using (var picture = new GumScreenPicture(project))
            {
                Assert.True(picture.Show("Preview"), picture.Problem);
                Assert.Equal("", picture.Problem);
                Assert.True(picture.Frame(0));
                Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), At(picture, 420, 320));
                whole = picture.Pixels.ToArray();
            }

            File.WriteAllText(project, File.ReadAllText(project).Replace("\"ComponentReferences\": []",
                "\"ComponentReferences\": [ { \"Name\": \"Ghost\", \"ElementType\": 1, \"LinkType\": 0 } ]"));
            using (var picture = new GumScreenPicture(project))
            {
                Assert.True(picture.Show("Preview"), picture.Problem);
                Assert.Contains("Ghost", picture.Problem);
                Assert.True(picture.Frame(0));
                Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), At(picture, 420, 320));
                Assert.True(whole.AsSpan().SequenceEqual(picture.Pixels), "the picture differs once another file is missing");
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void Gum_reads_linked_values_written_as_sums_in_every_picture_even_after_another_picture_took_Gum_down()
    {
        // Taking Gum's drawing down clears its expression reader (ElementSaveExtensions.ClearRegistrations), so each load
        // turns it on again, as Gum asks ("after GumService.Initialize()").
        using (var first = new GumScreenPicture(Original)) Assert.True(first.Show("Preview"), first.Problem);
        using var picture = new GumScreenPicture(Original);
        Assert.True(picture.Show("Preview"), picture.Problem);
        Assert.True(GumLibrary.Run(() => global::GumRuntime.ElementSaveExtensions.CustomEvaluateExpression is not null));
    }

    [Fact]
    public void A_project_that_is_not_there_does_not_throw()
    {
        using var picture = new GumScreenPicture(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid(), "x.gumj"));
        Assert.False(picture.Show("Preview"));
        Assert.Contains("isn't there", picture.Problem);
        Assert.False(picture.Frame(0));
    }

    [Fact]
    public void Saving_the_screen_file_redraws_the_picture_by_itself()
    {
        var folder = CopyOf("Original");
        try
        {
            using var picture = new GumScreenPicture(Path.Combine(folder, "Original.gumj"));
            Assert.True(picture.Show("Preview"), picture.Problem);
            picture.Frame(0);
            Assert.Equal((byte)20, At(picture, 30, 30).R);
            Assert.Equal(0, picture.Saves);

            var screen = Path.Combine(folder, "Screens", "Preview.gusj");
            var text = File.ReadAllText(screen);
            var red = text.IndexOf("\"Panel.Red\"", StringComparison.Ordinal);
            var value = text.IndexOf("\"ValueAsInt\": 20", red, StringComparison.Ordinal);
            File.WriteAllText(screen, text[..value] + "\"ValueAsInt\": 200" + text[(value + "\"ValueAsInt\": 20".Length)..]);

            var clock = Stopwatch.StartNew();
            var t = 0.0;
            while (At(picture, 30, 30).R != 200 && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(50);
                picture.Frame(t += 0.05);
            }
            Assert.Equal(((byte)200, (byte)60, (byte)90, (byte)255), At(picture, 30, 30));
            Assert.True(picture.Saves > 0);   // so the panel can say "Updated from Gum just now"
            output.WriteLine($"Redrawn {clock.ElapsedMilliseconds} ms after the save.");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Gum's reload follows a loop of parts until the stack runs out, which ends the program with no message (Gum preview 5).
    // Gum now skips recursive parts itself; the rest stays drawn and a later save can repair the screen.
    private static void FrameUntil(GumScreenPicture picture, Func<bool> done, ref double t)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(50); picture.Frame(t += 0.05); }
    }

    [Fact]
    public void A_screen_whose_parts_contain_each_other_says_so_and_a_save_does_not_take_BED_down()
    {
        var folder = CopyOf("Deps");
        try
        {
            using var picture = new GumScreenPicture(Path.Combine(folder, "Deps.gumj"));
            Assert.True(picture.Show("Looped"), picture.Problem);
            Assert.Contains("LoopA, which uses LoopB, which uses LoopA", picture.Problem);
            Assert.Contains("leaves out", picture.Problem);
            var t = 0.0;
            picture.Frame(t);
            var button = Path.Combine(folder, "Components", "Button.gucj");
            File.WriteAllText(button, File.ReadAllText(button));   // a save Gum's watcher sees
            FrameUntil(picture, () => picture.Saves > 0, ref t);
            Assert.True(picture.Saves > 0);
            Assert.True(picture.Drawn, picture.Problem);
            Assert.InRange(picture.Parts().Count, 1, 100);
            Assert.Contains("LoopA", picture.Problem);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void Saving_a_loop_into_a_shown_screen_says_so_and_breaking_it_shows_the_screen_again()
    {
        var folder = CopyOf("Deps");
        try
        {
            using var picture = new GumScreenPicture(Path.Combine(folder, "Deps.gumj"));
            Assert.True(picture.Show("Menu"), picture.Problem);
            var t = 0.0;
            picture.Frame(t);
            var button = Path.Combine(folder, "Components", "Button.gucj");
            var text = File.ReadAllText(button);
            File.WriteAllText(button, text.Replace("\"Instances\": [", "\"Instances\": [ { \"Name\": \"Inner\", \"BaseType\": \"LoopA\" },"));
            FrameUntil(picture, () => picture.Problem.Contains("LoopA"), ref t);
            Assert.True(picture.Drawn, picture.Problem);
            Assert.InRange(picture.Parts().Count, 1, 100);
            Assert.Contains("LoopA", picture.Problem);

            File.WriteAllText(button, text);
            FrameUntil(picture, () => !picture.Problem.Contains("loop"), ref t);
            Assert.True(picture.Drawn, picture.Problem);
            Assert.DoesNotContain("loop", picture.Problem);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_screen_whose_file_comes_back_appears_by_itself()
    {
        var folder = CopyOf("Original");
        try
        {
            var screen = Path.Combine(folder, "Screens", "Preview.gusj");
            var text = File.ReadAllText(screen);
            File.Delete(screen);
            using var picture = new GumScreenPicture(Path.Combine(folder, "Original.gumj"));
            Assert.False(picture.Show("Preview"));
            Assert.Contains("isn't in the Gum project's folder", picture.Problem);
            picture.Frame(0);
            File.WriteAllText(screen, text);
            var clock = Stopwatch.StartNew();
            var t = 0.0;
            while (!picture.Drawn && clock.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(50); picture.Frame(t += 0.05); }
            Assert.True(picture.Drawn, picture.Problem);
            picture.Frame(t + 0.05);
            Assert.Equal(((byte)20, (byte)60, (byte)90, (byte)255), At(picture, 30, 30));
            Assert.Equal("", picture.Problem);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void Other_Gum_calls_between_frames_and_alongside_them_leave_the_picture_right()
    {
        using var picture = new GumScreenPicture(Original);
        Assert.True(picture.Show("Preview"), picture.Problem);
        picture.Frame(0);
        // Another Gum user (the screens list) changes Gum's shared state; the next frame loads the screen again.
        Assert.Single(GumCatalog.Read(Original).Screens);
        picture.Frame(0.1);
        Assert.Equal(((byte)20, (byte)60, (byte)90, (byte)255), At(picture, 30, 30));

        // The screens list read on another thread while frames draw: Gum's one lock keeps them apart.
        using var stop = new CancellationTokenSource();
        var reads = 0;
        Exception? failed = null;
        var reader = new Thread(() =>
        {
            try { while (!stop.IsCancellationRequested) { Assert.Single(GumCatalog.Read(Original).Screens); reads++; } }
            catch (Exception e) { failed = e; }
        });
        reader.Start();
        for (var i = 0; i < 40; i++)
        {
            picture.Frame(0.2 + i / 60.0);
            Assert.True(picture.Drawn, picture.Problem);
            Assert.Equal(((byte)20, (byte)60, (byte)90, (byte)255), At(picture, 30, 30));
        }
        stop.Cancel();
        reader.Join();
        Assert.Null(failed);
        Assert.True(reads > 0);
    }

    [Fact]
    public void Two_pictures_take_turns_and_dispose_lets_go_of_Gum()
    {
        using var a = new GumScreenPicture(Original);
        var b = new GumScreenPicture(Path.Combine(GumFixtures.Folder("Deps"), "Deps.gumj"));
        Assert.True(a.Show("Preview"));
        Assert.True(b.Show("Menu"));
        a.Frame(0); b.Frame(0); a.Frame(0.1);
        Assert.Equal(((byte)20, (byte)60, (byte)90, (byte)255), At(a, 30, 30));
        b.Dispose();
        Assert.Throws<ObjectDisposedException>(() => b.Frame(0));
        a.Frame(0.2);
        Assert.True(a.Drawn);
        a.Dispose();
    }

    [Fact]
    public void A_frame_at_1280_by_720_takes_a_few_milliseconds()
    {
        using var picture = new GumScreenPicture(Original, 1280, 720);
        Assert.True(picture.Show("Preview"), picture.Problem);
        Assert.Equal((1280, 720), (picture.Width, picture.Height));
        for (var i = 0; i < 10; i++) picture.Frame(i / 60.0);   // warm up
        var draw = TimeSpan.Zero;
        var whole = Stopwatch.StartNew();
        const int frames = 120;
        for (var i = 0; i < frames; i++) { picture.Frame(1 + i / 60.0); draw += picture.LastDrawTime; }
        whole.Stop();
        output.WriteLine($"Gum screen picture at 1280x720: {draw.TotalMilliseconds / frames:0.00} ms per frame to update and draw, " +
            $"{whole.Elapsed.TotalMilliseconds / frames:0.00} ms with reading the pixels back.");
        Assert.True(whole.Elapsed.TotalMilliseconds / frames < 50);
    }
}
