using System.Text.Json;
using Bed.Data;

// INVENTED document/data. Uses only public headless data-session APIs.
internal static class Program
{
    private const string SchemaName = "parcel.schema.json";
    private const string Schema = """
        {"type":"object","required":["parcels"],"additionalProperties":false,
         "properties":{"parcels":{"type":"integer","minimum":0,"maximum":5}}}
        """;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Pass one NEW data directory; existing files are never overwritten.");
            return 2;
        }
        try
        {
            var directory = Path.GetFullPath(args[0]);
            if (Directory.Exists(directory) || File.Exists(directory))
            {
                Console.Error.WriteLine("Choose a new data directory; this one already exists.");
                return 2;
            }
            var schemas = new JsonSchemas().Add(SchemaName, Schema);
            var session = DataKit.Session(new DataFormat().Read("""{"parcels":1}"""), schemas, SchemaName);
            Require(Count() == 1 && !session.Dirty, "initial document differs");
            Console.WriteLine("Initial parcels: 1");

            Require(!session.Execute(new SetValue("/parcels", JsonSerializer.SerializeToElement(-1))),
                "schema accepted a negative count");
            Require(Count() == 1 && !session.Dirty && session.LastIssues.Count > 0,
                "refused edit changed the document");
            Console.WriteLine("Invalid edit refused; parcels: 1");

            Require(session.Execute(new SetValue("/parcels", JsonSerializer.SerializeToElement(3))),
                "valid edit was refused");
            Require(session.Dirty && Count() == 3, "valid edit did not commit");
            Require(session.Undo() && Count() == 1, "undo failed");
            Require(session.Redo() && Count() == 3, "redo failed");
            Console.WriteLine("Undo/redo restored parcels: 3");

            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "parcels.json");
            session.Save(file);
            var reopened = DataKit.Open(file, schemas, SchemaName);
            Require(!session.Dirty && !reopened.Dirty
                && reopened.Current.Root.GetProperty("parcels").GetInt32() == 3, "save/reopen differs");
            Console.WriteLine("Saved and reopened parcels: 3");
            return 0;

            int Count() => session.Current.Root.GetProperty("parcels").GetInt32();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("First-use check failed: " + error.Message);
            Console.Error.WriteLine("Keep any new data folder for inspection; do not overwrite an existing one.");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
