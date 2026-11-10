using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Brutal.Authoring.Editor.App;
internal sealed class SceneMeshBuffer : IDisposable
{
    private BufferEx vertices,indices;
    internal readonly int Count;
    internal SceneMeshBuffer(DeviceEx device,SceneIndexedGeometry geometry)
    {
        Count=geometry.IndexCount;
        try
        {
            vertices=Create(device,geometry.VertexBytes,VkBufferUsageFlags.VertexBufferBit,"BED mesh vertices");
            indices=Create(device,geometry.IndexBytes,VkBufferUsageFlags.IndexBufferBit,"BED mesh indices");
        }
        catch{Dispose();throw;}
    }
    private static BufferEx Create(DeviceEx device,ReadOnlySpan<byte> bytes,VkBufferUsageFlags usage,string name)
    {
        var result=device.CreateBuffer(new BufferEx.CreateInfo{Name=name,BufferSize=(ByteSize)Math.Max(1,bytes.Length),BufferUsage=usage,BufferSharingMode=VkSharingMode.Exclusive,
            AllocPreference=MemoryPreference.PreferCpu,AllocRequiredProperties=VkMemoryPropertyFlags.HostVisibleBit|VkMemoryPropertyFlags.HostCoherentBit});
        try{using(var map=result.Map())bytes.CopyTo(map.AsSpan());return result;}catch{result.Dispose();throw;}
    }
    internal void Draw(CommandBuffer command)
    {command.BindVertexBuffers(0,[vertices.VkBuffer],[(ByteSize64)0]);command.BindIndexBuffer(indices.VkBuffer,(ByteSize64)0,VkIndexType.UInt32);command.DrawIndexed(Count,1,0,0,0);}
    public void Dispose(){if(!indices.IsNull()){indices.Dispose();indices=default;}if(!vertices.IsNull()){vertices.Dispose();vertices=default;}}
}
