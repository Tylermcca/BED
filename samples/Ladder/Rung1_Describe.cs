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
