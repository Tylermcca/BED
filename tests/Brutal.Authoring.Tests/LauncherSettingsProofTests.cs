using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED neutral projects, corrupt files and interleavings. BORROWED: existing launcher contracts.
// Every path is owned scratch; no default settings, game, OS window, input or clipboard is used.
[Collection(ImGuiCollection.Name)]
public sealed class LauncherSettingsProofTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases =>
        (from target in new[] { "recent", "settings" }
         from kind in new[] { "missing", "empty", "nonjson", "shape", "ten_mb", "read_only", "locked", "directory" }
         select target + "_" + kind)
        .Concat(["entry_deleted", "entry_moved", "entry_missing_drive", "entry_relative", "entry_case_alias", "entry_trailing_alias",
            "entry_dotdot_alias", "entry_over_limit", "entry_path_file", "entry_invalid_path", "entry_malformed", "entry_null_fields",
            "write_two_launchers", "write_half_recent", "write_half_settings", "write_readonly", "write_lock_recovery"])
        .Select(n => new object[] { n });
    private sealed class Observation(string name)
    {
        public string Name { get; } = name;
        public string Phase { get; set; } = "starting";
        public Dictionary<string, object?> Values { get; } = [];
        public List<string> Faults { get; } = [];
    }
    private void Write(Observation o)
    {
        var s = JsonSerializer.Serialize(o); output.WriteLine(s);
        if (Environment.GetEnvironmentVariable("A203_OBSERVATIONS") is { Length: > 0 } path)
        { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, o.Name + ".json"), s); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root;
        internal string Settings => Path.Combine(Root, "settings");
        internal string RecentFile => Path.Combine(Settings, "recent.json");
        internal string SettingsFile => Path.Combine(Settings, "settings.json");
        internal string Initial => Path.Combine(Root, "initial");
        internal readonly ProjectPaths Good, Second, Third;
        internal Fixture(string name)
        {
            var parent = Environment.GetEnvironmentVariable("A203_EVIDENCE_ROOT") ?? Path.GetTempPath();
            Root = Path.Combine(parent, "launcher-settings-" + name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Settings); Directory.CreateDirectory(Initial);
            Good = Project("good"); Second = Project("second"); Third = Project("third");
            File.WriteAllText(RecentFile, JsonSerializer.Serialize(new[] { Good })); File.WriteAllText(SettingsFile, "{\"textScale\":1}");
        }
        internal ProjectPaths Project(string name)
        {
            var folder = Path.Combine(Root, name); var data = Path.Combine(folder, "data"); var schemas = Path.Combine(folder, "schemas");
            Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "item.json"), JsonSerializer.Serialize(new { value = name }));
            File.WriteAllText(Path.Combine(schemas, "item.schema.json"), "{}");
            return new(data, schemas);
        }
        internal void List(params object?[] entries) => File.WriteAllText(RecentFile, JsonSerializer.Serialize(entries));
        internal void RememberInputs()
        {
            foreach (var path in new[] { RecentFile, SettingsFile })
                if (File.Exists(path)) File.Copy(path, Path.Combine(Initial, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(Initial, "kinds.json"), JsonSerializer.Serialize(new { recent = Kind(RecentFile), settings = Kind(SettingsFile) }));
        }
        internal void MoveInside(string from, string to)
        {
            var boundary = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            Assert.StartsWith(boundary, Path.GetFullPath(from), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(boundary, Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase); Directory.Move(from, to);
        }
        public void Dispose()
        {
            foreach (var p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(p, FileAttributes.Normal);
            if (Environment.GetEnvironmentVariable("A203_EVIDENCE_ROOT") is { Length: > 0 }) return;
            var boundary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(boundary, Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase); Directory.Delete(Root, recursive: true);
        }
    }
    private static string Kind(string path) => File.Exists(path) ? "file" : Directory.Exists(path) ? "directory" : "missing";
    private static object Snapshot(string path)
    {
        try
        {
            if (!File.Exists(path)) return new { kind = Kind(path) };
            var bytes = File.ReadAllBytes(path);return new { kind = "file", bytes = bytes.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), text = bytes.Length < 20000 ? Encoding.UTF8.GetString(bytes) : "<large self-authored file; exact initial copy retained>", attributes = File.GetAttributes(path).ToString() };
        }
        catch (IOException e) { return new { kind = "unreadable", reason = e.Message }; }
    }
    private static bool Has(RecentProjects recent, ProjectPaths p) => recent.Items.Any(x => string.Equals(x.Data, p.Data, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Schemas, p.Schemas, StringComparison.OrdinalIgnoreCase));
    private float shownScale = 1;
    private string Screen(ProjectLauncher launcher, Action? userAction = null)
    {
        using var host = new HeadlessHost(1600, 900) { Drawing = false }; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        EditorWindow<DataDocument>.SetTextScale(shownScale);
        try
        {
            host.Frames(launcher.Draw, 3);userAction?.Invoke();if(userAction is not null)host.Frames(launcher.Draw,2);string text = "";
            host.Frame(() => { ImGui.LogToClipboard();launcher.Draw();ImGui.LogFinish();text = ImGui.GetClipboardText().ToString(); });return text;
        }
        finally { shownScale = EditorWindow<DataDocument>.TextScale; }
    }
    private sealed class ForeignLock : IDisposable
    {
        private readonly Process process;
        internal ForeignLock(string path)
        {
            // Win32 share mode zero is a real cross-process lock; Python open() alone would not be exclusive on Windows.
            const string code = "import ctypes,sys\nk=ctypes.WinDLL('kernel32',use_last_error=True)\nk.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p]\nk.CreateFileW.restype=ctypes.c_void_p\nk.CloseHandle.argtypes=[ctypes.c_void_p]\nh=k.CreateFileW(sys.argv[1],0xC0000000,0,None,3,0x80,None)\nassert h!=ctypes.c_void_p(-1).value,ctypes.get_last_error()\nprint('LOCKED',flush=True)\nsys.stdin.readline()\nk.CloseHandle(h)";
            var start = new ProcessStartInfo("python") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(code); start.ArgumentList.Add(path);
            process = Process.Start(start)!; var ready = process.StandardOutput.ReadLineAsync();
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));Assert.Equal("LOCKED", ready.Result);
            Assert.Throws<IOException>(() => { using var denied = File.OpenRead(path); });
        }
        public void Dispose()
        {
            if (!process.HasExited) process.StandardInput.WriteLine("release");
            if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.Equal(0, process.ExitCode);process.Dispose();
        }
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task Hostile_settings_start_safely_preserve_good_entries_and_open_only_the_requested_project(string name)
    {
        var o = new Observation(name); using var f = new Fixture(name);o.Values["root"] = f.Root;
        var expectedGood = new List<ProjectPaths>(); string? lockedPath = null; ProjectPaths? bad = null;

        if (name.StartsWith("recent_", StringComparison.Ordinal) || name.StartsWith("settings_", StringComparison.Ordinal))
        {
            var recent = name.StartsWith("recent_", StringComparison.Ordinal); var path = recent ? f.RecentFile : f.SettingsFile;var kind = name[(name.IndexOf('_') + 1)..];
            switch (kind)
            {
                case "missing": File.Delete(path);break;
                case "empty": File.WriteAllText(path, "");break;
                case "nonjson": File.WriteAllText(path, "not JSON");break;
                case "shape": File.WriteAllText(path, recent ? "{\"not\":\"a list\"}" : "[17]");break;
                case "ten_mb": var value = recent ? JsonSerializer.Serialize(new[] { f.Good }) : "{\"textScale\":1.25}";File.WriteAllText(path, new string(' ', 10_000_000 - Encoding.UTF8.GetByteCount(value)) + value);break;
                case "read_only": File.SetAttributes(path, FileAttributes.ReadOnly);Assert.Throws<UnauthorizedAccessException>(() => { using var denied = File.OpenWrite(path); });break;
                case "locked": lockedPath = path;break;
                case "directory": File.Delete(path);Directory.CreateDirectory(path);break;
            }
            if (!recent || kind is "ten_mb" or "read_only") expectedGood.Add(f.Good);
        }
        switch (name)
        {
            case "entry_deleted": bad = new(Path.Combine(f.Root, "deleted", "data"), f.Good.Schemas);f.List(f.Good, bad);expectedGood.Add(f.Good);break;
            case "entry_moved": var old = f.Project("old");f.MoveInside(Path.Combine(f.Root, "old"), Path.Combine(f.Root, "moved"));bad = old;f.List(f.Good, old);expectedGood.Add(f.Good);break;
            case "entry_missing_drive": var drive = Enumerable.Range('D', 23).Select(c => ((char)c) + ":\\").First(p => !Directory.Exists(p));bad = new(Path.Combine(drive, "a203-absent", "data"), f.Good.Schemas);f.List(f.Good, bad);expectedGood.Add(f.Good);break;
            case "entry_relative": bad = new(Path.GetRelativePath(Environment.CurrentDirectory, f.Second.Data), Path.GetRelativePath(Environment.CurrentDirectory, f.Second.Schemas));f.List(f.Good, bad);expectedGood.Add(f.Good);o.Values["relativeBase"] = Environment.CurrentDirectory;break;
            case "entry_case_alias": f.List(f.Good, new ProjectPaths(f.Good.Data.ToUpperInvariant(), f.Good.Schemas.ToUpperInvariant()), f.Second);expectedGood.AddRange([f.Good,f.Second]);break;
            case "entry_trailing_alias": f.List(f.Good, new ProjectPaths(f.Good.Data + Path.DirectorySeparatorChar, f.Good.Schemas + Path.DirectorySeparatorChar), f.Second);expectedGood.AddRange([f.Good,f.Second]);break;
            case "entry_dotdot_alias": f.List(f.Good, new ProjectPaths(Path.Combine(f.Good.Data, "..", "data"), Path.Combine(f.Good.Schemas, "..", "schemas")), f.Second);expectedGood.AddRange([f.Good,f.Second]);break;
            case "entry_over_limit": var items = new List<ProjectPaths> { f.Good }; for (var i = 0; i < 14; i++) items.Add(f.Project("limit"+i));f.List(items.Cast<object?>().ToArray());expectedGood.Add(f.Good);break;
            case "entry_path_file": var file = Path.Combine(f.Root, "now-file");File.WriteAllText(file, "not a folder");bad = new(file, f.Good.Schemas);f.List(f.Good,bad);expectedGood.Add(f.Good);break;
            case "entry_invalid_path": bad = new("bad\0path", f.Good.Schemas);f.List(f.Good,bad);expectedGood.Add(f.Good);break;
            case "entry_malformed": f.List(f.Good, 17, f.Second);expectedGood.AddRange([f.Good,f.Second]);break;
            case "entry_null_fields": f.List(f.Good, null, new { Data = (string?)null, Schemas = (string?)null }, f.Second);expectedGood.AddRange([f.Good,f.Second]);break;
            case "write_half_recent": File.WriteAllText(f.RecentFile, "[{\"Data\":\"");break;
            case "write_half_settings": File.WriteAllText(f.SettingsFile, "{\"textScale\":");expectedGood.Add(f.Good);break;
            case "write_readonly": File.SetAttributes(f.RecentFile, FileAttributes.ReadOnly);File.SetAttributes(f.SettingsFile, FileAttributes.ReadOnly);Assert.Throws<UnauthorizedAccessException>(()=>{using var denied=File.OpenWrite(f.RecentFile);});expectedGood.Add(f.Good);break;
            case "write_lock_recovery": f.List(f.Good,f.Third);lockedPath = f.RecentFile;break;
            case "write_two_launchers": expectedGood.Add(f.Good);break;
        }
        f.RememberInputs();o.Values["beforeRecent"] = Snapshot(f.RecentFile);o.Values["beforeSettings"] = Snapshot(f.SettingsFile);Write(o);
        var dataBefore = Directory.EnumerateFiles(f.Root,"item.json",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        ForeignLock? held = null; ProjectLauncher? launcher = null;
        try
        {
            if (lockedPath is not null) held = new ForeignLock(lockedPath);
            o.Phase = "start";Write(o);launcher = new ProjectLauncher(new RecentProjects(f.RecentFile));
            o.Values["loadedItems"] = launcher.Recent.Items.ToArray();o.Values["loadedCount"] = launcher.Recent.Items.Count;
            foreach (var good in expectedGood) if (!Has(launcher.Recent,good)) o.Faults.Add("A good entry was lost during loading: "+good.Data);
            try { o.Values["startScreen"] = Screen(launcher);o.Values["initialTextScale"] = shownScale; }
            catch(Exception e) { o.Values["startException"] = e.ToString();o.Faults.Add("Start screen escaped: "+e.GetType().Name+": "+e.Message); }
            if (bad is not null && name is not "entry_relative")
            {
                try { var openedBad = launcher.Open(bad);o.Values["badOpened"] = openedBad;o.Values["badSaid"] = launcher.Said;if(openedBad)o.Faults.Add("A missing/invalid project was opened."); }
                catch(Exception e) { o.Values["badOpenException"] = e.ToString();o.Faults.Add("Opening a bad recent entry escaped: "+e.GetType().Name+": "+e.Message); }
            }
            if (name == "entry_relative")
            {
                Assert.True(launcher.Open(bad!),launcher.Said);Assert.Equal(Path.GetFullPath(f.Second.Data),launcher.Current!.Data);
                Assert.Equal("second",launcher.Workspace!.Window!.Editor.Session.Current.At("/value")?.GetString());o.Values["relativeOpenedAbsolute"] = launcher.Current.Data;Assert.True(launcher.Close(discard:true));
            }
            o.Phase = "open-good";Write(o);Assert.True(launcher.Open(f.Good),launcher.Said);Assert.Equal(Path.GetFullPath(f.Good.Data),launcher.Current!.Data);
            Assert.Equal("good",launcher.Workspace!.Window!.Editor.Session.Current.At("/value")?.GetString());o.Values["goodOpened"] = true;o.Values["goodCurrent"] = launcher.Current;
            o.Values["openSaid"] = launcher.Said;o.Values["openedScreen"] = Screen(launcher);
            // Change the setting through the public editor command, then let the launcher persist it on frames.
            o.Values["afterTextChangeScreen"] = Screen(launcher,()=>Assert.True(launcher.Workspace.Window.Run("editor.text_larger")));o.Values["finalTextScale"] = shownScale;
            if (name == "write_lock_recovery")
            {
                held!.Dispose();held = null;Assert.True(launcher.Open(f.Second),launcher.Said);
                if (!Has(new RecentProjects(f.RecentFile),f.Third)) o.Faults.Add("Recovering from a temporary read lock overwrote an existing good entry.");
            }
            if (name == "write_two_launchers")
            {
                // Both instances read the same starting list before either writer runs. Their lifetimes overlap.
                var a = new ProjectLauncher(new RecentProjects(f.RecentFile));var b = new ProjectLauncher(new RecentProjects(f.RecentFile));
                using var ready = new Barrier(3);var first = Task.Run(()=>{ready.SignalAndWait();return a.Open(f.Second);});var second = Task.Run(()=>{ready.SignalAndWait();return b.Open(f.Third);});
                ready.SignalAndWait();var answers = await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(10));Assert.All(answers,answer=>Assert.True(answer));
                var end = new RecentProjects(f.RecentFile);o.Values["concurrentItems"] = end.Items.ToArray();
                if (!Has(end,f.Good)||!Has(end,f.Second)||!Has(end,f.Third)) o.Faults.Add("Two successful overlapping launchers lost a good recent entry on disk.");
            }
        }
        catch(Exception e) { o.Values["exception"] = e.ToString();o.Faults.Add(o.Phase+" escaped: "+e.GetType().Name+": "+e.Message); }
        finally { held?.Dispose(); }
        o.Values["afterRecent"] = Snapshot(f.RecentFile);o.Values["afterSettings"] = Snapshot(f.SettingsFile);
        if (launcher is not null) { o.Values["finalItems"] = launcher.Recent.Items.ToArray();o.Values["finalSaid"] = launcher.Said; }
        foreach(var (path,bytes) in dataBefore) Assert.Equal(bytes,File.ReadAllBytes(path));o.Values["allProjectDataUnchanged"] = true;
        o.Phase = "complete";Write(o);Assert.True(o.Faults.Count==0,string.Join("\n",o.Faults));
    }
}
