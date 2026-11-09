using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.Pointers.Extensions;
using Brutal.Strings;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Brutal.Authoring.Editor.App;

/// <summary>
/// The smallest Vulkan renderer an ImGui editor needs: one window, one device and queue, a swapchain, and one render pass
/// with a single colour attachment that is cleared and then left ready to present. Each frame it hands the open render
/// pass to a callback (ImGui's draw lists go there) and presents. It rebuilds the swapchain when the window is resized
/// or the surface goes out of date, and skips frames while the window is minimised.
/// BORROWED from the author's own BRUTAL game renderer (the constructor's instance, surface, device and
/// sync setup; CreateRenderPass; CreateSwapchain and RecreateSwapchain; Draw's acquire, submit and present;
/// CreateImGuiRenderer), cut down: no depth buffer, meshes, shaders, pipelines or screenshots.
/// </summary>
internal sealed unsafe class WindowRenderer : IDisposable
{
    private const int FramesInFlight = 2;
    private SceneDeviceOwner? sceneOwner;
    private bool framePrepared;
    internal SceneDeviceOwner SceneOwner => sceneOwner ??= new(device, physical, queue);
    internal Action<CommandBuffer, int>? ScenePrePass { get; set; }
    internal void ShutdownScenes(){device.WaitIdle();sceneOwner?.Close();sceneOwner=null;}
    // Future host calls before UI layout. Existing Draw remains valid without this optional scene path.
    internal int? PrepareSceneFrame()
    {
        if (Minimised) return null;
        if (!framePrepared) { device.WaitForFences([frames[frame].Fence], true, -1); framePrepared=true; }
        SceneOwner.Safe(frame);
        return frame;
    }

    private readonly GlfwWindow window;
    private readonly Instance instance;
    private readonly VkDebugUtilsMessengerEXT messenger;
    private readonly VkSurfaceKHR surface;
    private readonly PhysicalDevice physical;
    private readonly DeviceEx device;
    private readonly Queue queue;
    private readonly VkSurfaceFormatKHR surfaceFormat;
    private readonly VkRenderPass renderPass;

    private VkSwapchainKHR swapchain;
    private VkImage[] swapImages = [];
    private VkImageView[] swapViews = [];
    private VkFramebuffer[] framebuffers = [];
    private VkSemaphore[] renderFinished = [];   // one per swapchain image, so a semaphore is never reused while presenting
    private VkExtent2D extent;
    private bool resized;

    private readonly VkCommandPool[] pools = new VkCommandPool[FramesInFlight];
    private readonly SyncObjects[] frames = new SyncObjects[FramesInFlight];
    private int frame;

    /// <summary>The colour the window is cleared to before ImGui draws (linear 0..1 RGBA; the swapchain is UNORM where
    /// the GPU allows, so these are the values that reach the screen).</summary>
    public float4 ClearColor { get; set; } = new(0.08f, 0.08f, 0.09f, 1f);

    /// <summary>The name of the GPU in use.</summary>
    public string DeviceName { get; }

    /// <summary>Sets up Vulkan for <paramref name="window"/>, with the validation layers when <paramref name="validation"/>
    /// is set and they're installed.</summary>
    public WindowRenderer(GlfwWindow window, bool validation)
    {
        this.window = window;

        // Instance: GLFW's extensions, plus validation and its messenger when asked for and available.
        var extensions = new HashSet<string>();
        var required = Glfw.GetRequiredInstanceExtensions(out var requiredCount);
        for (var i = 0; i < requiredCount; i++)
            extensions.Add(Marshal.PtrToStringUTF8((nint)required[i])!);
        VulkanHelpers.AddSurfaceExtensions(extensions);
        VulkanHelpers.AddPlatformRequiredExtensions(extensions);
        var layers = new HashSet<string>();
        validation &= VulkanHelpers.Validation.IsSupported;
        if (validation)
        {
            VulkanHelpers.AddValidationLayers(layers);
            extensions.Add(VK.EXT_DEBUG_UTILS_EXTENSION_NAME);
        }
        var debugInfo = VulkanHelpers.BuildDefaultDebugMessengerCreateInfo(
            new PfnVkDebugUtilsMessengerCallbackEXT(&VulkanHelpers.DebugUtilsMessengerCallbacks.Default));
        instance = Vulkan.CreateInstance(new Instance.CreateInfo
        {
            AppInfo = new VkApplicationInfo
            {
                ApplicationName = "BED"u8.AsPointer(),
                EngineName = "BED"u8.AsPointer(),
                ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3,
            },
            Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
            EnabledExtensions = extensions,
            EnabledLayers = layers,
            Next = validation ? &debugInfo : null,
        });
        messenger = validation ? instance.CreateDebugUtilsMessengerEXT(debugInfo, null) : default;

        // The window's surface.
        if (Glfw.CreateWindowSurface(instance.VkHandle, window, out var rawSurface) != 0)
            throw new InvalidOperationException("Couldn't create a Vulkan surface for the window.");
        surface = new VkSurfaceKHR((nint)rawSurface);

        // A Vulkan 1.3 device that can draw and present to this window; a discrete GPU if there is one.
        PhysicalDevice? chosen = null;
        var family = -1;
        foreach (var candidate in instance.PhysicalDevices)
        {
            var properties = candidate.GetProperties();
            if (properties.ApiVersion < (int)VulkanHelpers.Api.VERSION_1_3 || !candidate.SupportsExtension("VK_KHR_swapchain"u8))
                continue;
            var count = candidate.GetQueueFamilyProperties_Count();
            var families = new VkQueueFamilyProperties[count];
            candidate.GetQueueFamilyProperties_Write(families);
            var found = -1;
            for (var i = 0; i < count && found < 0; i++)
                if (families[i].QueueFlags.HasFlag(VkQueueFlags.GraphicsBit) && candidate.GetSurfaceSupportKHR(i, surface))
                    found = i;
            if (found < 0) continue;
            if (chosen is null || properties.DeviceType == VkPhysicalDeviceType.DiscreteGpu)
                (chosen, family) = (candidate, found);
            if (properties.DeviceType == VkPhysicalDeviceType.DiscreteGpu) break;
        }
        physical = chosen ?? throw new InvalidOperationException("No Vulkan 1.3 GPU that can present to this window.");
        var chosenProperties = physical.GetProperties();
        DeviceName = Utf8.GetString(chosenProperties.DeviceName);

        var deviceExtensions = new HashSet<string>();
        VulkanHelpers.AddSwapchainExtensions(deviceExtensions);
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(deviceExtensions, physical);
        var priority = 1f;
        var features = new VkPhysicalDeviceFeatures();
        device = physical.CreateDevice(new DeviceEx.CreateInfo
        {
            Extensions = deviceExtensions,
            Features = &features,
            QueueCreateInfos = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }],
        });
        queue = device.GetQueue(family, 0);

        surfaceFormat = ChooseSurfaceFormat();
        window.OnFramebufferSize += (_, _) => resized = true;
        renderPass = CreateRenderPass();
        CreateSwapchain();

        // A command pool, command buffer, two semaphores and a fence per frame in flight.
        for (var i = 0; i < FramesInFlight; i++)
        {
            pools[i] = device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = queue.Family }, null);
            frames[i] = new SyncObjects(device)
            {
                ImageReadySemaphore = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
                RenderingFinishedSemaphore = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
                Fence = device.CreateFence(VkFenceCreateFlags.SignaledBit, null),
                CommandBuffer = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pools[i], Level = VkCommandBufferLevel.Primary }),
                CommandPool = pools[i],
                Allocator = null,
            };
        }
    }

    /// <summary>Whether the window has no area to draw into (minimised).</summary>
    public bool Minimised => window.FramebufferSize.X == 0 || window.FramebufferSize.Y == 0;

    /// <summary>BRUTAL's ImGui Vulkan renderer, drawing in this renderer's render pass.</summary>
    public Brutal.ImGuiApi.Backends.VulkanBackend CreateImGuiRenderer() { var backend = new Brutal.ImGuiApi.Backends.VulkanBackend(new Brutal.ImGuiApi.Backends.VulkanBackend.CreateInfo
    {
        Device = device,
        GraphicsQueue = queue,
        RenderPass = renderPass,
        SubPass = 0,
        MinImageCount = 2,
        ImageCount = Math.Max(2, swapImages.Length),
        SampleCount = VkSampleCountFlags._1Bit,
        DescriptorPoolSize = 16,
    }); SceneOwner.Attach(backend); return backend; }

    /// <summary>Waits until the GPU has finished everything it was given (before freeing anything it may still use).</summary>
    public void WaitIdle() => device.WaitIdle();

    /// <summary>
    /// Draws one frame: clears the window, calls <paramref name="record"/> inside the render pass (ImGui records its draw
    /// lists there), and presents. Returns false, drawing nothing, when the window is minimised.
    /// Adapted from a game renderer's frame (acquire, record, submit, present), without the scene.
    /// </summary>
    public bool Draw(Action<CommandBuffer> record)
    {
        if (Minimised) return false;
        var f = frames[frame];
        if (!framePrepared) device.WaitForFences([f.Fence], true, -1);
        framePrepared=false;

        int imageIndex;
        try
        {
            if (device.AcquireNextImageKHR(swapchain, -1, f.ImageReadySemaphore, default, out imageIndex) == VkResult.SuboptimalKHR)
                resized = true;
        }
        catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR)
        {
            RecreateSwapchain();
            return true;
        }

        device.ResetCommandPool(pools[frame], VkCommandPoolResetFlags.None);
        var cb = f.CommandBuffer;
        cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);

        sceneOwner?.Record(cb);
        ScenePrePass?.Invoke(cb, frame);
        // The render pass clears the colour image and hands it over ready to present.
        VkClearValue clear = new VkClearColorValue { Float32 = ClearColor };
        cb.BeginRenderPass(new VkRenderPassBeginInfo
        {
            RenderPass = renderPass,
            Framebuffer = framebuffers[imageIndex],
            RenderArea = new VkRect2D(extent),
            ClearValueCount = 1,
            ClearValues = &clear,
        }, VkSubpassContents.Inline);
        cb.SetViewport(0, [new VkViewport { Width = extent.Width, Height = extent.Height, MinDepth = 0f, MaxDepth = 1f }]);
        cb.SetScissor(0, [new VkRect2D(extent)]);
        record(cb);
        cb.EndRenderPass();
        cb.End();

        device.ResetFences([f.Fence]); // Recording succeeded; an exception above must not strand an unsignaled fence.
        queue.Submit([f.ImageReadySemaphore], [VkPipelineStageFlags.ColorAttachmentOutputBit], [cb], [renderFinished[imageIndex]], f.Fence);
        sceneOwner?.Submitted();

        var wait = renderFinished[imageIndex];
        var chain = swapchain;
        var index = imageIndex;
        try
        {
            if (queue.PresentKHR(new VkPresentInfoKHR
            {
                WaitSemaphoreCount = 1,
                WaitSemaphores = &wait,
                SwapchainCount = 1,
                Swapchains = &chain,
                ImageIndices = &index,
            }) == VkResult.SuboptimalKHR)
                resized = true;
        }
        catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR)
        {
            resized = true;
        }
        if (resized)
        {
            resized = false;
            RecreateSwapchain();
        }
        frame = (frame + 1) % FramesInFlight;
        return true;
    }

    // One subpass with one colour attachment, cleared and then left ready to present: the pattern BRUTAL's ImGui Vulkan
    // backend expects, so its pipeline draws in this pass. Adapted from a game renderer, with the depth attachment dropped.
    private VkRenderPass CreateRenderPass()
    {
        var attachment = new VkAttachmentDescription
        {
            Format = surfaceFormat.Format,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.PresentSrcKHR,
        };
        var colorReference = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorReference,
        };
        // Wait for the acquired image before writing to it (the acquire semaphore is waited on at this stage).
        var dependency = new VkSubpassDependency
        {
            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
        };
        return device.CreateRenderPass(new VkRenderPassCreateInfo
        {
            AttachmentCount = 1,
            Attachments = &attachment,
            SubpassCount = 1,
            Subpasses = &subpass,
            DependencyCount = 1,
            Dependencies = &dependency,
        }, null);
    }

    // An 8-bit UNORM format if there is one: ImGui's colours are already sRGB-encoded, so an sRGB swapchain would encode
    // them twice and wash them out. (A game picks an sRGB format for its lit scene; an editor wants ImGui's colours as made.)
    private VkSurfaceFormatKHR ChooseSurfaceFormat()
    {
        var count = physical.GetSurfaceFormatsKHR_Count(surface);
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[count];
        physical.GetSurfaceFormatsKHR_Write(surface, formats);
        foreach (var f in formats)
            if (f.Format is VkFormat.B8G8R8A8UNorm or VkFormat.R8G8B8A8UNorm && f.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR)
                return f;
        return formats[0];
    }

    // Adapted from a game renderer, without the depth image.
    private void CreateSwapchain()
    {
        var size = window.FramebufferSize;
        var caps = physical.GetSurfaceCapabilitiesKHR(surface);
        extent = caps.CurrentExtent.Width == -1
            ? new VkExtent2D(Math.Clamp(size.X, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(size.Y, caps.MinImageExtent.Height, caps.MaxImageExtent.Height))
            : caps.CurrentExtent;
        var imageCount = caps.MinImageCount + 1;
        if (caps.MaxImageCount != 0 && imageCount > caps.MaxImageCount) imageCount = caps.MaxImageCount;
        var old = swapchain;
        swapchain = device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
        {
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = VkSharingMode.Exclusive,
            PreTransform = caps.SupportedTransforms.HasFlag(VkSurfaceTransformFlagsKHR.IdentityBitKHR)
                ? VkSurfaceTransformFlagsKHR.IdentityBitKHR : caps.CurrentTransform,
            CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            PresentMode = VkPresentModeKHR.FifoKHR,   // vsync: always available, and an editor needn't draw faster
            Clipped = true,
            OldSwapchain = old,
        }, null);
        if (old.IsNotNull()) device.DestroySwapchainKHR(old, null);

        var count = device.GetSwapchainImagesKHR_Count(swapchain);
        swapImages = new VkImage[count];
        device.GetSwapchainImagesKHR_Write(swapchain, swapImages);
        swapViews = new VkImageView[count];
        renderFinished = new VkSemaphore[count];
        framebuffers = new VkFramebuffer[count];
        for (var i = 0; i < count; i++)
        {
            swapViews[i] = device.CreateImageView(new VkImageViewCreateInfo
            {
                Image = swapImages[i],
                ViewType = VkImageViewType._2D,
                Format = surfaceFormat.Format,
                Components = new VkComponentMapping { R = VkComponentSwizzle.R, G = VkComponentSwizzle.G, B = VkComponentSwizzle.B, A = VkComponentSwizzle.A },
                SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit),
            }, null);
            renderFinished[i] = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
            var view = swapViews[i];
            framebuffers[i] = device.CreateFramebuffer(new VkFramebufferCreateInfo
            {
                RenderPass = renderPass,
                AttachmentCount = 1,
                Attachments = &view,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1,
            }, null);
        }
    }

    private void DestroySwapchainResources()
    {
        foreach (var framebuffer in framebuffers) device.DestroyFramebuffer(framebuffer, null);
        foreach (var view in swapViews) device.DestroyImageView(view, null);
        foreach (var semaphore in renderFinished) device.DestroySemaphore(semaphore, null);
    }

    // Minimised windows wait for a size before rebuilding.
    private void RecreateSwapchain()
    {
        if (Minimised) return;   // try again on the next frame that has a size
        device.WaitIdle();
        DestroySwapchainResources();
        CreateSwapchain();
    }

    /// <summary>Frees everything, in reverse order of creation, after the GPU has finished.</summary>
    public void Dispose()
    {
        device.WaitIdle();
        sceneOwner?.Close();
        foreach (var f in frames) f.Dispose();
        foreach (var pool in pools) device.DestroyCommandPool(pool, null);
        DestroySwapchainResources();
        device.DestroySwapchainKHR(swapchain, null);
        device.DestroyRenderPass(renderPass, null);
        device.Dispose();
        instance.DestroySurfaceKHR(surface, null);
        if (messenger.IsNotNull()) instance.DestroyDebugUtilsMessengerEXT(messenger, null);
        instance.Dispose();
    }
}



