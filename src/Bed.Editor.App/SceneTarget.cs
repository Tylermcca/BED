using Bed.Editor;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Bed.Editor.App;

// Complete attachment set; no descriptor or image escapes until later backend integration.
internal sealed unsafe class SceneTarget : IDisposable
{
    private readonly DeviceEx device;
    private ImageEx color, depth;
    private ImageViewEx colorView, depthView;
    private VkRenderPass pass;
    private VkFramebuffer framebuffer;
    private bool disposed;
    private SceneRenderRequest? request;
    internal IReadOnlyDictionary<(string Item,int Part),SceneMeshBuffer> Meshes {get;set;}=new Dictionary<(string Item,int Part),SceneMeshBuffer>();
    private Func<SceneRenderIdentity>? currentIdentity;
    private IReadOnlyDictionary<(string Guid,string Hash),SceneSampledMaterial>? materials;
    internal string MaterialStatus {get;private set;}="";
    internal void ResetRequest() { request=null; currentIdentity=null;materials=null;MaterialStatus=""; }
    internal bool TryTexture(SceneRenderRequest expected, out ImTextureRef texture)
    {
        texture=default;
        if(disposed || Texture._TexID==0 || !ReferenceEquals(request,expected) || expected.Identity != currentIdentity!())return false;
        texture=Texture;return true;
    }
    internal void Use(SceneRenderRequest value, Func<SceneRenderIdentity> current,IReadOnlyDictionary<(string Guid,string Hash),SceneSampledMaterial>? bindings=null,string materialStatus="")
    {
        request=value; currentIdentity=current;materials=bindings;MaterialStatus=materialStatus;
    }
    private SceneTrianglePipeline? triangle;
    private VulkanBackend? backend;
    private VkSampler sampler;
    internal ImTextureRef Texture { get; private set; }
    internal int Width { get; }
    internal int Height { get; }
    internal VkRenderPass Pass => pass;
    internal VkFramebuffer Framebuffer => framebuffer;
    internal SceneTarget(DeviceEx device, PhysicalDevice physical, VulkanBackend backend, int width, int height)
    {
        this.device=device;this.backend=backend;Width=width;Height=height;
        var limit=Math.Min(2048,physical.GetProperties().Limits.MaxImageDimension2D);
        if(width<=0||height<=0||width>limit||height>limit) throw new ArgumentOutOfRangeException(nameof(width),"Target exceeds the initial 2048/device bound.");
        var cf=physical.FindSupportedFormat([VkFormat.R8G8B8A8UNorm],VkImageTiling.Optimal,VkFormatFeatureFlags.ColorAttachmentBit|VkFormatFeatureFlags.SampledImageBit);
        var df=physical.FindSupportedFormat([VkFormat.D32SFloat,VkFormat.D24UNormS8UInt,VkFormat.D32SFloatS8UInt],VkImageTiling.Optimal,VkFormatFeatureFlags.DepthStencilAttachmentBit);
        if(cf==VkFormat.Undefined||df==VkFormat.Undefined) throw new InvalidOperationException("No supported scene color/depth format.");
        try
        {
            color=Allocate(cf,VkImageUsageFlags.ColorAttachmentBit|VkImageUsageFlags.SampledBit,"BED scene color");
            depth=Allocate(df,VkImageUsageFlags.DepthStencilAttachmentBit,"BED scene depth");
            colorView=color.CreateImageView(VkImageViewType._2D,color.GetFullRange(VkImageAspectFlags.ColorBit),null);
            var aspect=VkImageAspectFlags.DepthBit;if(df!=VkFormat.D32SFloat) aspect|=VkImageAspectFlags.StencilBit;
            depthView=depth.CreateImageView(VkImageViewType._2D,depth.GetFullRange(aspect),null);
            VkAttachmentDescription* attachments=stackalloc VkAttachmentDescription[2];
            attachments[0]=new(){Format=cf,Samples=VkSampleCountFlags._1Bit,LoadOp=VkAttachmentLoadOp.Clear,StoreOp=VkAttachmentStoreOp.Store,
                StencilLoadOp=VkAttachmentLoadOp.DontCare,StencilStoreOp=VkAttachmentStoreOp.DontCare,InitialLayout=VkImageLayout.Undefined,FinalLayout=VkImageLayout.ShaderReadOnlyOptimal};
            attachments[1]=new(){Format=df,Samples=VkSampleCountFlags._1Bit,LoadOp=VkAttachmentLoadOp.Clear,StoreOp=VkAttachmentStoreOp.DontCare,
                StencilLoadOp=VkAttachmentLoadOp.DontCare,StencilStoreOp=VkAttachmentStoreOp.DontCare,InitialLayout=VkImageLayout.Undefined,FinalLayout=VkImageLayout.DepthStencilAttachmentOptimal};
            var c=new VkAttachmentReference{Attachment=0,Layout=VkImageLayout.ColorAttachmentOptimal};
            var d=new VkAttachmentReference{Attachment=1,Layout=VkImageLayout.DepthStencilAttachmentOptimal};
            var sub=new VkSubpassDescription{PipelineBindPoint=VkPipelineBindPoint.Graphics,ColorAttachmentCount=1,ColorAttachments=&c,DepthStencilAttachment=&d};
            VkSubpassDependency* deps=stackalloc VkSubpassDependency[2];
            deps[0]=new(){SrcSubpass=VK.SUBPASS_EXTERNAL,DstSubpass=0,SrcStageMask=VkPipelineStageFlags.FragmentShaderBit,
                DstStageMask=VkPipelineStageFlags.ColorAttachmentOutputBit|VkPipelineStageFlags.EarlyFragmentTestsBit,SrcAccessMask=VkAccessFlags.ShaderReadBit,
                DstAccessMask=VkAccessFlags.ColorAttachmentWriteBit|VkAccessFlags.DepthStencilAttachmentWriteBit};
            deps[1]=new(){SrcSubpass=0,DstSubpass=VK.SUBPASS_EXTERNAL,SrcStageMask=VkPipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask=VkPipelineStageFlags.FragmentShaderBit,SrcAccessMask=VkAccessFlags.ColorAttachmentWriteBit,DstAccessMask=VkAccessFlags.ShaderReadBit};
            pass=device.CreateRenderPass(new VkRenderPassCreateInfo{AttachmentCount=2,Attachments=attachments,SubpassCount=1,Subpasses=&sub,DependencyCount=2,Dependencies=deps},null);
            VkImageView* views=stackalloc VkImageView[2]{colorView.VkImageView,depthView.VkImageView};
            framebuffer=device.CreateFramebuffer(new VkFramebufferCreateInfo{RenderPass=pass,AttachmentCount=2,Attachments=views,Width=width,Height=height,Layers=1},null);
            triangle=new(device,pass);
            sampler=device.CreateSampler(new VkSamplerCreateInfo{MagFilter=VkFilter.Linear,MinFilter=VkFilter.Linear,MipmapMode=VkSamplerMipmapMode.Nearest,AddressModeU=VkSamplerAddressMode.ClampToEdge,AddressModeV=VkSamplerAddressMode.ClampToEdge,AddressModeW=VkSamplerAddressMode.ClampToEdge,MaxLod=0},null);
            Texture=backend.AddTexture(sampler,colorView.VkImageView,VkImageLayout.ShaderReadOnlyOptimal);
        }
        catch { Dispose();throw; }
    }
    private ImageEx Allocate(VkFormat format,VkImageUsageFlags usage,string name)=>device.CreateImage(new ImageEx.CreateInfo
    {Name=name,ImageType=VkImageType._2D,ImageFormat=format,ImageExtent=new VkExtent3D(Width,Height,1),ImageMipLevels=1,ImageArrayLayers=1,
        ImageSamples=VkSampleCountFlags._1Bit,ImageTiling=VkImageTiling.Optimal,ImageUsage=usage,ImageSharingMode=VkSharingMode.Exclusive,
        ImageInitialLayout=VkImageLayout.Undefined,AllocPreference=MemoryPreference.PreferGpu});
    internal void Record(CommandBuffer command)
    {
        VkClearValue* clear=stackalloc VkClearValue[2];
        clear[0]=new VkClearColorValue();clear[1]=new VkClearDepthStencilValue{Depth=1,Stencil=0};
        command.BeginRenderPass(new VkRenderPassBeginInfo{RenderPass=pass,Framebuffer=framebuffer,RenderArea=new VkRect2D(new VkExtent2D(Width,Height)),ClearValueCount=2,ClearValues=clear},VkSubpassContents.Inline);
        command.SetViewport(0,[new VkViewport{Width=Width,Height=Height,MinDepth=0,MaxDepth=1}]);
        command.SetScissor(0,[new VkRect2D(new VkExtent2D(Width,Height))]);if(request is null || request.Identity == currentIdentity!()) triangle!.Draw(command,request,Meshes,materials);
        command.EndRenderPass();
    }
    // Caller must establish fence safety before disposing or replacing this attachment set.
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(Texture._TexID!=0){backend!.RemoveTexture(Texture);Texture=default;}
        if(sampler.IsNotNull())device.DestroySampler(sampler,null);triangle?.Dispose();
        if(framebuffer.IsNotNull())device.DestroyFramebuffer(framebuffer,null);if(pass.IsNotNull())device.DestroyRenderPass(pass,null);
        if(!depthView.IsNull())depthView.Dispose();if(!colorView.IsNull())colorView.Dispose();
        if(!depth.IsNull())depth.Dispose();if(!color.IsNull())color.Dispose();
    }
}


