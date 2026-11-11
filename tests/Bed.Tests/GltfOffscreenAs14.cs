using System.Collections;
using System.Numerics;
using System.Reflection;
using Brutal;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Xunit;
namespace Bed.Tests;

// A tiny real native draw/readback. No GLFW, surface, swapchain, app window or input hook.
// Production mesh buffers, material upload/descriptor and compiled triangle pipeline are invoked unchanged.
internal static unsafe class GltfOffscreenAs14
{
    internal static byte[] Draw(SceneRenderRequest request,AssetPreview pixels)
    {
        using var instance=Vulkan.CreateInstance(new Instance.CreateInfo{AppInfo=new VkApplicationInfo{ApplicationName="BED offscreen fixture"u8.AsPointer(),EngineName="BED"u8.AsPointer(),ApiVersion=(int)VulkanHelpers.Api.VERSION_1_3},EnabledExtensions=[],EnabledLayers=[]});
        var physical=instance.PhysicalDevices.First(candidate=>candidate.GetProperties().ApiVersion>=(int)VulkanHelpers.Api.VERSION_1_3);var families=new VkQueueFamilyProperties[physical.GetQueueFamilyProperties_Count()];physical.GetQueueFamilyProperties_Write(families);
        var family=Array.FindIndex(families,f=>f.QueueFlags.HasFlag(VkQueueFlags.GraphicsBit));Assert.True(family>=0);
        var priority=1f;var features=new VkPhysicalDeviceFeatures();
        using var device=physical.CreateDevice(new DeviceEx.CreateInfo{Extensions=[],Features=&features,QueueCreateInfos=[new VkDeviceQueueCreateInfo{QueueFamilyIndex=family,QueueCount=1,QueuePriorities=&priority}]});
        var queue=device.GetQueue(family,0);const int width=128,height=128;
        using var image=device.CreateImage(new ImageEx.CreateInfo{Name="BED offscreen fixture color",ImageType=VkImageType._2D,ImageFormat=VkFormat.R8G8B8A8UNorm,
            ImageExtent=new(width,height,1),ImageMipLevels=1,ImageArrayLayers=1,ImageSamples=VkSampleCountFlags._1Bit,ImageTiling=VkImageTiling.Optimal,
            ImageUsage=VkImageUsageFlags.ColorAttachmentBit|VkImageUsageFlags.TransferSrcBit,ImageSharingMode=VkSharingMode.Exclusive,ImageInitialLayout=VkImageLayout.Undefined,AllocPreference=MemoryPreference.PreferGpu});
        using var view=image.CreateImageView(VkImageViewType._2D,image.GetFullRange(VkImageAspectFlags.ColorBit),null);
        using var readback=device.CreateBuffer(new BufferEx.CreateInfo{Name="BED offscreen fixture readback",BufferSize=(ByteSize)(width*height*4),BufferUsage=VkBufferUsageFlags.TransferDstBit,
            BufferSharingMode=VkSharingMode.Exclusive,AllocPreference=MemoryPreference.PreferCpu,AllocRequiredProperties=VkMemoryPropertyFlags.HostVisibleBit|VkMemoryPropertyFlags.HostCoherentBit});
        var attachment=new VkAttachmentDescription{Format=VkFormat.R8G8B8A8UNorm,Samples=VkSampleCountFlags._1Bit,LoadOp=VkAttachmentLoadOp.Clear,StoreOp=VkAttachmentStoreOp.Store,
            StencilLoadOp=VkAttachmentLoadOp.DontCare,StencilStoreOp=VkAttachmentStoreOp.DontCare,InitialLayout=VkImageLayout.Undefined,FinalLayout=VkImageLayout.TransferSrcOptimal};
        var reference=new VkAttachmentReference{Attachment=0,Layout=VkImageLayout.ColorAttachmentOptimal};
        var subpass=new VkSubpassDescription{PipelineBindPoint=VkPipelineBindPoint.Graphics,ColorAttachmentCount=1,ColorAttachments=&reference};
        var dependency=new VkSubpassDependency{SrcSubpass=0,DstSubpass=VK.SUBPASS_EXTERNAL,SrcStageMask=VkPipelineStageFlags.ColorAttachmentOutputBit,DstStageMask=VkPipelineStageFlags.TransferBit,
            SrcAccessMask=VkAccessFlags.ColorAttachmentWriteBit,DstAccessMask=VkAccessFlags.TransferReadBit};
        var pass=device.CreateRenderPass(new VkRenderPassCreateInfo{AttachmentCount=1,Attachments=&attachment,SubpassCount=1,Subpasses=&subpass,DependencyCount=1,Dependencies=&dependency},null);
        VkFramebuffer framebuffer=default;VkCommandPool pool=default;VkFence fence=default;
        var resources=new List<IDisposable>();
        try
        {
            var nativeView=view.VkImageView;framebuffer=device.CreateFramebuffer(new VkFramebufferCreateInfo{RenderPass=pass,AttachmentCount=1,Attachments=&nativeView,Width=width,Height=height,Layers=1},null);
            var assembly=typeof(EditorApp).Assembly;
            object Create(string name,params object[] args)
            {var value=Activator.CreateInstance(assembly.GetType("Bed.Editor.App."+name,true)!,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null)!;if(value is IDisposable disposable)resources.Add(disposable);return value;}
            void Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,args);
            var pipeline=Create("SceneTrianglePipeline",device,pass);var budget=Create("SceneTextureBudget");var texture=Create("SceneMaterialTexture",device,physical,budget,pixels);
            var bufferType=assembly.GetType("Bed.Editor.App.SceneMeshBuffer",true)!;
            var meshes=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof((string,int)),bufferType))!;
            foreach(var item in request.Items)for(var part=0;part<item.Payloads.Count;part++)meshes.Add((item.Id,part),Create("SceneMeshBuffer",device,new SceneIndexedGeometry(item.Payloads[part])));
            var materialType=assembly.GetType("Bed.Editor.App.SceneSampledMaterial",true)!;
            var materials=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof((string,string)),materialType))!;
            var material=Create("SceneSampledMaterial",texture,(Func<bool>)(()=>true));
            var declaration=Assert.Single(Assert.Single(request.Items).Payloads).Material;materials.Add((declaration.TextureAssetGuid!,declaration.TextureSourceHash!),material);
            pool=device.CreateCommandPool(new VkCommandPoolCreateInfo{QueueFamilyIndex=family},null);
            var command=device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo{CommandPool=pool,Level=VkCommandBufferLevel.Primary});command.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            Call(texture,"Record",command);VkClearValue clear=new VkClearColorValue();
            command.BeginRenderPass(new VkRenderPassBeginInfo{RenderPass=pass,Framebuffer=framebuffer,RenderArea=new VkRect2D(new VkExtent2D(width,height)),ClearValueCount=1,ClearValues=&clear},VkSubpassContents.Inline);
            command.SetViewport(0,[new VkViewport{Width=width,Height=height,MinDepth=0,MaxDepth=1}]);command.SetScissor(0,[new VkRect2D(new VkExtent2D(width,height))]);
            Call(pipeline,"Draw",command,request,meshes,materials);command.EndRenderPass();
            command.CopyImageToBuffer(image.VkImage,VkImageLayout.TransferSrcOptimal,readback.VkBuffer,[new VkBufferImageCopy{BufferOffset=readback.BindOffset,ImageSubresource=new VkImageSubresourceLayers{AspectMask=VkImageAspectFlags.ColorBit,LayerCount=1},ImageExtent=new(width,height,1)}]);
            command.PipelineBarrier(VkPipelineStageFlags.TransferBit,VkPipelineStageFlags.HostBit,VkDependencyFlags.None,[],[new VkBufferMemoryBarrier{SrcAccessMask=VkAccessFlags.TransferWriteBit,DstAccessMask=VkAccessFlags.HostReadBit,
                SrcQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,DstQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,Buffer=readback.VkBuffer,Offset=readback.BindOffset,Size=readback.BufferSize}],[]);
            command.End();fence=device.CreateFence(VkFenceCreateFlags.None,null);queue.Submit([],[],[command],[],fence);Call(texture,"Submitted");device.WaitForFences([fence],true,2_000_000_000);Call(texture,"UploadCompleted");
            using var mapped=readback.Map();return mapped.AsSpan().Slice(0,width*height*4).ToArray();
        }
        finally
        {
            device.WaitIdle();for(var i=resources.Count-1;i>=0;i--)resources[i].Dispose();
            if(fence.IsNotNull())device.DestroyFence(fence,null);if(pool.IsNotNull())device.DestroyCommandPool(pool,null);if(framebuffer.IsNotNull())device.DestroyFramebuffer(framebuffer,null);device.DestroyRenderPass(pass,null);
        }
    }
}
