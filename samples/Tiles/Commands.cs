using System.Collections.Immutable;
using Brutal.Authoring;

namespace Tiles;

// INVENTED: cell commands build immutable candidates; the shared session validates and commits.
public sealed record AddKind(string Id, string Label) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        if (map.Kinds.Any(k => k.Id == Id))
            return CommandResult<TileMap>.Refused(Issue.Error("Choose an unused tile kind id.", Id, "$.kinds"));
        return CommandResult<TileMap>.Done(map with { Kinds = map.Kinds.Add(new(Id, Label)) });
    }
}
public sealed record RenameKind(string Id, string Label) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        int index = -1;
        for (int i = 0; i < map.Kinds.Length; i++) if (map.Kinds[i].Id == Id) index = i;
        return index < 0
            ? CommandResult<TileMap>.Refused(Issue.Error("Choose an existing tile kind.", string.IsNullOrWhiteSpace(Id) ? "map" : Id, "$.kinds"))
            : CommandResult<TileMap>.Done(map with { Kinds = map.Kinds.SetItem(index, map.Kinds[index] with { Label = Label }) });
    }
}
public sealed record RemoveKind(string Id) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        if (!map.Kinds.Any(k => k.Id == Id))
            return CommandResult<TileMap>.Refused(Issue.Error("Choose an existing tile kind.", string.IsNullOrWhiteSpace(Id) ? "map" : Id, "$.kinds"));
        var used = map.Cells.Select((id, index) => (id, index)).Where(c => c.id == Id).ToArray();
        if (used.Length > 0)
        {
            string where = string.Join(", ", used.Take(8).Select(c => $"({c.index % map.Width},{c.index / map.Width})"));
            string more = used.Length > 8 ? $"; {used.Length - 8} more" : "";
            return CommandResult<TileMap>.Refused(Issue.Error(
                $"Tile kind '{Id}' is used by {used.Length} cell(s): {where}{more}. Clear or repaint them before removing it.", Id, $"$.cells[{used[0].index}]"));
        }
        return CommandResult<TileMap>.Done(map with { Kinds = map.Kinds.Where(k => k.Id != Id).ToImmutableArray() });
    }
}
public sealed record PaintCell(int X, int Y, string KindId) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        if (!TileRules.InBounds(map, X, Y))
            return CommandResult<TileMap>.Refused(Issue.Error("Choose a cell inside the map.", $"cell:{X},{Y}", "$.cells"));
        if (!map.Kinds.Any(k => k.Id == KindId))
            return CommandResult<TileMap>.Refused(Issue.Error("Add that tile kind before painting it.", $"cell:{X},{Y}", $"$.cells[{Y * map.Width + X}]"));
        return CommandResult<TileMap>.Done(map with { Cells = map.Cells.SetItem(Y * map.Width + X, KindId) });
    }
}
public sealed record ClearCell(int X, int Y) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map) => TileRules.InBounds(map, X, Y)
        ? CommandResult<TileMap>.Done(map with { Cells = map.Cells.SetItem(Y * map.Width + X, null) })
        : CommandResult<TileMap>.Refused(Issue.Error("Choose a cell inside the map.", $"cell:{X},{Y}", "$.cells"));
}
public sealed record FillRectangle(int X, int Y, int Width, int Height, string? KindId) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        if (Width <= 0 || Height <= 0 || X < 0 || Y < 0 ||
            (long)X + Width > map.Width || (long)Y + Height > map.Height)
            return CommandResult<TileMap>.Refused(Issue.Error("Choose a positive rectangle wholly inside the map.", $"cell:{X},{Y}", "$.cells"));
        if (!TileRules.HasKind(map, KindId))
            return CommandResult<TileMap>.Refused(Issue.Error("Add that tile kind before filling it.", string.IsNullOrWhiteSpace(KindId) ? "map" : KindId, "$.kinds"));
        var cells = map.Cells.ToBuilder();
        for (int y = Y; y < Y + Height; y++)
            for (int x = X; x < X + Width; x++) cells[y * map.Width + x] = KindId;
        return CommandResult<TileMap>.Done(map with { Cells = cells.ToImmutable() });
    }
}
public sealed record ResizeMap(int Width, int Height) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map)
    {
        if (!TileRules.ValidSize(Width, Height))
            return CommandResult<TileMap>.Refused(Issue.Error("Choose positive dimensions with at most 1,000,000 cells.", "map", "$.width"));
        var cells = new string?[Width * Height];
        for (int y = 0; y < Math.Min(map.Height, Height); y++)
            for (int x = 0; x < Math.Min(map.Width, Width); x++) cells[y * Width + x] = map.Cells[y * map.Width + x];
        return CommandResult<TileMap>.Done(map with { Width = Width, Height = Height, Cells = ImmutableArray.CreateRange(cells) });
    }
}
