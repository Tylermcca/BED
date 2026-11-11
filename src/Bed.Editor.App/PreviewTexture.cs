using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Bed.Editor.App;

// Immutable RGBA8 upload on the existing graphics queue; never submits or waits independently.
internal sealed class PreviewTexture : IDisposable
{
    private readonly DeviceEx device;
    private readonly VulkanBackend backend;
    private readonly SceneTextureBudget.Reservation? charge;
    private ImageEx image;
    private ImageViewEx view;
    private BufferEx staging;
    private VkSampler sampler;
    private bool uploaded, disposed;
    internal ImTextureRef Texture { get; private set; }
    private readonly int width,height;
    internal PreviewTexture(DeviceEx device, PhysicalDevice physical,VulkanBackend backend,AssetPreview pixels,SceneSpriteFilter filter=SceneSpriteFilter.Linear,SceneTextureBudget? budget=null,int maxDimension=2048)
    {
        this.device=device;this.backend=backend;width=pixels.Width;height=pixels.Height;
        if(!Enum.IsDefined(filter)||maxDimension<1||maxDimension>8192)throw new ArgumentException("Unsupported preview filter or dimension bound.");
        // Ordinary previews retain 2048; admitted sprites explicitly request the saved-artifact/device bound.
        if(width>maxDimension||height>maxDimension||width>physical.GetProperties().Limits.MaxImageDimension2D||height>physical.GetProperties().Limits.MaxImageDimension2D)
            throw new InvalidOperationException("Native preview exceeds its declared/device dimension budget.");
        if(budget is not null&&16L+pixels.ByteCount>Bed.Runtime.BedTextureReader.MaxBytes)throw new InvalidOperationException("Sprite exceeds the saved BEDTEX artifact bound.");
        var features=VkFormatFeatureFlags.SampledImageBit|VkFormatFeatureFlags.TransferDstBit;
        if(filter==SceneSpriteFilter.Linear)features|=VkFormatFeatureFlags.SampledImageFilterLinearBit;
        var format=physical.FindSupportedFormat([VkFormat.R8G8B8A8UNorm],VkImageTiling.Optimal,features);
        if(format==VkFormat.Undefined)throw new InvalidOperationException("No supported RGBA8 preview format.");
        charge=budget?.Reserve(pixels.ByteCount,pixels.ByteCount);
        try
        {
            staging=device.CreateBuffer(new BufferEx.CreateInfo{Name="BED preview staging",BufferSize=(Brutal.ByteSize)pixels.ByteCount,
                BufferUsage=VkBufferUsageFlags.TransferSrcBit,BufferSharingMode=VkSharingMode.Exclusive,
                AllocPreference=MemoryPreference.PreferCpu,AllocRequiredProperties=VkMemoryPropertyFlags.HostVisibleBit|VkMemoryPropertyFlags.HostCoherentBit});
            using(var map=staging.Map())pixels.CopyTo(map.AsSpan());
            image=device.CreateImage(new ImageEx.CreateInfo{Name="BED preview RGBA",ImageType=VkImageType._2D,ImageFormat=format,
                ImageExtent=new VkExtent3D(width,height,1),ImageMipLevels=1,ImageArrayLayers=1,ImageSamples=VkSampleCountFlags._1Bit,
                ImageTiling=VkImageTiling.Optimal,ImageUsage=VkImageUsageFlags.TransferDstBit|VkImageUsageFlags.SampledBit,
                ImageSharingMode=VkSharingMode.Exclusive,ImageInitialLayout=VkImageLayout.Undefined,AllocPreference=MemoryPreference.PreferGpu});
            view=image.CreateImageView(VkImageViewType._2D,image.GetFullRange(VkImageAspectFlags.ColorBit),null);
            sampler=device.CreateSampler(new VkSamplerCreateInfo{MagFilter=filter==SceneSpriteFilter.Nearest?VkFilter.Nearest:VkFilter.Linear,MinFilter=filter==SceneSpriteFilter.Nearest?VkFilter.Nearest:VkFilter.Linear,MipmapMode=VkSamplerMipmapMode.Nearest,
                AddressModeU=VkSamplerAddressMode.ClampToEdge,AddressModeV=VkSamplerAddressMode.ClampToEdge,AddressModeW=VkSamplerAddressMode.ClampToEdge,MaxLod=0},null);
            Texture=backend.AddTexture(sampler,view.VkImageView,VkImageLayout.ShaderReadOnlyOptimal);
        }
        catch {Dispose();throw;}
    }
    internal void Record(CommandBuffer command)
    {
        ObjectDisposedException.ThrowIf(disposed,this);if(uploaded)return;
        var range=image.GetFullRange(VkImageAspectFlags.ColorBit);
        command.PipelineBarrier(VkPipelineStageFlags.HostBit,VkPipelineStageFlags.TransferBit,VkDependencyFlags.None,[],
            [new VkBufferMemoryBarrier{SrcAccessMask=VkAccessFlags.HostWriteBit,DstAccessMask=VkAccessFlags.TransferReadBit,
                SrcQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,DstQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,Buffer=staging.VkBuffer,Offset=staging.BindOffset,Size=staging.BufferSize}],
            [new VkImageMemoryBarrier{DstAccessMask=VkAccessFlags.TransferWriteBit,OldLayout=VkImageLayout.Undefined,NewLayout=VkImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,DstQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,Image=image.VkImage,SubresourceRange=range}]);
        command.CopyBufferToImage(staging.VkBuffer,image.VkImage,VkImageLayout.TransferDstOptimal,
            [new VkBufferImageCopy{BufferOffset=staging.BindOffset,ImageSubresource=new VkImageSubresourceLayers{AspectMask=VkImageAspectFlags.ColorBit,LayerCount=1},ImageExtent=new VkExtent3D(width,height,1)}]);
        command.PipelineBarrier(VkPipelineStageFlags.TransferBit,VkPipelineStageFlags.FragmentShaderBit,VkDependencyFlags.None,[],[],
            [new VkImageMemoryBarrier{SrcAccessMask=VkAccessFlags.TransferWriteBit,DstAccessMask=VkAccessFlags.ShaderReadBit,
                OldLayout=VkImageLayout.TransferDstOptimal,NewLayout=VkImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,DstQueueFamilyIndex=VK.QUEUE_FAMILY_IGNORED,Image=image.VkImage,SubresourceRange=range}]);
    }
    // Called only after successful host queue submission. Staging remains alive until the slot is fence-safe.
    internal void Submitted()=>uploaded=true;
    internal void UploadCompleted()
    {
        ObjectDisposedException.ThrowIf(disposed,this);if(!uploaded)throw new InvalidOperationException("Preview upload has not been submitted.");
        if(!staging.IsNull()){staging.Dispose();staging=default;}charge?.ReleaseStaging();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(Texture._TexID!=0){backend.RemoveTexture(Texture);Texture=default;}
        if(sampler.IsNotNull())device.DestroySampler(sampler,null);
        if(!view.IsNull())view.Dispose();if(!image.IsNull())image.Dispose();if(!staging.IsNull())staging.Dispose();charge?.Dispose();
    }
}
