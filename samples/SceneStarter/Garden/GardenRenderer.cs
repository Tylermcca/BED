using Bed.Runtime;
namespace SceneStarter.Garden;

/// <summary>Draws the game into a 0xAARRGGBB pixel buffer with linear (bilinear) filtering. The window shows exactly
/// these pixels, and <c>--shot</c> saves them, so a headless picture is the same frame a player sees.</summary>
public sealed class GardenRenderer
{
    private sealed record Picture(int Width,int Height,uint[] Premultiplied);
    private readonly Dictionary<string,Picture> pictures=new(StringComparer.Ordinal);
    private readonly GardenLevel level;
    private uint[] background=[];
    private int firstMoving;   // first sprite in drawing order that is redrawn every frame
    public int Width {get;private set;}
    public int Height {get;private set;}
    public uint[] Pixels {get;private set;}=[];
    private float scale,originX,originY;

    public GardenRenderer(GardenLevel level,int width,int height)
    {
        this.level=level;
        foreach(var (id,texture) in level.Textures)pictures[id]=Premultiply(texture);
        var sprites=level.Sprites;firstMoving=sprites.Count;
        for(var i=0;i<sprites.Count;i++)if(sprites[i].Role is GardenRole.Player or GardenRole.Coin){firstMoving=i;break;}
        Resize(width,height);
    }

    /// <summary>A window size that fits the level's shape inside the given limits.</summary>
    public static (int Width,int Height) Fit(Box bounds,int maxWidth,int maxHeight)
    {
        var aspect=bounds.Width/bounds.Height;
        var width=maxWidth;var height=(int)MathF.Round(width/aspect);
        if(height>maxHeight){height=maxHeight;width=(int)MathF.Round(height*aspect);}
        return (Math.Max(64,width),Math.Max(64,height));
    }

    public void Resize(int width,int height)
    {
        width=Math.Max(1,width);height=Math.Max(1,height);
        if(width==Width&&height==Height)return;
        Width=width;Height=height;Pixels=new uint[width*height];background=new uint[width*height];
        var b=level.Bounds;scale=MathF.Min(width/b.Width,height/b.Height);
        originX=(width-b.Width*scale)/2;originY=(height-b.Height*scale)/2;
        Array.Fill(background,0xFF1E2A38u);   // letterbox colour
        for(var i=0;i<firstMoving;i++)Draw(background,level.Sprites[i],level.Sprites[i].Picture,false);
    }

    public void Render(GardenGame game)
    {
        background.CopyTo(Pixels,0);
        var coinIndex=0;
        for(var i=firstMoving;i<level.Sprites.Count;i++)
        {
            var sprite=level.Sprites[i];
            switch(sprite.Role)
            {
                case GardenRole.Player:Draw(Pixels,sprite,game.PlayerPicture,game.FacingLeft);break;
                case GardenRole.Coin:
                    var index=coinIndex++;
                    if(!game.Collected[index])
                    {
                        var bob=MathF.Sin(game.Frame*0.08f+index*1.7f)*0.04f;   // a gentle hover
                        Draw(Pixels,sprite,sprite.Picture.Offset(0,bob),false);
                    }
                    break;
                default:Draw(Pixels,sprite,sprite.Picture,false);break;
            }
        }
        DrawHud(game);
    }

    private void DrawHud(GardenGame game)
    {
        var unit=Math.Max(2,Height/160);var pad=unit*4;
        // Coin counter, top left: the level's own coin picture, then "2/3".
        var coin=level.Sprites.FirstOrDefault(s=>s.Role==GardenRole.Coin);
        var x=pad;
        if(coin is not null)
        {
            var size=unit*9;var p=pictures[coin.TextureId];
            Blit(Pixels,p,x,pad-unit,size,size,false);x+=size+unit*2;
        }
        PixelFont.Draw(Pixels,Width,Height,$"{game.CoinsCollected}/{game.CoinCount}",x,pad,unit,0xFFFFFFFF);
        if(game.Frame<60*6&&!game.Won)
            PixelFont.DrawCentered(Pixels,Width,Height,"ARROWS OR A/D MOVE   SPACE JUMP   R RESTART   ESC QUIT",Height-pad-7*Math.Max(2,unit*2/3),Math.Max(2,unit*2/3),0xFFFFFFFF);
        if(game.Won)
        {
            var big=unit*3;
            var title=Height/2-(big*7+unit*13)/2;var subtitle=title+big*7+unit*6;
            Shade(title-unit*6,big*7+unit*25);
            PixelFont.DrawCentered(Pixels,Width,Height,"YOU DID IT!",title,big,0xFFFFE066);
            PixelFont.DrawCentered(Pixels,Width,Height,"PRESS R TO PLAY AGAIN",subtitle,unit,0xFFFFFFFF);
        }
        else if(game.HintTime>0)
            PixelFont.DrawCentered(Pixels,Width,Height,"FIND EVERY COIN FIRST",pad*3,unit*2,0xFFFFFFFF);
    }

    private void Shade(int top,int height)
    {
        for(var y=Math.Max(0,top);y<Math.Min(Height,top+height);y++)
            for(int x=0,i=y*Width;x<Width;x++,i++)
            {var c=Pixels[i];Pixels[i]=0xFF000000u|(((c>>16)&0xFF)*5/8<<16)|(((c>>8)&0xFF)*5/8<<8)|((c&0xFF)*5/8);}
    }

    private void Draw(uint[] target,GardenSprite sprite,Box world,bool flip)
    {
        var picture=pictures[sprite.TextureId];var b=level.Bounds;
        var left=originX+(world.X0-b.X0)*scale;var top=originY+(b.Y1-world.Y1)*scale;
        Blit(target,picture,left,top,world.Width*scale,world.Height*scale,flip);
    }

    /// <summary>Bilinear, premultiplied "over" blend of a picture into the rectangle (pixel units, top-left origin).</summary>
    private void Blit(uint[] target,Picture picture,float left,float top,float width,float height,bool flip)
    {
        if(width<=0||height<=0)return;
        int x0=Math.Max(0,(int)MathF.Ceiling(left-0.5f)),x1=Math.Min(Width,(int)MathF.Ceiling(left+width-0.5f));
        int y0=Math.Max(0,(int)MathF.Ceiling(top-0.5f)),y1=Math.Min(Height,(int)MathF.Ceiling(top+height-0.5f));
        int pw=picture.Width,ph=picture.Height;var src=picture.Premultiplied;
        float sx=pw/width,sy=ph/height;
        for(var y=y0;y<y1;y++)
        {
            var ty=Math.Clamp((y+0.5f-top)*sy-0.5f,0,ph-1);var ry0=(int)ty;var ry1=Math.Min(ry0+1,ph-1);var fy=(int)((ty-ry0)*256);
            int row0=ry0*pw,row1=ry1*pw,line=y*Width;
            for(var x=x0;x<x1;x++)
            {
                var u=(x+0.5f-left)*sx-0.5f;if(flip)u=pw-1-u;
                var tx=Math.Clamp(u,0,pw-1);var rx0=(int)tx;var rx1=Math.Min(rx0+1,pw-1);var fx=(int)((tx-rx0)*256);
                var c=Mix(Mix(src[row0+rx0],src[row0+rx1],fx),Mix(src[row1+rx0],src[row1+rx1],fx),fy);
                var a=c>>24;if(a==0)continue;
                ref var d=ref target[line+x];
                if(a==255){d=c;continue;}
                var k=255-a;
                uint r=((c>>16)&0xFF)+(((d>>16)&0xFF)*k+127)/255,g=((c>>8)&0xFF)+(((d>>8)&0xFF)*k+127)/255,bl=(c&0xFF)+((d&0xFF)*k+127)/255;
                d=0xFF000000u|(Math.Min(r,255u)<<16)|(Math.Min(g,255u)<<8)|Math.Min(bl,255u);
            }
        }
    }

    private static uint Mix(uint p,uint q,int t)
    {
        if(t==0||p==q)return p;
        uint rb=((p&0x00FF00FFu)*(uint)(256-t)+(q&0x00FF00FFu)*(uint)t)>>8&0x00FF00FFu;
        uint ag=(((p>>8)&0x00FF00FFu)*(uint)(256-t)+((q>>8)&0x00FF00FFu)*(uint)t)&0xFF00FF00u;
        return rb|ag;
    }

    private static Picture Premultiply(BedTexture texture)
    {
        var pixels=new uint[texture.Width*texture.Height];var rgba=texture.Rgba;
        for(var i=0;i<pixels.Length;i++)
        {
            uint r=rgba[i*4],g=rgba[i*4+1],b=rgba[i*4+2],a=rgba[i*4+3];
            pixels[i]=(a<<24)|((r*a+127)/255<<16)|((g*a+127)/255<<8)|((b*a+127)/255);
        }
        return new(texture.Width,texture.Height,pixels);
    }
}
