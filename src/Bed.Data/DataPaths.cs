namespace Bed.Data;

// One boundary for opt-in nested resources. Recheck it before access as folders can change after admission.
internal sealed class DataPaths(string root, bool recursive, Func<string, bool>? admit = null)
{
    internal string? Refusal(string name) => SafetyRefusal(name) ?? AdmissionRefusal(name);

    internal string? AdmissionRefusal(string name)
    {
        try { return admit?.Invoke(name) == false ? "That file is outside this project's data folder." : null; }
        catch (Exception e) when (e is not OutOfMemoryException) { return "The data admission policy failed: " + e.Message; }
    }

    internal string? SafetyRefusal(string name)
    {
        if (!recursive && admit is null) return null;
        var parts = name.Split('/');
        if (name.Length == 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.Contains('\\') || parts.Any(p => p is "" or "." or ".." || p.Contains(':') || Reserved(p) || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return "Use a relative JSON path inside the data folder, with forward slashes and no traversal.";
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, name));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return "The data path is outside the project root.";
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return "Data paths cannot pass through a junction or symbolic link.";
            var current = root;
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                try
                {
                    // Unlike Exists, attributes identify dangling links as links too.
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return "Data paths cannot pass through a junction or symbolic link.";
                    if (index < parts.Length - 1 && (attributes & FileAttributes.Directory) == 0) return "A data path parent is not a folder.";
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "The data path cannot be accessed: " + e.Message; }
    }

    private static bool Reserved(string part)
    {
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(stem[3]);
    }

    internal IEnumerable<(string Name, string Path)> Files(bool missingIsEmpty = false)
    {
        if (!recursive)
        {
            if (admit is not null && SafetyRefusal("probe.json") is { } unsafeRoot) throw new IOException(unsafeRoot);
            if (!missingIsEmpty || Directory.Exists(root))
                foreach (var path in Directory.EnumerateFiles(root, "*.json"))
                {
                    var name = Path.GetFileName(path);
                    if (admit is not null && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Included(name)) yield return (name, path);
                }
            yield break;
        }
        if (SafetyRefusal("probe.json") is { } why) throw new IOException(why);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            if (SafetyRefusal(Path.GetRelativePath(root, Path.Combine(folder, "probe.json")).Replace('\\', '/')) is { } changed) throw new IOException(changed);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (SafetyRefusal(name) is { } unsafePath) throw new IOException(unsafePath);
                    if (Included(name)) yield return (name, path);
                }
            }
        }
    }

    private bool Included(string name)
    {
        try { return admit?.Invoke(name) ?? true; }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new IOException("The data admission policy failed: " + e.Message, e); }
    }

    internal void WriteNew(string name, byte[] bytes)
    {
        var path = Target(name);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".data-new-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            if (Refusal(name) is { } changed) throw new IOException(changed);
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (Refusal(name) is null && File.Exists(temporary)) File.Delete(temporary); }
    }

    internal string Target(string name)
    {
        if (Refusal(name) is { } why) throw new IOException(why);
        var path = Path.Combine(root, name);
        if (recursive)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (Refusal(name) is { } changed) throw new IOException(changed);
        }
        return path;
    }
}
