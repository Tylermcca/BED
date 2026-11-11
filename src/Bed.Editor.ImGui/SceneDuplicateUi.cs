namespace Bed.Editor.ImGui;

internal static class SceneDuplicateUi
{
    internal static bool Run(EditorModel<Scene> editor,DuplicateGroup duplicate,out string why)
        =>SceneObjectOperations.Duplicate(editor,duplicate,out why);
}
