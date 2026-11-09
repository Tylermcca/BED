using System.Text.Json;
using Brutal.Authoring.Data;

namespace DataCheck;

/// <summary>
/// <c>datacheck &lt;data-folder&gt; &lt;schema-folder&gt;</c>: validates every <c>*.json</c> file in the data folder
/// against its schema, found by the file's <c>$schema</c> field (its file name) or else by name
/// (<c>wares.json</c> → <c>wares.schema.json</c>). It prints each problem as <c>file path: message</c>, sorted.
/// Exit codes: 0 all valid; 1 problems found; 2 bad arguments, or a file or schema that can't be read.
/// </summary>
public static class Program
{
    private const string Usage = "usage: datacheck <data-folder> <schema-folder>";

    /// <summary>The entry point.</summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs a check, writing to <paramref name="output"/> and <paramref name="errors"/>. Tests call this.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        if (args is not [var dataDir, var schemaDir] || !Directory.Exists(dataDir) || !Directory.Exists(schemaDir))
        {
            errors.WriteLine(Usage);
            return 2;
        }
        JsonSchemas schemas;
        try { schemas = JsonSchemas.FromFolder(schemaDir); }
        catch (JsonException e) { errors.WriteLine($"a schema in {schemaDir} isn't valid JSON: {e.Message}"); return 2; }
        int files = 0, bad = 0, unreadable = 0;
        foreach (var file in Directory.EnumerateFiles(dataDir, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            JsonElement root;
            try { root = JsonDocument.Parse(File.ReadAllText(file)).RootElement.Clone(); }
            catch (JsonException e) { output.WriteLine($"{name} $: isn't valid JSON: {e.Message}"); unreadable++; continue; }
            var schema = DataProject.SchemaNameFor(name, root);   // the same rule a project uses
            if (schemas.Find(schema) is null) { output.WriteLine($"{name} $: no schema '{schema}' in {schemaDir}"); unreadable++; continue; }
            files++;
            var issues = schemas.Validate(root, schema);
            if (issues.Count > 0) bad++;
            foreach (var i in issues.OrderBy(i => i.Path, StringComparer.Ordinal)) output.WriteLine($"{name} {i.Path}: {i.Message}");
        }
        output.WriteLine($"{files} file(s) checked, {bad} with problems{(unreadable > 0 ? $", {unreadable} couldn't be checked" : "")}.");
        return unreadable > 0 ? 2 : bad > 0 ? 1 : 0;
    }
}
