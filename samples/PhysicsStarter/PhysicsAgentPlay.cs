using System.Globalization;
using System.Text;
using System.Text.Json;
using SceneStarter;

namespace PhysicsStarter;

// Optional private stdio observation, sharing the LIVE02 opt-in entry but leaving its selected-object route intact.
internal static class PhysicsAgentPlay
{
    internal static int Run(TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            var saved = Scene2RuntimeInput.Open(Environment.GetEnvironmentVariable("BED_PROJECT") ?? "", true);
            using var toy = new CrateToy(saved.Level.Scene);
            using var frames = new ToyWindow.ObservationFrames(new(saved.Meshes, saved.Textures));
            var controls = new ToyControls(new OrbitCamera());
            long frame = 0, last = 0; double accumulator = 0;
            string Description() => string.Create(CultureInfo.InvariantCulture,
                $"Island picture {frame}; physics updates: {toy.Ticks}, {toy.World.StepSeconds:0.######} seconds each; camera angle {controls.Camera.Yaw:0.000} radians; {toy.World.Count} objects. Controls: left/right turn the camera, jump lifts the first saved crate, restart resets.");
            void Reply(long id, byte[]? png = null, string? refusal = null)
            {
                output.WriteLine(JsonSerializer.Serialize(new { id, frame, description = Description(), png, error = refusal, mode = "headless; simulation pauses between requests" }));
                output.Flush();
            }
            Reply(0);
            while (Read(input) is { } line)
            {
                using var json = JsonDocument.Parse(line, new() { MaxDepth = 4 }); var root = json.RootElement;
                var fields = root.EnumerateObject().Select(p => p.Name).ToArray();
                if (fields.Length != 4 || fields.Distinct().Count() != 4 || fields.Except(new[] { "id", "op", "key", "seconds" }).Any())
                    throw new InvalidDataException("The island picture request contains missing or extra information.");
                var id = root.GetProperty("id"u8).GetInt64();
                if (id != last + 1) throw new InvalidDataException("The island picture request is out of date."); last = id;
                var op = root.GetProperty("op"u8).GetString();
                var key = root.GetProperty("key"u8).ValueKind == JsonValueKind.Null ? null : root.GetProperty("key"u8).GetString();
                var seconds = root.GetProperty("seconds"u8).GetDouble();
                if (op == "stop" && key is null && seconds == 0) { Reply(id); return 0; }
                if (op == "shot" && key is null && seconds == 0) { }
                else if (op == "hold" && key is "left" or "right" or "jump" or "restart" && double.IsFinite(seconds) && seconds > 0 && seconds <= 5)
                {
                    var button = key == "left" ? ToyKey.Left : key == "right" ? ToyKey.Right : key == "jump" ? ToyKey.Space : ToyKey.R;
                    controls.Key(button, true);
                    try
                    {
                        var count = (int)Math.Ceiling(seconds * 60 - 1e-9);
                        for (var i = 0; i < count; i++)
                        {
                            foreach (var (command, _) in controls.Update(1f / 60))
                                if (command == ToyCommand.Reset) { toy.Reset(); accumulator = 0; }
                                else if (command == ToyCommand.Jump) toy.Jump();
                            accumulator += 1.0 / 60;
                            while (accumulator + 1e-9 >= toy.World.StepSeconds) { toy.Step(); accumulator -= toy.World.StepSeconds; }
                            frame++;
                        }
                    }
                    finally { controls.Key(button, false); }
                }
                else { Reply(id, refusal: "Use left/right to turn the camera, jump to lift the first saved crate or restart to reset. Hold a key for more than zero and at most five seconds of play."); continue; }
                var png = frames.Draw(toy, controls.Camera);
                if (png.Length > 1024 * 1024) throw new InvalidDataException("The island picture is larger than the allowed size.");
                Reply(id, png);
            }
            return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { error.WriteLine("The island picture run stopped: " + e.Message); return 3; }
    }
    private static string? Read(TextReader input)
    {
        var line = new StringBuilder();
        while (true)
        {
            var c = input.Read();
            if (c < 0) return line.Length == 0 ? null : throw new InvalidDataException("The island picture request is incomplete.");
            if (c == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= 512) throw new InvalidDataException("The island picture request is too long.");
            line.Append((char)c);
        }
    }
}