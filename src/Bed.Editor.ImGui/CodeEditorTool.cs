using System.Diagnostics;
namespace Bed.Editor.ImGui;

internal sealed record CodeEditorPlace(string Kind,string Program);
// User-local editor choice; compiler output supplies only an admitted text path and positive numbers.
internal static class CodeEditorTool
{
    internal static Func<IReadOnlyList<CodeEditorPlace>> Places=UsualPlaces;
    internal static Func<ProcessStartInfo,bool> Launch=start=>{using var process=Process.Start(start);return start.UseShellExecute||process is not null;};
    internal static string Label(string kind)=>kind switch{"vscode"=>"VS Code","visualstudio"=>"Visual Studio","rider"=>"Rider",_=>"Windows choice"};
    internal static IReadOnlyList<CodeEditorPlace> UsualPlaces()
    {
        var result=new List<CodeEditorPlace>();
        void Add(string kind,string path){if(File.Exists(path)&&!result.Any(p=>p.Kind==kind))result.Add(new(kind,path));}
        Add("vscode",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Microsoft VS Code","Code.exe"));
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Where(p=>p.Length>0).Distinct())
        {
            Add("vscode",Path.Combine(folder,"Microsoft VS Code","Code.exe"));
            foreach(var version in new[]{"2026","2022","2019"})foreach(var edition in new[]{"Community","Professional","Enterprise"})
                Add("visualstudio",Path.Combine(folder,"Microsoft Visual Studio",version,edition,"Common7","IDE","devenv.exe"));
            var jetbrains=Path.Combine(folder,"JetBrains");
            if(Directory.Exists(jetbrains))try{foreach(var rider in Directory.EnumerateDirectories(jetbrains,"*Rider*").Take(32).OrderDescending())Add("rider",Path.Combine(rider,"bin","rider64.exe"));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Take(128))
        {
            if(!Path.IsPathFullyQualified(folder))continue;
            Add("vscode",Path.Combine(folder,"Code.exe"));Add("visualstudio",Path.Combine(folder,"devenv.exe"));Add("rider",Path.Combine(folder,"rider64.exe"));
        }
        return result;
    }
    private static string? Source(string root,string working,ProjectBuildError error)
    {
        if(error.File is not {Length:>0} file||file.Length>4096||error.Line is not >0||error.Column is <=0)return null;
        var path=Path.GetFullPath(file,working);var relative=Path.GetRelativePath(root,path);
        if(relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)||Path.IsPathRooted(relative)||relative.Contains(':')||relative.Any(char.IsControl))return null;
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(!new[]{".cs",".csx",".fs",".fsx",".vb",".c",".h",".cpp",".hpp",".cc",".cxx",".hh",".hxx",".hlsl",".glsl",".vert",".frag",".json",".xml",".xaml",".props",".targets",".csproj",".fsproj",".sln",".slnx",".txt",".md"}.Contains(extension))return null;
        if(!File.Exists(path))return null;
        for(string? at=path;at is not null;at=Path.GetDirectoryName(at))if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)return null;
        return path;
    }
    internal static ProcessStartInfo Plan(string root,string working,string kind,string program,ProjectBuildError? error=null)
    {
        root=Path.GetFullPath(root);working=Path.GetFullPath(working);
        if(!Directory.Exists(root)||kind is not("windows" or "vscode" or "visualstudio" or "rider"))throw new InvalidOperationException("Choose a code editor in Options > Locations.");
        string target;
        if(error is not null)target=Source(root,working,error)??throw new InvalidOperationException("This error does not name a file and line that BED can open in this project.");
        else
        {
            var solutions=Directory.EnumerateFiles(root).Where(p=>Path.GetExtension(p) is ".sln" or ".slnx").Take(2).ToArray();
            target=solutions.Length==1?solutions[0]:root;
        }
        if(kind=="windows")return new(target){UseShellExecute=true,WorkingDirectory=root};
        if(program.Length==0)program=Places().FirstOrDefault(p=>p.Kind==kind)?.Program??"";
        if(!Path.IsPathFullyQualified(program)||!File.Exists(program)||!string.Equals(Path.GetExtension(program),".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose the editor's .exe in Options > Locations; BED could not find it.");
        var start=new ProcessStartInfo(program){UseShellExecute=false,WorkingDirectory=root};
        var line=error?.Line??1;var column=error?.Column??1;
        if(kind=="vscode")
        {start.ArgumentList.Add("--reuse-window");if(error is not null){start.ArgumentList.Add("--goto");target+=$":{line}:{column}";}start.ArgumentList.Add(target);}
        else if(kind=="rider")
        {if(error is not null){start.ArgumentList.Add("--line");start.ArgumentList.Add(line.ToString(System.Globalization.CultureInfo.InvariantCulture));start.ArgumentList.Add("--column");start.ArgumentList.Add(column.ToString(System.Globalization.CultureInfo.InvariantCulture));}start.ArgumentList.Add(target);}
        else
        {if(error is not null)start.ArgumentList.Add("/Edit");start.ArgumentList.Add(target);if(error is not null){start.ArgumentList.Add("/Command");start.ArgumentList.Add("Edit.GoTo "+line.ToString(System.Globalization.CultureInfo.InvariantCulture));}}
        return start;
    }
    internal static bool Open(string root,string working,ProjectBuildError? error,out string note)
    {
        try
        {
            var plan=Plan(root,working,Locations.CodeEditor,Locations.CodeEditorProgram,error);
            if(!Launch(plan)){note="The code editor did not start.";return false;}
            note=error is null?"Asked "+Label(Locations.CodeEditor)+" to open the game code.":Locations.CodeEditor=="windows"?$"Asked Windows to open the source; go to line {error.Line}. The usual Windows app cannot jump to a line.":$"Asked {Label(Locations.CodeEditor)} to open line {error.Line}.";
            return true;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {note="Cannot open game code: "+e.Message;return false;}
    }
}
