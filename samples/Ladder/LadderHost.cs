using Brutal.Authoring;
using Brutal.Authoring.Editor;

namespace Ladder;

// Shared host, not a hidden part of a rung: see the guide's bootstrap inventory.
public static class LadderHost
{
    public static EditorModel<Notebook> Open(Notebook? document = null)
    {
#pragma warning disable BRUTALCORE0011 // command ids, not JSON keys; Register has no UTF-8 overload
        var catalog = new CommandCatalog<Notebook>()
            .Register<SetCounterLabel>("ladder.label", 1, "Rename counter")
            .Register<SetCounterValue>("ladder.value", 1, "Set counter value");
#pragma warning restore BRUTALCORE0011
        var session = new DocumentSession<Notebook>(
            new NotebookFormat(), new NotebookValidator(), catalog, document ?? Notebook.Starter());
        var outline = new FlatOutline<Notebook>(d =>
            d.Counters.Select(c => (c.Id, c.Label)).Concat(d.Notes.Select(n => (n.Id, n.Label))));
        return new EditorModel<Notebook>(session, outline);
    }
}

