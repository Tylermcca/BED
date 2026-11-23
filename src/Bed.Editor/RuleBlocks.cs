namespace Bed.Editor;

// INVENTED (BED-BLOCKS01): a game whose rules are snap blocks (a "When ..." block starting a stack, blocks under it, blocks that wrap
// others, slots inside each block's sentence). The game's adapter owns the model: what the blocks are, what each slot may hold, what
// an edit means and whether the file still reads. BED only draws rows, slots and the palette in its own style, and turns each kept
// change into one whole-file edit of the open document, so undo, redo, saving and outside-edit checks are BED's as for any file.

/// <summary>What a row of the block stack is.</summary>
public enum RuleBlockKind
{
    /// <summary>A heading over a list of blocks (for example "Before play"). Blocks can be added under it.</summary>
    Heading,
    /// <summary>A "When ..." block that starts a stack.</summary>
    Hat,
    /// <summary>A block in a stack.</summary>
    Block,
    /// <summary>The "Otherwise" part of a block that wraps two lists.</summary>
    Otherwise,
    /// <summary>Something the game lists above its stacks that isn't a block: a board, a place, a counter.</summary>
    Part,
}

/// <summary>How a block is drawn.</summary>
public enum RuleBlockShape
{
    /// <summary>A plain block with a notch on top and a tab underneath.</summary>
    Plain,
    /// <summary>A rounded top: it starts a stack.</summary>
    Hat,
    /// <summary>It wraps the blocks under it.</summary>
    Wrap,
}

/// <summary>Whether a block is a big block (a named group of blocks shared by several games), and how this game holds it.</summary>
public enum BigBlockState
{
    /// <summary>Not a big block.</summary>
    None,
    /// <summary>Linked to the standard one: it changes when the standard one does.</summary>
    Linked,
    /// <summary>The game's own copy, made with "Make it mine".</summary>
    Own,
    /// <summary>Sealed: it can be used but not opened or changed.</summary>
    Sealed,
}

/// <summary>One row of the stack, in order.</summary>
/// <param name="Id">Where it is in the file; stable until the file changes. The adapter reads it back, BED never parses it.</param>
/// <param name="Depth">How far it sits inside stacks, wrapping blocks and open big blocks.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Title">Its plain name.</param>
/// <param name="Group">The palette group it belongs to (<see cref="RuleBlockGroup.Key"/>), which gives its colour.</param>
/// <param name="Shape">How it is drawn.</param>
/// <param name="Big">For a big block, how this game holds it.</param>
/// <param name="Open">For a big block, whether its blocks are listed under it.</param>
/// <param name="CanGoBack">For the game's own copy of a big block, whether the standard one is there to go back to.</param>
public sealed record RuleBlockRow(string Id, int Depth, RuleBlockKind Kind, string Title, string Group,
    RuleBlockShape Shape = RuleBlockShape.Plain, BigBlockState Big = BigBlockState.None, bool Open = false, bool CanGoBack = false);

/// <summary>What a slot in a block's sentence holds.</summary>
public enum RuleBlockSlotKind
{
    /// <summary>A whole number between <see cref="RuleBlockSlot.Min"/> and <see cref="RuleBlockSlot.Max"/>.</summary>
    Number,
    /// <summary>Yes or no. The value set is "true" or "false".</summary>
    YesNo,
    /// <summary>One of <see cref="RuleBlockSlot.Choices"/>. The value set is the choice's <see cref="RuleBlockChoice.Value"/>.</summary>
    Choice,
    /// <summary>Words typed in. The value set is the words themselves.</summary>
    Words,
    /// <summary>Shown only; it can't be changed here.</summary>
    Shown,
    /// <summary>A list of things (<see cref="RuleBlockSlot.Items"/>): items can be added, taken out and put in another order
    /// (<see cref="IRuleBlockDocument.AddItem"/>, <see cref="IRuleBlockDocument.RemoveItem"/>, <see cref="IRuleBlockDocument.MoveItem"/>).
    /// <see cref="RuleBlockSlot.Choices"/> are what can be added; with none, an item is typed in.</summary>
    List,
}

/// <summary>One of the things a choice slot can hold.</summary>
/// <param name="Value">What is passed back to <see cref="IRuleBlockDocument.Set"/>; BED never reads it.</param>
/// <param name="Label">What people see.</param>
public sealed record RuleBlockChoice(string Value, string Label);

/// <summary>One slot in a block's sentence ("Deal [7] cards face [down]").</summary>
/// <param name="Key">The adapter's name for the slot, passed back to <see cref="IRuleBlockDocument.Set"/>.</param>
/// <param name="Label">Its plain name, for screen readers and the slot's tooltip.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Shown">Its value in plain words.</param>
/// <param name="Choices">For a choice slot, what can be chosen.</param>
/// <param name="Min">For a number, the least it can be.</param>
/// <param name="Max">For a number, the most it can be.</param>
/// <param name="Items">For a list, each item in plain words, in order.</param>
public sealed record RuleBlockSlot(string Key, string Label, RuleBlockSlotKind Kind, string Shown, IReadOnlyList<RuleBlockChoice> Choices,
    int Min = 0, int Max = 0, IReadOnlyList<string>? Items = null);

/// <summary>One part of a block's sentence: words, or the slot named <paramref name="Slot"/> where its value goes
/// ("Deal ", [count], " cards face ", [face], " to each hand").</summary>
/// <param name="Words">The words, when this part is words.</param>
/// <param name="Slot">The slot's <see cref="RuleBlockSlot.Key"/>, when this part is a slot.</param>
public sealed record RuleBlockPart(string Words, string? Slot = null);

/// <summary>A palette group: its plain name and colour.</summary>
/// <param name="Key">Its key, as rows and palette items name it.</param>
/// <param name="Name">Its plain name.</param>
/// <param name="Colour">Its colour as 0xRRGGBB.</param>
public sealed record RuleBlockGroup(string Key, string Name, uint Colour);

/// <summary>A block the palette offers.</summary>
/// <param name="Key">The adapter's name for it, passed back to <see cref="IRuleBlockDocument.Add"/>.</param>
/// <param name="Title">Its plain name.</param>
/// <param name="Group">Its group (<see cref="RuleBlockGroup.Key"/>).</param>
public sealed record RuleBlockPaletteItem(string Key, string Title, string Group);

/// <summary>One thing Check found, next to the block it names (by <see cref="RuleBlockRow.Id"/>) or about the whole game (null).</summary>
public sealed record RuleBlockFinding(string? RowId, string Words);

/// <summary>What Check found.</summary>
/// <param name="Passed">Whether the game can be played as it stands.</param>
/// <param name="Words">The verdict in plain words.</param>
/// <param name="Findings">Each problem, by block.</param>
/// <param name="Stamped">The file's text with Check's record written in, when the adapter keeps one in the file (so the file can show it
/// was checked); null for none. BED keeps it as one edit, which Undo takes back, and only when the file hasn't changed since Check began.</param>
public sealed record RuleBlockCheckResult(bool Passed, string Words, IReadOnlyList<RuleBlockFinding> Findings, string? Stamped = null);

/// <summary>
/// A game's rule blocks, offered by its adapter (<c>IProjectAdapter.RuleBlocks</c>). BED shows its block editor for every file
/// <see cref="Handles"/> accepts. All the game's knowledge stays here; BED draws what it is given.
/// </summary>
public interface IRuleBlocks
{
    /// <summary>Whether the file called <paramref name="fileName"/>, holding <paramref name="text"/>, is a block file.</summary>
    bool Handles(string fileName, string text);

    /// <summary>The palette's groups in order, with their colours.</summary>
    IReadOnlyList<RuleBlockGroup> Groups { get; }

    /// <summary>Every block the palette offers, in order.</summary>
    IReadOnlyList<RuleBlockPaletteItem> Palette { get; }

    /// <summary>A model over a block file's text. Throws <see cref="InvalidDataException"/> with the reason when it can't be read.</summary>
    IRuleBlockDocument Open(string text);

    /// <summary>Check: whether the game can be played as it stands, each finding by block. Runs off the window's thread, on a
    /// snapshot of the text, so it must not touch any open model. Stops early when <paramref name="cancel"/> is cancelled.</summary>
    RuleBlockCheckResult Check(string text, CancellationToken cancel);

    /// <summary>Play's arguments set to play this file with the game, given the project's current ones; null with
    /// <paramref name="why"/> in plain words when it can't be played that way.</summary>
    IReadOnlyList<string>? PlayArguments(IReadOnlyList<string> current, string fileName, string text, out string why);
}

/// <summary>
/// One block file being edited. Each change is made whole or not at all: a refused change leaves <see cref="Text"/> as it was
/// and says why in <see cref="Problem"/>; a kept one says what it did in <see cref="Done"/>. BED keeps undo and redo itself.
/// </summary>
public interface IRuleBlockDocument
{
    /// <summary>The file's text as it stands.</summary>
    string Text { get; }

    /// <summary>The game's name.</summary>
    string Name { get; }

    /// <summary>Why the last change was refused, in plain words, or null when it was kept.</summary>
    string? Problem { get; }

    /// <summary>What the last kept change did, in plain words.</summary>
    string Done { get; }

    /// <summary>Every row of the stack, in order.</summary>
    IReadOnlyList<RuleBlockRow> Rows();

    /// <summary>The slots in a row's sentence, in order.</summary>
    IReadOnlyList<RuleBlockSlot> Slots(RuleBlockRow row);

    /// <summary>What the row does, in plain words ("Deal 7 cards face down to each hand.").</summary>
    string Says(RuleBlockRow row);

    /// <summary>The row's sentence with its slots woven in where their values go ("Deal [7] cards face [down] to each hand").
    /// A slot the sentence leaves out is drawn after it with its label. Unless the game says otherwise: the title, then each slot
    /// with its label.</summary>
    IReadOnlyList<RuleBlockPart> Sentence(RuleBlockRow row) =>
        [new(row.Title), .. Slots(row).SelectMany(s => new RuleBlockPart[] { new(" " + s.Label.ToLowerInvariant() + " "), new("", s.Key) })];

    /// <summary>Sets a slot (see <see cref="RuleBlockSlotKind"/> for what each kind takes).</summary>
    bool Set(RuleBlockRow row, string key, string value);

    /// <summary>One more or one less on a number slot.</summary>
    bool Step(RuleBlockRow row, RuleBlockSlot slot, int by);

    /// <summary>Adds an item to the end of a list slot: a choice's <see cref="RuleBlockChoice.Value"/>, or the words typed in.</summary>
    bool AddItem(RuleBlockRow row, string key, string value) => false;

    /// <summary>Takes the item at <paramref name="index"/> out of a list slot.</summary>
    bool RemoveItem(RuleBlockRow row, string key, int index) => false;

    /// <summary>Moves the item at <paramref name="index"/> of a list slot up (-1) or down (+1).</summary>
    bool MoveItem(RuleBlockRow row, string key, int index, int by) => false;

    /// <summary>Adds a palette block for the chosen row (null: where the game puts new blocks).</summary>
    bool Add(string paletteKey, RuleBlockRow? at);

    /// <summary>Moves a block up (-1) or down (+1) in its list.</summary>
    bool Move(RuleBlockRow row, int by);

    /// <summary>Moves a block to sit after <paramref name="target"/>, or first inside it when it holds blocks (drag and drop).</summary>
    bool MoveTo(RuleBlockRow row, RuleBlockRow target);

    /// <summary>Takes a block, with what's inside it, out.</summary>
    bool Delete(RuleBlockRow row);

    /// <summary>Lists a big block's blocks under it, or hides them. View only: the file doesn't change.</summary>
    bool SetOpen(RuleBlockRow row, bool open);

    /// <summary>"Make it mine": a linked big block becomes the game's own copy.</summary>
    bool MakeItMine(RuleBlockRow row);

    /// <summary>"Go back to the standard one": the game's own copy is linked to the standard one again.</summary>
    bool GoBack(RuleBlockRow row);

    /// <summary>Reads the file again after it changed elsewhere (undo, redo, another panel), keeping what is open.</summary>
    void Reload(string text);
}
