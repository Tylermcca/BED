using Brutal.Authoring;

namespace Ladder;

// Existing document commands reused by the descriptor rung.
public sealed record SetCounterLabel(string Id, string Label) : ICommand<Notebook>
{
    public CommandResult<Notebook> Apply(Notebook d) => d.FindCounter(Id) is { } c
        ? CommandResult<Notebook>.Done(d.WithCounter(c with { Label = Label }))
        : CommandResult<Notebook>.Refused(Issue.Error("Choose an existing counter.", Id, "$.counters"));
}
public sealed record SetCounterValue(string Id, int Value) : ICommand<Notebook>
{
    public CommandResult<Notebook> Apply(Notebook d) => d.FindCounter(Id) is { } c
        ? CommandResult<Notebook>.Done(d.WithCounter(c with { Value = Value }))
        : CommandResult<Notebook>.Refused(Issue.Error("Choose an existing counter.", Id, "$.counters"));
}
