using System.Text.Json;
using Bed.Data;

namespace Bed.Editor;

/// <summary>A memory-only, schema-validated settings draft. It never writes a sidecar or imports source content.</summary>
public sealed class AssetSettingsSession
{
    private readonly AssetRecord original;
    private readonly DocumentSession<DataDocument> session;

    /// <summary>Creates a draft using the host's supported importer settings schema; no schema is inferred.</summary>
    public AssetSettingsSession(AssetRecord record, JsonSchemas schemas, string schema)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Settings.ValueKind != JsonValueKind.Object) throw new ArgumentException("Asset settings must be an object.", nameof(record));
        original = record with { Settings = record.Settings.Clone(), RightsFound = record.RightsFound?.ToArray() };
        session = DataKit.Session(new DataDocument(original.Settings), schemas, schema);
    }

    /// <summary>Current validated settings, independent of the caller's JSON document lifetime.</summary>
    public JsonElement Settings => session.Current.Root;
    /// <summary>A candidate record with only Settings replaced; not a persisted or reimported record.</summary>
    public AssetRecord Candidate => original with { Settings = Settings.Clone() };
    /// <summary>Whether the draft differs from its original settings.</summary>
    public bool Dirty => session.Dirty;
    /// <summary>Current in-memory edit revision, not an import or catalog generation.</summary>
    public long Revision => session.Revision;
    /// <summary>Why the latest edit was refused, or its validation warnings.</summary>
    public IReadOnlyList<Issue> LastIssues { get; private set; } = [];
    /// <summary>Number of undoable settings transactions.</summary>
    public int UndoCount => session.History.UndoCount;

    /// <summary>Replaces settings as one validated transaction. Refusal leaves settings/history/revision unchanged.</summary>
    public bool TryReplace(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            LastIssues = [Issue.Error("Asset settings must be an object.")];
            return false;
        }
        var accepted = session.Execute(new SetValue("", settings.Clone()));
        LastIssues = session.LastIssues;
        return accepted;
    }

    /// <summary>Undoes one settings transaction without touching source, sidecar or import state.</summary>
    public bool Undo()
    {
        var changed = session.Undo(); LastIssues = session.LastIssues; return changed;
    }
    /// <summary>Redoes one settings transaction without touching source, sidecar or import state.</summary>
    public bool Redo()
    {
        var changed = session.Redo(); LastIssues = session.LastIssues; return changed;
    }
}
