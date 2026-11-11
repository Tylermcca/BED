using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using GL=Brutal.OpenGlApi.OpenGl.Native;
namespace PhysicsStarter;

/// <summary>The window around the toy: GLFW with an OpenGL 4.1 core context (BRUTAL's recipe 7.21 setup), drawing into
/// a 4x multisampled framebuffer. The same drawing renders a hidden window's frame to a PNG for checks.</summary>
internal static unsafe class ToyWindow
{
    internal const string Title="Crate drop - click: drop a crate | right-drag or A/D: orbit | wheel: zoom | R: reset | Esc: quit";
    private static GlGetProcAddressFunc? loader;   // kept rooted: GLAD keeps the pointer (BRUTAL T35)

    /// <summary>Opens the window and plays until it is closed. Returns false when no OpenGL 4.1 window could be made.</summary>
    internal static bool Play(CrateToy toy,Scene2RuntimeAssets assets,TextWriter error)
    {
        if(!Glfw.Init()){error.WriteLine("No window is available here (GLFW could not start).");return false;}
        try
        {
            if(Create(false,1280,720) is not {} window){error.WriteLine("This PC has no OpenGL 4.1 window; running without one.");return false;}
            try
            {
                window.Title=Title;
                var controls=new ToyControls(new OrbitCamera());
                window.OnKey+=(_,key,_,action,_)=>{if(action!=GlfwKeyAction.Repeat)controls.Key(Map(key),action==GlfwKeyAction.Press);};
                window.OnMouseButton+=(_,button,action,_)=>controls.Button(button==GlfwMouseButton.Left?ToyButton.Left:button==GlfwMouseButton.Right?ToyButton.Right:ToyButton.Other,action==GlfwButtonAction.Press);
                window.OnCursorPos+=(_,at)=>controls.Cursor((float)at.X,(float)at.Y);
                window.OnScroll+=(_,offset)=>controls.Scroll((float)offset.Y);
                using var renderer=new ToyRenderer(assets.Meshes,assets.Textures);
                Target? target=null;
                try
                {
                    var clock=Stopwatch.StartNew();var last=0.0;
                    while(!window.ShouldClose)
                    {
                        Glfw.PollEvents();
                        var now=clock.Elapsed.TotalSeconds;var seconds=Math.Min(now-last,.25);last=now;
                        foreach(var (command,at) in controls.Update((float)seconds))
                            switch(command)
                            {
                                case ToyCommand.Quit:window.ShouldClose=true;break;
                                case ToyCommand.Reset:toy.Reset();break;
                                case ToyCommand.DropCrate:
                                    var size=window.Size;var (origin,direction)=controls.Camera.Ray(at.X,at.Y,MathF.Max(1,size.X),MathF.Max(1,size.Y));
                                    toy.Drop(toy.DropPoint(origin,direction));break;
                            }
                        toy.Advance(seconds);
                        var pixels=window.FramebufferSize;
                        if(pixels.X<=0||pixels.Y<=0){Glfw.WaitEventsTimeout(.1);continue;}   // minimised
                        if(target is null||target.Width!=pixels.X||target.Height!=pixels.Y){target?.Dispose();target=new Target(pixels.X,pixels.Y);}
                        renderer.Draw(toy.Drawables(),controls.Camera,pixels.X,pixels.Y,target.Frame);
                        target.BlitTo(default);
                        Glfw.PInvoke.SwapBuffers(window);
                    }
                }
                finally{target?.Dispose();}
            }
            finally{window.Dispose();}
            return true;
        }
        finally{Glfw.Terminate();}
    }

    /// <summary>Renders the toy's current state through a hidden window into a PNG; nothing appears on screen.</summary>
    internal static void Shot(CrateToy toy,Scene2RuntimeAssets assets,string path,int width,int height,OrbitCamera camera)
    {
        if(!Glfw.Init())throw new InvalidOperationException("GLFW could not start, so no frame can be rendered.");
        try
        {
            var window=Create(true,64,64)??throw new InvalidOperationException("This PC has no OpenGL 4.1 context, so no frame can be rendered.");
            try
            {
                using var renderer=new ToyRenderer(assets.Meshes,assets.Textures);using var target=new Target(width,height);
                renderer.Draw(toy.Drawables(),camera,width,height,target.Frame);
                WritePng(path,width,height,target.Read());
            }
            finally{window.Dispose();}
        }
        finally{Glfw.Terminate();}
    }

    private static GlfwWindow? Create(bool hidden,int width,int height)
    {
        Glfw.WindowHint(GlfwWindowHint.ClientApi,Glfw.Constants.GLFW_OPENGL_API);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor,4);Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor,1);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile,Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
        Glfw.WindowHint(GlfwWindowHint.Visible,hidden?0:1);
        var window=Glfw.CreateWindow(new GlfwWindow.CreateInfo{Title="Crate drop",Size=new int2(width,height)});
        if(window.IsNull())return null;
        Glfw.PInvoke.MakeContextCurrent(window);Glfw.PInvoke.SwapInterval(1);
        loader??=name=>Glfw.PInvoke.GetProcAddress(name);
        if(!Glad.Initialize(loader)){window.Dispose();return null;}
        return window;
    }
    private static ToyKey Map(GlfwKey key)=>key switch{GlfwKey.A=>ToyKey.A,GlfwKey.D=>ToyKey.D,GlfwKey.Left=>ToyKey.Left,GlfwKey.Right=>ToyKey.Right,GlfwKey.R=>ToyKey.R,GlfwKey.Escape=>ToyKey.Escape,_=>ToyKey.Other};

    /// <summary>A 4x multisampled colour and depth framebuffer, resolved by a blit.</summary>
    private sealed class Target:IDisposable
    {
        internal int Width{get;}internal int Height{get;}internal GlFramebuffer Frame{get;}
        private readonly GlRenderbuffer colour,depth;
        internal Target(int width,int height)
        {
            Width=width;Height=height;GlFramebuffer frame;GL.glGenFramebuffers(1,&frame);Frame=frame;GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,frame);
            var buffers=stackalloc GlRenderbuffer[2];GL.glGenRenderbuffers(2,buffers);colour=buffers[0];depth=buffers[1];
            GL.glBindRenderbuffer(OpenGl.GL_RENDERBUFFER,colour);GL.glRenderbufferStorageMultisample(OpenGl.GL_RENDERBUFFER,4,OpenGl.GL_RGBA8,width,height);
            GL.glFramebufferRenderbuffer(OpenGl.GL_FRAMEBUFFER,OpenGl.GL_COLOR_ATTACHMENT0,OpenGl.GL_RENDERBUFFER,colour);
            GL.glBindRenderbuffer(OpenGl.GL_RENDERBUFFER,depth);GL.glRenderbufferStorageMultisample(OpenGl.GL_RENDERBUFFER,4,OpenGl.GL_DEPTH_COMPONENT24,width,height);
            GL.glFramebufferRenderbuffer(OpenGl.GL_FRAMEBUFFER,OpenGl.GL_DEPTH_ATTACHMENT,OpenGl.GL_RENDERBUFFER,depth);
            if(GL.glCheckFramebufferStatus(OpenGl.GL_FRAMEBUFFER).Id!=OpenGl.GL_FRAMEBUFFER_COMPLETE)throw new InvalidOperationException("The multisampled framebuffer is incomplete.");
            GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,default);
        }
        internal void BlitTo(GlFramebuffer destination)
        {
            GL.glBindFramebuffer(OpenGl.GL_READ_FRAMEBUFFER,Frame);GL.glBindFramebuffer(OpenGl.GL_DRAW_FRAMEBUFFER,destination);
            GL.glBlitFramebuffer(0,0,Width,Height,0,0,Width,Height,OpenGl.GL_COLOR_BUFFER_BIT,OpenGl.GL_NEAREST);GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,default);
        }
        /// <summary>Resolves into a single-sample framebuffer and reads it back, top row first.</summary>
        internal byte[] Read()
        {
            GlFramebuffer resolved;GL.glGenFramebuffers(1,&resolved);GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,resolved);GlRenderbuffer plain;GL.glGenRenderbuffers(1,&plain);
            GL.glBindRenderbuffer(OpenGl.GL_RENDERBUFFER,plain);GL.glRenderbufferStorage(OpenGl.GL_RENDERBUFFER,OpenGl.GL_RGBA8,Width,Height);
            GL.glFramebufferRenderbuffer(OpenGl.GL_FRAMEBUFFER,OpenGl.GL_COLOR_ATTACHMENT0,OpenGl.GL_RENDERBUFFER,plain);
            BlitTo(resolved);GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,resolved);
            var pixels=new byte[Width*Height*4];fixed(byte* p=pixels)GL.glReadPixels(0,0,Width,Height,OpenGl.GL_RGBA,OpenGl.GL_UNSIGNED_BYTE,p);
            GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,default);GL.glDeleteRenderbuffers(1,&plain);GL.glDeleteFramebuffers(1,&resolved);
            var flipped=new byte[pixels.Length];var row=Width*4;for(var y=0;y<Height;y++)Array.Copy(pixels,y*row,flipped,(Height-1-y)*row,row);
            return flipped;
        }
        public void Dispose()
        {
            var buffers=stackalloc GlRenderbuffer[2]{colour,depth};GL.glDeleteRenderbuffers(2,buffers);var frame=Frame;GL.glDeleteFramebuffers(1,&frame);
        }
    }

    /// <summary>An RGBA PNG with zlib-compressed rows; no image library needed.</summary>
    internal static void WritePng(string path,int width,int height,byte[] rgba)
    {
        using var raw=new MemoryStream();
        using(var z=new ZLibStream(raw,CompressionLevel.Optimal,true))
            for(var y=0;y<height;y++){z.WriteByte(0);z.Write(rgba,y*width*4,width*4);}
        using var file=new MemoryStream();file.Write([0x89,(byte)'P',(byte)'N',(byte)'G',13,10,26,10]);
        void Chunk(string kind,ReadOnlySpan<byte> data)
        {
            Span<byte> length=stackalloc byte[4];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length,data.Length);file.Write(length);
            var body=new byte[4+data.Length];System.Text.Encoding.ASCII.GetBytes(kind,body);data.CopyTo(body.AsSpan(4));file.Write(body);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length,Crc(body));file.Write(length);
        }
        var header=new byte[13];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header,width);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=6;Chunk("IHDR",header);Chunk("IDAT",raw.ToArray());Chunk("IEND",[]);
        var directory=Path.GetDirectoryName(Path.GetFullPath(path));if(directory is not null)Directory.CreateDirectory(directory);
        File.WriteAllBytes(path,file.ToArray());
    }
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc=0xFFFFFFFFu;
        foreach(var b in data){crc^=b;for(var k=0;k<8;k++)crc=(crc&1)!=0?0xEDB88320u^(crc>>1):crc>>1;}
        return ~crc;
    }
}

/// <summary>The decoded models and textures a frame draws, from the checked runtime input.</summary>
internal sealed record Scene2RuntimeAssets(IReadOnlyDictionary<string,Bed.Runtime.BedMesh> Meshes,IReadOnlyDictionary<string,Bed.Runtime.BedTexture> Textures);
