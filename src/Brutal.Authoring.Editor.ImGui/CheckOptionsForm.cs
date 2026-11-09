using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>A memory-only check draft drawn by the existing schema inspector. It never edits project files.</summary>
public sealed class CheckOptionsForm
{
    /// <summary>The reason shown when a check's options use a kind of schema the panel can't edit.</summary>
    public const string UnsupportedReason = "This check has options that cannot be edited here.";
    private const string SchemaName = "check-options.schema.json";
    private readonly JsonSchemas schemas = new();
    private readonly JsonElement schema;
    private readonly DocumentSession<DataDocument>? session;
    private readonly InspectorController<DataDocument>? controller;
    private string? unsupported = UnsupportedReason;
    private long validatedRevision = -1;
    private string? validationProblem;

    /// <summary>A form for one check's options, filled from its schema's defaults.</summary>
    public CheckOptionsForm(CheckDefinition definition)
    {
        if (definition.OptionsSchema is not { } text) return;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            schema = parsed.RootElement.Clone();
            if (!SupportedSchema(schema)) return;
            schemas.Add(SchemaName, text);
            var initial = new JsonObject();
            if (schema.TryGetProperty("properties"u8, out var properties))
                foreach (var property in properties.EnumerateObject())
                    if (property.Value.TryGetProperty("default"u8, out var value))
                        initial[property.Name] = JsonNode.Parse(value.GetRawText());
            // Incomplete options are drafts, not project content. The full schema still gates Run below and in Checks.Start.
            session = new DocumentSession<DataDocument>(new DataFormat(), new DraftValidator(), DataKit.Catalog(),
                new DataDocument(JsonSerializer.SerializeToElement(initial)));
            var editor = new EditorModel<DataDocument>(session,
                new FlatOutline<DataDocument>(_ => [("", "Options")]));
            editor.Select("");
            Inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, SchemaName));
            controller = new InspectorController<DataDocument>(editor, Inspector, _ => "Options", _ => false,
                (ok, done, why) => { Status = ok ? done : why; return ok; },
                issues => string.Join(" ", issues.Select(i => i.Message)), value => Status = value);
            unsupported = null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidDocumentException or InvalidOperationException)
        {
            unsupported = UnsupportedReason;
        }
    }

    /// <summary>Whether the panel can edit this check's options.</summary>
    public bool Supported => unsupported is null;
    /// <summary>The inspector that draws the option fields, when supported.</summary>
    public Inspector<DataDocument>? Inspector { get; }
    /// <summary>The last message from editing an option.</summary>
    public string Status { get; private set; } = "";
    /// <summary>Whether a typed value hasn't been committed yet; Run waits for it.</summary>
    public bool HasPendingEdits => controller?.HasPendingEdits == true;
    /// <summary>The options as they stand, for the check's snapshot.</summary>
    public JsonElement Options => session?.Current.Root.Clone() ?? JsonSerializer.SerializeToElement(new { });

    /// <summary>A missing/refused edit blocks the form; defaults are never reinserted on a later draw.</summary>
    public string? Problem
    {
        get
        {
            if (unsupported is not null) return unsupported;
            if (HasPendingEdits) return "Finish or correct the option being edited before running the check.";
            if (validatedRevision == session!.Revision) return validationProblem;
            validatedRevision = session.Revision;
            var options = session.Current.Root;
            if (schema.TryGetProperty("required"u8, out var required))
                foreach (var item in required.EnumerateArray())
                {
                    var name = item.GetString()!;
                    if (!options.TryGetProperty(name, out var value)
                        || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                        return validationProblem = $"Fill required option '{name}'.";
                }
            var issues = schemas.Validate(options, SchemaName).Where(i => i.Severity == Severity.Error).ToList();
            return validationProblem = issues.Count == 0 ? null : string.Join(" ", issues.Select(i => i.Message));
        }
    }

    internal bool TryGetOptions(out string? options, out string why)
    {
        options = null;
        if (controller is not null && !controller.TryCommitPending(out why)) return false;
        if (Problem is { } problem) { why = problem; return false; }
        options = Options.GetRawText();
        why = "";
        return true;
    }

    /// <summary>Draw inside the caller's Checks panel; all field widgets and keyboard behavior are the inspector's.</summary>
    public void Draw()
    {
        if (!Supported) { Ui.TextWrapped(UnsupportedReason); return; }
        controller!.Draw();
        if (Problem is { } why) Ui.TextWrapped(why);
        if (Status.Length > 0) Ui.TextWrapped(Status);
    }

    private static bool Scalar(JsonElement value) => value.ValueKind is JsonValueKind.String
        or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static bool Complex(JsonElement node) => new[] { "$ref", "$dynamicRef", "allOf", "anyOf", "oneOf",
        "if", "then", "else", "dependentSchemas", "patternProperties", "items", "prefixItems" }
        .Any(key => node.TryGetProperty(key, out _));

    private static bool SupportedSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Complex(root)) return false;
        if (root.TryGetProperty("type"u8, out var type) && (type.ValueKind != JsonValueKind.String || type.GetString() != "object")) return false;
        if (root.TryGetProperty("properties"u8, out var properties) && properties.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("additionalProperties"u8, out var additional)
            && additional.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (properties.ValueKind == JsonValueKind.Object)
            foreach (var property in properties.EnumerateObject())
                if (!names.Add(property.Name)) return false;
        if (root.TryGetProperty("required"u8, out var required)
            && (required.ValueKind != JsonValueKind.Array || required.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || !names.Contains(v.GetString()!)))) return false;
        if (properties.ValueKind != JsonValueKind.Object) return true;
        foreach (var property in properties.EnumerateObject())
        {
            var field = property.Value;
            if (field.ValueKind != JsonValueKind.Object || Complex(field)) return false;
            var typed = field.TryGetProperty("type"u8, out var kind);
            var enumerated = field.TryGetProperty("enum"u8, out var choices);
            if (!typed && !enumerated) return false;
            if (typed && (kind.ValueKind != JsonValueKind.String || kind.GetString() is not ("string" or "number" or "integer" or "boolean"))) return false;
            if (enumerated && (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || choices.EnumerateArray().Any(v => !Scalar(v)))) return false;
            if (field.TryGetProperty("default"u8, out var initial) && !Scalar(initial)) return false;
        }
        return true;
    }

    private sealed class DraftValidator : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document) => [];
    }
}
