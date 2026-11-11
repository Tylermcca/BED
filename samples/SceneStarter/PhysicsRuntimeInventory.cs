using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace SceneStarter;

// Separate exact Windows-x64 inventory; the pure sample inventory is unchanged. GLFW and OpenGL draw the physics game's window.
internal static class PhysicsRuntimeInventory
{
    internal const string NativeHash="dd3cca563c9ffb7f9f3af76dd133547f777353e015ece98d255795d278cf8595";
    internal static readonly string[] Files=[
        "Bed.Runtime.dll",
        "Bed.dll",
        "Brutal.Collections.dll",
        "Brutal.Core.dll",
        "Brutal.Glfw.dll",
        "Brutal.Interop.dll",
        "Brutal.Jolt.dll",
        "Brutal.Logging.dll",
        "Brutal.Memory.dll",
        "Brutal.Numerics.dll",
        "Brutal.OpenGl.dll",
        "Brutal.Strings.dll",
        "CommunityToolkit.HighPerformance.dll",
        "LICENSE.txt",
        "Microsoft.Extensions.Configuration.Abstractions.dll",
        "Microsoft.Extensions.Configuration.Binder.dll",
        "Microsoft.Extensions.Configuration.dll",
        "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        "Microsoft.Extensions.DependencyInjection.dll",
        "Microsoft.Extensions.Logging.Abstractions.dll",
        "Microsoft.Extensions.Logging.Configuration.dll",
        "Microsoft.Extensions.Logging.Console.dll",
        "Microsoft.Extensions.Logging.dll",
        "Microsoft.Extensions.ObjectPool.dll",
        "Microsoft.Extensions.Options.ConfigurationExtensions.dll",
        "Microsoft.Extensions.Options.dll",
        "Microsoft.Extensions.Primitives.dll",
        "NOTICE.txt",
        "PhysicsStarter.deps.json",
        "PhysicsStarter.dll",
        "PhysicsStarter.exe",
        "PhysicsStarter.runtimeconfig.json",
        "licenses/BRUTAL-GLFW-THIRD-PARTY-NOTICES.txt",
        "licenses/BRUTAL-JOLT-THIRD-PARTY-NOTICES.txt",
        "licenses/BRUTAL-OPENGL-THIRD-PARTY-NOTICES.txt",
        "licenses/NVorbis-LICENSE.txt",
        "runtimes/win-x64/native/glad.dll",
        "runtimes/win-x64/native/glfw3.dll",
        "runtimes/win-x64/native/jolt.dll"
    ];
    private static readonly string[] Packages=[
        "Ahwoo.BRUTAL.Collections/2026.10.1",
        "Ahwoo.BRUTAL.Core/2026.10.1",
        "Ahwoo.BRUTAL.Glfw/2026.10.4",
        "Ahwoo.BRUTAL.Interop/2026.10.1",
        "Ahwoo.BRUTAL.Jolt/2026.10.2",
        "Ahwoo.BRUTAL.Logging/2026.10.2",
        "Ahwoo.BRUTAL.Memory/2026.10.1",
        "Ahwoo.BRUTAL.Numerics/2026.10.1",
        "Ahwoo.BRUTAL.OpenGl/2026.10.1",
        "Ahwoo.BRUTAL.Strings/2026.10.1",
        "CommunityToolkit.HighPerformance/8.4.2",
        "Microsoft.Extensions.Configuration.Abstractions/10.0.0",
        "Microsoft.Extensions.Configuration.Binder/10.0.0",
        "Microsoft.Extensions.Configuration/10.0.0",
        "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.0",
        "Microsoft.Extensions.DependencyInjection/10.0.0",
        "Microsoft.Extensions.Logging.Abstractions/10.0.0",
        "Microsoft.Extensions.Logging.Configuration/10.0.0",
        "Microsoft.Extensions.Logging.Console/10.0.0",
        "Microsoft.Extensions.Logging/10.0.0",
        "Microsoft.Extensions.ObjectPool/11.0.0-preview.5.26302.115",
        "Microsoft.Extensions.Options.ConfigurationExtensions/10.0.0",
        "Microsoft.Extensions.Options/10.0.0",
        "Microsoft.Extensions.Primitives/10.0.0"
    ];
    internal static string[] BuildInputs(string source)=>new[]{source,".bed/project.json",".bed/cache/current.json"}.Concat(Files.Select(f=>"game/"+f)).ToArray();
    internal static Dictionary<string,byte[]> Read(string directory)
    {
        var root=Path.GetFullPath(directory);var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
        foreach(var relative in Files)
        {
            var path=Path.Combine(root,relative);for(string? at=path;at is not null;at=Path.GetDirectoryName(at))if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked physics runtime file.");
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);if(file.Length is <=0 or >67108864||(total+=file.Length)>134217728)throw new IOException("Physics runtime exceeds its byte budget.");var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);files.Add(relative,bytes);
        }
        using var json=JsonDocument.Parse(files["PhysicsStarter.deps.json"]);var libraries=json.RootElement.GetProperty("libraries"u8).EnumerateObject().ToArray();
        var packages=libraries.Where(p=>p.Value.GetProperty("type"u8).GetString()=="package").Select(p=>p.Name).Order(StringComparer.Ordinal).ToArray();
        var projects=libraries.Where(p=>p.Value.GetProperty("type"u8).GetString()!="package").Select(p=>p.Name.Split('/')[0]).Order(StringComparer.Ordinal).ToArray();
        if(!packages.SequenceEqual(Packages)||!projects.SequenceEqual(new[]{"Bed","Bed.Runtime","PhysicsStarter"}))throw new InvalidDataException("Physics dependencies differ from the pinned profile.");
        if(Convert.ToHexStringLower(SHA256.HashData(files["runtimes/win-x64/native/jolt.dll"]))!=NativeHash)throw new InvalidDataException("The physics native binary differs from the verified pin.");return files;
    }
    internal static void CheckLoadedNative()
    {
        var native=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m=>m.ModuleName.Equals("jolt.dll",StringComparison.OrdinalIgnoreCase));
        using var file=File.OpenRead(native.FileName);if(Convert.ToHexStringLower(SHA256.HashData(file))!=NativeHash)throw new InvalidDataException("The loaded native physics binary differs from the verified pin.");
    }
}
