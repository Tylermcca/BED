using System.IO.Compression;
namespace SceneStarter.Garden;

/// <summary>A tiny self-drawn 5x7 capital-letter font, so the sample ships no font file.</summary>
public static class PixelFont
{
    private static readonly Dictionary<char,string[]> Glyphs=new()
    {
        ['A']=[" ### ","#   #","#   #","#####","#   #","#   #","#   #"],
        ['B']=["#### ","#   #","#   #","#### ","#   #","#   #","#### "],
        ['C']=[" ####","#    ","#    ","#    ","#    ","#    "," ####"],
        ['D']=["#### ","#   #","#   #","#   #","#   #","#   #","#### "],
        ['E']=["#####","#    ","#    ","#### ","#    ","#    ","#####"],
        ['F']=["#####","#    ","#    ","#### ","#    ","#    ","#    "],
        ['G']=[" ####","#    ","#    ","#  ##","#   #","#   #"," ####"],
        ['H']=["#   #","#   #","#   #","#####","#   #","#   #","#   #"],
        ['I']=["#####","  #  ","  #  ","  #  ","  #  ","  #  ","#####"],
        ['J']=["  ###","   # ","   # ","   # ","   # ","#  # "," ##  "],
        ['K']=["#   #","#  # ","# #  ","##   ","# #  ","#  # ","#   #"],
        ['L']=["#    ","#    ","#    ","#    ","#    ","#    ","#####"],
        ['M']=["#   #","## ##","# # #","# # #","#   #","#   #","#   #"],
        ['N']=["#   #","##  #","# # #","#  ##","#   #","#   #","#   #"],
        ['O']=[" ### ","#   #","#   #","#   #","#   #","#   #"," ### "],
        ['P']=["#### ","#   #","#   #","#### ","#    ","#    ","#    "],
        ['Q']=[" ### ","#   #","#   #","#   #","# # #","#  # "," ## #"],
        ['R']=["#### ","#   #","#   #","#### ","# #  ","#  # ","#   #"],
        ['S']=[" ####","#    ","#    "," ### ","    #","    #","#### "],
        ['T']=["#####","  #  ","  #  ","  #  ","  #  ","  #  ","  #  "],
        ['U']=["#   #","#   #","#   #","#   #","#   #","#   #"," ### "],
        ['V']=["#   #","#   #","#   #","#   #","#   #"," # # ","  #  "],
        ['W']=["#   #","#   #","#   #","# # #","# # #","# # #"," # # "],
        ['X']=["#   #","#   #"," # # ","  #  "," # # ","#   #","#   #"],
        ['Y']=["#   #","#   #"," # # ","  #  ","  #  ","  #  ","  #  "],
        ['Z']=["#####","    #","   # ","  #  "," #   ","#    ","#####"],
        ['0']=[" ### ","#   #","#  ##","# # #","##  #","#   #"," ### "],
        ['1']=["  #  "," ##  ","  #  ","  #  ","  #  ","  #  "," ### "],
        ['2']=[" ### ","#   #","    #","   # ","  #  "," #   ","#####"],
        ['3']=["#####","   # ","  #  ","   # ","    #","#   #"," ### "],
        ['4']=["   # ","  ## "," # # ","#  # ","#####","   # ","   # "],
        ['5']=["#####","#    ","#### ","    #","    #","#   #"," ### "],
        ['6']=["  ## "," #   ","#    ","#### ","#   #","#   #"," ### "],
        ['7']=["#####","    #","   # ","  #  ","  #  ","  #  ","  #  "],
        ['8']=[" ### ","#   #","#   #"," ### ","#   #","#   #"," ### "],
        ['9']=[" ### ","#   #","#   #"," ####","    #","   # "," ##  "],
        ['!']=["  #  ","  #  ","  #  ","  #  ","  #  ","     ","  #  "],
        ['/']=["    #","    #","   # ","  #  "," #   ","#    ","#    "],
        [':']=["     ","  #  ","     ","     ","     ","  #  ","     "],
        ['.']=["     ","     ","     ","     ","     ","     ","  #  "],
        ['-']=["     ","     ","     ","#####","     ","     ","     "],
    };

    public static int Measure(string text,int unit)=>text.Length*6*unit-unit;

    public static void DrawCentered(uint[] pixels,int width,int height,string text,int top,int unit,uint color)
        =>Draw(pixels,width,height,text,(width-Measure(text,unit))/2,top,unit,color);

    /// <summary>Draws text with a soft dark shadow so it reads on sky and grass alike.</summary>
    public static void Draw(uint[] pixels,int width,int height,string text,int left,int top,int unit,uint color)
    {
        var shadow=Math.Max(1,unit/2);
        Paint(pixels,width,height,text,left+shadow,top+shadow,unit,0xFF2B2140);
        Paint(pixels,width,height,text,left,top,unit,color);
    }

    private static void Paint(uint[] pixels,int width,int height,string text,int left,int top,int unit,uint color)
    {
        for(var n=0;n<text.Length;n++)
        {
            if(!Glyphs.TryGetValue(char.ToUpperInvariant(text[n]),out var rows))continue;
            for(var r=0;r<7;r++)for(var c=0;c<5;c++)
            {
                if(rows[r][c]!='#')continue;
                var x0=left+(n*6+c)*unit;var y0=top+r*unit;
                for(var y=Math.Max(0,y0);y<Math.Min(height,y0+unit);y++)
                    for(var x=Math.Max(0,x0);x<Math.Min(width,x0+unit);x++)pixels[y*width+x]=color;
            }
        }
    }
}

/// <summary>Minimal PNG writer (RGB, no dependencies) for <c>--shot</c>.</summary>
public static class PngWriter
{
    public static void Write(string path,uint[] argb,int width,int height)
    {
        var raw=new byte[(width*3+1)*height];
        for(int y=0,o=0;y<height;y++)
        {
            raw[o++]=0;
            for(var x=0;x<width;x++){var c=argb[y*width+x];raw[o++]=(byte)(c>>16);raw[o++]=(byte)(c>>8);raw[o++]=(byte)c;}
        }
        using var compressed=new MemoryStream();
        using(var z=new ZLibStream(compressed,CompressionLevel.Optimal,true))z.Write(raw);
        var header=new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header,width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=2;   // 8-bit RGB
        var directory=Path.GetDirectoryName(Path.GetFullPath(path));if(directory is not null)Directory.CreateDirectory(directory);
        using var file=File.Create(path);
        file.Write([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]);
        Chunk(file,"IHDR"u8,header);Chunk(file,"IDAT"u8,compressed.ToArray());Chunk(file,"IEND"u8,[]);
    }

    private static void Chunk(Stream stream,ReadOnlySpan<byte> kind,byte[] data)
    {
        Span<byte> number=stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number,data.Length);stream.Write(number);
        stream.Write(kind);stream.Write(data);
        var crc=Crc(Crc(0xFFFFFFFFu,kind),data)^0xFFFFFFFFu;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number,crc);stream.Write(number);
    }

    private static uint Crc(uint crc,ReadOnlySpan<byte> data)
    {
        foreach(var b in data){crc^=b;for(var k=0;k<8;k++)crc=(crc&1)!=0?0xEDB88320u^(crc>>1):crc>>1;}
        return crc;
    }
}
