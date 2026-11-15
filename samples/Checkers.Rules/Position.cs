using Bed;

namespace Checkers;

/// <summary>Reads a position: the game's own file (<see cref="GameFormat"/>), with a data project's <c>$schema</c> field
/// allowed and ignored, and checked by the game's rules (<see cref="GameValidator"/>).</summary>
public static class Position
{
    /// <summary>The position in <paramref name="text"/>. Throws <see cref="DocumentFormatException"/> or
    /// <see cref="InvalidDocumentException"/>, saying what's wrong.</summary>
    public static Game Read(string text)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject
            ?? throw new DocumentFormatException("$", "expected an object");
        node.Remove("$schema");
        var game = new GameFormat().Read(node.ToJsonString());
        if (new GameValidator().Validate(game).Where(i => i.Severity == Severity.Error).ToList() is { Count: > 0 } errors)
            throw new InvalidDocumentException(errors);
        return game;
    }
}
