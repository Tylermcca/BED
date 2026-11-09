using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Brutal.Authoring.Editor.App;

// One immutable descriptor per shared image, reused across views. Cache retirement owns fence safety.
internal sealed unsafe class SceneMaterialDescriptor:IDisposable
{
    private readonly DeviceEx device;
    private DescriptorSetLayoutEx? layout;private DescriptorPoolEx? pool;private VkSampler sampler;private bool disposed;
    internal VkDescriptorSet Set {get;private set;}
    internal static DescriptorSetLayoutEx CreateLayout(DeviceEx device)=>device.CreateDescriptorSetLayout(
        new DescriptorSetLayoutEx.CreateInfo{Bindings=[new VkDescriptorSetLayoutBinding{Binding=0,DescriptorCount=1,
            DescriptorType=VkDescriptorType.CombinedImageSampler,StageFlags=VkShaderStageFlags.FragmentBit}]},null);
    internal SceneMaterialDescriptor(DeviceEx device,VkImageView image)
    {
        this.device=device;
        try
        {
            layout=CreateLayout(device);
            pool=device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo{MaxSets=1,
                PoolSizes=[new VkDescriptorPoolSize{Type=VkDescriptorType.CombinedImageSampler,DescriptorCount=1}]},null);
            sampler=device.CreateSampler(new VkSamplerCreateInfo{MagFilter=VkFilter.Linear,MinFilter=VkFilter.Linear,MipmapMode=VkSamplerMipmapMode.Nearest,
                AddressModeU=VkSamplerAddressMode.ClampToEdge,AddressModeV=VkSamplerAddressMode.ClampToEdge,AddressModeW=VkSamplerAddressMode.ClampToEdge,MaxLod=0},null);
            Set=device.AllocateDescriptorSet(pool,layout);
            var info=new VkDescriptorImageInfo{Sampler=sampler,ImageView=image,ImageLayout=VkImageLayout.ShaderReadOnlyOptimal};
            device.UpdateDescriptorSets([new VkWriteDescriptorSet{DstSet=Set,DstBinding=0,DescriptorCount=1,
                DescriptorType=VkDescriptorType.CombinedImageSampler,ImageInfo=&info}],[]);
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        pool?.Dispose();pool=null;Set=default;
        if(sampler.IsNotNull()){device.DestroySampler(sampler,null);sampler=default;}
        layout?.Dispose();layout=null;
    }
}
internal sealed record SceneSampledMaterial(SceneMaterialTexture Texture,Func<bool> IsCurrent);
