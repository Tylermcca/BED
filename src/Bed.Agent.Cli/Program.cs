using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;

namespace Bed.Agent.Cli;

/// <summary>
/// <c>bed-cli</c>: a data project for AI agents and scripts (goal 20). Every answer is JSON on stdout with a
/// <c>next</c> hint. <c>mcp</c> serves the same calls over stdio for MCP clients (Claude Code, Codex).
/// </summary>
public static class Program
{
    private const string Usage = """
        --help                                  print usage without opening a project or window; use alone
        report                                  print a redacted problem report; no project, files or browser opened
        --version                               print the BED build version without opening a project or window
        usage: bed-cli --data <folder> --schemas <folder> [--map <reference_map.json>] [--adapter <game adapter dll>] [--assets <folder> [--own-name <name>]...] [--read-only] <call> [args]
          ai-setup [--json]                        print AI setup examples for --data/--schemas and optional --map/--adapter;
                                                    no project opening or adapter loading; writes no configuration
          ui [--json] [--script <json array>]          read/action only its own headless instance, never a live window; max32 steps
          scene <file> <action> <args-json> [--preview] [--save]
                                                    typed scene v1 edits; v2 also supports prefab/sprite actions; previews by default
          describe                                  start here: files, what can be saved, lists, problems, calls
          find <file> <list> [text] [--limit N]     items in a list, by label or field text (50 shown unless --limit)
          get <file> <at>                           a value, its label, and where an id leads
          problems [file]                           what the schemas find wrong
          refs <file> <at>                          everything that refers to an item (needs --map)
          repair <file>                             open a file that doesn't fit its schema, to fix it
          set <file> <at> <json> [--preview] [--save]
          insert <file> <list> <index> <json> [--preview] [--save]
          remove <file> <at> [--preview] [--save]
          create <file> <schema> [--save]           a new file from a schema
          rename <file> <new name> [--save]         says how many links point into it (with --map)
          delete <file> [--save]                    says how many links would name nothing (with --map)
          checks                                    the project's checks
          check <id> [--limit N] [--first N]        run one and wait; exit 1 unless clean (every finding unless --limit;
                                                    --first skips to that finding, counting from 0;
                                                    --options <json> gives the choices it takes, as checks lists them)
                                                    (--run works only inside mcp)
          import <file> <origin> [--licence L] [--source S] [--author A] [--ai allowed|forbidden]
                                                    import an asset (needs --assets) with its origin declared:
                                                    ours, licensed (--licence and --source) or publicDomain (--source);
                                                    refused when the file's own rights tags contradict it
                                                    (--own-name, repeatable: the project's own rights holders)
          declare <file> <origin> [--licence L] [--source S] [--save]
                                                    change an imported asset's declared origin
          mcp                                       serve these calls over stdio (MCP)
        <at> and <list> are JSON Pointers (/wares/3/price). An edit, create, rename or delete changes nothing on disk
        without --save: it shows what would happen.

        BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited. See NOTICE.txt.
        """;

    /// <summary>The entry point.</summary>
    public static int Main(string[] args) => Run(args, Console.In, Console.Out, Console.Error);

    /// <summary>Runs one call, or serves MCP. Exit codes: 0 done; 1 the call was refused or found an error; 2 bad
    /// arguments, or a folder or map that can't be read.</summary>
    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter errors)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            if (args.Length != 1) { errors.WriteLine("--help must be used alone."); return 2; }
            output.WriteLine(Usage);
            return 0;
        }
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            if (args.Length != 1) { errors.WriteLine("--version must be used alone."); return 2; }
            output.WriteLine("BED " + BuildVersion.Current);
            return 0;
        }
        if(args.Length>0&&args[0]=="report")
        {
            if(args.Length!=1){errors.WriteLine("usage: bed-cli report");return 2;}
            output.Write(Bed.Editor.ImGui.ProblemReport.Format(new(),log:Bed.Editor.ImGui.ProblemReportLog.Read()));return 0;
        }
        var rest = new List<string>();
        string? data = null, schemas = null, mapPath = null, adapterPath = null, assets = null, licence = null, source = null, author = null, ai = null;
        var limit = 50;
        int? givenLimit = null, first = null;
        string? checkOptions = null;
        string? uiScript = null;
        bool readOnly = false, preview = false, save = false;
        var ownNames = new List<string>();
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--script" when i + 1 < args.Length && uiScript is null: uiScript = args[++i]; break;
                case "--run":
                    errors.WriteLine("run ids last only within one session; use MCP or the agent");
                    return 2;
                case "--data" when i + 1 < args.Length: data = args[++i]; break;
                case "--schemas" when i + 1 < args.Length: schemas = args[++i]; break;
                case "--map" when i + 1 < args.Length: mapPath = args[++i]; break;
                case "--adapter" when i + 1 < args.Length: adapterPath = args[++i]; break;
                case "--assets" when i + 1 < args.Length: assets = args[++i]; break;
                case "--own-name" when i + 1 < args.Length: ownNames.Add(args[++i]); break;
                case "--licence" when i + 1 < args.Length: licence = args[++i]; break;
                case "--source" when i + 1 < args.Length: source = args[++i]; break;
                case "--author" when i + 1 < args.Length: author = args[++i]; break;
                case "--ai" when i + 1 < args.Length: ai = args[++i]; break;
                case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0: limit = n; givenLimit = n; i++; break;   // as MCP's limit (A045)
                case "--first" when i + 1 < args.Length && int.TryParse(args[i + 1], out var from) && from >= 0: first = from; i++; break;
                case "--limit" or "--first":   // a number that can't be used is refused before anything runs (found by A074)
                    errors.WriteLine($"{args[i]} needs a whole number{(args[i] == "--limit" ? " above 0" : ", 0 or more")}");
                    errors.Write(Usage);
                    return 2;
                case "--options" when i + 1 < args.Length: checkOptions = args[++i]; break;
                case "--options":   // nothing to go with it is refused, not run without (found by A077)
                    errors.WriteLine("--options needs a JSON object");
                    errors.Write(Usage);
                    return 2;
                case "--read-only": readOnly = true; break;
                case "--preview": preview = true; break;
                case "--save": save = true; break;
                default: rest.Add(args[i]); break;
            }
        if (uiScript is not null && (rest.Count == 0 || rest[0] != "ui")) { errors.WriteLine("--script is only for the local ui command."); return 2; }
        // Setup examples are formatting only: dispatch before map reads, adapter loading or project opening.
        if (rest.Count > 0 && rest[0] == "ai-setup")
        {
            if (data is null || schemas is null)
            {
                errors.WriteLine("ai-setup needs --data and --schemas.");
                return 2;
            }
            if (rest.Count > 2 || rest.Skip(1).Any(value => value != "--json")
                || assets is not null || ownNames.Count > 0 || licence is not null || source is not null
                || author is not null || ai is not null || givenLimit is not null || first is not null
                || checkOptions is not null || readOnly || preview || save)
            {
                errors.WriteLine("ai-setup accepts only --data, --schemas, --map, --adapter and --json.");
                return 2;
            }
            if (adapterPath is not null && !File.Exists(adapterPath))
            {
                errors.WriteLine($"No adapter file at {adapterPath}.");
                return 2;
            }
            try
            {
                var snippets = Bed.Editor.ImGui.AiSetupSnippets.Create(AppContext.BaseDirectory,
                    new Bed.Editor.ImGui.ProjectPaths(data, schemas, mapPath) { AdapterPath = adapterPath });
                if (rest.Contains("--json", StringComparer.Ordinal))
                    output.WriteLine(JsonSerializer.Serialize(new
                    {
                        command = snippets.Command, desktopJson = snippets.DesktopJson,
                        claudeCode = snippets.ClaudeCode, guidePath = snippets.GuidePath,
                    }, new JsonSerializerOptions { WriteIndented = true }));
                else
                {
                    output.WriteLine("bed-cli (PowerShell):"); output.WriteLine(snippets.Command);
                    output.WriteLine("Claude Desktop:"); output.WriteLine(snippets.DesktopJson);
                    output.WriteLine("Claude Code (PowerShell):"); output.WriteLine(snippets.ClaudeCode);
                    output.WriteLine("Guide:"); output.WriteLine(snippets.GuidePath);
                }
                return 0;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.WriteLine($"Cannot format AI setup paths: {e.Message}");
                return 2;
            }
        }
        // UI observation owns a separate headless host; it never contacts a live editor.
        if (rest.Count > 0 && rest[0] == "ui")
        {
            if (rest.Count > 2 || rest.Skip(1).Any(value => value != "--json")
                || assets is not null || ownNames.Count > 0 || licence is not null || source is not null
                || author is not null || ai is not null || givenLimit is not null || first is not null
                || checkOptions is not null || readOnly || preview || save)
            {
                errors.WriteLine("ui accepts only --data, --schemas, --map, --adapter and --json.");
                return 2;
            }
            return UiSnapshotCommand.Run(data, schemas, mapPath, adapterPath, rest.Contains("--json", StringComparer.Ordinal), output, errors, uiScript);
        }
        if (data is null || schemas is null || rest.Count == 0) { errors.Write(Usage); return 2; }
        AgentSession session;
        try
        {
            var map = mapPath is null ? null : ReferenceMap.Read(File.ReadAllText(mapPath));
            // With a game adapter, the AI gets the game's own rules and checks, as a person does in the editor (found by A188).
            if (adapterPath is not null)
            {
                if (readOnly) { errors.WriteLine("--read-only with --adapter isn't supported yet: an adapter opens its own project, and its contract has no read-only option"); return 2; }
                if (!Directory.Exists(data)) { errors.WriteLine($"there's no data folder at {data}. Check --data."); return 2; }
                if (!Directory.Exists(schemas)) { errors.WriteLine($"there's no schema folder at {schemas}. Check --schemas."); return 2; }
                var paths = new Bed.Editor.ImGui.ProjectPaths(data, schemas, mapPath);
                if (CliAdapterSelection.Load(adapterPath, paths, errors) is not {} adapter) return 2;
                DataProject? project;
                try { project = adapter.Open(new Bed.Editor.ImGui.ProjectPaths(data, schemas, mapPath)); }
                catch (Exception e) { errors.WriteLine($"{adapter.Name} couldn't open the project: {e.Message}. That's a fault in the adapter, not your data: fix it, or leave --adapter out to work on the plain data."); return 2; }
                if (project is null) { errors.WriteLine($"{adapter.Name} gave back no project from Open. That's a fault in the adapter, not your data: fix it, or leave --adapter out to work on the plain data."); return 2; }
                try { Bed.Editor.ImGui.Adapters.ApplyProtectedNames(adapter, project); }
                catch (Exception e) { errors.WriteLine($"{adapter.Name}'s protected names couldn't be read: {e.Message}. That's a fault in the adapter: fix it, or leave --adapter out."); return 2; }
                session = new AgentSession(project, map);
                try { ownNames.AddRange(adapter.OwnNames); }
                catch (Exception e) { errors.WriteLine($"{adapter.Name}'s own names couldn't be read: {e.Message}. That's a fault in the adapter: fix it, or leave --adapter out."); return 2; }
                // All or none, as in the editor: a duplicate id costs the adapter's checks, never the session, and it's said (found by A200).
                try { session.Checks.Add([.. adapter.Checks]); }
                catch (Exception e) { errors.WriteLine($"{adapter.Name}'s checks weren't added: {e.Message}. The project is open without them."); }
            }
            else session = new AgentSession(DataProject.Open(data, schemas, readOnly), map);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDocumentException or UnauthorizedAccessException)
        {
            errors.WriteLine($"can't open the project: {e.Message}");
            return 2;
        }
        if (assets is not null)
        {
            if (!Directory.Exists(assets)) { errors.WriteLine($"there's no asset folder at {assets}. Check --assets."); return 2; }
            session.UseAssets(assets, ownNames);
        }
        // Gum screens next to the data, as the editor finds them: the folder above the data folder and its subfolders.
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data))) is { } gumRoot) session.UseGum(gumRoot);
        if(rest[0]=="scene")
        {
            JsonObject sceneResult;
            try{sceneResult=SceneCommandCall.Run(session,rest,preview,save);}
            catch(ArgumentException error){errors.WriteLine(error.Message);errors.Write(Usage);return 2;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or DocumentFormatException or InvalidDocumentException)
            {sceneResult=new(){["error"]="The scene command was refused: "+error.Message,["next"]="Resolve the file or validation error before trying again."};}
            output.WriteLine(sceneResult.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            return sceneResult.ContainsKey("error")?1:0;
        }
        if (rest[0] == "mcp")
        {
            // Stdin as bytes, read as strict UTF-8, when it's the real console: never decoded through the console's code page (A207).
            if (ReferenceEquals(input, Console.In)) new McpServer(session).Run(Console.OpenStandardInput(), output);
            else new McpServer(session).Run(input, output);
            return 0;
        }
        string A(int n) => n < rest.Count ? rest[n] : throw new ArgumentException($"'{rest[0]}' needs more arguments");
        JsonObject result;
        try
        {
            result = rest[0] switch
            {
                "describe" => session.Describe(),
                "find" => session.Find(A(1), A(2), rest.Count > 3 ? rest[3] : null, limit),
                "get" => session.Get(A(1), A(2)),
                "problems" => session.Problems(rest.Count > 1 ? rest[1] : null),
                "refs" => session.Refs(A(1), A(2)),
                "repair" => session.Repair(A(1)),
                "set" => session.Set(A(1), A(2), A(3), preview),
                "insert" => session.Insert(A(1), A(2), int.Parse(A(3), System.Globalization.CultureInfo.InvariantCulture), A(4), preview),
                "remove" => session.Remove(A(1), A(2), preview),
                // Files change on disk at once, and a CLI run ends with nothing to restore from, so they only preview without --save.
                "create" => session.Create(A(1), A(2), preview: !save),
                "rename" => session.Rename(A(1), A(2), preview: !save),
                "delete" => session.Delete(A(1), preview: !save),
                "checks" => session.ListChecks(),
                "check" => session.RunCheck(A(1), first, givenLimit, checkOptions),
                "import" => session.Import(A(1), A(2), licence, source, author, ai),
                "declare" => session.Declare(A(1), A(2), licence, source, preview: !save),
                _ => throw new ArgumentException($"there's no call '{rest[0]}'"),
            };
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            errors.WriteLine(e.Message);
            errors.Write(Usage);
            return 2;
        }
        if (save && !preview && result["applied"] is not null)
        {
            result["save"] = session.Save(rest[1]);
            // Saved: the answer says so, rather than "unsaved" and "save when ready" (found by A065).
            if (result["save"] is JsonObject s0 && s0["refused"] is not JsonArray { Count: > 0 })
            {
                result["unsaved"] = false;
                result["next"] = "saved; run get to see it, or another set";
            }
        }
        else if (!preview && result["applied"] is not null)
            // A command line run ends here, so its edit isn't kept: say how to keep it (there's no separate save call).
            result["next"] = "nothing was written: run the same command again with --save to keep it";
        output.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var failed = result.ContainsKey("error") || result.ContainsKey("refused")
            || rest[0] == "problems" && result["count"]?.GetValue<int>() > 0   // found problems (found by A045)
            || rest[0] == "check" && result["clean"]?.GetValue<bool>() != true
            || result["save"] is JsonObject s && s["refused"] is JsonArray { Count: > 0 };
        return failed ? 1 : 0;
    }
}
