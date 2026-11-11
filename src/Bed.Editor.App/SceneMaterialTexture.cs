using Bed.Editor.ImGui;


using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Bed.Editor.App;

// Immutable RGBA8 upload on the existing graphics queue; never submits or waits independently.
internal sealed class SceneMaterialTexture : IDisposable
{
    private readonly DeviceEx device;
    private readonly SceneTextureBudget.Reservation charge;
    private ImageEx image;
    private ImageViewEx view;
    private BufferEx staging;

    private bool uploaded, disposed;
    private SceneMaterialDescriptor? descriptor;
    internal VkDescriptorSet Descriptor => descriptor?.Set ?? throw new ObjectDisposedException(nameof(SceneMaterialTexture));
    internal VkImageView View => view.VkImageView;
    private readonly int width,height;
    internal SceneMaterialTexture(DeviceEx device, PhysicalDevice physical,SceneTextureBudget budget,AssetPreview pixels)
    {
        this.device=device;width=pixels.Width;height=pixels.Height;
        // Retain the reviewed initial native texture cap; never resample implicitly.
        if(width>2048||height>2048||width>physical.GetProperties().Limits.MaxImageDimension2D||height>physical.GetProperties().Limits.MaxImageDimension2D)
            throw new InvalidOperationException("Material texture exceeds the initial 2048/device dimension budget.");
        var format=physical.FindSupportedFormat([VkFormat.R8G8B8A8SRGB],VkImageTiling.Optimal,VkFormatFeatureFlags.SampledImageBit|VkFormatFeatureFlags.SampledImageFilterLinearBit|VkFormatFeatureFlags.TransferDstBit);
        if(format==VkFormat.Undefined)throw new InvalidOperationException("No supported sRGB RGBA8 material texture format.");
        charge=budget.Reserve(pixels.ByteCount,pixels.ByteCount);
        try
        {
            staging=device.CreateBuffer(new BufferEx.CreateInfo{Name="BED material staging",BufferSize=(Brutal.ByteSize)pixels.ByteCount,
                BufferUsage=VkBufferUsageFlags.TransferSrcBit,BufferSharingMode=VkSharingMode.Exclusive,
                AllocPreference=MemoryPreference.PreferCpu,AllocRequiredProperties=VkMemoryPropertyFlags.HostVisibleBit|VkMemoryPropertyFlags.HostCoherentBit});
            using(var map=staging.Map())pixels.CopyTo(map.AsSpan());
            image=device.CreateImage(new ImageEx.CreateInfo{Name="BED material RGBA",ImageType=VkImageType._2D,ImageFormat=format,
                ImageExtent=new VkExtent3D(width,height,1),ImageMipLevels=1,ImageArrayLayers=1,ImageSamples=VkSampleCountFlags._1Bit,
                ImageTiling=VkImageTiling.Optimal,ImageUsage=VkImageUsageFlags.TransferDstBit|VkImageUsageFlags.SampledBit,
                ImageSharingMode=VkSharingMode.Exclusive,ImageInitialLayout=VkImageLayout.Undefined,AllocPreference=MemoryPreference.PreferGpu});
            view=image.CreateImageView(VkImageViewType._2D,image.GetFullRange(VkImageAspectFlags.ColorBit),null);
            descriptor=new(device,view.VkImageView);

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
    // Host calls only after successful queue submission; no independent submit or wait.
    internal void Submitted()=>uploaded=true;
    // Host must prove the upload submission fence completed before releasing its staging buffer.
    internal void UploadCompleted()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!uploaded)throw new InvalidOperationException("Material upload has not been submitted.");
        if(!staging.IsNull()){staging.Dispose();staging=default;}charge.ReleaseStaging();
    }
    // Only after every sampling frame is fence-safe, or before any submission on constructor failure.
    public void Dispose()
    {
        if(disposed)return;disposed=true;


        descriptor?.Dispose();descriptor=null;
        if(!view.IsNull())view.Dispose();if(!image.IsNull())image.Dispose();if(!staging.IsNull())staging.Dispose();charge.Dispose();
    }
}
