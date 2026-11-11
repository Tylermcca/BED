using System.Collections.Immutable;
using Bed;
using Bed.Editor;

namespace Ladder;

// MIXED: explicit type/command registration and workspace grouping from the existing API.
// This is ordinary trusted C# registration, not a discovered or loaded plug-in.
public static class NotebookExtension
{
    public const string WorkspaceId = "ladder.notes";
    public static void Register(EditorModel<Notebook> editor, Inspector<Notebook> inspector, Shortcuts shortcuts)
    {
        Rung1Describe.Register(inspector);
        inspector.Add(new ObjectType<Notebook, Note>("ladder.note", "Note",
            (d, id) => d.Notes.FirstOrDefault(n => n.Id == id),
            [new("label", "Label", PropertyKind.Text, n => n.Label,
                (n, value) => new RenameNote(n.Id, (string)value!), Help: "A short note title.")]));
#pragma warning disable BRUTALCORE0011 // command ids, not JSON keys; Register has no UTF-8 overload
        editor.Session.Catalog.Register<AddNote>("ladder.note_add", 1, "Add note")
            .Register<RenameNote>("ladder.note_label", 1, "Rename note");
#pragma warning restore BRUTALCORE0011
        DoubleCounter.Register(editor.Session.Catalog);
        editor.Add(new Workspace(WorkspaceId, "Notes and counters",
            ["ladder.note_add", "ladder.note_label", DoubleCounter.CommandId]));
        shortcuts.Default(DoubleCounter.CommandId, "Alt+D");
    }
    public sealed record AddNote(string Id, string Label) : ICommand<Notebook>
    {
        public CommandResult<Notebook> Apply(Notebook d) =>
            CommandResult<Notebook>.Done(d with { Notes = d.Notes.Add(new Note(Id, Label)) });
    }
    public sealed record RenameNote(string Id, string Label) : ICommand<Notebook>
    {
        public CommandResult<Notebook> Apply(Notebook d) => d.Notes.Any(n => n.Id == Id)
            ? CommandResult<Notebook>.Done(d with
                { Notes = d.Notes.Select(n => n.Id == Id ? n with { Label = Label } : n).ToImmutableArray() })
            : CommandResult<Notebook>.Refused(Issue.Error("Choose an existing note.", Id, "$.notes"));
    }
}

