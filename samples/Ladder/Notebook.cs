using System.Collections.Immutable;

namespace Ladder;

// INVENTED neutral sample data; counters have an explicit local range of 0..100.
public sealed record Counter(string Id, string Label, int Value);
public sealed record Note(string Id, string Label);
public sealed record Notebook(ImmutableArray<Counter> Counters, ImmutableArray<Note> Notes)
{
    public Counter? FindCounter(string id) => Counters.FirstOrDefault(c => c.Id == id);
    public Notebook WithCounter(Counter counter) => this with
    {
        Counters = Counters.Select(c => c.Id == counter.Id ? counter : c).ToImmutableArray()
    };
    public static Notebook Starter() => new([new("counter", "Example", 3)], []);
}
