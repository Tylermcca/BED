using System.Text;
using Bed.Editor;
namespace Bed.Editor.App;

// Release text uses only captured producing records. AiUse grants permission, not a generation history.
internal static class ExportReleaseFiles
{
    internal const string Credit = "BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";
    internal static IReadOnlyDictionary<string, byte[]> Create(GameExportPlan plan)
    {
        var credits = new StringBuilder("Credits\n\n" + Credit + "\n\nAssets declared for this export\n");
        var draft = new StringBuilder("DRAFT - CHECK BEFORE SUBMITTING TO STEAM\n\n" +
            "This is a worksheet, not a completed disclosure or approval to sell.\n" +
            "Source: https://partner.steamgames.com/doc/gettingstarted/contentsurvey\n\n" +
            "Pre-generated player-facing content: [Describe the art, sound, narrative, localization and other shipped content made with AI, or confirm none.]\n" +
            "Live-generated content: [Describe any AI content created while the game runs, or confirm none.]\n" +
            "Live-generation guardrails: [Describe how illegal content is prevented, if applicable.]\n\n" +
            "BED records permission to use assets with AI, not whether AI made them. Permission is never evidence of generation.\n" +
            "Review all entries below, plus code, text, sound, store assets and anything outside this captured asset inventory.\n\n");
        if (plan.Admission.SourceRecords.IsEmpty)
        {
            credits.AppendLine("No producing asset records were supplied. This does not prove the game has no assets.");
            draft.AppendLine("No producing asset records were supplied. AI-generation history is unknown.");
        }
        foreach (var record in plan.Admission.SourceRecords.OrderBy(r => r.Guid, StringComparer.Ordinal))
        {
            var p = record.Provenance;
            credits.AppendLine($"\nAsset: {Text(record.Source)} [{Text(record.Guid)}]");
            credits.AppendLine($"Author: {Text(p?.Author)}\nDeclared origin: {p?.Declared.ToString() ?? "MISSING"}\nOrigin: {Text(p?.Origin)}");
            credits.AppendLine($"Licence: {Text(p?.Licence)}\nNotes: {Text(p?.Notes)}");
            draft.AppendLine($"Asset: {Text(record.Source)} [{Text(record.Guid)}]\nOrigin: {Text(p?.Origin)}\nDeclared notes: {Text(p?.Notes)}\nAI use permission: {record.AiUse}; generation history: UNKNOWN - check with the creator.\n");
        }
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CREDITS.txt"] = Encoding.UTF8.GetBytes(credits.ToString()),
            ["STEAM-AI-DISCLOSURE-DRAFT.txt"] = Encoding.UTF8.GetBytes(draft.ToString()),
            ["BRUTAL-RELEASE-CHECKLIST.txt"] = Encoding.UTF8.GetBytes(Credit + "\n\nShow this credit in the game's credits screen or equivalent.\nShow LOGO_BRUTAL_WHT.png unaltered except scaling before gameplay or the primary function, long enough to read and at least as prominent as other logos. A logo file alone does not satisfy this requirement. Written waivers and non-graphical products are covered in licence section 2.2.\nKeep NOTICE.txt beside the game, the BRUTAL licence and every applicable dependency/asset notice.\nCheck the game's own runtime splash/credits and all redistribution rights before sale. BED has not verified these.\n")
        };
        foreach (var (resource, path) in new[] { ("BRUTAL-NOTICE", "licenses/BRUTAL-NOTICE.txt"), ("BRUTAL-LICENSE", "licenses/BRUTAL-LICENSE.txt"), ("BRUTAL-LOGO", "LOGO_BRUTAL_WHT.png") })
        {
            using var stream = typeof(ExportReleaseFiles).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("A bundled BRUTAL release file is missing.");
            using var copy = new MemoryStream(); stream.CopyTo(copy); files.Add(path, copy.ToArray());
        }
        if (!plan.Outputs.Paths.Contains("NOTICE.txt", StringComparer.OrdinalIgnoreCase)) files.Add("NOTICE.txt", files["licenses/BRUTAL-NOTICE.txt"]);
        return files;
    }
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "MISSING - check before release" :
        value.Replace("\r", " ").Replace("\n", " ").Replace("\0", " ");
    internal static GameExportPlan AddToPlan(GameExportPlan plan, IReadOnlyDictionary<string, byte[]> files) =>
        new(plan.Admission, plan.Outputs.WithReleaseFiles(files), plan.SavedPayloadOutput);
    internal static void Write(string output, GameExportPlan original, IReadOnlyDictionary<string, byte[]> files)
    {
        // Preserve the existing complete inventory check before adding our exact allowlisted files.
        _ = BuildOutputInventory.Capture(output, original.Outputs, Guid.NewGuid());
        if (original.Outputs.Paths.Contains("NOTICE.txt", StringComparer.OrdinalIgnoreCase))
        {
            var path = original.Outputs.Paths.Single(p => p.Equals("NOTICE.txt", StringComparison.OrdinalIgnoreCase));
            var notice=Encoding.UTF8.GetString(files["licenses/BRUTAL-NOTICE.txt"]).Replace("\r\n","\n").Trim();
            if (!File.ReadAllText(Path.Combine(output, path)).Replace("\r\n","\n").Contains(notice, StringComparison.Ordinal))
                throw new IOException("NOTICE.txt is missing the BRUTAL notice. Keep the official notice beside the game.");
        }
        foreach (var (relative, bytes) in files)
        {
            var path = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }
}
