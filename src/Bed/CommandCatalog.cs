using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Bed;

/// <summary>What the catalogue knows about one command: its stable id, argument version, the label people see and search
/// (the command palette, goal 1), and whether it is internal (hidden from search).</summary>
/// <param name="Id">The stable command id used by the catalog and journal.</param>
/// <param name="Version">The argument format version registered for this command.</param>
/// <param name="Label">The display label used when showing or searching for the command.</param>
/// <param name="Type">The registered concrete command type used for JSON serialization.</param>
/// <param name="Internal">Whether search hides the command. This does not prevent direct catalog use.</param>
public sealed record CommandInfo(string Id, int Version, string Label, Type Type, bool Internal = false);

/// <summary>
/// The commands a document type supports, registered by hand (no reflection scans). Each command type gets a stable
/// id and an argument version, and the catalogue turns commands into <see cref="JournalEntry"/> records and back. Reading
/// is strict: an unknown id, an unsupported version or an unknown argument is refused, never guessed.
/// </summary>
public sealed class CommandCatalog<TDoc> where TDoc : class
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.Strict,
        // A nullable argument may be left out (it reads as null); every other argument is required.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NullableArgumentsAreOptional } },
    };

    static CommandCatalog()
    {
        foreach (var c in VectorJson<Vector3>.All) Json.Converters.Add(c);   // vectors as [x, y, z], never {}
    }

    private static void NullableArgumentsAreOptional(JsonTypeInfo info)
    {
        foreach (var p in info.Properties)
            if (p.AssociatedParameter is { IsNullable: true }) p.IsRequired = false;
    }

    private readonly List<CommandInfo> commands = [];
    private readonly Dictionary<string, CommandInfo> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, CommandInfo> byType = [];

    /// <summary>Every registered command, in registration order.</summary>
    public IReadOnlyList<CommandInfo> Commands => commands;

    /// <summary>Registers a command type under a stable id. Ids are lowercase and dotted (for example <c>toy.move</c>).</summary>
    public CommandCatalog<TDoc> Register<TCommand>(string id, int version, string label, bool isInternal = false) where TCommand : ICommand<TDoc>
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$"))
            throw new ArgumentException($"command id '{id}' must be lowercase and dotted, like 'toy.move'", nameof(id));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "versions start at 1");
        if (byId.ContainsKey(id)) throw new ArgumentException($"command id '{id}' is already registered", nameof(id));
        if (byType.ContainsKey(typeof(TCommand))) throw new ArgumentException($"{typeof(TCommand).Name} is already registered", nameof(TCommand));
        var info = new CommandInfo(id, version, label, typeof(TCommand), isInternal);
        commands.Add(info);
        byId[id] = info;
        byType[typeof(TCommand)] = info;
        return this;
    }

    /// <summary>The catalogue entry for a command, or an exception if its type isn't registered.</summary>
    public CommandInfo InfoFor(ICommand<TDoc> command) =>
        byType.TryGetValue(command.GetType(), out var info) ? info : throw new InvalidOperationException($"{command.GetType().Name} is not in the command catalogue");

    /// <summary>Commands whose label or id contains <paramref name="text"/> (case-insensitive), internal ones left out.</summary>
    public IEnumerable<CommandInfo> Search(string text) => commands.Where(c => !c.Internal &&
        (c.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || c.Id.Contains(text, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A journal entry recording <paramref name="command"/>.</summary>
    public JournalEntry ToEntry(ICommand<TDoc> command)
    {
        var info = InfoFor(command);
        // The existing move payload is fixed and tiny; avoid cold reflection for thousands of gesture entries.
        var args=command is MoveObject move?SceneMoveBatch.Arguments(move):JsonSerializer.SerializeToElement(command,info.Type,Json);
        return JournalEntry.Do(info.Id,info.Version,args);
    }

    /// <summary>The command a <c>do</c> entry records. Throws <see cref="DocumentFormatException"/> on an unknown id, an
    /// unsupported version, or arguments that don't fit.</summary>
    public ICommand<TDoc> FromEntry(JournalEntry entry, string path = "$")
    {
        if (entry.Op != JournalEntry.DoOp || entry.Command is null || entry.Version is null || entry.Args is null)
            throw new DocumentFormatException(path, "expected a command entry with command, version and args");
        if (!byId.TryGetValue(entry.Command, out var info))
            throw new DocumentFormatException(path + ".command", $"unknown command '{entry.Command}'");
        if (entry.Version != info.Version)
            throw new DocumentFormatException(path + ".version", $"'{entry.Command}' is version {info.Version} here; this entry is version {entry.Version}");
        try
        {
            return (ICommand<TDoc>?)entry.Args.Value.Deserialize(info.Type, Json)
                ?? throw new DocumentFormatException(path + ".args", "arguments are null");
        }
        catch (JsonException e)
        {
            var at = path + ".args" + (e.Path is { Length: > 1 } p ? p[1..] : "");
            // A converter that reads strictly (a scene object, say) reports its own path inside the value: join them.
            if (e.InnerException is DocumentFormatException inner)
                throw new DocumentFormatException(at + (inner.Path.Length > 1 ? inner.Path[1..] : ""), inner.Message);
            throw new DocumentFormatException(at, e.Message);
        }
    }
}
