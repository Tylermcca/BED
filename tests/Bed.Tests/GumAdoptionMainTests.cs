using Bed.Editor.Gum;
using Gum.Managers;
using Gum.ProjectServices;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class GumAdoptionMainTests
{
    [Fact]
    public void Checker_of_another_project_keeps_the_live_picture_project_and_pixels()
    {
        var other = GumLibrary.Run(() =>
        {
            var result = GumLibrary.Load(Path.Combine(GumFixtures.Folder("Deps"), "Deps.gumj"));
            GumLibrary.Release();
            return result.Project!;
        });
        using var picture = new GumScreenPicture(Path.Combine(GumFixtures.Folder("Original"), "Original.gumj"));
        Assert.True(picture.Show("Preview"), picture.Problem);
        picture.Frame(0);
        var pixels = picture.Pixels.ToArray();
        var active = ObjectFinder.Self.GumProjectSave;
        var errors = new HeadlessErrorChecker(new DefaultTypeResolver(), []).GetAllErrors(other);
        Assert.NotEmpty(errors);
        Assert.Same(active, ObjectFinder.Self.GumProjectSave);
        picture.Frame(0.1);
        Assert.Equal(pixels, picture.Pixels.ToArray());
        Assert.True(picture.Drawn, picture.Problem);
    }
    [Fact]
    public void Published_picture_types_come_from_the_Skia_loader()
    {
        var types = RenderingLibrary.Content.ContentLoader.SupportedImageExtensions;
        foreach (var expected in new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "wbmp" })
            Assert.Contains(expected, types);
        Assert.DoesNotContain("svg", types); // Gum loads SVG through its own element, not a raster Sprite.
        Assert.DoesNotContain(".png", types); // API returns extensions without the dot.
    }

}
