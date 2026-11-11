using Brutal;
using System.Numerics;
using Bed.Editor;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Bed.Editor.App;

// Opaque/unlit packets. Flat/placeholder pipeline needs no dummy texture or hidden unbounded descriptor.
internal sealed unsafe class SceneTrianglePipeline : IDisposable
{
    private readonly DeviceEx device;
    private VkPipelineLayout layout,texturedLayout;
    private VkPipeline pipeline,texturedPipeline;
    private DescriptorSetLayoutEx? textureLayout;
    internal SceneTrianglePipeline(DeviceEx device,VkRenderPass pass)
    {
        this.device=device;VkShaderModule vertex=default,fragment=default,textured=default;
        try
        {
            vertex=ShaderModuleExtensions.FromSpirv(device,Read("BED.Scene.vert.spv"),null);
            fragment=ShaderModuleExtensions.FromSpirv(device,Read("BED.Scene.frag.spv"),null);
            textured=ShaderModuleExtensions.FromSpirv(device,Read("BED.Scene.textured.frag.spv"),null);
            var push=new VkPushConstantRange{StageFlags=VkShaderStageFlags.VertexBit,Offset=(ByteSize32)0,Size=(ByteSize32)96};
            layout=device.CreatePipelineLayout([],[push],null);
            textureLayout=SceneMaterialDescriptor.CreateLayout(device);
            texturedLayout=device.CreatePipelineLayout([textureLayout.Handle],[push],null);
            pipeline=Create(vertex,fragment,pass,layout);texturedPipeline=Create(vertex,textured,pass,texturedLayout);
        }
        catch{Dispose();throw;}
        finally{if(vertex.IsNotNull())device.DestroyShaderModule(vertex,null);if(fragment.IsNotNull())device.DestroyShaderModule(fragment,null);if(textured.IsNotNull())device.DestroyShaderModule(textured,null);}
    }
    private VkPipeline Create(VkShaderModule vertex,VkShaderModule fragment,VkRenderPass pass,VkPipelineLayout targetLayout)
    {
        fixed(byte* entry="main"u8)
            {
                VkPipelineShaderStageCreateInfo* stages=stackalloc VkPipelineShaderStageCreateInfo[2];
                stages[0]=new(){Stage=VkShaderStageFlags.VertexBit,Module=vertex,Name=entry};
                stages[1]=new(){Stage=VkShaderStageFlags.FragmentBit,Module=fragment,Name=entry};
                var binding=new VkVertexInputBindingDescription{Binding=0,Stride=(ByteSize32)20,InputRate=VkVertexInputRate.Vertex};
                VkVertexInputAttributeDescription* attributes=stackalloc VkVertexInputAttributeDescription[2];
                attributes[0]=new(){Location=0,Binding=0,Format=VkFormat.R32G32B32SFloat,Offset=(ByteSize32)0};
                attributes[1]=new(){Location=1,Binding=0,Format=VkFormat.R32G32SFloat,Offset=(ByteSize32)12};
                var vertices=new VkPipelineVertexInputStateCreateInfo{VertexBindingDescriptionCount=1,VertexBindingDescriptions=&binding,VertexAttributeDescriptionCount=2,VertexAttributeDescriptions=attributes};
                var assembly=new VkPipelineInputAssemblyStateCreateInfo{Topology=VkPrimitiveTopology.TriangleList};
                var viewport=new VkPipelineViewportStateCreateInfo{ViewportCount=1,ScissorCount=1};
                var raster=new VkPipelineRasterizationStateCreateInfo{PolygonMode=VkPolygonMode.Fill,CullMode=VkCullModeFlags.None,FrontFace=VkFrontFace.CounterClockwise,LineWidth=1};
                var samples=new VkPipelineMultisampleStateCreateInfo{RasterizationSamples=VkSampleCountFlags._1Bit};
                var depth=new VkPipelineDepthStencilStateCreateInfo{DepthTestEnable=true,DepthWriteEnable=true,DepthCompareOp=VkCompareOp.Less};
                var attachment=new VkPipelineColorBlendAttachmentState{ColorWriteMask=VkColorComponentFlags.RBit|VkColorComponentFlags.GBit|VkColorComponentFlags.BBit|VkColorComponentFlags.ABit};
                var blend=new VkPipelineColorBlendStateCreateInfo{AttachmentCount=1,Attachments=&attachment};
                VkDynamicState* states=stackalloc VkDynamicState[2]{VkDynamicState.Viewport,VkDynamicState.Scissor};
                var dynamicState=new VkPipelineDynamicStateCreateInfo{DynamicStateCount=2,DynamicStates=states};
                return device.CreateGraphicsPipeline(default,new VkGraphicsPipelineCreateInfo{StageCount=2,Stages=stages,VertexInputState=&vertices,
                    InputAssemblyState=&assembly,ViewportState=&viewport,RasterizationState=&raster,MultisampleState=&samples,DepthStencilState=&depth,
                    ColorBlendState=&blend,DynamicState=&dynamicState,Layout=targetLayout,RenderPass=pass,Subpass=0},null);
            }
    }
    private static byte[] Read(string name)
    {
        using var input=typeof(SceneTrianglePipeline).Assembly.GetManifestResourceStream(name)??throw new InvalidOperationException("Missing compiled scene shader.");
        using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MeshPush {internal Matrix4x4 Matrix;internal Vector4 Color,Mode;}
    internal void Draw(CommandBuffer command,SceneRenderRequest? request,IReadOnlyDictionary<(string Item,int Part),SceneMeshBuffer> meshes,IReadOnlyDictionary<(string Guid,string Hash),SceneSampledMaterial>? materials=null)
    {
        void Emit(SceneMeshBuffer mesh,Matrix4x4 matrix,Vector4 color,float mode,SceneSampledMaterial? material)
        {
            var sampled=material is not null&&material.IsCurrent();
            if(mode>0&&!sampled)mode=2;
            var activeLayout=sampled?texturedLayout:layout;
            command.BindPipeline(VkPipelineBindPoint.Graphics,sampled?texturedPipeline:pipeline);
            if(sampled)command.BindDescriptorSets(VkPipelineBindPoint.Graphics,activeLayout,0,[material!.Texture.Descriptor],[]);
            var push=new MeshPush{Matrix=matrix,Color=color,Mode=new(0,0,mode,0)};
            CommandBufferExtensions.PushConstants(command,activeLayout,VkShaderStageFlags.VertexBit,ByteSize.Of<byte>(0),push);
            mesh.Draw(command);
        }
        if(request is null){Emit(meshes[("fallback",0)],Matrix4x4.Identity,Vector4.One,-1,null);return;}
        var vp=request.Camera.View*request.Camera.Projection(request.Identity.Extent);
        foreach(var item in request.Items)
        {
            for(var part=0;part<item.Payloads.Count;part++)
            {
                var payload=item.Payloads[part];if(payload.Indices.Count==0)continue;
                SceneSampledMaterial? material=null;var declaration=payload.Material;
                if(declaration.TextureAssetGuid is {} guid&&materials is not null)materials.TryGetValue((guid,declaration.TextureSourceHash!),out material);
                Emit(meshes[(item.Id,part)],item.World*vp,declaration.BaseColor,declaration.TextureAssetGuid is null?0:1,material);
            }
        }
    }
    public void Dispose()
    {
        if(texturedPipeline.IsNotNull()){device.DestroyPipeline(texturedPipeline,null);texturedPipeline=default;}
        if(pipeline.IsNotNull()){device.DestroyPipeline(pipeline,null);pipeline=default;}
        if(texturedLayout.IsNotNull()){device.DestroyPipelineLayout(texturedLayout,null);texturedLayout=default;}
        if(layout.IsNotNull()){device.DestroyPipelineLayout(layout,null);layout=default;}
        textureLayout?.Dispose();textureLayout=null;
    }
}
