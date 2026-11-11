using Bed;

namespace Ladder;

// INVENTED tool: one registered class; the session still validates and records it.
public sealed record DoubleCounter(string Id) : ICommand<Notebook>
{
    public const string CommandId = "ladder.double";
#pragma warning disable BRUTALCORE0011 // command ids, not JSON keys; Register has no UTF-8 overload
    public static void Register(CommandCatalog<Notebook> catalog) =>
        catalog.Register<DoubleCounter>(CommandId, 1, "Double counter");
#pragma warning restore BRUTALCORE0011
    public CommandResult<Notebook> Apply(Notebook d) => d.FindCounter(Id) is { } c
        ? CommandResult<Notebook>.Done(d.WithCounter(c with { Value = c.Value * 2 }))
        : CommandResult<Notebook>.Refused(Issue.Error(
            "Choose an existing counter before doubling it.", Id, "$.counters"));
}

