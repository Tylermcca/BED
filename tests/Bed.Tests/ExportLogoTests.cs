using PhysicsStarter;
using Xunit;
namespace Bed.Tests;
[Collection(PhysicsWorldCollection.Name)]
public sealed class ExportLogoTests
{
    [OpenGlFact]
    public void Official_logo_is_decoded_and_drawn_through_a_hidden_OpenGL_context()
    {
        var path=Path.Combine(Path.GetTempPath(),"bed-release-logo-"+Guid.NewGuid().ToString("N")+".png");
        try
        {
            var image=BrutalLogo.Image;Assert.True(image.Width>100);Assert.True(image.Height>50);Assert.NotEmpty(image.Runs);
            ToyWindow.ShotBranding(path,800,600);
                        var png=File.ReadAllBytes(path);
            var length=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33,4));
            using var compressed=new MemoryStream(png,41,length);
            using var z=new System.IO.Compression.ZLibStream(compressed,System.IO.Compression.CompressionMode.Decompress);
            var pixels=new byte[(800*4+1)*600];z.ReadExactly(pixels);
            var bright=0;
            for(var y=0;y<600;y++)for(var x=0;x<800;x++)if(pixels[y*(800*4+1)+1+x*4]>100)bright++;
            Assert.True(bright>1000,"The rendered logo must contain visible white pixels.");
            Assert.Equal(0,pixels[10*(800*4+1)+1+10*4]);
        }
        finally{File.Delete(path);}
    }
}
