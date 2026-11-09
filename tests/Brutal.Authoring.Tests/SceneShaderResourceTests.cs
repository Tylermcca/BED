using System.Buffers.Binary;
using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class SceneShaderResourceTests
{
    [Theory]
    [InlineData("BED.Scene.vert.spv",0u)]
    [InlineData("BED.Scene.frag.spv",4u)]
    [InlineData("BED.Scene.textured.frag.spv",4u)]
    public void Embedded_shader_has_valid_instruction_bounds_and_expected_entry_stage(string resource,uint stage)
    {
        using var input=typeof(EditorApp).Assembly.GetManifestResourceStream(resource);Assert.NotNull(input);
        using var output=new MemoryStream();input.CopyTo(output);var bytes=output.ToArray();
        Assert.True(bytes.Length>=20);Assert.Equal(0,bytes.Length%4);Assert.Equal(0x07230203u,BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        var found=false;
        for(var offset=20;offset<bytes.Length;)
        {
            var instruction=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));var words=(int)(instruction>>16);
            Assert.True(words>0);Assert.True((long)offset+words*4<=bytes.Length);
            if((instruction&65535)==15){Assert.True(words>=4);Assert.Equal(stage,BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+4)));found=true;}
            offset+=words*4;
        }
        Assert.True(found);
    }
    [Fact]
    public void Textured_shader_uses_one_combined_sampler_at_set_zero_binding_zero_and_flat_shader_has_none()
    {
        static uint[][] Instructions(string name)
        {
            using var stream=typeof(EditorApp).Assembly.GetManifestResourceStream(name)!;using var buffer=new MemoryStream();stream.CopyTo(buffer);
            var bytes=buffer.ToArray();var result=new List<uint[]>();
            for(var at=20;at<bytes.Length;){var word=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));var count=(int)(word>>16);
                Assert.True(count>0);Assert.True(at+count*4<=bytes.Length);var row=new uint[count];for(var i=0;i<count;i++)row[i]=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+i*4));result.Add(row);at+=count*4;}
            return result.ToArray();
        }
        var sampled=Instructions("BED.Scene.textured.frag.spv");
        var binding=Assert.Single(sampled,r=>(r[0]&65535)==71&&r.Length==4&&r[2]==33&&r[3]==0);
        Assert.Contains(sampled,r=>(r[0]&65535)==71&&r.Length==4&&r[1]==binding[1]&&r[2]==34&&r[3]==0);
        Assert.Single(sampled,r=>(r[0]&65535)==87); // One filtered sample; actual sRGB/color output remains owner-native acceptance
        Assert.DoesNotContain(Instructions("BED.Scene.frag.spv"),r=>(r[0]&65535)==27); // no sampled-image type, no dummy descriptor required
    }
    [Fact]
    public void Runtime_assembly_does_not_reference_build_time_compiler()
    {
        Assert.DoesNotContain(typeof(EditorApp).Assembly.GetReferencedAssemblies(),a=>a.Name!.Contains("ShaderC",StringComparison.OrdinalIgnoreCase)||a.Name.Contains("SceneShaderCompiler",StringComparison.OrdinalIgnoreCase));
    }
}
