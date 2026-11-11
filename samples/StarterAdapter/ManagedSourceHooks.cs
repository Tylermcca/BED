using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace StarterAdapter;

/// <summary>Self-authored examples of the managed source hooks; independent of the installed sample adapter.</summary>
public static class ManagedSourceHooks
{
    // DOC19 ADMIT BEGIN
    /// <summary>Admits one index and nested entries; other files never become project documents.</summary>
    public static DataProject OpenSources(ProjectPaths paths, bool readOnly = false)
    {
        var project = DataProject.Open(paths.Data, paths.Schemas, readOnly,
            readOnlyFile: null, rules: null, recursive: true,
            admit: name => name == "index.json"
                || name.StartsWith("entries/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));
        project.SchemaFor = _ => "source.schema.json";
        return project;
    }
    // DOC19 ADMIT END

    // DOC19 SAVED BEGIN
    /// <summary>Observes committed dirty saves on the save caller's thread; dispose on the owner to unsubscribe.</summary>
    public static IDisposable AfterSaved(DataProject project, Action<IReadOnlyList<string>> react)
    {
        void Saved(IReadOnlyList<DataFile> files) => react(files.Select(file => file.Name).ToArray());
        project.FilesSaved += Saved;
        return new Subscription(() => project.FilesSaved -= Saved);
    }
    // DOC19 SAVED END

    // DOC19 TICK BEGIN
    /// <summary>Delivers one completed task from the workspace's owner-thread pump; disposal unhooks, not cancels, it.</summary>
    public static IDisposable DeliverOnTick<T>(DataWorkspace workspace, Task<T> work, Action<T> accept, Action<string> failed)
    {
        void Tick()
        {
            if (!work.IsCompleted) return;
            workspace.Ticked -= Tick;
            if (work.IsCompletedSuccessfully) accept(work.Result);
            else failed(work.IsCanceled ? "Background work was cancelled." : work.Exception!.GetBaseException().Message);
        }
        workspace.Ticked += Tick;
        return new Subscription(() => workspace.Ticked -= Tick);
    }
    // DOC19 TICK END

    // DOC19 CHECK BEGIN
    /// <summary>Checks the proposed persisted text total; other unsaved drafts do not count in a single-file save.</summary>
    public static void ConfigureBackgroundCheck(DataWorkspace workspace, int maximumCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCharacters);
        var project = workspace.Project;
        project.SaveCheck = saving =>
        {
            var drafts = project.BeingSaved(saving).ToDictionary(file => file.Name, file => file.Session!.TextToSave);
            var total = 0;
            foreach (var file in project.Files)
            {
                var text = drafts.TryGetValue(file.Name, out var draft) ? draft : File.ReadAllText(file.Path);
                using var json = JsonDocument.Parse(text);
                total = checked(total + json.RootElement.GetProperty("text"u8).GetString()!.Length);
            }
            return total <= maximumCharacters ? null : $"Combined source text exceeds {maximumCharacters} characters.";
        };
        workspace.BackgroundSaves = true;
    }
    // DOC19 CHECK END

    // DOC19 TEXT BEGIN
    /// <summary>Edits /text through the file's existing session, including Undo refresh and read-only refusal.</summary>
    public static Panel TextPanel(DataFile file, EditorModel<DataDocument> editor)
    {
        if (!ReferenceEquals(file.Session, editor.Session)) throw new ArgumentException("Use the file's own editor session.", nameof(editor));
        var buffer = new ImInputString(4096, ""u8);
        var message = "";
        return new Panel("sample.source.text", "Source text", () =>
        {
            var value = editor.Session.Current.At("/text")!.Value.GetString()!;
            if (System.Text.Encoding.UTF8.GetByteCount(value) >= buffer.Capacity)
            { Ui.TextWrapped("This example's source text exceeds its buffer; increase the supported limit before editing."u8); return; }
            var id = ImGui.GetID("Source prose"u8).Value;
            void Reset()
            {
                if (ImGui.GetCurrentContext().ActiveId.Value == id) ImGui.Internal.ClearActiveID();
                buffer.SetValue(value.AsSpan());
            }
            if (buffer.ToString() != value) Reset();
            var why = file.ReadOnlyReason;
            Ui.BeginDisabled(why is not null, why);
            if (Ui.InputTextMultiline("Source prose"u8, buffer, new float2(-1, 140), why is null ? 0 : ImGuiInputTextFlags.ReadOnly))
            {
                if (editor.Execute(new SetValue("/text", JsonSerializer.SerializeToElement(buffer.ToString())))) message = "";
                else { message = string.Join("; ", editor.Refused.Concat(editor.Session.LastIssues).Select(issue => issue.Message)); Reset(); }
            }
            Ui.EndDisabled();
            if (why is not null) Ui.TextWrapped(why);
            if (message.Length > 0) Ui.Status(message, "sample.source.text.refusal");
        }, Side: PanelSide.Centre);
    }
    // DOC19 TEXT END

    private sealed class Subscription(Action stop) : IDisposable
    {
        private Action? unsubscribe = stop;
        public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
    }
}
