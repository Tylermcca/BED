namespace Brutal.Authoring.Editor;

/// <summary>A point in a map's own frame, or a movement in it. A carrier for showing and moving, not a replacement for the
/// numbers in the document: a source maps a movement back into them, and leaves everything else alone.</summary>
/// <param name="X">Along the frame's horizontal axis.</param>
/// <param name="Y">Along the frame's vertical axis; up the screen is up the frame.</param>
public readonly record struct MapPoint(double X, double Y)
{
    /// <summary>Both numbers are finite.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

/// <summary>What a thing on a map is, whatever list it's in now: a scope (a file, say), a kind and an id. Two things in
/// different scopes can share an id, and a list's order can change, so neither a label nor an index stands in for this.</summary>
/// <param name="Scope">Where its id is unique.</param>
/// <param name="Kind">What sort of thing it is.</param>
/// <param name="Id">Its id in that scope.</param>
public readonly record struct MapKey(string Scope, string Kind, string Id);

/// <summary>The space a map's positions are in.</summary>
/// <param name="Id">Which frame this is.</param>
/// <param name="Version">Changes when positions stop meaning what they did, such as a new origin; a drag under way is dropped.</param>
/// <param name="Unit">What a position's numbers are in, for people.</param>
/// <param name="HorizontalAxis">Which of the document's axes runs across, for people.</param>
/// <param name="VerticalAxis">Which of the document's axes runs up, for people.</param>
public sealed record MapFrame(string Id, long Version, string Unit, string HorizontalAxis, string VerticalAxis);

/// <summary>A thing at a place on a map.</summary>
/// <param name="Key">What it is.</param>
/// <param name="OutlineId">The object the editor selects for it.</param>
/// <param name="Label">What people see.</param>
/// <param name="Position">Where it is in the frame.</param>
/// <param name="Movable">Whether dragging it asks the source for a move.</param>
public sealed record MapMarker(MapKey Key, string OutlineId, string Label, MapPoint Position, bool Movable = true);

/// <summary>A line between two markers. An end the source couldn't resolve is null, with <paramref name="Problem"/> saying
/// why, so it's listed rather than silently not drawn.</summary>
/// <param name="Key">What it is.</param>
/// <param name="From">One end.</param>
/// <param name="To">The other end.</param>
/// <param name="Problem">What's wrong with it, or null.</param>
public sealed record MapEdge(MapKey Key, MapKey? From, MapKey? To, string? Problem = null);

/// <summary>A map as a document holds it, read once for a frame. The source owns these lists and doesn't change them later.</summary>
/// <param name="Frame">The space the positions are in.</param>
/// <param name="Markers">What's on it.</param>
/// <param name="Edges">The lines.</param>
public sealed record MapSnapshot(MapFrame Frame, IReadOnlyList<MapMarker> Markers, IReadOnlyList<MapEdge> Edges);

/// <summary>A grid to snap moves to. Showing a map or saving one never snaps; only a move does.</summary>
/// <param name="Step">The spacing in the frame's units; 0 leaves that axis free.</param>
/// <param name="Origin">Where the lines cross.</param>
/// <param name="Enabled">Whether moves snap unless Alt is held; off, they snap only when it is.</param>
public sealed record MapGrid(MapPoint Step, MapPoint Origin, bool Enabled = true);

/// <summary>How a map is shown.</summary>
/// <param name="Grid">The grid to snap to, or null for none.</param>
/// <param name="PreserveAspect">One unit is the same size both ways, as a physical plan needs. A layout whose axes mean
/// nothing physical can turn it off to fill the panel.</param>
public sealed record MapViewOptions(MapGrid? Grid = null, bool PreserveAspect = true);

/// <summary>What a change on a map does: its commands, as one undo step, and every object they change.</summary>
/// <typeparam name="TDoc">The document type.</typeparam>
/// <param name="Commands">The commands.</param>
/// <param name="Touches">Every object they change, for a focus to judge; not only the selected one.</param>
public sealed record MapEdit<TDoc>(IReadOnlyList<ICommand<TDoc>> Commands, IReadOnlyList<string> Touches) where TDoc : class;

/// <summary>What a game supplies to put its data on a map: what's there, and what a move means. The canvas knows nothing of
/// the game's fields, and the game's own rules still judge every edit. Other changes, such as linking two things, are the
/// game's own commands, through <see cref="EditorModel{TDoc}.ExecuteAt"/>.</summary>
/// <typeparam name="TDoc">The document type.</typeparam>
public interface IMapSource<TDoc> where TDoc : class
{
    /// <summary>The map as <paramref name="document"/> holds it.</summary>
    MapSnapshot Read(TDoc document);

    /// <summary>What moving <paramref name="key"/> by <paramref name="delta"/> means in <paramref name="document"/>, or null
    /// when it means nothing: the source keeps the document's numbers, so it adds the movement to them, never a drawn position.</summary>
    MapEdit<TDoc>? MoveBy(TDoc document, MapKey key, MapPoint delta);
}
