using System.Globalization;
using System.Numerics;

namespace Brutal.Authoring.Import;

/// <summary>
/// Reads Wavefront OBJ geometry (worksheet IM-09: a kit module in its native frame). Each <c>o</c> or <c>g</c> starts a
/// named mesh; faces are fanned into triangles; <c>v/vt/vn</c> references, including negative ones, are resolved. The
/// frame converts every point once (pass <see cref="ProjectFrame.Gltf"/>, the identity, for a file already in the
/// project's frame). Materials and texture files are not read here.
/// </summary>
public static class ObjImporter
{
    /// <summary>Imports <paramref name="path"/>, converting with <paramref name="frame"/>.</summary>
    public static ImportResult Import(string path, ProjectFrame frame)
    {
        var name = Path.GetFileName(path);
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Refuse($"{name}: can't read it: {e.Message}"); }

        var v = new List<Vector3>();
        var vt = new List<Vector2>();
        var vn = new List<Vector3>();
        var meshes = new List<ImportedMesh>();
        var current = "default";
        var faces = new List<(int V, int T, int N)[]>();
        var issues = new List<Issue>();

        ImportResult? Bad(int line, string why) => Refuse($"{name}: line {line}: {why}");

        void Close()
        {
            if (faces.Count == 0) return;
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var nrm = new List<Vector3>();
            var tris = new List<int>();
            var seen = new Dictionary<(int, int, int), int>();
            int Vertex((int V, int T, int N) c)
            {
                if (seen.TryGetValue(c, out var i)) return i;
                i = pos.Count;
                pos.Add(frame.Point(v[c.V]));
                if (c.T >= 0) uv.Add(vt[c.T]);
                if (c.N >= 0) nrm.Add(Vector3.Normalize(frame.Direction(vn[c.N])));
                seen[c] = i;
                return i;
            }
            var flip = frame.Determinant < 0;
            foreach (var f in faces)
                for (var k = 1; k + 1 < f.Length; k++)
                {
                    var (a, b, c) = (Vertex(f[0]), Vertex(f[k]), Vertex(f[k + 1]));
                    if (flip) tris.AddRange([a, c, b]); else tris.AddRange([a, b, c]);
                }
            meshes.Add(new ImportedMesh(current, current, [.. pos], nrm.Count == pos.Count ? [.. nrm] : [], uv.Count == pos.Count ? [.. uv] : [],
                [.. tris], null, 1f, flip, false));
            faces.Clear();
        }

        for (var n = 0; n < lines.Length; n++)
        {
            var hash = lines[n].IndexOf('#');
            var parts = (hash >= 0 ? lines[n][..hash] : lines[n]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#')) continue;
            try
            {
                switch (parts[0])
                {
                    case "v": v.Add(new(F(parts[1]), F(parts[2]), F(parts[3]))); break;
                    case "vt": vt.Add(new(F(parts[1]), parts.Length > 2 ? F(parts[2]) : 0)); break;
                    case "vn": vn.Add(new(F(parts[1]), F(parts[2]), F(parts[3]))); break;
                    case "o" or "g":
                        Close();
                        current = parts.Length > 1 ? string.Join(' ', parts[1..]) : "default";
                        break;
                    case "f":
                        if (parts.Length < 4) return Bad(n + 1, "a face needs at least three corners")!;
                        var corners = new (int, int, int)[parts.Length - 1];
                        for (var k = 1; k < parts.Length; k++)
                        {
                            var refs = parts[k].Split('/');
                            var vi = Index(refs[0], v.Count);
                            var ti = refs.Length > 1 && refs[1].Length > 0 ? Index(refs[1], vt.Count) : -1;
                            var ni = refs.Length > 2 && refs[2].Length > 0 ? Index(refs[2], vn.Count) : -1;
                            if (vi < 0 || vi >= v.Count) return Bad(n + 1, $"vertex {refs[0]} doesn't exist (there are {v.Count})")!;
                            var hasT = refs.Length > 1 && refs[1].Length > 0;
                            var hasN = refs.Length > 2 && refs[2].Length > 0;
                            if ((hasT && (ti < 0 || ti >= vt.Count)) || (hasN && (ni < 0 || ni >= vn.Count)))
                                return Bad(n + 1, $"'{parts[k]}' names a texture or normal that doesn't exist")!;
                            corners[k - 1] = (vi, ti, ni);
                        }
                        faces.Add(corners);
                        break;
                }
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                var why = e.Message == NotFinite ? " (not a finite number)" : "";
                return Bad(n + 1, $"'{lines[n].Trim()}' has a number this reader can't read{why}")!;
            }
            catch (IndexOutOfRangeException) { return Bad(n + 1, $"'{lines[n].Trim()}' is missing values")!; }
        }
        Close();

        if (meshes.Count == 0) return Refuse($"{name}: no faces");
        if (meshes.All(m => m.Uv0.Length == 0)) issues.Add(Issue.Warning($"{name}: UV0 is absent"));
        var asset = new ImportedAsset(meshes, [], [], [], 0, 0, [], meshes.Count);
        return new ImportResult(issues.Count == 0 ? ImportOutcome.Imports : ImportOutcome.ImportsWithWarnings, asset, issues);
    }

    private const string NotFinite = "not a finite number";

    private static float F(string s)
    {
        var f = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsFinite(f) ? f : throw new FormatException(NotFinite);
    }

    // OBJ indices start at 1; negative ones count back from the latest.
    private static int Index(string s, int count)
    {
        var i = int.Parse(s, CultureInfo.InvariantCulture);
        return i > 0 ? i - 1 : count + i;
    }

    private static ImportResult Refuse(string message) => new(ImportOutcome.Refused, null, [Issue.Error(message)]);
}

/// <summary>A named point with directions, read from a marker file beside a module (a socket, a dock, a mount).</summary>
public sealed record Marker(string Id, Vector3 Position, Vector3 Up, Vector3 Outward);

/// <summary>
/// A marker file: the points of interest a module carries, in the module's own frame. Read strictly; the caller says which
/// <c>kind</c> it expects, so a game's own marker format can use this reader under its own name.
/// </summary>
public static class MarkerFile
{
    /// <summary>Reads markers. Throws <see cref="DocumentFormatException"/> naming the JSON path of the first problem.</summary>
    public static (IReadOnlyList<Marker> Markers, string? Collision, string? Frame) Read(string text, string kind)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "markers", "collision", "frame", "module_id", "placement");
        if (r.String("kind") != kind) throw new DocumentFormatException("$.kind", $"expected \"{kind}\"");
        if (r.Int("version") != 1) throw new DocumentFormatException("$.version", "expected version 1");
        var markers = r.Array("markers").Select(m =>
        {
            m.Only("id", "position", "up", "outward");
            return new Marker(m.String("id"), Vec(m, "position"), Vec(m, "up"), Vec(m, "outward"));
        }).ToList();
        var dup = markers.GroupBy(m => m.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new DocumentFormatException("$.markers", $"marker id '{dup.Key}' is used twice");
        return (markers, r.Has("collision") ? r.String("collision") : null, r.Has("frame") ? r.String("frame") : null);
    }

    private static Vector3 Vec(StrictReader r, string name)
    {
        var a = r.Element(name);
        if (a.ValueKind != System.Text.Json.JsonValueKind.Array || a.GetArrayLength() != 3)
            throw new DocumentFormatException($"{r.Path}.{name}", "expected three numbers");
        var v = new float[3];
        for (var i = 0; i < 3; i++)
            if (a[i].ValueKind != System.Text.Json.JsonValueKind.Number || !a[i].TryGetSingle(out v[i]) || !float.IsFinite(v[i]))
                throw new DocumentFormatException($"{r.Path}.{name}[{i}]", "expected a finite number");
        return new Vector3(v[0], v[1], v[2]);
    }
}
