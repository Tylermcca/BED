using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bed.Editor.ImGui;

internal sealed record AiConnectionTarget(string Name, string File, bool Toml = false, bool ExplicitStdio = false);
internal sealed record AiConnectionLocations(string UserProfile, string AppData, string? CodexHome = null)
{
    internal static AiConnectionLocations Current => new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetEnvironmentVariable("CODEX_HOME"));
    internal IReadOnlyList<AiConnectionTarget> Targets =>
    [
        new("Claude Desktop", Path.Combine(AppData, "Claude", "claude_desktop_config.json")),
        new("Claude Code", Path.Combine(UserProfile, ".claude.json"), ExplicitStdio: true),
        new("Codex", Path.Combine(string.IsNullOrWhiteSpace(CodexHome) ? Path.Combine(UserProfile, ".codex") : Path.GetFullPath(CodexHome), "config.toml"), Toml: true),
        new("Cursor", Path.Combine(UserProfile, ".cursor", "mcp.json"), ExplicitStdio: true)
    ];
}

internal sealed record AiConnectionChange(AiConnectionTarget Target, string Preview, byte[]? Before, byte[] After,
    bool Removing, string? Backup, string? Refusal = null)
{
    internal bool CanApply => Refusal is null;
}
internal sealed record AiConnectionResult(bool Success, string Message);

// Config files are read for previews; only Apply writes. Tests supply fake profile/config folders.
internal sealed class AiConnectionFiles
{
    private const int MaxConfigBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions Strings = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly string command;
    private readonly string[] args;
    internal AiConnectionFiles(AiSetupSnippets snippets)
    {
        using var json = JsonDocument.Parse(snippets.DesktopJson);
        var server = json.RootElement.GetProperty("mcpServers"u8).GetProperty("bed"u8);
        command = server.GetProperty("command"u8).GetString()!;
        args = server.GetProperty("args"u8).EnumerateArray().Select(v => v.GetString()!).ToArray();
    }
    private static string Quote(string text) => JsonSerializer.Serialize(text, Strings);
    private string Server(bool explicitStdio) => (explicitStdio ? "{\"type\":\"stdio\", " : "{") +
        "\"command\": " + Quote(command) + ", \"args\": " + JsonSerializer.Serialize(args, Strings) + "}";

    internal AiConnectionChange Preview(AiConnectionTarget target)
    {
        byte[]? before = null;
        try
        {
            if (!File.Exists(command)) throw new IOException("bed-cli.exe is missing from this BED installation. Build or reinstall the complete BED package.");
            if (File.Exists(target.File))
            {
                if (new FileInfo(target.File).Length > MaxConfigBytes) throw new IOException("The settings file is too large for this preview.");
                before = File.ReadAllBytes(target.File);
                if (before.Length > MaxConfigBytes) throw new IOException("The settings file is too large for this preview.");
            }
            var bom = before is [0xef, 0xbb, 0xbf, ..];
            var text = before is null ? "" : Utf8.GetString(before, bom ? 3 : 0, before.Length - (bom ? 3 : 0));
            var (after, preview, removing) = target.Toml ? Toml(text) : Json(text, target.ExplicitStdio);
            var bytes = Utf8.GetBytes(after);
            if (bom) bytes = [0xef, 0xbb, 0xbf, .. bytes];
            var backup = before is null ? null : target.File + ".bed-backup-" + Guid.NewGuid().ToString("N") + ".bak";
            return new(target, preview, before, bytes, removing, backup);
        }
        catch (Exception e) when (FileProblem(e))
        { return new(target, "", before, [], false, null, e is JsonException ? "The settings file could not be read. Fix it in the AI app, then refresh the preview." : e.Message); }
    }

    internal AiConnectionResult Apply(AiConnectionChange change)
    {
        if (!change.CanApply) return new(false, change.Refusal!);
        string? temporary = null;
        try
        {
            var parent = Path.GetDirectoryName(change.Target.File)!;
            if (change.Before is null)
            {
                Directory.CreateDirectory(parent);
                temporary = Path.Combine(parent, ".bed-connect-" + Guid.NewGuid().ToString("N"));
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(change.After); output.Flush(true); }
                File.Move(temporary, change.Target.File); // Never replace a config created after the preview.
                temporary = null;
            }
            else
            {
                // Hold an exclusive file lock from comparison through backup/write, so another writer cannot race the preview.
                using var current = new FileStream(change.Target.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (current.Length != change.Before.Length) return Changed();
                var bytes = new byte[change.Before.Length]; current.ReadExactly(bytes);
                if (!bytes.AsSpan().SequenceEqual(change.Before)) return Changed();
                using (var backup = new FileStream(change.Backup!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { backup.Write(bytes); backup.Flush(true); }
                try
                {
                    current.Position = 0; current.Write(change.After); current.SetLength(change.After.Length); current.Flush(true);
                }
                catch
                {
                    // Best effort restoration; the already completed backup remains if the disk cannot accept a write.
                    try { current.Position = 0; current.Write(bytes); current.SetLength(bytes.Length); current.Flush(true); } catch (IOException) { }
                    throw;
                }
            }
            return new(true, (change.Removing ? "Removed the BED connection. " : "Saved the BED connection. ") +
                "Restart " + change.Target.Name + " to load the change." + (change.Backup is null ? "" : " Backup: " + change.Backup));
        }
        catch (Exception e) when (FileProblem(e))
        { return new(false, "The settings file could not be changed: " + e.Message + (change.Backup is not null && File.Exists(change.Backup) ? " Backup: " + change.Backup : "")); }
        finally { if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { } }
    }
    private static AiConnectionResult Changed() => new(false, "The settings file changed after this preview. Refresh before changing it.");
    private static bool FileProblem(Exception e) => e is IOException or UnauthorizedAccessException or JsonException or FormatException or DecoderFallbackException or ArgumentException;
    private bool Matches(JsonElement entry) => entry.ValueKind == JsonValueKind.Object &&
        entry.TryGetProperty("command"u8, out var exe) && exe.ValueKind == JsonValueKind.String && exe.GetString() == command &&
        entry.TryGetProperty("args"u8, out var values) && values.ValueKind == JsonValueKind.Array &&
        values.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String) && values.EnumerateArray().Select(v => v.GetString()).SequenceEqual(args);
    private static string FreeName(IEnumerable<string> names)
    {
        var occupied = names.ToHashSet(StringComparer.Ordinal);
        if (!occupied.Contains("bed")) return "bed";
        for (var n = 2; n <= 1000; n++) if (!occupied.Contains("bed-" + n)) return "bed-" + n;
        throw new FormatException("There are too many existing BED entries. Remove an unused connection in the AI app first.");
    }

    private (string After, string Preview, bool Removing) Json(string text, bool explicitStdio)
    {
        if (text.Length == 0) text = "{}";
        var bytes = Utf8.GetBytes(text);
        using var json = JsonDocument.Parse(bytes, JsonOptions);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("The settings file must group its settings inside { and }.");
        var root = Object(bytes, 0);
        var field = root.Properties.SingleOrDefault(p => p.Name == "mcpServers");
        if (field is null)
        {
            var key = "bed"; var value = "{" + Quote(key) + ": " + Server(explicitStdio) + "}";
            return (Add(bytes, root, "mcpServers", value), "Add the AI connections section with this entry:\n" + Quote(key) + ": " + Server(explicitStdio), false);
        }
        if (json.RootElement.GetProperty("mcpServers"u8).ValueKind != JsonValueKind.Object)
            throw new FormatException("The AI connections section must group named connections inside { and }. The file was left unchanged.");
        var servers = Object(bytes, field.ValueStart);
        var entries = json.RootElement.GetProperty("mcpServers"u8);
        var configured = servers.Properties.FirstOrDefault(p => Matches(entries.GetProperty(p.Name)));
        if (configured is not null)
            return (Remove(bytes, servers, configured), "Remove only the AI connection named " + Quote(configured.Name) + ".", true);
        var name = FreeName(servers.Properties.Select(p => p.Name));
        return (Add(bytes, servers, name, Server(explicitStdio)), "Add this entry to the AI connections section:\n" + Quote(name) + ": " + Server(explicitStdio), false);
    }
    private sealed record Property(string Name, int Start, int ValueStart, int End);
    private sealed record ObjectRange(int Start, int Close, List<Property> Properties);
    private static ObjectRange Object(byte[] bytes, int start)
    {
        var reader = new Utf8JsonReader(bytes.AsSpan(start), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new FormatException("Each settings group must begin with { and end with }.");
        var properties = new List<Property>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new(start, start + (int)reader.TokenStartIndex, properties);
            if (reader.TokenType != JsonTokenType.PropertyName) throw new FormatException("The settings file contains a value BED cannot read.");
            var name = reader.GetString()!;
            if (properties.Any(p => p.Name == name)) throw new FormatException("The settings file repeats a setting name. Fix it in the AI app first.");
            var propertyStart = start + (int)reader.TokenStartIndex;
            reader.Read(); var valueStart = start + (int)reader.TokenStartIndex; reader.Skip();
            properties.Add(new(name, propertyStart, valueStart, start + (int)reader.BytesConsumed));
        }
        throw new FormatException("The settings file is incomplete.");
    }
    // Only insertion/removal ranges change; unrelated settings, comments, whitespace and UTF-8 bytes are retained.
    private static string Add(byte[] bytes, ObjectRange target, string name, string value)
    {
        var newline = Utf8.GetString(bytes).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insert = newline + "  " + Quote(name) + ": " + value + newline;
        var result = bytes.ToList(); result.InsertRange(target.Close, Utf8.GetBytes(insert));
        if (target.Properties.LastOrDefault() is { } last && Comma(bytes, last.End, target.Close) < 0) result.Insert(last.End, (byte)',');
        return Utf8.GetString(result.ToArray());
    }
    private static string Remove(byte[] bytes, ObjectRange target, Property property)
    {
        var i = target.Properties.IndexOf(property);
        var comma = Comma(bytes, property.End, i + 1 < target.Properties.Count ? target.Properties[i + 1].Start : target.Close);
        if (comma < 0 && i > 0) comma = Comma(bytes, target.Properties[i - 1].End, property.Start);
        var spans = new List<(int Start, int Count)> { (property.Start, property.End - property.Start) };
        if (comma >= 0) spans.Add((comma, 1));
        var result = bytes.ToList(); foreach (var span in spans.OrderByDescending(s => s.Start)) result.RemoveRange(span.Start, span.Count);
        return Utf8.GetString(result.ToArray());
    }
    private static int Comma(byte[] bytes, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] == ',' ) return i;
            if (bytes[i] == '/' && i + 1 < end && bytes[i + 1] == '/') { while (i < end && bytes[i] != '\n') i++; }
            else if (bytes[i] == '/' && i + 1 < end && bytes[i + 1] == '*') { i += 2; while (i + 1 < end && !(bytes[i] == '*' && bytes[i + 1] == '/')) i++; i++; }
        }
        return -1;
    }

    private (string After, string Preview, bool Removing) Toml(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = TomlLines(text);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            // Escaped quoted table keys need a complete TOML parser; refuse rather than risk a duplicate name.
            if (line.Text.TrimStart().StartsWith('[') && line.Text.Contains((char)92))
                throw new FormatException("This Codex settings file writes connection names in a way BED cannot edit. Use Codex to change the connection instead.");
            var match = Regex.Match(line.Text, "^\\s*\\[\\[?\\s*(?:mcp_servers|\"mcp_servers\"|'mcp_servers')\\s*\\.\\s*(?:\"(?<n>[^\"]+)\"|'(?<n>[^']+)'|(?<n>[A-Za-z0-9_-]+))");
            if (match.Success) names.Add(match.Groups["n"].Value);
            if (Regex.IsMatch(line.Text, "^\\s*(?:mcp_servers|\"mcp_servers\"|'mcp_servers')\\s*="))
                throw new FormatException("This Codex settings file puts its connections on one line. Add the BED connection in Codex instead.");
            if (Regex.IsMatch(line.Text, "^\\s*\\[\\s*(?:mcp_servers|\"mcp_servers\"|'mcp_servers')\\s*\\]"))
                throw new FormatException("This Codex settings file groups connections in a way BED cannot edit. Add the BED connection in Codex instead.");
        }
        foreach (var line in lines.Where(l => l.Text.StartsWith("# BED connection ", StringComparison.Ordinal)))
        {
            var key = line.Text[17..].Trim();
            var block = TomlBlock(key, newline);
            if (text.AsSpan(line.Start).StartsWith(block, StringComparison.Ordinal))
            {
                var end = line.Start + block.Length;
                // Do not orphan later settings which have become part of this table.
                foreach (var following in lines.Where(l => l.Start >= end))
                {
                    var value = following.Text.TrimStart();
                    if (value.StartsWith('[')) break;
                    if (value.Length > 0 && !value.StartsWith('#'))
                        throw new FormatException("The BED connection has extra settings. Remove it in Codex.");
                }
                if (lines.Any(l => (l.Start < line.Start || l.Start >= end) && Regex.IsMatch(l.Text, "^\\s*\\[\\[?\\s*mcp_servers\\s*\\.\\s*" + Regex.Escape(key) + "(?:\\s*\\.|\\s*\\])")))
                    throw new FormatException("The BED connection has extra sections. Remove it in Codex.");
                return (text.Remove(line.Start, block.Length), "Remove only the BED connection block named " + Quote(key) + ".", true);
            }
        }
        var name = FreeName(names); var addition = TomlBlock(name, newline);
        return (text + (text.Length == 0 || text.EndsWith('\n') ? "" : newline) + addition, "Append this block:\n" + addition, false);
    }
    private string TomlBlock(string name, string newline) => string.Join(newline,
        "# BED connection " + name, "[mcp_servers." + name + "]", "command = " + Quote(command),
        "args = " + JsonSerializer.Serialize(args, Strings), "# End BED connection " + name, "");
    private sealed record TomlLine(int Start, string Text);
    private static List<TomlLine> TomlLines(string text)
    {
        var lines = new List<TomlLine>(); char quote = '\0'; var triple = false; var depth = 0; var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (quote == '\0' && depth == 0)
            {
                lines.Add(new(offset, line.TrimEnd('\r')));
                if (trimmed.StartsWith('#') || trimmed.StartsWith('[')) { offset += line.Length + 1; continue; }
            }
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quote != '\0')
                {
                    if (quote == '"' && c == '\\') { i++; continue; }
                    if (c == quote && (!triple || i + 2 < line.Length && line[i + 1] == quote && line[i + 2] == quote))
                    { if (triple) i += 2; quote = '\0'; triple = false; }
                }
                else if (c == '#') break;
                else if (c is '"' or '\'')
                { quote = c; triple = i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c; if (triple) i += 2; }
                else if (c is '[' or '{') depth++;
                else if (c is ']' or '}') { if (--depth < 0) throw new FormatException("The Codex settings file has a bracket without a matching closing bracket."); }
            }
            if (quote != '\0' && !triple) throw new FormatException("The Codex settings file has text with a missing closing quote.");
            offset += line.Length + 1;
        }
        if (quote != '\0' || depth != 0) throw new FormatException("The Codex settings file has text or a list that is not closed.");
        return lines;
    }
}
