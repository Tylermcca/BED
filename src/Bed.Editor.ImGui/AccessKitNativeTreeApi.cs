using System.Runtime.InteropServices;
using System.Text;

namespace Bed.Editor.ImGui;

/// <summary>The four doubles in AccessKit C 0.23.2's accesskit_rect, in client physical pixels.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AccessKitNativeRect(double X0, double Y0, double X1, double Y1);

/// <summary>The allocation/ownership subset of AccessKit C used by the tree encoder. Implementations must either return normally after transfer or throw before transfer.</summary>
public interface IAccessKitTreeApi
{
    /// <summary>Allocates an update with the supplied focus; zero means allocation failure.</summary>
    nint NewUpdate(nuint capacity, ulong focus);
    /// <summary>Frees an update and all nodes it owns.</summary>
    void FreeUpdate(nint update);
    /// <summary>Allocates and transfers tree information with this root to the update.</summary>
    void SetTree(nint update, ulong root);
    /// <summary>Allocates a node using the pinned byte-sized C role.</summary>
    nint NewNode(byte role);
    /// <summary>Frees a node that has not been transferred to an update.</summary>
    void FreeNode(nint node);
    /// <summary>Sets the entire UTF-8 label, including embedded zero characters.</summary>
    void Label(nint node, string value);
    /// <summary>Sets the entire UTF-8 value.</summary>
    void Value(nint node, string value);
    /// <summary>Sets the entire UTF-8 description.</summary>
    void Description(nint node, string value);
    /// <summary>Sets the access-key character as metadata, without input dispatch.</summary>
    void AccessKey(nint node, string value);
    /// <summary>Sets physical client bounds.</summary>
    void Bounds(nint node, AccessKitNativeRect bounds);
    /// <summary>Sets all children, in order, on a newly allocated node.</summary>
    void Children(nint node, IReadOnlyList<ulong> children);
    /// <summary>Marks a newly allocated node disabled.</summary>
    void Disabled(nint node);
    /// <summary>Sets selection using a one-byte C bool.</summary>
    void Selected(nint node, bool value);
    /// <summary>Sets expansion using a one-byte C bool.</summary>
    void Expanded(nint node, bool value);
    /// <summary>Sets a known check state (false=0, true=1), not the mixed state.</summary>
    void Toggled(nint node, byte value);
    /// <summary>Adds one byte-sized action from the pinned C enum.</summary>
    void AddAction(nint node, byte action);
    /// <summary>Transfers the node to the update. The caller must no longer free it.</summary>
    void PushNode(nint update, ulong id, nint node);
}

/// <summary>Windows x64 P/Invoke implementation. Constructing it does not load a DLL; native methods require the owning thread. Nothing attaches to a window here.</summary>
public sealed class AccessKitNativeTreeApi : IAccessKitTreeApi
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    /// <summary>Requires the approved Windows x64 target. Requires the app-local DLL; native calls search only the application and protected System32 directories for it and its Windows dependencies.</summary>
    public AccessKitNativeTreeApi()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("AccessKit C 0.23.2 is packaged only for Windows x64.");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "accesskit.dll")))
            throw new DllNotFoundException("The packaged accesskit.dll must be beside the BED host.");
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("AccessKit tree calls belong to the owning UI thread.");
    }
    /// <inheritdoc/>
    public nint NewUpdate(nuint capacity, ulong focus) { CheckThread(); return Native.accesskit_tree_update_with_capacity_and_focus(capacity, focus); }
    /// <inheritdoc/>
    public void FreeUpdate(nint update) { CheckThread(); Native.accesskit_tree_update_free(update); }
    /// <inheritdoc/>
    public void SetTree(nint update, ulong root)
    {
        CheckThread();
        var tree = Native.accesskit_tree_info_new(root);
        if (tree == 0) throw new OutOfMemoryException();
        try { Native.accesskit_tree_update_set_tree_info(update, tree); tree = 0; }
        finally { if (tree != 0) Native.accesskit_tree_info_free(tree); }
    }
    /// <inheritdoc/>
    public nint NewNode(byte role)
    {
        CheckThread();var node=Native.accesskit_node_new(role);
        if(node!=0 && role==AccessKitTreeEncoder.NativeRole(AccessKitProjectedRole.Status))
        {
            try
            {
                Native.accesskit_node_set_live(node,1); // ACCESSKIT_LIVE_POLITE in the pinned header.
                Native.accesskit_node_set_live_atomic(node);
            }
            catch {Native.accesskit_node_free(node);throw;}
        }
        return node;
    }
    /// <inheritdoc/>
    public void FreeNode(nint node) { CheckThread(); Native.accesskit_node_free(node); }
    /// <inheritdoc/>
    public void Label(nint node, string value) { CheckThread(); var bytes = Encoding.UTF8.GetBytes(value); Native.accesskit_node_set_label_with_length(node, bytes, (nuint)bytes.Length); }
    /// <inheritdoc/>
    public void Value(nint node, string value) { CheckThread(); var bytes = Encoding.UTF8.GetBytes(value); Native.accesskit_node_set_value_with_length(node, bytes, (nuint)bytes.Length); }
    /// <inheritdoc/>
    public void Description(nint node, string value) { CheckThread(); var bytes = Encoding.UTF8.GetBytes(value); Native.accesskit_node_set_description_with_length(node, bytes, (nuint)bytes.Length); }
    /// <inheritdoc/>
    public void AccessKey(nint node, string value) { CheckThread(); var bytes = Encoding.UTF8.GetBytes(value); Native.accesskit_node_set_access_key_with_length(node, bytes, (nuint)bytes.Length); }
    /// <inheritdoc/>
    public void Bounds(nint node, AccessKitNativeRect bounds) { CheckThread(); Native.accesskit_node_set_bounds(node, bounds); }
    /// <inheritdoc/>
    public void Children(nint node, IReadOnlyList<ulong> children) { CheckThread(); foreach (var child in children) Native.accesskit_node_push_child(node, child); }
    /// <inheritdoc/>
    public void Disabled(nint node) { CheckThread(); Native.accesskit_node_set_disabled(node); }
    /// <inheritdoc/>
    public void Selected(nint node, bool value) { CheckThread(); Native.accesskit_node_set_selected(node, value); }
    /// <inheritdoc/>
    public void Expanded(nint node, bool value) { CheckThread(); Native.accesskit_node_set_expanded(node, value); }
    /// <inheritdoc/>
    public void Toggled(nint node, byte value) { CheckThread(); Native.accesskit_node_set_toggled(node, value); }
    /// <inheritdoc/>
    public void AddAction(nint node, byte action) { CheckThread(); Native.accesskit_node_add_action(node, action); }
    /// <inheritdoc/>
    public void PushNode(nint update, ulong id, nint node) { CheckThread(); Native.accesskit_tree_update_push_node(update, id, node); }

    // All types and entry points match the unmodified vendored header. In particular role/toggled are uint8_t,
    // node_id is uint64_t, capacity/UTF-8 lengths are size_t, and C bool is one byte. No current-directory/PATH search; System32 supplies UIA/CRT dependencies.
    private static class Native
    {
        private const string Dll = "accesskit.dll";
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern nint accesskit_tree_update_with_capacity_and_focus(nuint capacity, ulong focus);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_tree_update_free(nint update);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern nint accesskit_tree_info_new(ulong root);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_tree_info_free(nint tree);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_tree_update_set_tree_info(nint update, nint tree);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern nint accesskit_node_new(byte role);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_free(nint node);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_live(nint node,byte value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_live_atomic(nint node);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_label_with_length(nint node, byte[] value, nuint length);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_value_with_length(nint node, byte[] value, nuint length);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_description_with_length(nint node, byte[] value, nuint length);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_access_key_with_length(nint node, byte[] value, nuint length);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_bounds(nint node, AccessKitNativeRect value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_push_child(nint node, ulong id);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_disabled(nint node);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_selected(nint node, [MarshalAs(UnmanagedType.I1)] bool value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_expanded(nint node, [MarshalAs(UnmanagedType.I1)] bool value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_set_toggled(nint node, byte value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_node_add_action(nint node, byte action);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_tree_update_push_node(nint update, ulong id, nint node);
    }
}
