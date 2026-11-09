namespace RoutePuzzleSample;

/// <summary>A detached play state; it never edits the catalog or an authoring document.</summary>
public sealed class RouteGame
{
    private readonly List<string> visited = [];
    public RouteGame(RouteCatalog catalog)
    {
        Catalog = catalog; Current = catalog.Start; Energy = catalog.Energy;
        visited.Add(Current); Visited = visited.AsReadOnly();
    }
    public RouteCatalog Catalog { get; }
    public string Current { get; private set; }
    public int Energy { get; private set; }
    public IReadOnlyList<string> Visited { get; }
    public bool Won => Current == Catalog.Goal;
    public bool Lost => !Won && !Catalog.Place(Current).Exits.Any(e => e.Cost <= Energy);
    public string Status => Won ? $"Reached {Catalog.Place(Current).Label} with {Energy} energy left."
        : Lost ? $"No affordable exit at {Catalog.Place(Current).Label}. Stop and try another route."
        : $"At {Catalog.Place(Current).Label}: {Energy} energy left.";

    /// <summary>False means no change. Every successful move spends at least one energy.</summary>
    public bool Choose(string destination)
    {
        if (Won || Lost) return false;
        var exit = Catalog.Place(Current).Exits.FirstOrDefault(e => e.To == destination);
        if (exit is null || exit.Cost > Energy) return false;
        Current = exit.To; Energy -= exit.Cost; visited.Add(Current);
        return true;
    }
}
