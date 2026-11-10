using System.Text;
using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class SaveEolTests
{
    // Self-authored files. Mixed endings deliberately put the minority at the end,
    // so the body uses the majority while its original final newline survives.
    public static IEnumerable<object[]> Files()
    {
        yield return ["{\n  \"n\": 1,\n  \"word\": \"雪\"\n}\r\n", "\n",
            "{\n  \"n\": 2,\n  \"word\": \"雪\"\n}\r\n"];
        yield return ["{\r\n  \"n\": 1,\r\n  \"word\": \"雪\"\r\n}\n", "\r\n",
            "{\r\n  \"n\": 2,\r\n  \"word\": \"雪\"\r\n}\n"];
        yield return ["{\n  \"n\": 1\n}\n", "\n", "{\n  \"n\": 2\n}\n"];
        yield return ["{\r\n  \"n\": 1\r\n}\r\n", "\r\n", "{\r\n  \"n\": 2\r\n}\r\n"];
        yield return ["{\n  \"n\": 1\n}", "\n", "{\n  \"n\": 2\n}"];
        yield return ["{\r\n  \"n\": 1\r\n}", "\r\n", "{\r\n  \"n\": 2\r\n}"];
        yield return [" \t{ \"n\": 1, \"escaped\": \"\\r\\n\" }\r\n\n \t", "\n",
            " \t{ \"n\": 2, \"escaped\": \"\\r\\n\" }\r\n\n \t"];
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Save_edit_reopen_and_undo_keep_the_original_file_ending(string original, string majority, string edited)
    {
        var directory = Path.Combine(Path.GetTempPath(), "save-eol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var originalBytes = Encoding.UTF8.GetBytes(original);
            File.WriteAllBytes(path, originalBytes);
            var schemas = new JsonSchemas().Add("settings.schema.json", "{\"type\":\"object\"}");
            var session = DataKit.Open(path, schemas, "settings.schema.json");
            Assert.Equal(majority, session.Current.LineEnding);
            Assert.False(session.Dirty);
            session.Save(); // Exercise the atomic writer even when the document is clean.
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal(DiskState.Unchanged, session.CheckDisk());

            Assert.True(session.Execute(new SetValue("/n", JsonSerializer.SerializeToElement(2))));
            session.Save();
            Assert.Equal(Encoding.UTF8.GetBytes(edited), File.ReadAllBytes(path));
            var reopened = DataKit.Open(path, schemas, "settings.schema.json");
            Assert.Equal(2, reopened.Current.At("/n")!.Value.GetInt32());
            reopened.Save();
            Assert.Equal(Encoding.UTF8.GetBytes(edited), File.ReadAllBytes(path));

            Assert.True(session.Undo());
            session.Save();
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.False(session.Dirty);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
