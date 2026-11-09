using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED: window-owned inspector state, using self-authored data and one serial ImGui context.
[Collection(ImGuiCollection.Name)]
public sealed class InspectorControllerTests
{
    private static (EditorModel<DataDocument> Model, Inspector<DataDocument> Inspector) Model()
    {
        var schemas = new JsonSchemas();
        schemas.Add("neutral", "{}");
        var session = DataKit.Session(new DataFormat().Read("""{"items":[{"title":"first"},{"title":"second"}]}"""), schemas, "neutral");
        var model = new EditorModel<DataDocument>(session, DataEditing.Outline);
        model.Select("/items/0");
        return (model, new Inspector<DataDocument>(model).Add(DataEditing.ObjectType(schemas, "neutral")));
    }

    [Fact]
    public void Searches_and_status_belong_to_the_window_even_with_a_shared_model()
    {
        var (model, inspector) = Model();
        var first = new EditorWindow<DataDocument>(model, inspector) { Searcher = (d, q) => DataSearch.Find(d, q) };
        var second = new EditorWindow<DataDocument>(model, inspector) { Searcher = (d, q) => DataSearch.Find(d, q) };
        Assert.Equal(1, first.Find("first"));
        Assert.Empty(second.Found);
        Assert.Equal("", second.Status);
        Assert.Equal(1, second.Find("second"));
        Assert.Equal("/items/0/title", Assert.Single(first.Found).Pointer);
        Assert.Equal("/items/1/title", Assert.Single(second.Found).Pointer);
        Assert.True(first.Reveal("/items/0/title"));
        Assert.Equal("At /items/0/title.", first.Status);
        Assert.Equal("Found 1.", second.Status);
        Assert.Equal(0, model.Session.History.UndoCount);
        Assert.False(model.Session.Dirty);
    }

    [Fact]
    public void No_inspector_or_searcher_preserves_the_existing_refusal_and_noop()
    {
        var (model, _) = Model();
        var window = new EditorWindow<DataDocument>(model);
        Assert.False(window.Reveal("/items/0/title"));
        Assert.Equal("There's no inspector here to show that.", window.Status);
        Assert.Equal(0, window.Find("first"));
        Assert.Equal("There's no inspector here to show that.", window.Status);
        Assert.Empty(window.Found);
        Assert.Null(window.InspectorPlace("title"));
        Assert.Equal(0, window.FieldsHeld);
    }

    [Fact]
    public void Draft_storage_and_control_positions_are_owned_by_each_window()
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var (model, inspector) = Model();
        var first = new EditorWindow<DataDocument>(model, inspector);
        var second = new EditorWindow<DataDocument>(model, inspector);
        var row = Assert.Single(inspector.Rows());
        host.Frames(first.Draw, 4);
        Assert.True(first.FieldsHeld > 0);
        Assert.NotNull(first.InspectorPlace(row.Id));
        Assert.Equal(0, second.FieldsHeld);
        Assert.Null(second.InspectorPlace(row.Id));
        host.Frames(second.Draw, 4);
        Assert.True(second.FieldsHeld > 0);
        Assert.NotNull(second.InspectorPlace(row.Id));
        model.Select();
        host.Frames(first.Draw, 4);
        Assert.Null(first.InspectorPlace(row.Id));
        Assert.NotNull(second.InspectorPlace(row.Id)); // Last drawn coordinates, not global state.
        Assert.Equal(0, model.Session.History.UndoCount);
        Assert.False(model.Session.Dirty);
    }
}
