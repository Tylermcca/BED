using System.Numerics;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;

namespace LightsPlugin;

/// <summary>Lights for the general scene: a light is a scene object whose <c>kind</c> property is <c>light</c>.</summary>
public sealed class Lights : IEditorPlugin<Scene>
{
    /// <inheritdoc />
    public PluginManifest Manifest { get; } = PluginManifest.Read(
        new StreamReader(typeof(Lights).Assembly.GetManifestResourceStream("plugin.json")!).ReadToEnd());

    /// <inheritdoc />
    public void Register(PluginRegistrar<Scene> add) => add
        .Command<AddLight>("lights.add", 1, "Add light")
        .ObjectType(new ObjectType<Scene, SceneObject>("lights.light", "Light", (d, id) => d.Find(id) is { } o && IsLight(o) ? o : null,
        [
            Number("intensity", "Intensity", 1, "How bright it is. 1 is a household bulb."),
            Number("range", "Range", 10, "How far it reaches, in metres."),
        ]))
        .Workspace(new Workspace("lights.lighting", "Lighting", ["lights.add", "scene.move"]))
        .Panel(new Panel("lights.overview", "Lights", () =>
        {
            var lights = add.Editor.Session.Current.Objects.Where(IsLight).ToList();
            ImGui.Text(lights.Count == 0 ? "No lights yet. Alt+L adds one." : $"{lights.Count} light(s)");
            foreach (var o in lights) ImGui.BulletText($"{o.Id}: intensity {(o.Properties.TryGetValue("intensity", out var i) ? i.GetDouble() : 1):0.##}");
        }))
        .Maker("lights.add", e => new AddLight(NextId(e.Session.Current), Vector3.Zero))
        .Shortcut("lights.add", "Alt+L");

    private static string NextId(Scene s) => Enumerable.Range(1, int.MaxValue).Select(n => $"light_{n}").First(id => s.Find(id) is null);

    private static bool IsLight(SceneObject o) => o.Properties.TryGetValue("kind", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == "light";

    private static PropertyDescriptor<Scene, SceneObject> Number(string name, string label, double fallback, string help) =>
        new(name, label, PropertyKind.Number, o => o.Properties.TryGetValue(name, out var v) && v.TryGetDouble(out var x) ? x : fallback,
            (o, v) => new SetSceneProperty(o.Id, name, JsonSerializer.SerializeToElement(Convert.ToDouble(v))),
            (_, _) => fallback, Help: help);
}

/// <summary>Adds a light at <see cref="Position"/>.</summary>
public sealed record AddLight(string Id, Vector3 Position, double Intensity = 1) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => new AddObject(SceneObject.At(Id, null, Position) with
    {
        Properties = SceneObject.At(Id, null, Position).Properties
            .Add("kind", JsonSerializer.SerializeToElement("light")).Add("intensity", JsonSerializer.SerializeToElement(Intensity)),
    }).Apply(d);
}
