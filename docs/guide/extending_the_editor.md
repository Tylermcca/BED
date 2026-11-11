# Extend the editor in three small steps

Add fields, a tool and a grouped extension to an editor using the Ladder sample. Each step builds on the same document and session.

You need this checkout and the .NET 10 SDK. Run commands from the repository root. Ladder is a class library; its tests exercise the editor model without a graphics window.

The first step describes fields with C# registration. It adds no custom inspector UI or command implementation. There is no zero-C# schema loader in this example. The final step groups an extension in code. A full plug-in adds a manifest and goes through `PluginHost` (below, and `samples/LightsPlugin`).

The example uses original notebooks, counters and notes. Its 0-100 counter limit belongs to the sample, not the library.

## Start here

In Windows PowerShell 5.1 or PowerShell 7:

```powershell
dotnet build samples/Ladder/Ladder.csproj --nologo -v:q
dotnet test tests/Bed.Tests/Bed.Tests.csproj --filter "FullyQualifiedName~LadderTests" --nologo -v:q
```

The build should succeed with no errors. The filtered run should report 23 passed and zero failed or skipped. The commands run headless. The first build may restore the repository's existing packages.

## The document and host you build on

The checkout includes the document, format, validator, commands and host used by all three steps:

| Shared file in samples/Ladder | Lines, including blanks and comments | What it supplies |
|---|---:|---|
| Notebook.cs | 16 | Immutable Counter, Note and Notebook records; one starter counter. |
| NotebookFormat.cs | 45 | Strict versioned reading and canonical text writing. |
| NotebookValidator.cs | 24 | Unique nonblank ids, nonblank labels up to 80 characters, values from 0 to 100. |
| CounterEdits.cs | 17 | The document's existing typed label and value commands. |
| LadderHost.cs | 20 | A catalogue, a DocumentSession and a flat outline for a fresh editor. |

The project references only the core and editor model. Each step extends the supplied host; it is not a complete editor on its own. For a full document walkthrough, read [Your first document type](your_first_document_type.md).

## Rung 1 — Describe a type

Whole file: samples/Ladder/Rung1_Describe.cs, 21 lines.

```csharp
using Bed.Editor;

namespace Ladder;

// INVENTED field description. C# registration today; no custom inspector UI.
public static class Rung1Describe
{
    public static Inspector<Notebook> Register(Inspector<Notebook> inspector) =>
        inspector.Add(new ObjectType<Notebook, Counter>(
            "ladder.counter", "Counter", (d, id) => d.FindCounter(id),
            [
                new("id", "Id", PropertyKind.Text, c => c.Id,
                    Help: "A stable reference; it cannot be edited."),
                new("label", "Label", PropertyKind.Text, c => c.Label,
                    (c, value) => new SetCounterLabel(c.Id, (string)value!),
                    Help: "A short name for the counter."),
                new("value", "Value", PropertyKind.Integer, c => c.Value,
                    (c, value) => new SetCounterValue(c.Id, Convert.ToInt32(value)),
                    Default: (_, _) => 0, Help: "A whole number from 0 to 100.")
            ]));
}
```

ObjectType says how to find a counter. Each PropertyDescriptor supplies a label, a value reader and, for editable fields, a typed command builder. Id has no edit delegate, so it is read-only. Value has a default and a reset action.

The descriptor reuses CounterEdits.cs and NotebookValidator.cs. The inspector builds commands; the session validates and records them. It does not invent a validator from these field descriptions. A change is undoable through that existing session.

To try this step:
1. Open a fresh editor with LadderHost.Open.
2. Create an Inspector<Notebook> over that editor.
3. Call Rung1Describe.Register once and select the counter whose id is counter.
4. Change its label or value, then undo. An out-of-range value should be refused.

The focused examples are Rung1LabelEditUsesTypedCommandAndOneUndo, Rung1MultiEditIsOneUndoStep and Rung1ValidationRefusesOutOfRangeValueWithObjectAndReason.

This is C# field registration today. A future zero-C# authoring surface would need its own loader and contract.

## Rung 2 — Add one tool class

Whole file: samples/Ladder/Rung2_OneClassTool.cs, 15 lines.

```csharp
using Bed;

namespace Ladder;

// INVENTED tool: one registered class; the session still validates and records it.
public sealed record DoubleCounter(string Id) : ICommand<Notebook>
{
    public const string CommandId = "ladder.double";
    public static void Register(CommandCatalog<Notebook> catalog) =>
        catalog.Register<DoubleCounter>(CommandId, 1, "Double counter");
    public CommandResult<Notebook> Apply(Notebook d) => d.FindCounter(Id) is { } c
        ? CommandResult<Notebook>.Done(d.WithCounter(c with { Value = c.Value * 2 }))
        : CommandResult<Notebook>.Refused(Issue.Error(
            "Choose an existing counter before doubling it.", Id, "$.counters"));
}
```

A command is one record class. Its Register method puts a stable id and a label in the existing catalogue. Its Apply method returns a candidate snapshot, or an issue with a target and a next action. The session still runs NotebookValidator. Doubling 60 is refused because 120 exceeds the local limit. Command and validator reasons are in session.LastIssues; editor.Refused is reserved for editor focus refusals.

Use a fresh LadderHost editor. Call DoubleCounter.Register on its session catalogue. Search for Double counter in the palette. Execute a DoubleCounter through EditorModel.Execute, passing the touched counter id so focus locking applies.

Use Rung2RegistersItsOneClassForPaletteSearch and Rung2ExecutesThroughTheRealSession to check registration and journaling. Check both missing targets and validator refusals with Rung2CommandItselfRefusesMissingTargetWithHelpfulReason and Rung2SharedValidatorCanRefuseAnOtherwiseConstructibleEdit. Rung2UndoAndRedoRestoreTheExpectedValues covers history.

## Rung 3 — Register a whole extension

Whole file: samples/Ladder/Rung3_Extension.cs, 38 lines.

```csharp
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
        editor.Session.Catalog.Register<AddNote>("ladder.note_add", 1, "Add note")
            .Register<RenameNote>("ladder.note_label", 1, "Rename note");
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
```

This registers two object types, the extra note commands, the one-class tool, a workspace and a shortcut default. Start from a fresh host and inspector. Create Shortcuts, then call NotebookExtension.Register once. It includes rung 1 and rung 2; do not register those again in that same host.

Switch to ladder.notes with EditorModel.Use. Workspace commands lead the empty palette search, followed by the host's other commands. The workspace changes the view; it shares the same session, undo and save. A person's shortcut choice wins over this extension's default.

Check workspace ordering with Rung3WorkspaceCommandsLeadThePalette. Rung3RegistersBothObjectTypesWithTheSameInspector and Rung3InspectorEditsItsNewTypeThroughTheSession cover the note type. Keep the replay and focus checks in Rung3MixedJournalReplaysToIdenticalCanonicalText and Rung3NewInspectorTypeHonorsExistingFocusLock.

All test code is in tests/Bed.Tests/LadderTests.cs.

## Where to go next

Keep document meaning in your own extension. Add another typed command or descriptor before adding a new framework. See [The library at a glance](api_overview.md) for the existing seams.

This registration group is the shape a plug-in takes. To make it one, give it a `plugin.json` manifest and implement `IEditorPlugin<TDoc>`. `samples/LightsPlugin` does this in 43 lines:
- an id;
- a version;
- the oldest authoring API it works with;
- optionally, an SPDX licence. It's information for the people using the plug-in, not a gate: a missing licence is noted, and licences that can't ship together (with another plug-in, or with your project's licence, if you give one) get a warning;
- any permissions it asks for, each with a reason.

`PluginHost<TDoc>.Add` checks the manifest against `AuthoringInfo.ApiVersion` before any of the plug-in's code runs, so a plug-in built for a newer API is refused with a clear message. It then gathers everything the plug-in registers and checks it:
- every id is named `<plugin id>.<name>`;
- nothing is already taken;
- workspaces name real commands;
- shortcuts parse.

Only when every check passes does registration take effect. If registration throws or fails a check, the host disables the plug-in and reports the problem.

PluginHost receives plug-in objects from your host; it does not discover or load their assemblies. It supports registered panels. See [Writing a plug-in](writing_a_plugin.md) for the panel and loading workflow.

Load only code you trust. In-process permissions describe intent; they do not create a sandbox.
