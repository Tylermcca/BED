# Use BED with an AI assistant

BED can give your assistant tools to read, check and edit JSON game data. There is no AI inside BED. Its command line runs on your computer; BED itself does not send your data to an AI service. Your assistant may send tool inputs and results to its own provider. Check that app's privacy settings before opening private data.

Edits are checked against the file's schema and any validation rules supplied by a loaded game adapter. Separate project checks must be requested: a valid edit is not proof that every check passed. Start with a copy of the bundled sample.

## The quick way: from inside BED

Open a project and a data file first. To try it, click **Try the example** on the start screen. Then choose **Help > Use with an AI assistant**, or find **Use with an AI assistant** in the command palette. The examples have that project's paths filled in.

The panel has three Copy buttons:

- **Copy bed-cli command:** a PowerShell command that describes the project.
- **Copy Claude Desktop JSON:** a configuration entry connecting Claude Desktop to BED over MCP.
- **Copy claude mcp add:** a PowerShell command for adding BED to Claude Code.

Copy only puts text on your clipboard. It does not run a command or change an app's configuration. Check your assistant app's current setup instructions before using its example. The manual steps below explain what to do next and when tools write files.

If the project's adapter is built into BED rather than available as a separate file, the panel explains why it cannot provide runnable examples. Do not leave out the adapter to work around that refusal: its rules would be missing.

## Try the command line first

Unzip BED and open PowerShell in the folder containing `bed-cli.exe`. These commands use the bundled `samples/recipes` project. Leave its data, schemas and reference map together.

1. List the files and available calls:

```powershell
./bed-cli.exe --data samples/recipes/data --schemas samples/recipes/schemas --map samples/recipes/reference_map.json describe
```

2. Find the Milling recipe. The result gives its JSON Pointer, a path into the file:

```powershell
./bed-cli.exe --data samples/recipes/data --schemas samples/recipes/schemas --map samples/recipes/reference_map.json find recipes.json /recipes Milling
```

3. Preview changing its minutes from 20 to 25. In the unchanged sample, Milling is `/recipes/0`. If you have reordered the sample, use the pointer returned by `find` instead.

```powershell
./bed-cli.exe --data samples/recipes/data --schemas samples/recipes/schemas --map samples/recipes/reference_map.json set recipes.json /recipes/0/minutes 25 --preview
```

Read the JSON response. A refusal explains what needs fixing. Preview does not apply or save the change.

4. If the preview is right, run the same edit with `--save` instead:

```powershell
./bed-cli.exe --data samples/recipes/data --schemas samples/recipes/schemas --map samples/recipes/reference_map.json set recipes.json /recipes/0/minutes 25 --save
```

This writes the sample file. Without `--save`, a one-shot CLI edit is lost when the command ends; there is no separate CLI `save` command. If saving is refused, read the reason and preserve/reconcile any outside edits rather than forcing an overwrite.

5. Run the built-in link check with the map:

```powershell
./bed-cli.exe --data samples/recipes/data --schemas samples/recipes/schemas --map samples/recipes/reference_map.json check links.all
```

This checks references, not every possible game rule. `checks` lists available checks; `problems` reports schema problems. Replies are JSON; exit 0 means success, 1 means a refusal or failed check, and 2 means bad arguments or a project that could not open.

## Connect an assistant through MCP

MCP is a way for an assistant app to call local tools. Here the app starts `bed-cli.exe` and talks to it through standard input and output (stdio). BED does not need a listening port or a web server. A persistent MCP session keeps draft edits until saved or discarded when that session ends.

The following are configuration examples, not an installation step. **Check your app's current docs** before using them; app formats and setup commands can change. Replace `FULL_PATH_TO_BED` everywhere with the full path to your unzipped BED folder, using forward slashes in these examples. The placeholder is not a working path. In Windows File Explorer, open that folder and copy its full address. Keep your app's approval prompts enabled and start with a disposable copy.

### Claude Desktop

Check Claude Desktop's current docs for how to find and edit `claude_desktop_config.json`. Merge the `bed` entry into an existing `mcpServers` object; do not overwrite other servers.

```json
{
  "mcpServers": {
    "bed": {
      "command": "FULL_PATH_TO_BED/bed-cli.exe",
      "args": ["--data", "FULL_PATH_TO_BED/samples/recipes/data", "--schemas", "FULL_PATH_TO_BED/samples/recipes/schemas", "--map", "FULL_PATH_TO_BED/samples/recipes/reference_map.json", "mcp"]
    }
  }
}
```

### Claude Code

Check Claude Code's current docs for the command and configuration scope you want. Example for PowerShell:

```powershell
claude mcp add bed -- "FULL_PATH_TO_BED/bed-cli.exe" --data "FULL_PATH_TO_BED/samples/recipes/data" --schemas "FULL_PATH_TO_BED/samples/recipes/schemas" --map "FULL_PATH_TO_BED/samples/recipes/reference_map.json" mcp
```

### Another stdio MCP client

Choose a local **stdio** server. Set its command to the full path of `bed-cli.exe`. Supply separate argument values for `--data`, the full data-folder path, `--schemas`, the schema-folder path, optionally `--map` and its path, then `mcp`. Use the same command and argument array as the Desktop example, in your client's format. Check that client's current docs; the example is not a universal config file.

## Three requests to try

Some MCP clients may show BED's starter prompts: `explore`, `safe-edit` (with `what-to-change` text), and `check-and-explain`; choosing one does not approve tool actions.

- **“Describe the recipe data and find Milling. Do not change anything.”** The assistant should call `describe`, then `find`, and explain the returned fields and pointer.
- **“Preview changing Milling to 25 minutes. Show me the result and ask before applying or saving.”** It should use `set` with `preview: true`. After your approval it can apply the edit, check it, and call `save` only when you approve writing the file.
- **“Check the recipe links and explain any failures. Do not fix them yet.”** It should list `checks`, run `check` for `links.all`, and report the findings. It should not invent a clean result or treat schema validation as a link check.

Connecting a server does not mean every write is approved. Ask your assistant to preview changes and request your approval before it applies or saves them.

## Saves, limits and trusted adapters

For ordinary MCP `set`, `insert` and `remove` edits, applying changes the in-memory draft; `save` writes it. **File operations such as `create`, `rename` and `delete` can write immediately unless previewed.** Do not assume every tool waits for a later save. Check the tool description and preview first. A deleted file can be restored with `restore` while the same MCP session lasts.

Add `--read-only` before `mcp` for a plain-data session that refuses writes. It is not a sandbox for arbitrary programs. **`--read-only` with `--adapter` is not supported** and is refused.

To load your game's rules and registered checks, add `--adapter` followed by the full path to its DLL before the call or `mcp`. For example, the zip includes `adapters/starter/StarterAdapter.dll`. Only load adapters you trust: they are programs running on your computer. Review adapter errors and confirm the expected checks are listed before relying on them; loading an adapter is not proof that every check ran.

Check run IDs last only within the current MCP session and may become stale or be evicted. A new CLI invocation cannot reuse one; `--run` is refused by the one-shot CLI. In MCP the check tool uses a `run` field, not a command-line flag. Restarting the assistant's BED server loses that session's unsaved drafts and run history.
