namespace Bed.Editor.ImGui;

/// <summary>What the last action said (clean-PC test: stale status lines stayed up). A newer message replaces the last
/// one at once. A routine message that only says something went as asked (<see cref="Done"/>) leaves the screen after
/// <see cref="ShownFor"/>; anything else (a refusal, a problem, a question) stays until a newer message replaces it, so a
/// person who stepped away, or is listening with a screen reader, doesn't miss it. <see cref="Text"/> always keeps the
/// last message; only <see cref="Showing"/> goes false.</summary>
internal sealed class StatusNotice(Func<long>? clock = null)
{
    /// <summary>How long a routine message stays on screen, in milliseconds.</summary>
    internal const long ShownFor = 30_000;
    private readonly Func<long> clock = clock ?? (() => Environment.TickCount64);
    private string text = "";
    private long since;
    private bool fades;

    /// <summary>The last message, kept after it leaves the screen.</summary>
    public string Text => text;

    /// <summary>Puts up <paramref name="message"/>, which stays until a newer one replaces it.</summary>
    public void Say(string message) => Put(message, false);

    /// <summary>Puts up a routine message that something went as asked; it leaves the screen after a while.</summary>
    public void Done(string message) => Put(message, true);

    /// <summary>Puts up <paramref name="message"/>, routine when <paramref name="ok"/>.</summary>
    public void Say(bool ok, string message) => Put(message, ok);

    /// <summary>True while the last message should still be on screen.</summary>
    public bool Showing => text.Length > 0 && (!fades || clock() - since < ShownFor);

    private void Put(string message, bool routine) { text = message ?? ""; since = clock(); fades = routine; }
}
