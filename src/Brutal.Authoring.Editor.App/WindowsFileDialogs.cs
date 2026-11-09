using System.Runtime.InteropServices;
using Brutal.Authoring.Editor.ImGui;

namespace Brutal.Authoring.Editor.App;

/// <summary>Windows shell dialogs, modal on their owner's STA UI thread. No file is written here.</summary>
internal sealed class WindowsFileDialogs : IFileDialogs, IDisposable
{
    private nint owner;
    private readonly int thread = Environment.CurrentManagedThreadId;
    public WindowsFileDialogs(nint owner)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows common item dialogs require Windows.");
        if (owner == 0) throw new ArgumentException("A BED owner window is required.", nameof(owner));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Windows file dialogs require the owning STA UI thread.");
        this.owner = owner;
    }
    public void Dispose() => owner = 0;
    public string? PickFolder(string title, string? start) => Show(false, true, title, [], start, null);
    public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => Show(false, false, title, filters, start, null);
    public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => Show(true, false, title, filters, null, suggestedName);

    private string? Show(bool save, bool folder, string title, IReadOnlyList<FileDialogFilter> filters, string? start, string? name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows common item dialogs require Windows.");
        ObjectDisposedException.ThrowIf(owner == 0, this);
        if (Environment.CurrentManagedThreadId != thread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Windows file dialogs require the owning STA UI thread.");
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(filters);
        var specs = filters.Select(f => new FilterSpec { Name = f.Name, Pattern = f.Pattern }).ToArray();
        foreach (var f in specs)
            if (string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.Pattern))
                throw new ArgumentException("A file filter needs a label and wildcard pattern.", nameof(filters));
        var clsid = new Guid(save ? "C0B4E2F3-BA21-4773-8DBA-335EC946EB8B" : "DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        var dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)!;
        try
        {
            dialog.GetOptions(out var options);
            // Filesystem paths only; don't change the process working directory.
            options |= 0x40u | 0x8u | 0x800u; // FORCEFILESYSTEM, NOCHANGEDIR, PATHMUSTEXIST
            options |= folder ? 0x20u : save ? 0x2u : 0x1000u; // PICKFOLDERS / OVERWRITEPROMPT / FILEMUSTEXIST
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            if (!folder && specs.Length != 0) dialog.SetFileTypes((uint)specs.Length, specs);
            if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
            {
                var iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(start), 0, ref iid, out var initial));
                try { dialog.SetFolder(initial); }
                finally { Marshal.FinalReleaseComObject(initial); }
            }
            if (!string.IsNullOrEmpty(name)) dialog.SetFileName(name);
            int hr = dialog.Show(owner);
            if (hr == unchecked((int)0x800704C7)) return null; // ERROR_CANCELLED only
            Marshal.ThrowExceptionForHR(hr);
            dialog.GetResult(out var result);
            try
            {
                result.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
                try { return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("The dialog returned no filesystem path."); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.FinalReleaseComObject(result); }
        }
        finally { Marshal.FinalReleaseComObject(dialog); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Pattern;
    }

    // COM vtable order includes every inherited IModalWindow/IFileDialog slot.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(nint filter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint value);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint format, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
