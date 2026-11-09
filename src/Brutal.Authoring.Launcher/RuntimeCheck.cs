namespace Brutal.Authoring.Launcher;

// The decision/reporting seam is BCL-only; the caller supplies platform loading and dialog behavior.
internal static class RuntimeCheck
{
    internal const int MissingRuntimeExitCode = 3;
    internal const string FirstLibrary = "VCRUNTIME140.dll";
    internal const string SecondLibrary = "VCRUNTIME140_1.dll";
    internal const string RedistributablePage = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist";

    internal static int Check(Func<string, bool> canLoad, TextWriter errors, Action<string> showMessage)
    {
        ArgumentNullException.ThrowIfNull(canLoad);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(showMessage);
        foreach (var library in new[] { FirstLibrary, SecondLibrary })
        {
            if (canLoad(library)) continue;
            var message = $"BED couldn't load {library}.\n" +
                "BED needs the Microsoft Visual C++ Redistributable (x64).\n" +
                "Install or repair the x64 redistributable from Microsoft's official page, then start BED again:\n" +
                RedistributablePage;
            errors.WriteLine(message);
            showMessage(message);
            return MissingRuntimeExitCode;
        }
        return 0;
    }
}
