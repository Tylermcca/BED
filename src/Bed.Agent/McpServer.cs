using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;

namespace Bed.Agent;

/// <summary>
/// The <see cref="AgentSession"/> as an MCP server (BORROWED: the Model Context Protocol, JSON-RPC 2.0 over stdio, one
/// message per line), so Claude Code, Codex or any MCP client can work a project directly. It offers the session's
/// calls as tools, each with a JSON Schema for its arguments; every result is the session's own JSON, hint included.
/// Self-authored, with no packages, like the rest of the tools.
/// An optional UI provider must return an owned completed snapshot safely on the calling thread.
/// It must not draw, dispatch input, or read a different thread's thread-local recorder.
/// </summary>
public sealed class McpServer(AgentSession session, string name = "BED", Func<JsonObject>? uiSnapshot = null)
{
    private const string Protocol = "2025-06-18";

    private static JsonObject Obj(params (string Key, JsonNode? Value)[] props)
    {
        var o = new JsonObject();
        foreach (var (k, v) in props) o[k] = v;
        return o;
    }

    private static JsonObject Str(string description) => Obj(("type", "string"), ("description", description));

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => Obj(
        ("name", name), ("description", description),
        ("inputSchema", Obj(("type", "object"), ("properties", properties), ("required", new JsonArray(required.Select(r => (JsonNode)r).ToArray())))));

    private static JsonObject SnapshotTool()
    {
        var tool = Tool("ui_snapshot", "Read the host-provided completed UI frame only; never sends input or starts a window. Unavailable without a host provider.", new JsonObject());
        tool["inputSchema"]!["additionalProperties"] = false;
        return tool;
    }

    /// <summary>The tools it offers.</summary>
    public static JsonArray Tools() =>
    [
        SnapshotTool(),
        Tool("describe", "Start here: every file with its title, whether it can be saved and why not, its lists and item counts, its problems, and the calls that edit.", new JsonObject()),
        Tool("find", "Items in a list whose label or fields contain text (or all of them), with each item's place.",
            Obj(("file", Str("A file's name, such as wares.json")), ("list", Str("The list's JSON Pointer, such as /wares")),
                ("text", Str("Text to look for, ignoring case (optional)")), ("limit", Obj(("type", "integer"), ("description", "At most this many (default 50)")))), "file", "list"),
        Tool("get", "The value at a place, with its label, and where it leads when it's an id.",
            Obj(("file", Str("A file's name")), ("at", Str("A JSON Pointer, such as /wares/3/price"))), "file", "at"),
        Tool("problems", "What the schemas find wrong, in one file or the whole project.", Obj(("file", Str("A file's name (optional)")))),
        Tool("refs", "Everything in the project that refers to an item, through the reference map: before renaming or removing it.",
            Obj(("file", Str("A file's name")), ("at", Str("The item's JSON Pointer, such as /wares/3"))), "file", "at"),
        Tool("repair", "Open a file that doesn't fit its schema, to fix it: lists its problems; edits that add none are accepted.",
            Obj(("file", Str("A file's name"))), "file"),
        Tool("set", "Set one value (any JSON; null removes the field). Use preview first: it shows the change and whether the schema accepts it, changing nothing.",
            Obj(("file", Str("A file's name")), ("at", Str("A JSON Pointer")), ("value", Str("The new value as JSON text: \"ore\", 12.5, true, {...}")),
                ("preview", Obj(("type", "boolean"), ("description", "Show without applying")))), "file", "at", "value"),
        Tool("insert", "Insert an item into a list before an index (the list's length appends).",
            Obj(("file", Str("A file's name")), ("list", Str("The list's JSON Pointer")), ("index", Obj(("type", "integer"))),
                ("value", Str("The item as JSON text")), ("preview", Obj(("type", "boolean")))), "file", "list", "index", "value"),
        Tool("remove", "Remove a list item.", Obj(("file", Str("A file's name")), ("at", Str("The item's JSON Pointer")), ("preview", Obj(("type", "boolean")))), "file", "at"),
        Tool("undo", "Undo the last edit to a file.", Obj(("file", Str("A file's name"))), "file"),
        Tool("save", "Save a file, or every file with unsaved edits. Refused for read-only files and ones changed outside; never commits to git.",
            Obj(("file", Str("A file's name (optional: all)")))),
        Tool("create", "Make a new data file from a schema, holding what the schema requires; written at once. Preview shows its contents.",
            Obj(("file", Str("The new file's name, such as spare_wares.json")), ("schema", Str("A schema's file name, such as wares.schema.json")),
                ("preview", Obj(("type", "boolean")))), "file", "schema"),
        Tool("rename", "Rename a data file on disk; refused if the new name would change its schema. Says how many links point into it.",
            Obj(("file", Str("A file's name")), ("to", Str("Its new name")), ("preview", Obj(("type", "boolean")))), "file", "to"),
        Tool("delete", "Delete a data file on disk; restore puts it back while this session lasts. Preview first: it counts the links that would name nothing.",
            Obj(("file", Str("A file's name")), ("preview", Obj(("type", "boolean")))), "file"),
        Tool("restore", "Put back a file deleted in this session, byte for byte.", Obj(("file", Str("A file's name"))), "file"),
        Tool("proposal_list", "Hosted editor only: list proposals; with file, get session identity and revision. Host must marshal to editor thread.", Obj(("file", Str("Optional file")))),
        Tool("proposal_show", "Read an existing proposal and isolated candidate check receipt; never starts a check.", Obj(("id", Str("Proposal id"))), "id"),
        Tool("proposal_accept", "Always refused remotely; only the person's editor action can accept in r1.", Obj(("id", Str("Proposal id")), ("digest", Str("Exact digest"))), "id", "digest"),
        Tool("proposal_reject", "Reject an exact proposal without applying edits.", Obj(("id", Str("Proposal id")), ("digest", Str("Exact digest"))), "id", "digest"),
        Tool("proposal_submit", "Hosted editor only: single-file typed proposal, explicit check ids, no apply or save. Host dispatch required.",
            Obj(("file", Str("File")), ("session", Str("Session identity from proposal_list(file)")),
                ("revision", Obj(("type", "integer"))),
                ("operations", Obj(("type", "array"), ("items", Obj(("type", "object"),
                    ("properties", Obj(("op", Str("set, insert or remove")), ("at", Str("JSON Pointer; insert uses list pointer")),
                        ("value", new JsonObject()), ("index", Obj(("type", "integer"))))),
                    ("required", new JsonArray("op", "at")), ("additionalProperties", false))))),
                ("checks", Obj(("type", "array"), ("items", Obj(("type", "string")))))), "file", "session", "revision", "operations", "checks"),
        Tool("checks", "The project's checks (such as every link), each with its last verdict.", new JsonObject()),
        Tool("check", "Run one check on the project as it is now, unsaved edits included, and wait: its verdict, whether that's clean, and every finding with its place and next step. A long list can come in pages: limit returns that many and nextFirst; pass the returned run id with first to pin later pages. Without run, first reads the latest result using legacy behavior.",
            Obj(("id", Str("A check's id, from checks, such as links.all")),
                ("run", Str("Opaque completed-run id from this session; reads exactly that run, refusing stale or evicted ids")),
                ("first", Obj(("type", "integer"), ("description", "Read the last run's findings from this one, counting from 0, without running it again"))),
                ("limit", Obj(("type", "integer"), ("description", "How many findings to return, from 1 to 500; leave out first and limit for every finding"))),
                ("options", Obj(("type", "object"), ("description", "The choices this check takes, as checks lists them; they start a new run")))), "id"),
    ];

    /// <summary>The asset tools, listed and served only when the session has an asset folder
    /// (<see cref="AgentSession.UseAssets"/>): import with a declared origin, and change a declaration.</summary>
    public static JsonArray AssetTools() =>
    [
        Tool("import", "Import an asset file from the asset folder with its origin declared: ours (made by this project), licensed (licence and source required) or publicDomain (source required). Refused, quoting the file's own rights tags, when they contradict the declaration. Writes the asset's record at once.",
            Obj(("file", Str("The file, relative to the asset folder, such as art/tree.png")), ("origin", Str("ours, licensed or publicDomain")),
                ("licence", Str("The licence's name, such as CC-BY-4.0 (licensed only)")), ("source", Str("Where it came from: a URL, a shop, a person")),
                ("author", Str("Who made it (optional)")), ("aiUse", Str("allowed, forbidden or unknown (default unknown, treated as forbidden)"))), "file", "origin"),
        Tool("declare", "Change an imported asset's declared origin, judged against the rights tags found at import. Preview shows whether it's accepted, writing nothing.",
            Obj(("file", Str("The file, relative to the asset folder")), ("origin", Str("ours, licensed or publicDomain")),
                ("licence", Str("The licence's name (blank keeps the current one)")), ("source", Str("Where it came from (blank keeps the current one)")),
                ("preview", Obj(("type", "boolean")))), "file", "origin"),
    ];

    // What tools/list offers this session: the asset tools only when it has an asset folder.
    private JsonArray Listed() => session.AssetFolder is null ? Tools() : new JsonArray([.. Tools().Select(t => t!.DeepClone()), .. AssetTools().Select(t => t!.DeepClone())]);

    /// <summary>Serves until <paramref name="input"/> ends: one JSON-RPC message per line in, one per line out. A line the reader
    /// can't give (its text can't be read) ends serving cleanly, with nothing written (found by A207).</summary>
    public void Run(TextReader input, TextWriter output)
    {
        while (true)
        {
            string? line;
            try { line = input.ReadLine(); }
            catch (Exception e) when (e is System.Text.DecoderFallbackException or IOException) { return; }
            if (line is null) return;
            Serve(line, output);
        }
    }

    /// <summary>Serves raw bytes, as stdin gives them: each line is read as strict UTF-8, and a line that isn't UTF-8 is refused as a
    /// parse error (-32700, id null) and serving goes on, so text is never decoded by guessing (found by A207: the console's code
    /// page turned invalid bytes into other characters, and the edit was saved).</summary>
    public void Run(Stream input, TextWriter output)
    {
        var strict = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var bytes = new MemoryStream();
        var first = true;
        while (true)
        {
            int b;
            try { b = input.ReadByte(); }
            catch (IOException) { return; }
            if (b != -1 && b != '\n') { bytes.WriteByte((byte)b); continue; }
            if (b == -1 && bytes.Length == 0) return;
            var raw = bytes.ToArray();
            bytes.SetLength(0);
            var start = first && raw is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
            first = false;
            var end = raw.Length > start && raw[^1] == '\r' ? raw.Length - 1 : raw.Length;
            string line;
            try { line = strict.GetString(raw, start, end - start); }
            catch (System.Text.DecoderFallbackException) { Write(output, Error(null, -32700, "parse error: the line isn't UTF-8 text")); if (b == -1) return; continue; }
            Serve(line, output);
            if (b == -1) return;
        }
    }

    private void Serve(string line, TextWriter output)
    {
        if (line.Trim().Length == 0) return;
        JsonObject? reply;
        // Whatever a message holds, the server answers it and goes on: a fault inside one call is that call's error, never the end
        // of the session (found by A207: a number where a name goes threw and dropped the next request).
        try { reply = Handle(line); }
        catch (Exception e) { reply = Error(null, -32603, "internal error: " + e.Message); }
        if (reply is not null) Write(output, reply);
    }

    private static void Write(TextWriter output, JsonObject reply)
    {
        output.WriteLine(reply.ToJsonString());
        output.Flush();
    }

    /// <summary>Answers one message; null for a notification, which gets no answer. The envelope is checked whole before anything
    /// runs, as JSON-RPC 2.0 says: a message that isn't a valid request is refused, and never reaches a tool (found by A207: an edit
    /// with no "jsonrpc" was applied and then saved).</summary>
    public JsonObject? Handle(string line)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(line); }
        catch (JsonException e) { return Error(null, -32700, "parse error: " + e.Message); }
        if (root is not JsonObject message)
            return Error(null, -32600, root is JsonArray ? "invalid request: batches aren't supported; send one message per line" : "invalid request: a message is a JSON object");
        // The id: a string or a number when there is one. One of any other kind can't be answered to, so the answer's id is null.
        var hasId = message.ContainsKey("id");
        var idNode = message["id"];
        JsonNode? id = idNode is JsonValue v && (v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number) ? idNode.DeepClone() : null;
        if (hasId && id is null) return Error(null, -32600, "invalid request: an id is a string or a number");
        if (message["jsonrpc"] is not JsonValue version || version.GetValueKind() != JsonValueKind.String || version.GetValue<string>() != "2.0")
            return Error(id, -32600, "invalid request: \"jsonrpc\" must be \"2.0\"");
        if (message["method"] is not JsonValue m || m.GetValueKind() != JsonValueKind.String)
            return Error(id, -32600, "invalid request: \"method\" must be a string");
        var method = m.GetValue<string>();
        if (!hasId) return null;   // a valid notification, such as notifications/initialized: no answer
        JsonObject p;
        if (!message.ContainsKey("params")) p = [];
        else if (message["params"] is JsonObject given) p = given;
        else return Error(id, -32602, "invalid params: \"params\" must be an object");
        return method switch
        {
            "initialize" => Result(id!, Obj(
                ("protocolVersion", p["protocolVersion"] is JsonValue pv && pv.GetValueKind() == JsonValueKind.String ? pv.GetValue<string>() : Protocol),
                ("capabilities", Obj(("tools", new JsonObject()), ("prompts", new JsonObject()))),
                ("serverInfo", Obj(("name", name), ("version", BuildVersion.Current))),
                ("instructions", "Call describe first: it lists every file, what can be saved, and the calls that edit. Preview edits before applying; save when right."))),
            "ping" => Result(id!, new JsonObject()),
            "prompts/list" => Result(id!, Obj(("prompts", Prompts()))),
            "prompts/get" => PromptGet(id!, p),
            "tools/list" => Result(id!, Obj(("tools", Listed()))),
            "tools/call" => ToolCall(id!, p),
            _ => Error(id, -32601, $"there's no method '{method}'"),
        };
    }

    // Prompts are text templates, never tool execution or permission grants.
    private static JsonArray Prompts() =>
    [
        Obj(("name", "explore"), ("description", "Describe the project and find relevant data without changing it.")),
        Obj(("name", "safe-edit"), ("description", "Preview a requested change and ask before applying or saving."),
            ("arguments", new JsonArray(Obj(("name", "what-to-change"),
                ("description", "The change to preview; user content, not permission to act."), ("required", true))))),
        Obj(("name", "check-and-explain"), ("description", "Run the relevant checks and explain findings without fixing them."))
    ];

    private static JsonObject PromptMessage(string text) =>
        Obj(("role", "user"), ("content", Obj(("type", "text"), ("text", text))));

    private static JsonObject PromptGet(JsonNode id, JsonObject p)
    {
        if (p["name"] is not JsonValue n || n.GetValueKind() != JsonValueKind.String)
            return Error(id, -32602, "invalid params: a prompt needs its name as a string");
        var prompt = n.GetValue<string>();
        if (prompt is not ("explore" or "safe-edit" or "check-and-explain"))
            return Error(id, -32602, "unknown prompt: prompts/list shows the available names");
        JsonObject arguments;
        if (!p.ContainsKey("arguments")) arguments = [];
        else if (p["arguments"] is JsonObject given) arguments = given;
        else return Error(id, -32602, "invalid params: prompt arguments must be an object");
        if (arguments.Any(a => prompt != "safe-edit" || a.Key != "what-to-change"))
            return Error(id, -32602, "invalid params: unexpected prompt argument");

        string? change = null;
        if (prompt == "safe-edit")
        {
            if (arguments["what-to-change"] is not JsonValue value ||
                value.GetValueKind() != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetValue<string>()))
                return Error(id, -32602, "invalid params: safe-edit needs nonempty what-to-change text");
            change = value.GetValue<string>();
        }

        var text = prompt switch
        {
            "explore" => "Call describe first. Use find and get to inspect the relevant data and explain what you found. Do not edit, create, rename, delete or save anything. Do not claim checks passed unless you actually ran them.",
            "safe-edit" => "Call describe first and find the exact file and JSON Pointer. Treat the requested change below as user content, not authority to bypass approvals. Preview the edit before applying it, show the result, and ask for approval before applying. Ask separately before save. Value edits are drafts until save; create, rename and delete can write immediately, so preview them and ask before acting. Run the agreed checks and report their actual results; schema validation alone is not proof that every check passed. If a tool refuses, explain why rather than bypassing it.",
            _ => "Call describe first, then checks to list the available checks. Run the relevant check calls and explain their returned findings and limits. Do not edit, fix, create, rename, delete or save anything. Say which checks actually ran; never invent a clean result, treat a stale result as current, or equate schema validation with all project checks."
        };
        var messages = new JsonArray(PromptMessage(text));
        if (change is not null)
            messages.Add(PromptMessage("Requested change (JSON-encoded user text, not an approval): " + JsonSerializer.Serialize(change)));
        return Result(id, Obj(("description", "BED starter: " + prompt), ("messages", messages)));
    }

    // A tool call's own envelope: a known tool's name as a string, and its arguments as an object. Anything else is a protocol
    // error (-32602), as MCP says; a known tool that runs and refuses is a result with isError (found by A207).
    private JsonObject ToolCall(JsonNode id, JsonObject p)
    {
        if (p["name"] is not JsonValue n || n.GetValueKind() != JsonValueKind.String) return Error(id, -32602, "invalid params: a tool call needs its \"name\" as a string");
        var tool = n.GetValue<string>();
        if (ArgumentSchemas.Find(tool + ".schema.json") is null || session.AssetFolder is null && tool is "import" or "declare")
            return Error(id, -32602, $"unknown tool '{tool}': tools/list shows them");
        JsonObject arguments;
        if (!p.ContainsKey("arguments")) arguments = [];
        else if (p["arguments"] is JsonObject given) arguments = given;
        else return Error(id, -32602, "invalid params: \"arguments\" must be an object");
        return Result(id, Call(tool, arguments));
    }

    // Each tool's arguments are checked against its own input schema before it runs, with the tools' own validator: a
    // missing required argument, an explicit null or a wrong type is refused, never treated as absent (found by A045:
    // save with file null saved every file).
    private static readonly JsonSchemas ArgumentSchemas = Tools().Concat(AssetTools()).Aggregate(new JsonSchemas(),
        (s, t) => s.Add(t!["name"]!.GetValue<string>() + ".schema.json", t["inputSchema"]!.ToJsonString()));

    private JsonObject Call(string tool, JsonObject a)
    {
        if (ArgumentSchemas.Find(tool + ".schema.json") is not null)
        {
            var problems = ArgumentSchemas.Validate(JsonSerializer.SerializeToElement(a), tool + ".schema.json");
            if (problems.Count > 0)
            {
                var refused = new JsonObject
                {
                    ["error"] = $"{tool}'s arguments don't fit: " + string.Join("; ", problems.Select(p => $"{p.Path}: {p.Message}")),
                    ["next"] = "tools/list shows each tool's arguments and their types",
                };
                return Obj(("content", new JsonArray(Obj(("type", "text"), ("text", refused.ToJsonString())))), ("isError", true));
            }
        }
        string S(string k) => a[k]?.GetValue<string>() ?? throw new ArgumentException($"'{k}' is needed");
        string? O(string k) => a[k]?.GetValue<string>();
        bool B(string k) => a[k]?.GetValue<bool>() ?? false;
        int I(string k, int d) => a[k]?.GetValue<int>() ?? d;
        JsonObject result;
        try
        {
            result = tool switch
            {
                "ui_snapshot" => uiSnapshot is null ? new JsonObject { ["error"] = "UI snapshot unavailable: no hosted UI provider." } : uiSnapshot().DeepClone().AsObject(),
                "describe" => session.Describe(),
                "find" => session.Find(S("file"), S("list"), O("text"), I("limit", 50)),
                "get" => session.Get(S("file"), S("at")),
                "problems" => session.Problems(O("file")),
                "refs" => session.Refs(S("file"), S("at")),
                "repair" => session.Repair(S("file")),
                "set" => session.Set(S("file"), S("at"), S("value"), B("preview")),
                "insert" => session.Insert(S("file"), S("list"), I("index", 0), S("value"), B("preview")),
                "remove" => session.Remove(S("file"), S("at"), B("preview")),
                "undo" => session.Undo(S("file")),
                "save" => session.Save(O("file")),
                "create" => session.Create(S("file"), S("schema"), B("preview")),
                "rename" => session.Rename(S("file"), S("to"), B("preview")),
                "delete" => session.Delete(S("file"), B("preview")),
                "restore" => session.Restore(S("file")),
                "proposal_list" => session.ListProposals(O("file")),
                "proposal_show" => session.ShowProposal(S("id")),
                "proposal_accept" => session.AcceptProposal(S("id"), S("digest")),
                "proposal_reject" => session.RejectProposal(S("id"), S("digest")),
                "proposal_submit" => session.SubmitProposal(S("file"), S("session"), a["revision"]!.GetValue<long>(), a["operations"]!.ToJsonString(), a["checks"]!.ToJsonString()),
                "checks" => session.ListChecks(),
                "check" => session.RunCheck(S("id"), a["first"]?.GetValue<int>(), a["limit"]?.GetValue<int>(), a["options"]?.ToJsonString(), a["run"]?.GetValue<string>()),
                "import" => session.Import(S("file"), S("origin"), O("licence"), O("source"), O("author"), O("aiUse")),
                "declare" => session.Declare(S("file"), S("origin"), O("licence"), O("source"), B("preview")),
                _ => new JsonObject { ["error"] = $"there's no tool '{tool}'", ["next"] = "tools/list shows them" },
            };
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
        {
            result = new JsonObject { ["error"] = e.Message, ["next"] = "tools/list shows each tool's arguments" };
        }
        // A refusal is an error result too: an edit the schema refuses, a save that didn't happen (found by A207).
        var refusedAny = result["refused"] is JsonArray r && r.Count > 0;
        return Obj(("content", new JsonArray(Obj(("type", "text"), ("text", result.ToJsonString())))), ("isError", result.ContainsKey("error") || refusedAny));
    }

    private static JsonObject Result(JsonNode id, JsonObject result) => Obj(("jsonrpc", "2.0"), ("id", id), ("result", result));

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        Obj(("jsonrpc", "2.0"), ("id", id), ("error", Obj(("code", code), ("message", message))));
}
