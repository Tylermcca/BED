using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brutal.Authoring;

namespace Tiles;

// INVENTED: a neutral row-major tile document, independent of the board and all games.
public sealed record TileKind(string Id, string Label);
public sealed record TileMap(int Width, int Height, double CellSize,
    ImmutableArray<TileKind> Kinds, ImmutableArray<string?> Cells)
{
    public const int MaxCells = 1_000_000;
    public static TileMap Empty(int width = 2, int height = 2, double cellSize = 1)
    {
        if (!TileRules.ValidSize(width, height) || !double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Use positive dimensions, at most 1,000,000 cells, and a positive finite cell size.");
        return new(width, height, cellSize, [], ImmutableArray.CreateRange(new string?[width * height]));
    }
}
public static class TileRules
{
    public static bool ValidSize(int width, int height) => width > 0 && height > 0 && (long)width * height <= TileMap.MaxCells;
    public static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"\A[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant);
    public static bool InBounds(TileMap map, int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height;
    public static bool HasKind(TileMap map, string? id) => id is null || map.Kinds.Any(k => k.Id == id);
}
public sealed class TileValidator : IValidator<TileMap>
{
    public IReadOnlyList<Issue> Validate(TileMap map)
    {
        var issues = new List<Issue>();
        if (!TileRules.ValidSize(map.Width, map.Height))
            issues.Add(Issue.Error("Choose positive dimensions with at most 1,000,000 cells.", "map", "$.width"));
        if (!double.IsFinite(map.CellSize) || map.CellSize <= 0)
            issues.Add(Issue.Error("Choose a positive finite cell size in metres.", "map", "$.cell_size"));
        if (map.Kinds.IsDefault) issues.Add(Issue.Error("Supply a tile kinds list.", "map", "$.kinds"));
        if (map.Cells.IsDefault) issues.Add(Issue.Error("Supply a cells array.", "map", "$.cells"));
        if (map.Kinds.IsDefault || map.Cells.IsDefault) return issues;
        if ((long)map.Width * map.Height != map.Cells.Length)
            issues.Add(Issue.Error("Make the cell count equal width times height.", "map", "$.cells"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < map.Kinds.Length; i++)
        {
            var kind = map.Kinds[i];
            if (!TileRules.ValidId(kind.Id))
                issues.Add(Issue.Error("Use an id starting with a lowercase letter, then lowercase letters, digits or underscores.", string.IsNullOrEmpty(kind.Id) ? $"kind:{i}" : kind.Id, $"$.kinds[{i}].id"));
            if (!ids.Add(kind.Id))
                issues.Add(Issue.Error("Choose a unique tile kind id.", string.IsNullOrEmpty(kind.Id) ? $"kind:{i}" : kind.Id, $"$.kinds[{i}].id"));
            if (string.IsNullOrWhiteSpace(kind.Label) || kind.Label.Length > 80)
                issues.Add(Issue.Error("Use a nonblank label of 1 to 80 characters.", string.IsNullOrWhiteSpace(kind.Id) ? $"kind:{i}" : kind.Id, $"$.kinds[{i}].label"));
        }
        for (int i = 0; i < map.Cells.Length; i++)
            if (map.Cells[i] is string id && !ids.Contains(id))
                issues.Add(Issue.Error($"Tile kind '{id}' is missing; add it or clear the cell.",
                    $"cell:{i % Math.Max(map.Width, 1)},{i / Math.Max(map.Width, 1)}", $"$.cells[{i}]"));
        return issues;
    }
}
public sealed class TileFormat : IDocumentFormat<TileMap>
{
    public string Kind => "tiles.map";
    public int Version => 1;
    public TileMap Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "width", "height", "cell_size", "kinds", "cells");
        if (r.String("kind") != Kind) throw new DocumentFormatException("$.kind", "expected tiles.map");
        if (r.Int("version") != Version) throw new DocumentFormatException("$.version", "unsupported tiles.map version");
        int width = r.Int("width"), height = r.Int("height");
        var kinds = r.Array("kinds").Select(k =>
        {
            k.Only("id", "label");
            return new TileKind(k.String("id"), k.String("label"));
        }).ToImmutableArray();
        var rawCells = r.Element("cells");
        if (rawCells.ValueKind != JsonValueKind.Array) throw new DocumentFormatException("$.cells", "expected an array");
        if (rawCells.GetArrayLength() > TileMap.MaxCells) throw new DocumentFormatException("$.cells", "cell array exceeds the sample's limit");
        var cells = ImmutableArray.CreateBuilder<string?>();
        int index = 0;
        foreach (var value in rawCells.EnumerateArray())
        {
            if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                throw new DocumentFormatException($"$.cells[{index}]", "expected a tile id string or null");
            cells.Add(value.ValueKind == JsonValueKind.Null ? null : value.GetString());
            index++;
        }
        return new(width, height, r.Double("cell_size"), kinds, cells.ToImmutable());
    }
    public string Write(TileMap map)
    {
        using var bytes = new MemoryStream();
        using (var w = new Utf8JsonWriter(bytes, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind); w.WriteNumber("version", Version);
            w.WriteNumber("width", map.Width); w.WriteNumber("height", map.Height);
            w.WriteNumber("cell_size", map.CellSize);
            w.WriteStartArray("kinds");
            foreach (var kind in map.Kinds)
            {
                w.WriteStartObject(); w.WriteString("id", kind.Id); w.WriteString("label", kind.Label); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("cells");
            foreach (var cell in map.Cells)
                if (cell is null) w.WriteNullValue(); else w.WriteStringValue(cell);
            w.WriteEndArray(); w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(bytes.ToArray()) + "\n";
    }
}
public static class TilesKit
{
    public static readonly TileFormat Format = new();
    public static readonly TileValidator Validator = new();
    public static CommandCatalog<TileMap> Catalog() => new CommandCatalog<TileMap>()
        .Register<AddKind>("tiles.add_kind", 1, "Add tile kind")
        .Register<RenameKind>("tiles.rename_kind", 1, "Rename tile kind")
        .Register<RemoveKind>("tiles.remove_kind", 1, "Remove tile kind")
        .Register<PaintCell>("tiles.paint", 1, "Paint cell")
        .Register<FillRectangle>("tiles.fill", 1, "Fill rectangle")
        .Register<ClearCell>("tiles.clear", 1, "Clear cell")
        .Register<ResizeMap>("tiles.resize", 1, "Resize map");
    public static DocumentSession<TileMap> Session(TileMap map) => new(Format, Validator, Catalog(), map);
    public static DocumentSession<TileMap> Open(string path) => DocumentSession<TileMap>.Open(path, Format, Validator, Catalog());
}
