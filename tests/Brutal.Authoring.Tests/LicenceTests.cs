using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Plug-in licences are optional information (the user: "Optional for the user, perhaps it's an extra level of
/// license protection or explanation. Perhaps a warning that there are incompatible licenses together").</summary>
public sealed class LicenceTests
{
    private static PluginManifest Manifest(string id, string? license) => PluginManifest.Read(license is null
        ? $$"""{"id":"{{id}}","name":"{{id}} tools","version":"1.0.0","api":"0.1"}"""
        : $$"""{"id":"{{id}}","name":"{{id}} tools","version":"1.0.0","api":"0.1","license":"{{license}}"}""");

    private sealed class Plugin(PluginManifest m) : IEditorPlugin<Scene>
    {
        public PluginManifest Manifest => m;
        public void Register(PluginRegistrar<Scene> add) { }
    }

    private static PluginHost<Scene> Host(string? project = null)
    {
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), Scene.Empty), SceneEditing.Outline);
        return new PluginHost<Scene>(editor, new Inspector<Scene>(editor), new Shortcuts(), null, project);
    }

    [Fact]
    public void ALicenceIsOptionalAndAMissingOneIsOnlyNoted()
    {
        Assert.Null(Manifest("plain", null).License);
        var host = Host();
        Assert.True(host.Add(new Plugin(Manifest("plain", null))));
        var w = Assert.Single(host.Plugins[0].Warnings);
        Assert.Equal(Severity.Warning, w.Severity);
        Assert.Equal("'plain tools' gives no licence; check its terms before you share or ship it", w.Message);
    }

    [Fact]
    public void AGivenLicenceIsStillCheckedAsAnExpression() =>
        Assert.Equal("$.license", Assert.Throws<DocumentFormatException>(() => Manifest("bad", "(MIT OR")).Path);

    [Theory]
    [InlineData("GPL-2.0-only", "Apache-2.0", true)]
    [InlineData("GPL-2.0-only", "GPL-3.0-or-later", true)]
    [InlineData("GPL-3.0-only", "LicenseRef-Studio", true)]
    [InlineData("AGPL-3.0-or-later", "proprietary", true)]
    [InlineData("GPL-2.0-or-later", "Apache-2.0", false)]   // "or later" can move to GPL-3.0, which accepts Apache-2.0
    [InlineData("GPL-3.0-only", "Apache-2.0", false)]
    [InlineData("MIT", "LicenseRef-Studio", false)]
    [InlineData("MPL-2.0", "proprietary", false)]
    [InlineData("MIT", "GPL-2.0-only", false)]
    public void KnownClashesAreFound(string a, string b, bool clash)
    {
        Assert.Equal(clash, Licences.Clash(a, b) is not null);
        Assert.Equal(clash, Licences.Clash(b, a) is not null);   // either order
    }

    [Fact]
    public void AnOrChoiceThatWorksIsEnoughAndAndNeedsAllToWork()
    {
        Assert.Null(Licences.Clash(Licences.Choices("GPL-2.0-only OR MIT"), Licences.Choices("Apache-2.0")));
        Assert.NotNull(Licences.Clash(Licences.Choices("GPL-2.0-only AND MIT"), Licences.Choices("Apache-2.0")));
        Assert.Equal([["Apache-2.0"], ["MIT", "Zlib"]], Licences.Choices("(Apache-2.0) OR (MIT AND Zlib)"));
        Assert.Equal([["GPL-2.0-or-later"]], Licences.Choices("GPL-2.0-or-later WITH Classpath-exception-2.0"));
    }

    [Fact]
    public void ClashingPluginsAreWarnedAboutNamingBothAndBothStayEnabled()
    {
        var host = Host();
        Assert.True(host.Add(new Plugin(Manifest("old", "GPL-2.0-only"))));
        Assert.True(host.Add(new Plugin(Manifest("new", "Apache-2.0"))));
        Assert.True(host.Plugins.All(p => p.Enabled));
        var w = Assert.Single(host.Plugins[1].Warnings);
        Assert.Equal("'new tools' (Apache-2.0) and 'old tools' (GPL-2.0-only): GPL-2.0-only can't be combined with Apache-2.0 "
            + "(GPL version 2 only doesn't accept their terms); check the licences before you distribute them together", w.Message);
    }

    [Fact]
    public void APluginThatCantShipWithTheProjectIsWarnedAbout()
    {
        var host = Host(project: "LicenseRef-MyStudio");
        Assert.True(host.Add(new Plugin(Manifest("copyleft", "GPL-3.0-or-later"))));
        Assert.Contains("can't ship with closed-source LicenseRef-MyStudio code", Assert.Single(host.Plugins[0].Warnings).Message);
        Assert.True(host.Add(new Plugin(Manifest("permissive", "MIT"))));
        Assert.Empty(host.Plugins[1].Warnings);
    }

    [Fact]
    public void AnUnknownLicenceSaysItCantBeChecked()
    {
        var host = Host();
        Assert.True(host.Add(new Plugin(Manifest("odd", "WTFPL"))));
        Assert.Equal("'odd tools' uses the licence 'WTFPL', which this editor can't check; check the licences before you distribute them together",
            Assert.Single(host.Plugins[0].Warnings).Message);
    }

    [Fact]
    public void ARefusedPluginGetsNoLicenceWarnings()
    {
        var host = Host();
        Assert.True(host.Add(new Plugin(Manifest("twice", "GPL-2.0-only"))));
        Assert.False(host.Add(new Plugin(Manifest("twice", "Apache-2.0"))));   // same id: refused
        Assert.Empty(host.Plugins[1].Warnings);
    }
}
