using System.Numerics;
using System.Text;
using Bed.Runtime;
using Brutal.OpenGlApi;
using GL=Brutal.OpenGlApi.OpenGl.Native;
namespace PhysicsStarter;

/// <summary>Draws the toy with OpenGL 4.1 core: a sky gradient, a calm sea, every saved model with its material colour
/// and texture, one sun with a shadow map and a soft sky/ground ambient. The sample's textures carry the light level
/// BED's own view bakes in (one tile per face direction); the shader divides that level out again from the model-space
/// face normal, so the real light here doesn't darken faces twice and BED's view keeps its look.</summary>
internal sealed unsafe class ToyRenderer:IDisposable
{
    // The fixed light BED's view bakes into the sample textures (generate_assets.py LIGHT), and the sun used here.
    private static readonly Vector3 BakedLight=Vector3.Normalize(new(-.4f,1,-.6f));
    internal static readonly Vector3 Sun=Vector3.Normalize(new(-.45f,1,.35f));
    private static readonly Vector3 Sky=new(.47f,.72f,.94f),Horizon=new(.83f,.92f,.98f);
    private const int ShadowSize=2048;
    private readonly GlProgram scene,sky,depth;
    private readonly Dictionary<string,List<Part>> models=new(StringComparer.Ordinal);
    private readonly Dictionary<string,GlTexture> textures=new(StringComparer.Ordinal);
    private readonly Part water;
    private GlVertexArray empty;
    private GlFramebuffer shadowFrame;private GlTexture shadowMap;
    private readonly record struct Part(GlVertexArray Array,GlBuffer Buffer,int Count,Vector3 Colour,string? Texture);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector3 Position,Vector3 Normal,Vector2 Uv);

    internal ToyRenderer(IReadOnlyDictionary<string,BedMesh> meshes,IReadOnlyDictionary<string,BedTexture> images)
    {
        scene=Program(SceneVertex,SceneFragment);sky=Program(SkyVertex,SkyFragment);depth=Program(DepthVertex,DepthFragment);
        foreach(var (id,image) in images)textures[id]=Texture(image);
        foreach(var (id,mesh) in meshes)models[id]=mesh.Parts.Select(Upload).ToList();
        // The sea: one large quad just under the island's top edge.
        const float s=120,y=-.32f;
        water=Upload([new(new(-s,y,-s),Vector3.UnitY,default),new(new(-s,y,s),Vector3.UnitY,default),new(new(s,y,s),Vector3.UnitY,default),
            new(new(-s,y,-s),Vector3.UnitY,default),new(new(s,y,s),Vector3.UnitY,default),new(new(s,y,-s),Vector3.UnitY,default)],new(.03f,.22f,.36f),null);
        GlVertexArray array;GL.glGenVertexArrays(1,&array);empty=array;
        GlTexture map;GL.glGenTextures(1,&map);shadowMap=map;GL.glBindTexture(OpenGl.GL_TEXTURE_2D,map.Id);
        GL.glTexImage2D(OpenGl.GL_TEXTURE_2D,0,(int)OpenGl.GL_DEPTH_COMPONENT24,ShadowSize,ShadowSize,0,OpenGl.GL_DEPTH_COMPONENT,OpenGl.GL_FLOAT,null);
        Parameters(OpenGl.GL_LINEAR,OpenGl.GL_CLAMP_TO_BORDER);var white=stackalloc float[]{1,1,1,1};GL.glTexParameterfv(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_BORDER_COLOR,white);
        GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_COMPARE_MODE,(int)OpenGl.GL_COMPARE_REF_TO_TEXTURE);GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_COMPARE_FUNC,(int)OpenGl.GL_LEQUAL);
        GlFramebuffer frame;GL.glGenFramebuffers(1,&frame);shadowFrame=frame;GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,frame);
        GL.glFramebufferTexture2D(OpenGl.GL_FRAMEBUFFER,OpenGl.GL_DEPTH_ATTACHMENT,OpenGl.GL_TEXTURE_2D,map.Id,0);GL.glDrawBuffer(OpenGl.GL_NONE);GL.glReadBuffer(OpenGl.GL_NONE);
        if(GL.glCheckFramebufferStatus(OpenGl.GL_FRAMEBUFFER).Id!=OpenGl.GL_FRAMEBUFFER_COMPLETE)throw new InvalidOperationException("The shadow framebuffer is incomplete.");
        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,default);
    }

    /// <summary>Draws one frame into the bound framebuffer (width x height pixels).</summary>
    internal void Draw(IEnumerable<(string Asset,Matrix4x4 World)> drawables,OrbitCamera camera,int width,int height,GlFramebuffer target)
    {
        var items=drawables.Where(d=>models.ContainsKey(d.Asset)).ToArray();
        // Shadow pass: an orthographic view along the sun covering the island and what sits on it.
        var light=Matrix4x4.CreateLookAt(Sun*20+new Vector3(0,.5f,0),new(0,.5f,0),Vector3.UnitY)*Matrix4x4.CreateOrthographic(17,17,1,45);
        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,shadowFrame);GL.glViewport(0,0,ShadowSize,ShadowSize);
        GL.glEnable(GlCapability.DepthTest);GL.glDepthMask(true);GL.glClear(OpenGl.GL_DEPTH_BUFFER_BIT);GL.glDisable(GlCapability.CullFace);
        GL.glEnable(GlCapability.PolygonOffsetFill);GL.glPolygonOffset(2,4);
        GL.glUseProgram(depth);var depthModel=Uniform(depth,"uModel"u8);Matrix(Uniform(depth,"uViewProj"u8),light);
        foreach(var (asset,world) in items){Matrix(depthModel,world);foreach(var part in models[asset])Draw(part);}
        GL.glDisable(GlCapability.PolygonOffsetFill);

        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER,target);GL.glViewport(0,0,width,height);
        GL.glClear(OpenGl.GL_COLOR_BUFFER_BIT|OpenGl.GL_DEPTH_BUFFER_BIT);
        var viewProj=camera.View*OrbitCamera.Projection(width/(float)height);
        // Sky: a full-screen triangle at the far plane, its gradient from the view direction.
        GL.glDepthMask(false);GL.glDisable(GlCapability.DepthTest);GL.glUseProgram(sky);
        Matrix4x4.Invert(viewProj,out var inverse);Matrix(Uniform(sky,"uInverse"u8),inverse);Vec(Uniform(sky,"uSky"u8),Sky);Vec(Uniform(sky,"uHorizon"u8),Horizon);Vec(Uniform(sky,"uSun"u8),Sun);
        GL.glBindVertexArray(empty);GL.glDrawArrays(OpenGl.GL_TRIANGLES,0,3);
        GL.glDepthMask(true);GL.glEnable(GlCapability.DepthTest);GL.glEnable(GlCapability.CullFace);GL.glCullFace(GlFace.Back);

        GL.glUseProgram(scene);
        Matrix(Uniform(scene,"uViewProj"u8),viewProj);Matrix(Uniform(scene,"uLight"u8),light);
        Vec(Uniform(scene,"uSun"u8),Sun);Vec(Uniform(scene,"uBaked"u8),BakedLight);Vec(Uniform(scene,"uEye"u8),camera.Eye);Vec(Uniform(scene,"uHorizon"u8),Horizon);
        GL.glUniform1i(Uniform(scene,"uTexture"u8),0);GL.glUniform1i(Uniform(scene,"uShadow"u8),1);
        GL.glActiveTexture(OpenGl.GL_TEXTURE1);GL.glBindTexture(OpenGl.GL_TEXTURE_2D,shadowMap.Id);GL.glActiveTexture(OpenGl.GL_TEXTURE0);
        int model=Uniform(scene,"uModel"u8),colour=Uniform(scene,"uColour"u8),textured=Uniform(scene,"uTextured"u8),sea=Uniform(scene,"uWater"u8);
        GL.glUniform1i(sea,0);
        foreach(var (asset,world) in items)
        {
            Matrix(model,world);
            foreach(var part in models[asset])
            {
                Vec(colour,part.Colour);GL.glUniform1i(textured,part.Texture is null?0:1);
                if(part.Texture is {} id&&textures.TryGetValue(id,out var texture))GL.glBindTexture(OpenGl.GL_TEXTURE_2D,texture.Id);
                Draw(part);
            }
        }
        Matrix(model,Matrix4x4.Identity);Vec(colour,water.Colour);GL.glUniform1i(textured,0);GL.glUniform1i(sea,1);Draw(water);
        GL.glBindVertexArray(default);
    }

    private static void Draw(Part part){GL.glBindVertexArray(part.Array);GL.glDrawArrays(OpenGl.GL_TRIANGLES,0,part.Count);}
    private static Part Upload(BedMeshPart part)
    {
        var g=part.Geometry;var vertices=new Vertex[g.Indices.Length];
        for(var i=0;i+2<g.Indices.Length;i+=3)
        {
            var (a,b,c)=(g.Indices[i],g.Indices[i+1],g.Indices[i+2]);var face=Vector3.Cross(g.Positions[b]-g.Positions[a],g.Positions[c]-g.Positions[a]);
            face=face.LengthSquared()>0?Vector3.Normalize(face):Vector3.UnitY;
            foreach(var (k,index) in new[]{(0,a),(1,b),(2,c)})
            {
                var uv=g.Uv0 is {} uvs?uvs[index]:default;if(g.Uv0Origin==BedMeshUv0Origin.ObjBottomLeft)uv.Y=1-uv.Y;
                vertices[i+k]=new(g.Positions[index],face,uv);
            }
        }
        var colour=part.Material?.BaseColor is {} c4?new Vector3(c4.X,c4.Y,c4.Z):new(.8f);
        return Upload(vertices,colour,part.Material?.Texture?.ToString("N"));
    }
    private static Part Upload(Vertex[] vertices,Vector3 colour,string? texture)
    {
        GlVertexArray array;GL.glGenVertexArrays(1,&array);GL.glBindVertexArray(array);
        GlBuffer buffer;GL.glGenBuffers(1,&buffer);GL.glBindBuffer(OpenGl.GL_ARRAY_BUFFER,buffer);
        fixed(Vertex* p=vertices)GL.glBufferData(OpenGl.GL_ARRAY_BUFFER,vertices.Length*sizeof(Vertex),p,OpenGl.GL_STATIC_DRAW);
        GL.glVertexAttribPointer(0,3,OpenGl.GL_FLOAT,false,sizeof(Vertex),null);GL.glEnableVertexAttribArray(0);
        GL.glVertexAttribPointer(1,3,OpenGl.GL_FLOAT,false,sizeof(Vertex),(void*)12);GL.glEnableVertexAttribArray(1);
        GL.glVertexAttribPointer(2,2,OpenGl.GL_FLOAT,false,sizeof(Vertex),(void*)24);GL.glEnableVertexAttribArray(2);
        GL.glBindVertexArray(default);return new(array,buffer,vertices.Length,colour,texture);
    }
    private static GlTexture Texture(BedTexture image)
    {
        GlTexture texture;GL.glGenTextures(1,&texture);GL.glBindTexture(OpenGl.GL_TEXTURE_2D,texture.Id);GL.glPixelStorei(OpenGl.GL_UNPACK_ALIGNMENT,1);
        fixed(byte* p=image.Rgba)GL.glTexImage2D(OpenGl.GL_TEXTURE_2D,0,(int)OpenGl.GL_RGBA8,image.Width,image.Height,0,OpenGl.GL_RGBA,OpenGl.GL_UNSIGNED_BYTE,p);
        // No mipmaps: each texture holds side-by-side tiles for different face directions, which mipmaps would blend.
        Parameters(OpenGl.GL_LINEAR,OpenGl.GL_CLAMP_TO_EDGE);return texture;
    }
    private static void Parameters(uint filter,uint wrap)
    {
        GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_MIN_FILTER,(int)filter);GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_MAG_FILTER,(int)filter);
        GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_WRAP_S,(int)wrap);GL.glTexParameteri(OpenGl.GL_TEXTURE_2D,OpenGl.GL_TEXTURE_WRAP_T,(int)wrap);
    }
    private static int Uniform(GlProgram program,ReadOnlySpan<byte> name){fixed(byte* p=name)return GL.glGetUniformLocation(program,p);}
    // System.Numerics matrices are row-vector; uploaded as-is, GLSL's column-major "M * v" applies them the same way.
    private static void Matrix(int location,Matrix4x4 m)=>GL.glUniformMatrix4fv(location,1,false,(float*)&m);
    private static void Vec(int location,Vector3 v)=>GL.glUniform3f(location,v.X,v.Y,v.Z);
    private static GlProgram Program(string vertex,string fragment)
    {
        var vs=Compile(GlShaderType.Vertex,vertex);var fs=Compile(GlShaderType.Fragment,fragment);var program=GL.glCreateProgram();
        GL.glAttachShader(program,vs);GL.glAttachShader(program,fs);GL.glLinkProgram(program);int linked;GL.glGetProgramiv(program,OpenGl.GL_LINK_STATUS,&linked);
        GL.glDeleteShader(vs);GL.glDeleteShader(fs);
        if(linked==0){var log=stackalloc byte[2048];int length;GL.glGetProgramInfoLog(program,2048,&length,log);throw new InvalidOperationException("Shader link failed: "+Encoding.UTF8.GetString(log,length));}
        return program;
    }
    private static GlShader Compile(GlShaderType type,string source)
    {
        var shader=GL.glCreateShader(type);var bytes=Encoding.UTF8.GetBytes(source);
        fixed(byte* p=bytes){var text=p;var length=bytes.Length;GL.glShaderSource(shader,1,&text,&length);}
        GL.glCompileShader(shader);int ok;GL.glGetShaderiv(shader,OpenGl.GL_COMPILE_STATUS,&ok);
        if(ok==0){var log=stackalloc byte[2048];int length;GL.glGetShaderInfoLog(shader,2048,&length,log);throw new InvalidOperationException($"{type} shader failed: "+Encoding.UTF8.GetString(log,length));}
        return shader;
    }
    public void Dispose()
    {
        foreach(var part in models.Values.SelectMany(p=>p).Append(water)){var a=part.Array;var b=part.Buffer;GL.glDeleteVertexArrays(1,&a);GL.glDeleteBuffers(1,&b);}
        foreach(var texture in textures.Values){var t=texture;GL.glDeleteTextures(1,&t);}
        var e=empty;GL.glDeleteVertexArrays(1,&e);var m=shadowMap;GL.glDeleteTextures(1,&m);var f=shadowFrame;GL.glDeleteFramebuffers(1,&f);
        GL.glDeleteProgram(scene);GL.glDeleteProgram(sky);GL.glDeleteProgram(depth);
    }

    private const string Common="""
        #version 410 core
        vec3 encode(vec3 c){c=clamp(c,0.0,1.0);return mix(12.92*c,1.055*pow(c,vec3(1.0/2.4))-0.055,greaterThan(c,vec3(0.0031308)));}
        vec3 decode(vec3 c){c=max(c,0.0);return mix(c/12.92,pow((c+0.055)/1.055,vec3(2.4)),greaterThan(c,vec3(0.04045)));}

        """;
    private const string SceneVertex="""
        #version 410 core
        layout(location=0) in vec3 aPosition;layout(location=1) in vec3 aNormal;layout(location=2) in vec2 aUv;
        uniform mat4 uModel,uViewProj,uLight;
        out vec3 vWorld,vNormal,vModelNormal;out vec2 vUv;out vec4 vShadow;
        void main(){vec4 world=uModel*vec4(aPosition,1.0);vWorld=world.xyz;vNormal=mat3(uModel)*aNormal;vModelNormal=aNormal;vUv=aUv;vShadow=uLight*world;gl_Position=uViewProj*world;}
        """;
    private const string SceneFragment=Common+"""
        in vec3 vWorld,vNormal,vModelNormal;in vec2 vUv;in vec4 vShadow;
        uniform sampler2D uTexture;uniform sampler2DShadow uShadow;
        uniform vec3 uColour,uSun,uBaked,uEye,uHorizon;uniform int uTextured,uWater;
        out vec4 colour;
        float lit(){
            vec3 p=vShadow.xyz/vShadow.w;p.xy=p.xy*0.5+0.5;p.z=p.z*0.5+0.5;float sum=0.0;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)sum+=texture(uShadow,vec3(p.xy+vec2(x,y)/2048.0,p.z));
            return sum/9.0;}
        void main(){
            vec3 n=normalize(vNormal);vec3 albedo=uColour;
            if(uTextured==1){
                // Undo the light level baked into this tile, found from the model-space face normal as the generator did.
                float level=floor((0.35+0.65*max(0.0,dot(normalize(vModelNormal),uBaked)))*40.0+0.5)/40.0;
                albedo*=decode(min(encode(texture(uTexture,vUv).rgb)/level,vec3(1.0)));}
            float sun=max(dot(n,uSun),0.0)*lit();
            vec3 ambient=mix(vec3(0.20,0.22,0.17),vec3(0.42,0.52,0.66),n.y*0.5+0.5);
            vec3 c=albedo*(ambient+vec3(1.05,0.98,0.88)*sun);
            vec3 view=normalize(uEye-vWorld);
            if(uWater==1){float fresnel=pow(1.0-max(dot(view,n),0.0),4.0);c=mix(c,decode(uHorizon),0.45*fresnel);c+=vec3(1.0,0.95,0.85)*pow(max(dot(reflect(-uSun,n),view),0.0),200.0)*lit();}
            else c+=albedo*0.12*pow(1.0-max(dot(view,n),0.0),3.0);
            float fog=smoothstep(28.0,90.0,length(vWorld-uEye));c=mix(c,decode(uHorizon),fog);
            colour=vec4(encode(c),1.0);}
        """;
    private const string SkyVertex="""
        #version 410 core
        out vec2 vNdc;
        void main(){vec2 p=vec2((gl_VertexID==1)?3.0:-1.0,(gl_VertexID==2)?3.0:-1.0);vNdc=p;gl_Position=vec4(p,0.0,1.0);}
        """;
    private const string SkyFragment=Common+"""
        in vec2 vNdc;uniform mat4 uInverse;uniform vec3 uSky,uHorizon,uSun;out vec4 colour;
        void main(){
            vec4 a=uInverse*vec4(vNdc,0.0,1.0);vec4 b=uInverse*vec4(vNdc,1.0,1.0);vec3 d=normalize(b.xyz/b.w-a.xyz/a.w);
            float up=clamp(d.y,0.0,1.0);vec3 c=mix(uHorizon,uSky,pow(up,0.55));
            c+=vec3(1.0,0.96,0.85)*pow(max(dot(d,uSun),0.0),64.0)*0.35;
            colour=vec4(c,1.0);}
        """;
    private const string DepthVertex="""
        #version 410 core
        layout(location=0) in vec3 aPosition;uniform mat4 uModel,uViewProj;
        void main(){gl_Position=uViewProj*uModel*vec4(aPosition,1.0);}
        """;
    private const string DepthFragment="""
        #version 410 core
        void main(){}
        """;
}
