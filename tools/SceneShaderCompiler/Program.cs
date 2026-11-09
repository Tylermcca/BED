using Brutal.ShaderCApi;
if(args.Length!=2) throw new ArgumentException("Expected shader source directory and output directory.");
Directory.CreateDirectory(args[1]);
using var compiler=ShaderC.CreateCompiler();
using var options=ShaderC.CreateCompilerOptions();
options.SetTargetEnv(TargetEnv.Vulkan,(uint)EnvVersion.Vulkan_1_3);
foreach(var (file,kind) in new[]{("scene.vert",ShaderKind.VertexShader),("scene.frag",ShaderKind.FragmentShader),("scene-textured.frag",ShaderKind.FragmentShader)})
{
    using var result=compiler.Compile(File.ReadAllBytes(Path.Combine(args[0],file)),kind,System.Text.Encoding.UTF8.GetBytes(file),"main"u8,options);
    unsafe { File.WriteAllBytes(Path.Combine(args[1],file+".spv"),new ReadOnlySpan<byte>(result.GetBytes(),(int)result.GetLength().Bytes).ToArray()); }
}
