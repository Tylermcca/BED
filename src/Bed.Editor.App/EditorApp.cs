using Brutal.GlfwApi;
using Brutal.GlfwApi.Win32;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using System.Runtime.InteropServices;

namespace Bed.Editor.App;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>How the editor's window opens.</summary>
/// <param name="Title">The window's title.</param>
/// <param name="Width">The window's width in screen pixels.</param>
/// <param name="Height">The window's height in screen pixels.</param>
/// <param name="LayoutPath">Optional app-owned docking settings path; null uses local app data.</param>
/// <param name="Validation">Turn on Vulkan's validation layers (when installed), for finding rendering mistakes.</param>
public sealed record EditorAppOptions(string Title = "BED", int Width = 1600, int Height = 900, bool Validation = false, bool OpenGl = false, string? LayoutPath = null);

/// <summary>
/// Runs an ImGui editor in a real window: GLFW for the window and input, Vulkan to draw, and BRUTAL's ImGui backends
/// (GLFW platform, Vulkan renderer) in between. This is the interactive counterpart of the headless host the tests use.
/// BORROWED from the author's own BRUTAL game client: its window setup, its renderer's Vulkan setup and frame loop
/// and its tools overlay's ImGui wiring.
/// </summary>
public static class EditorApp
{
    /// <summary>
    /// Opens a window, sets up Vulkan and ImGui, and lets <paramref name="configure"/> set ImGui up (for an editor
    /// window, pass <c>io =&gt; EditorWindow&lt;T&gt;.Configure(io)</c>). Then, each frame, it reads input, starts an ImGui
    /// frame, calls <paramref name="draw"/>, and draws the result. It returns when the window is closed, having freed
    /// everything in reverse order. Layout settings are persisted to the app-owned path, never next to the program. Only one may run at a
    /// time: ImGui's current context is process-wide.
    /// </summary>
    /// <param name="options">The window's title, size and whether to validate.</param>
    /// <param name="configure">Called once, after ImGui and its backends are set up, before the first frame.</param>
    /// <param name="draw">Called every frame between <c>ImGui.NewFrame</c> and <c>ImGui.Render</c>.</param>
    /// <param name="fileDialogs">Optional host service receiver: set after setup; cleared before window disposal. Dialogs are modal and pause drawing.</param>
    /// <param name="close">Optional host close decision, called once per loop with the OS request after clearing it. Return true only when closing is accepted; false keeps the editor running.</param>
    /// <param name="title">Optional current title, read after drawing; the native title is updated only when its text changes.</param>
    /// <param name="drop">Queued drop receiver with captured cursor and last completed UI frame; called after polling, before drawing.</param>
    public static void Run(EditorAppOptions options, Action<ImGuiIOPtr> configure, Action draw, Action<IFileDialogs?>? fileDialogs = null, Func<bool, bool>? close = null, Func<string>? title = null, Action<WindowDrop, UiSnapshot>? drop = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(draw);

        // Vulkan where there is one; OpenGL where there isn't, or where Vulkan won't start (found on the user's laptop, 2026-11-03:
        // no vulkan-1.dll). Asking for OpenGL in the options skips Vulkan.
        if (options.OpenGl || !VulkanPresent()) { Renderer = "OpenGL"; RunOpenGl(options, configure, draw, fileDialogs, close, title, drop); return; }
        Renderer = "Vulkan";

        // The window: no OpenGL context, since Vulkan draws into it.
        if (!Glfw.Init()) throw new InvalidOperationException("Couldn't start GLFW (no display?).");
        try
        {
            Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
            var window = CreateHostWindow(options);
            if (window.IsNull()) throw new InvalidOperationException("GLFW could not create the editor window.");
            AccessKitWindowBridge? accessibility;
            try { accessibility = AttachAccessibility(window, options.Title); }
            catch { window.Dispose(); throw; }
            WindowRenderer renderer;
            try { renderer = new WindowRenderer(window, options.Validation); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Vulkan is there but won't draw here (an old driver, no suitable GPU): OpenGL instead, in a fresh window.
                accessibility?.Dispose();
                window.Dispose();
                Glfw.Terminate();
                VulkanFailure = e.Message;
                ProblemReportLog.Record(ReportEvent.VulkanFallback);
                Renderer = "OpenGL";
                RunOpenGl(options, configure, draw, fileDialogs, close, title, drop);
                return;
            }
            catch
            {
                // Non-fallback failures (including allocation failure) still detach before GLFW destroys the HWND.
                try { accessibility?.Dispose(); }
                finally { window.Dispose(); }
                throw;
            }
            try
            {
                using (renderer) Loop(window, renderer, configure, draw, fileDialogs, close, title, options.Title, accessibility, drop, options.LayoutPath);
            }
            finally
            {
                try { accessibility?.Dispose(); }
                finally { window.Dispose(); }
            }
        }
        finally
        {
            Glfw.Terminate();
        }
    }

    /// <summary>What the last <see cref="Run"/> drew with: "Vulkan" or "OpenGL".</summary>
    public static string Renderer { get; private set; } = "";

    /// <summary>Why Vulkan wasn't used, when it was there but wouldn't start; null otherwise.</summary>
    public static string? VulkanFailure { get; private set; }

    /// <summary>Whether this PC has a Vulkan loader at all. Without one, the window draws with OpenGL.</summary>
    public static bool VulkanPresent()
    {
        var name = OperatingSystem.IsWindows() ? "vulkan-1.dll" : OperatingSystem.IsMacOS() ? "libvulkan.1.dylib" : "libvulkan.so.1";
        if (!NativeLibrary.TryLoad(name, typeof(EditorApp).Assembly, DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    private static void DeliverDrops(WindowDropQueue queue, Action<WindowDrop, UiSnapshot>? receive)
    {
        while (queue.TryTakeDrop(out var drop)) receive?.Invoke(drop!, Ui.Snapshot());
    }

    // Called only after the host window/backends exist; never on headless hosts.
    private static unsafe WindowsFileDialogs? CreateDialogs(GlfwWindow window) =>
        OperatingSystem.IsWindows() ? new WindowsFileDialogs((nint)window.GetWin32Window()) : null;

    // GLFW's visibility hint is read at creation. AccessKit's subclass constructor must run before first show/focus.
    private static GlfwWindow CreateHostWindow(EditorAppOptions options)
    {
        var hidden = OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
        if (hidden) Glfw.WindowHint(GlfwWindowHint.Visible, 0);
        try { return Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = options.Title, Size = new int2(options.Width, options.Height) }); }
        finally { if (hidden) Glfw.WindowHint(GlfwWindowHint.Visible, 1); }
    }

    private static unsafe AccessKitWindowBridge? AttachAccessibility(GlfwWindow window, string title)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return null;
        if (window.Visible) throw new InvalidOperationException("Screen-reader support must attach before the editor window is first shown.");
        try { return AccessKitWindowBridge.Attach((nint)window.GetWin32Window(), title); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException("BED screen-reader support could not load the packaged Windows x64 accesskit.dll. Repair the BED folder and check the Visual C++ x64 prerequisite.", error);
        }
    }

    private static void PublishAccessibility(AccessKitWindowBridge? accessibility, ImGuiIOPtr io, string title)
    {
        if (accessibility is null) return;
        var viewport = ImGui.GetMainViewport();
        accessibility.Publish(Bed.Editor.ImGui.Ui.Snapshot(0), title,
            new UiBounds(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y),
            io.DisplayFramebufferScale.X, io.DisplayFramebufferScale.Y);
    }

    private static void RenderSecondaryWindows(AccessKitPlatformWindows? accessibility, bool openGl, GlfwWindow window)
    {
        if (!ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable)) return;
        try
        {
            EditorFrame.RenderSecondary(accessibility);
        }
        finally
        {
            // GLFW's secondary rendering selects shared contexts; the next main frame and swap use this one.
            if (openGl) Glfw.PInvoke.MakeContextCurrent(window);
        }
    }

    private static GlGetProcAddressFunc? glLoader;   // kept rooted: GLAD keeps the pointer (BRUTAL T35)

    // The same editor frame drawn with OpenGL 4.1 core, BRUTAL's run-verified recipe 7.21 (GLFW, GLAD, its OpenGlBackend).
    private static unsafe void RunOpenGl(EditorAppOptions options, Action<ImGuiIOPtr> configure, Action draw, Action<IFileDialogs?>? fileDialogs, Func<bool, bool>? close, Func<string>? title, Action<WindowDrop, UiSnapshot>? drop)
    {
        if (!Glfw.Init()) throw new InvalidOperationException("Couldn't start GLFW (no display?).");
        try
        {
            Glfw.WindowHint(GlfwWindowHint.ClientApi, Glfw.Constants.GLFW_OPENGL_API);
            Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);   // the OpenGL backend needs 4.1+ core (BRUTAL T22)
            Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
            Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
            var window = CreateHostWindow(options);
            if (window.IsNull()) throw new InvalidOperationException("This PC has neither Vulkan nor OpenGL 4.1, so there's nothing to draw with. Updating the graphics driver usually adds one.");
            try
            {
                using var accessibility = AttachAccessibility(window, options.Title);
                using var themeNotifications = WindowsThemeNotifications.Attach(window);
                Glfw.PInvoke.MakeContextCurrent(window);
                Glfw.PInvoke.SwapInterval(1);
                glLoader = name => Glfw.PInvoke.GetProcAddress(name);
                if (!Glad.Initialize(glLoader)) throw new InvalidOperationException("OpenGL wouldn't load (GLAD).");
                var context = ImGui.CreateContext();
                try
                {
                    var io = ImGui.GetIO();
                    io.IniFilename = default;
                    io.LogFilename = default;
                    Bed.Editor.ImGui.EditorStyle.Apply();
                    using var platform = new GlfwBackend(window, GlfwBackend.ClientApi.OpenGL);
                    var renderer = new OpenGlBackend();
                    using var drops = new WindowDropQueue();
                    if (drop is not null) window.OnDrop += drops.Callback;
                    WindowsFileDialogs? dialogs = null;
                    AccessKitPlatformWindows? secondary = null;
                    try
                    {
                        var windowTitle = new WindowTitle(options.Title);
                        configure(io);
                        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
                        var layout = new EditorLayoutStore(options.LayoutPath);
                        layout.LoadCurrent();
                        if (accessibility is not null) secondary = new AccessKitPlatformWindows(options.Title);
                        var dpi = new DpiScale {TextScaleReader=WindowsTextScale.Read};
                        if (fileDialogs is not null) { dialogs = CreateDialogs(window); fileDialogs(dialogs); }
                        var currentTitle = options.Title;
                        var framebuffer = window.FramebufferSize;
                        var frame = new EditorFrame(draw,
                            () => { currentTitle = title?.Invoke() ?? options.Title; if (title is not null) windowTitle.Update(currentTitle, value => window.Title = value); },
                            () => PublishAccessibility(accessibility, io, currentTitle),
                            () => { if (io.WantSaveIniSettings) { layout.SaveCurrent(); io.WantSaveIniSettings = false; } },
                            () => {
                                OpenGl.Native.glViewport(0, 0, framebuffer.X, framebuffer.Y);
                                OpenGl.Native.glClearColor(0.12f, 0.13f, 0.16f, 1f);
                                OpenGl.Native.glClear(OpenGl.GL_COLOR_BUFFER_BIT);
                                renderer.RenderDrawData(ImGui.GetDrawData());
                            },
                            () => RenderSecondaryWindows(secondary, true, window));
                        ProblemReportHost.Capture=WindowsReportPicture.Capture;
                        window.Visible = true;
                        while (true)
                        {
                            Glfw.PollEvents();
                            DeliverDrops(drops, drop);
                            if (ShellClose.Decide(window.ShouldClose, () => window.ShouldClose = false, close)) { layout.SaveCurrent(); break; }
                            framebuffer = window.FramebufferSize;
                            if (framebuffer.X <= 0 || framebuffer.Y <= 0) { Glfw.WaitEvents(); continue; }   // minimised
                            // Use the vertical content scale for text and uniform logical dimensions.
                            accessibility?.DrainRequests();
                            secondary?.DrainRequests();
                            dpi.Apply(window.ContentScale.Y);
                            renderer.NewFrame();
                            platform.NewFrame();
                            frame.Run();
                            Glfw.PInvoke.SwapBuffers(window);
                        }
                    }
                    finally
                    {
                        try { if (drop is not null) window.OnDrop -= drops.Callback; fileDialogs?.Invoke(null); }
                        finally
                        {
                            try { dialogs?.Dispose(); }
                            finally
                            {
                                try { secondary?.Dispose(); }
                                finally
                                {
                                    // Secondary destruction may have selected another GL context.
                                    Glfw.PInvoke.MakeContextCurrent(window);
                                    renderer.Dispose();
                                }
                            }
                        }
                    }
                }
                finally { ProblemReportHost.Capture=null;
                    ImGui.DestroyContext(context); }
            }
            finally { window.Dispose(); }
        }
        finally { Glfw.Terminate(); }
    }

    // ImGui's context and backends live inside the renderer's lifetime and are freed before it.
    private static void Loop(GlfwWindow window, WindowRenderer renderer, Action<ImGuiIOPtr> configure, Action draw, Action<IFileDialogs?>? fileDialogs, Func<bool, bool>? close, Func<string>? title, string initialTitle, AccessKitWindowBridge? accessibility, Action<WindowDrop, UiSnapshot>? drop, string? layoutPath)
    {
        using var themeNotifications = WindowsThemeNotifications.Attach(window);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = default;   // no imgui.ini written wherever the program runs
            io.LogFilename = default;
            Bed.Editor.ImGui.EditorStyle.Apply();
            using var platform = new GlfwBackend(window, GlfwBackend.ClientApi.Vulkan);
            using var vulkan = renderer.CreateImGuiRenderer();
            using var drops = new WindowDropQueue();
            if (drop is not null) window.OnDrop += drops.Callback;
            WindowsFileDialogs? dialogs = null;
            AccessKitPlatformWindows? secondary = null;
            ScenePresentation? scenePresentation = null;
            try
            {
                var windowTitle = new WindowTitle(initialTitle);
                scenePresentation = ScenePresentation.BindMaterials((view,request,current,catalog) =>
                {
                    var target=renderer.SceneOwner.Target(view,request,current,catalog);
                    return target.TryTexture(request,out var texture)
                        ? new SceneImage(request,texture,()=>target.TryTexture(request,out _)){Status=target.MaterialStatus} : null;
                },renderer.SceneOwner.RetainViews).WithSprites((view,catalog,selection,filter)=>
                {
                    var image=renderer.SceneOwner.Sprite(view,catalog,selection,filter);
                    return new SpriteImage(selection,filter,image.Resource.Texture,()=>renderer.SceneOwner.SpriteCurrent(image,filter));
                });
                configure(io);
                io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
                var layout = new EditorLayoutStore(layoutPath);
                layout.LoadCurrent();
                if (accessibility is not null) secondary = new AccessKitPlatformWindows(initialTitle);
                var dpi = new DpiScale {TextScaleReader=WindowsTextScale.Read};
                if (fileDialogs is not null) { dialogs = CreateDialogs(window); fileDialogs(dialogs); }
                var currentTitle = initialTitle;
                var frame = new EditorFrame(draw,
                    () => { scenePresentation.CompleteFrame(); currentTitle = title?.Invoke() ?? initialTitle; if (title is not null) windowTitle.Update(currentTitle, value => window.Title = value); },
                    () => PublishAccessibility(accessibility, io, currentTitle),
                    () => { if (io.WantSaveIniSettings) { layout.SaveCurrent(); io.WantSaveIniSettings = false; } },
                    () => renderer.Draw(cb => vulkan.RenderDrawData(ImGui.GetDrawData(), cb)),
                    () => RenderSecondaryWindows(secondary, false, window));
                ProblemReportHost.Capture=WindowsReportPicture.Capture;
                window.Visible = true;
                while (true)
                {
                    Glfw.PollEvents();
                    DeliverDrops(drops, drop);
                    if (ShellClose.Decide(window.ShouldClose, () => window.ShouldClose = false, close)) { layout.SaveCurrent(); break; }
                    if (renderer.Minimised)
                    {
                        Glfw.WaitEvents();   // nothing to draw into: sleep until something happens
                        continue;
                    }
                    accessibility?.DrainRequests();
                    secondary?.DrainRequests();
                    dpi.Apply(window.ContentScale.Y);
                    // Retire the slot's previous GPU work before UI collection can request its textures.
                    if (renderer.PrepareSceneFrame() is null) continue;
                    scenePresentation.BeginFrame();
                    platform.NewFrame();
                    vulkan.NewFrame();
                    frame.Run();
                }
            }
            finally
            {
                try { if (drop is not null) window.OnDrop -= drops.Callback; fileDialogs?.Invoke(null); }
                finally
                {
                    try { dialogs?.Dispose(); }
                    finally
                    {
                        // Waits for the GPU, then frees scene descriptors while the ImGui backend still exists.
                        try { scenePresentation?.Dispose(); }
                        finally
                        {
                            try { renderer.ShutdownScenes(); }
                            finally { secondary?.Dispose(); }
                        }
                    }
                }
            }
        }
        finally
        {
            ProblemReportHost.Capture=null;
            ImGui.DestroyContext(context);
        }
    }
}
