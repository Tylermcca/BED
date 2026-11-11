using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bed;

namespace Toy;

/// <summary>One labelled item on the board, at a position in metres.</summary>
/// <param name="Id">A stable, readable id (lowercase letters, digits and underscores).</param>
/// <param name="Label">What people see.</param>
/// <param name="X">Position across, in metres.</param>
/// <param name="Y">Position up, in metres.</param>
public sealed record Item(string Id, string Label, double X, double Y);

/// <summary>The sample document: a board of items. Immutable; commands return a new board.</summary>
public sealed record Board(ImmutableArray<Item> Items)
{
    /// <summary>A board with nothing on it.</summary>
    public static readonly Board Empty = new(ImmutableArray<Item>.Empty);

    /// <summary>The item with <paramref name="id"/>, or null.</summary>
    public Item? Find(string id) => Items.FirstOrDefault(i => i.Id == id);

    internal Board With(Item item) => new(Items.Select(i => i.Id == item.Id ? item : i).ToImmutableArray());
}

/// <summary>Reads and writes boards as canonical JSON: <c>{"kind": "toy.board", "version": 1, "items": [...]}</c>.</summary>
public sealed class BoardFormat : IDocumentFormat<Board>
{
    /// <inheritdoc />
    public string Kind => "toy.board";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public Board Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "items");
        if (r.String("kind") != Kind) throw new DocumentFormatException("$.kind", $"expected \"{Kind}\"");
        var version = r.Int("version");
        if (version != Version) throw new DocumentFormatException("$.version", $"board version {version} is not supported (this tool reads version {Version})");
        return new Board(r.Array("items").Select(i =>
        {
            i.Only("id", "label", "x", "y");
            return new Item(i.String("id"), i.String("label"), i.Double("x"), i.Double("y"));
        }).ToImmutableArray());
    }

    /// <inheritdoc />
    public string Write(Board document)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            w.WriteNumber("version", Version);
            w.WriteStartArray("items");
            foreach (var item in document.Items)
            {
                w.WriteStartObject();
                w.WriteString("id", item.Id);
                w.WriteString("label", item.Label);
                w.WriteNumber("x", item.X);
                w.WriteNumber("y", item.Y);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}

/// <summary>What makes a board valid: well-formed unique ids, a label of 1-80 characters, finite positions.</summary>
public sealed partial class BoardValidator : IValidator<Board>
{
    /// <summary>Longest label allowed.</summary>
    public const int MaxLabel = 80;

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex IdPattern();

    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(Board document)
    {
        var issues = new List<Issue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var n = 0; n < document.Items.Length; n++)
        {
            var item = document.Items[n];
            var path = $"$.items[{n}]";
            if (!IdPattern().IsMatch(item.Id))
                issues.Add(Issue.Error($"id '{item.Id}' must start with a letter and use only lowercase letters, digits and underscores", item.Id, path + ".id"));
            else if (!seen.Add(item.Id))
                issues.Add(Issue.Error($"id '{item.Id}' is used twice; give one of them another id", item.Id, path + ".id"));
            if (string.IsNullOrWhiteSpace(item.Label))
                issues.Add(Issue.Error("the label is empty; give the item a name", item.Id, path + ".label"));
            else if (item.Label.Length > MaxLabel)
                issues.Add(Issue.Error($"the label is {item.Label.Length} characters; keep it to {MaxLabel}", item.Id, path + ".label"));
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y))
                issues.Add(Issue.Error("the position must be finite numbers", item.Id, path));
        }
        return issues;
    }
}

/// <summary>Adds an item. With no <see cref="Id"/>, the next free <c>item_N</c> is chosen and recorded, so replays match.</summary>
public sealed record AddItem(string? Id, string Label, double X, double Y) : ICommand<Board>
{
    /// <inheritdoc />
    public CommandResult<Board> Apply(Board document)
    {
        var id = Id ?? NextId(document);
        if (document.Find(id) is not null) return CommandResult<Board>.Refused(Issue.Error($"an item called '{id}' already exists", id));
        return CommandResult<Board>.Done(new Board(document.Items.Add(new Item(id, Label, X, Y))), this with { Id = id });
    }

    private static string NextId(Board document)
    {
        var used = document.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var n = document.Items.Length + 1;
        while (used.Contains($"item_{n}")) n++;
        return $"item_{n}";
    }
}

/// <summary>Moves an item to a new position.</summary>
public sealed record MoveItem(string Id, double X, double Y) : ICommand<Board>
{
    /// <inheritdoc />
    public CommandResult<Board> Apply(Board document) => document.Find(Id) is { } item
        ? CommandResult<Board>.Done(document.With(item with { X = X, Y = Y }))
        : CommandResult<Board>.Refused(Issue.Error($"there is no item called '{Id}'", Id));
}

/// <summary>Renames an item (its label; the id stays).</summary>
public sealed record RenameItem(string Id, string Label) : ICommand<Board>
{
    /// <inheritdoc />
    public CommandResult<Board> Apply(Board document) => document.Find(Id) is { } item
        ? CommandResult<Board>.Done(document.With(item with { Label = Label }))
        : CommandResult<Board>.Refused(Issue.Error($"there is no item called '{Id}'", Id));
}

/// <summary>Removes an item.</summary>
public sealed record RemoveItem(string Id) : ICommand<Board>
{
    /// <inheritdoc />
    public CommandResult<Board> Apply(Board document) => document.Find(Id) is not null
        ? CommandResult<Board>.Done(new Board(document.Items.RemoveAll(i => i.Id == Id)))
        : CommandResult<Board>.Refused(Issue.Error($"there is no item called '{Id}'", Id));
}

/// <summary>The board's format, validator and commands, registered by hand in one place.</summary>
public static class BoardKit
{
    /// <summary>How boards are read and written.</summary>
    public static readonly BoardFormat Format = new();

    /// <summary>What makes a board valid.</summary>
    public static readonly BoardValidator Validator = new();

    /// <summary>The board's commands.</summary>
    public static CommandCatalog<Board> Catalog() => new CommandCatalog<Board>()
        .Register<AddItem>("toy.add", 1, "Add item")
        .Register<MoveItem>("toy.move", 1, "Move item")
        .Register<RenameItem>("toy.rename", 1, "Rename item")
        .Register<RemoveItem>("toy.remove", 1, "Remove item");

    /// <summary>A new session over <paramref name="board"/>.</summary>
    public static DocumentSession<Board> Session(Board board) => new(Format, Validator, Catalog(), board);

    /// <summary>Opens a board file.</summary>
    public static DocumentSession<Board> Open(string path) => DocumentSession<Board>.Open(path, Format, Validator, Catalog());
}
