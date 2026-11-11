namespace Bed.Editor.App;

/// <summary>The window-close decision shared by both native render loops and headless contract tests.</summary>
public static class ShellClose
{
    /// <summary>Without a host decision, honor the OS request immediately. Otherwise clear the native request before asking the host; only its acceptance exits.</summary>
    public static bool Decide(bool requested, Action clearRequest, Func<bool, bool>? decision = null)
    {
        ArgumentNullException.ThrowIfNull(clearRequest);
        if (decision is null) return requested;
        clearRequest();
        return decision(requested);
    }
}
