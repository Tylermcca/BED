using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoutePuzzleSample;

// INVENTED: a tiny, self-authored energy-and-routes puzzle. No editor dependencies.
public sealed record RouteExit(string To, int Cost);

public sealed class RoutePlace
{
    internal RoutePlace(string id, string label, IEnumerable<RouteExit> exits)
    { Id = id; Label = label; Exits = Array.AsReadOnly(exits.ToArray()); }
    public string Id { get; }
    public string Label { get; }
    public IReadOnlyList<RouteExit> Exits { get; }
}

public sealed class RouteProblem(string at, string message) : FormatException(message)
{
    public string At { get; } = at;
}

/// <summary>The native game's loader. The adapter and Check call this same code.</summary>
public sealed class RouteCatalog
{
    public const string Kind = "sample.route-puzzle";
    public const int Version = 1;
    private readonly IReadOnlyDictionary<string, RoutePlace> byId;
    private RouteCatalog(string title, string start, string goal, int energy, RoutePlace[] places)
    {
        Title = title; Start = start; Goal = goal; Energy = energy;
        Places = Array.AsReadOnly(places);
        byId = new ReadOnlyDictionary<string, RoutePlace>(places.ToDictionary(p => p.Id, StringComparer.Ordinal));
    }
    public string Title { get; }
    public string Start { get; }
    public string Goal { get; }
    public int Energy { get; }
    public IReadOnlyList<RoutePlace> Places { get; }
    public RoutePlace Place(string id) => byId[id];

    public static RouteCatalog Read(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            Fields(root, "", "kind", "version", "title", "start", "goal", "energy", "places");
            if (Text(root, "kind", "", 80) != Kind) throw Bad("/kind", $"Use kind '{Kind}'.");
            if (Integer(root, "version", "", 1, int.MaxValue) != Version)
                throw Bad("/version", "This sample reads version 1; use a matching sample reader.");
            var title = Text(root, "title", "", 80);
            var start = Id(root, "start", ""); var goal = Id(root, "goal", "");
            var energy = Integer(root, "energy", "", 1, 30);
            var list = Required(root, "places", "");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 2 or > 24)
                throw Bad("/places", "Provide 2 to 24 places.");
            var places = new List<RoutePlace>(); var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list.EnumerateArray())
            {
                var at = $"/places/{places.Count}";
                Fields(item, at, "id", "label", "exits");
                var id = Id(item, "id", at);
                if (!known.Add(id)) throw Bad(at + "/id", $"Place '{id}' is repeated; choose a unique id.");
                var label = Text(item, "label", at, 50);
                var exits = Required(item, "exits", at);
                if (exits.ValueKind != JsonValueKind.Array || exits.GetArrayLength() > 4)
                    throw Bad(at + "/exits", "Provide 0 to 4 exits.");
                var routes = new List<RouteExit>(); var targets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var exit in exits.EnumerateArray())
                {
                    var where = $"{at}/exits/{routes.Count}";
                    Fields(exit, where, "to", "cost");
                    var to = Id(exit, "to", where);
                    if (!targets.Add(to)) throw Bad(where + "/to", "An exit to this place already exists; keep one.");
                    routes.Add(new(to, Integer(exit, "cost", where, 1, 9)));
                }
                places.Add(new(id, label, routes));
            }
            if (!known.Contains(start)) throw Bad("/start", "Choose the id of an existing place.");
            if (!known.Contains(goal)) throw Bad("/goal", "Choose the id of an existing place.");
            if (start == goal) throw Bad("/goal", "Choose a goal different from the start.");
            for (var i = 0; i < places.Count; i++)
            {
                if (places[i].Id == goal && places[i].Exits.Count != 0)
                    throw Bad($"/places/{i}/exits", "The goal ends the game; remove its exits.");
                for (var j = 0; j < places[i].Exits.Count; j++)
                    if (!known.Contains(places[i].Exits[j].To))
                        throw Bad($"/places/{i}/exits/{j}/to", "Choose the id of an existing place.");
            }
            var catalog = new RouteCatalog(title, start, goal, energy, places.ToArray());
            if (catalog.MinimumCost() > energy)
                throw Bad("/energy", "No route reaches the goal within this energy; lower route costs or add a reachable route.");
            return catalog;
        }
        catch (JsonException) { throw Bad("", "The catalog is not valid JSON; correct its JSON syntax."); }
    }

    // At most 24 settled vertices and 96 exits; no seed search or game simulation.
    public int MinimumCost()
    {
        var distances = Places.ToDictionary(p => p.Id, _ => int.MaxValue, StringComparer.Ordinal);
        distances[Start] = 0;
        var remaining = new HashSet<string>(distances.Keys, StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var here = remaining.MinBy(id => distances[id])!;
            if (distances[here] == int.MaxValue) break;
            if (here == Goal) return distances[here];
            remaining.Remove(here);
            foreach (var exit in Place(here).Exits)
                distances[exit.To] = Math.Min(distances[exit.To], distances[here] + exit.Cost);
        }
        return int.MaxValue;
    }

    private static RouteProblem Bad(string at, string why) => new(at, why);
    private static void Fields(JsonElement value, string at, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Bad(at, "Use an object here.");
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal))
                throw Bad(at, $"Unknown field '{field.Name}'; remove it or use the catalog's documented fields.");
    }
    private static JsonElement Required(JsonElement root, string name, string at) =>
        root.TryGetProperty(name, out var value) ? value : throw Bad(at + "/" + name, $"Add the required '{name}' field.");
    private static string Text(JsonElement root, string name, string at, int max)
    {
        var value = Required(root, name, at);
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length < 1 || text.Length > max)
            throw Bad(at + "/" + name, $"Use text of 1 to {max} characters.");
        return text;
    }
    private static string Id(JsonElement root, string name, string at)
    {
        var text = Text(root, name, at, 24);
        if (!Regex.IsMatch(text, "^[a-z][a-z0-9_-]{0,23}$", RegexOptions.CultureInvariant))
            throw Bad(at + "/" + name, "Use a lowercase id: a letter, then letters, digits, underscore or hyphen; at most 24 characters.");
        return text;
    }
    private static int Integer(JsonElement root, string name, string at, int min, int max)
    {
        var value = Required(root, name, at);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
            throw Bad(at + "/" + name, $"Use a whole number from {min} to {max}.");
        return number;
    }
}
