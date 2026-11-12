using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

// A CPU picture shown through Dear ImGui's own texture list (ImTextureData, RegisterUserTexture). The renderer backend
// creates, updates and destroys the texture from those requests the same way for Vulkan, OpenGL and the headless host, so
// nothing here touches a graphics API. BED had no such path before: PreviewTexture is Vulkan-only and fixed once made, and
// AssetThumbnail and BrutalCredit draw small pictures as coloured rectangles. BORROWED: ImGui 1.92's managed-texture
// protocol (WantCreate, WantUpdates with UpdateRect and Updates[], WantDestroy after UnusedFrames) as the BRUTAL backends
// read it; INVENTED: counting UnusedFrames for a retired user texture, which ImGui counts only for its own font atlas.

/// <summary>An RGBA picture ImGui can draw, re-uploaded only when its pixels change. UI thread only. A retired picture
/// is freed by <see cref="Tick"/> once the renderer has let go of it; the editor calls that every frame.</summary>
public sealed unsafe class PictureTexture : IDisposable
{
    private static readonly List<(nint Texture, ImGuiContextPtr Context)> Retiring = [];
    private static (ImGuiContextPtr Context, int Frame) lastTick;
    private ImTextureData* texture;
    private ImGuiContextPtr context;
    private long version = -1;

    /// <summary>The picture's size in pixels; 0 before the first <see cref="Update"/>.</summary>
    public int Width => texture is null ? 0 : texture->Width;

    /// <summary>See <see cref="Width"/>.</summary>
    public int Height => texture is null ? 0 : texture->Height;

    /// <summary>Where the renderer is with the texture, for tests.</summary>
    internal (ImTextureStatus Status, long Id) State => texture is null ? (ImTextureStatus.Destroyed, 0) : (texture->Status, (long)texture->TexID.Value);

    /// <summary>How many retired pictures wait for the renderer to let go of them, for tests.</summary>
    internal static int Waiting => Retiring.Count;

    /// <summary>What to hand ImGui's draw list.</summary>
    // Built here rather than with ImTextureData.GetTexRef(): that binding (BRUTAL ImGui 2026.10.2) comes back with the
    // texture's address in the id field, which ImGui then takes for a renderer id (found by the headless panel test).
    public ImTextureRef Ref => new() { _TexData = texture, _TexID = default };

    /// <summary>Copies <paramref name="rgba"/> (straight RGBA, 8 bits each, top row first) in when
    /// <paramref name="newVersion"/> differs from the last one copied, and asks the renderer to upload it. A new size
    /// makes a new texture and retires the old one.</summary>
    public void Update(int width, int height, ReadOnlySpan<byte> rgba, long newVersion)
    {
        if (width < 1 || height < 1 || width > ushort.MaxValue || height > ushort.MaxValue || rgba.Length != width * height * 4) return;
        if (texture is not null && (texture->Width != width || texture->Height != height || !context.Equals(Im.GetCurrentContext()))) Retire();
        if (texture is null)
        {
            texture = (ImTextureData*)NativeMemory.AllocZeroed((nuint)sizeof(ImTextureData));
            texture->Status = ImTextureStatus.Destroyed;
            ((ImTextureDataPtr)texture).Create(ImTextureFormat.RGBA32, width, height);
            context = Im.GetCurrentContext();
            Im.Internal.RegisterUserTexture(texture);
            version = -1;
        }
        if (newVersion == version) return;
        version = newVersion;
        rgba.CopyTo(new Span<byte>((void*)((ImTextureDataPtr)texture).GetPixels(), rgba.Length));
        if (texture->Status == ImTextureStatus.OK)
        {
            // The whole picture, as one region: Vulkan's backend reads UpdateRect, OpenGL's reads Updates[].
            var all = new ImTextureRect { x = 0, y = 0, w = (ushort)width, h = (ushort)height };
            texture->UpdateRect = all;
            texture->Updates.Reset();
            texture->Updates.Add(all);
            ((ImTextureDataPtr)texture).SetStatus(ImTextureStatus.WantUpdates);
        }
        // WantCreate uploads all of it; a pending WantUpdates already covers all of it.
    }

    // Asks the renderer to destroy the texture once it is no longer drawn; Tick frees it after.
    private void Retire()
    {
        if (texture is null) return;
        if (!context.Equals(Im.GetCurrentContext())) Retiring.Add(((nint)texture, context));   // freed when its ImGui is current
        else if (texture->TexID.Value == 0) Free(texture);   // never made on the graphics side
        else
        {
            texture->UnusedFrames = 0;
            ((ImTextureDataPtr)texture).SetStatus(ImTextureStatus.WantDestroy);
            Retiring.Add(((nint)texture, context));
        }
        texture = null;
    }

    /// <summary>Counts frames for retired pictures and frees each one the renderer has let go of. Call once a frame,
    /// with the window's ImGui current; pictures of another ImGui wait for that one.</summary>
    public static void Tick()
    {
        if (Retiring.Count == 0) return;
        var current = Im.GetCurrentContext();
        if (current.Equals(lastTick.Context) && Im.GetFrameCount() == lastTick.Frame) return;   // once a frame, however many callers
        lastTick = (current, Im.GetFrameCount());
        for (var i = Retiring.Count - 1; i >= 0; i--)
        {
            var (pointer, owner) = Retiring[i];
            if (!owner.Equals(current)) continue;
            var t = (ImTextureData*)pointer;
            if (t->Status == ImTextureStatus.Destroyed || t->TexID.Value == 0) { Retiring.RemoveAt(i); Free(t); }
            else
            {
                if (t->Status != ImTextureStatus.WantDestroy) ((ImTextureDataPtr)t).SetStatus(ImTextureStatus.WantDestroy);
                t->UnusedFrames++;
            }
        }
    }

    private static void Free(ImTextureData* t)
    {
        Im.Internal.UnregisterUserTexture(t);
        ((ImTextureDataPtr)t).DestroyPixels();
        t->Updates.Clear();
        NativeMemory.Free(t);
    }

    /// <summary>Retires the texture; the renderer destroys it over the next frames and <see cref="Tick"/> frees it.</summary>
    public void Dispose() => Retire();
}
