using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// Runs ImGui UI with no window, no GPU and no OS input, for tests and tools (goal 1: UI checked like any other code).
/// It owns a real ImGui context, feeds it scripted mouse and keyboard input, and draws each frame on the CPU into an
/// image that can be saved as a PNG and looked at. Nothing appears on screen and focus is never taken. INVENTED for this
/// library; the event calls are ImGui's own backend API, and the rasteriser follows ImGui's draw-list contract (textured,
/// vertex-coloured triangles, clip rectangles, alpha blending). Only one host may exist at a time: ImGui's current
/// context is process-wide. Its clipboard is its own, in memory: copy and paste in a headless run never read or replace
/// the person's clipboard, and two runs at once can't race over it.
/// </summary>
public sealed unsafe class HeadlessHost : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly Dictionary<ulong, (int Width, int Height, byte[] Rgba)> textures = [];
    private ulong nextTexture = 1;
    private readonly HeadlessStartupTiming? startupTiming;

    /// <summary>A host with a display of <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    public HeadlessHost(int width = 1280, int height = 800)
    {
        startupTiming=HeadlessStartupTiming.Create();
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
        context = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default;
        io.LogFilename = default;
        io.DisplaySize = new float2(width, height);
        io.DeltaTime = 1f / 60;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures | ImGuiBackendFlags.RendererHasVtxOffset;
        io.Fonts.AddFontDefault();
        EditorStyle.Apply();   // the window's own look, so renders measure what people see
        // ImGui's default clipboard on Windows is the system's, shared with the person and with every other run: tests
        // overwrote what people had copied, and parallel runs raced over it (an intermittent paste failure).
        var platform = ImGui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = (nint)(delegate* unmanaged[Cdecl]<nint, byte*>)&GetClipboard;
        platform.Platform_SetClipboardTextFn = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, void>)&SetClipboard;
        clipboard = Marshal.StringToCoTaskMemUTF8("");
        startupTiming?.Mark("headless_context_ms");
    }

    /// <summary>The display width in pixels.</summary>
    public int Width { get; }

    /// <summary>The display height in pixels.</summary>
    public int Height { get; }

    /// <summary>The last frame drawn, as 0xAABBGGRR pixels, row by row from the top.</summary>
    public uint[] Pixels { get; }

    /// <summary>Whether frames are drawn into <see cref="Pixels"/>. Turn it off for long scripted runs and back on (then
    /// run a frame) before looking; input and UI logic work the same either way.</summary>
    public bool Drawing { get; set; } = true;

    /// <summary>The background colour frames are drawn over (0xAABBGGRR).</summary>
    public uint Background { get; set; } = 0xFF202020;

    /// <summary>Runs one frame of <paramref name="draw"/> and rasterises it into <see cref="Pixels"/>.</summary>
    public void Frame(Action draw)
    {
        startupTiming?.Mark("before_frame_ms");
        ImGui.SetCurrentContext(context);
        ImGuiFrame.Begin();
        startupTiming?.Mark("begin_frame_ms");
        draw();
        startupTiming?.Mark("draw_ui_ms");
        ImGui.Render();
        UiTree.EndFrame();
        startupTiming?.Mark("imgui_render_ms");
        if (Drawing) Rasterise(ImGui.GetDrawData());
        else UpdateTextures(ImGui.GetDrawData());   // ImGui still needs its texture requests answered
        startupTiming?.Mark("raster_ms");
        startupTiming?.EndFrame(Drawing,ImGui.GetDrawData().TotalVtxCount,Width,Height);
    }

    /// <summary>Runs <paramref name="count"/> frames, letting layout and popups settle.</summary>
    public void Frames(Action draw, int count = 3)
    {
        for (var i = 0; i < count; i++) Frame(draw);
    }

    /// <summary>Moves the mouse to (<paramref name="x"/>, <paramref name="y"/>), then runs a frame.</summary>
    public void MoveMouse(Action draw, float x, float y)
    {
        ImGui.GetIO().AddMousePosEvent(x, y);
        Frame(draw);
    }

    /// <summary>Clicks the left mouse button at (<paramref name="x"/>, <paramref name="y"/>): move, press, release,
    /// each in its own frame as a person's click would arrive, then one more frame so <see cref="Pixels"/> shows what
    /// the click did (UI reacts partway through the frame it's handled in).</summary>
    public void Click(Action draw, float x, float y)
    {
        var io = ImGui.GetIO();
        io.AddMousePosEvent(x, y);
        Frame(draw);
        io.AddMouseButtonEvent(0, true);
        Frame(draw);
        io.AddMouseButtonEvent(0, false);
        Frame(draw);
        Frame(draw);
    }

    /// <summary>Drags with mouse <paramref name="button"/> (0 left, 1 right, 2 middle) from <paramref name="from"/> to
    /// <paramref name="to"/>: press, move in <paramref name="steps"/> steps (a frame each), release, then one settled frame.</summary>
    public void Drag(Action draw, float2 from, float2 to, int button = 0, int steps = 8)
    {
        var io = ImGui.GetIO();
        io.AddMousePosEvent(from.X, from.Y);
        Frame(draw);
        io.AddMouseButtonEvent(button, true);
        Frame(draw);
        for (var i = 1; i <= steps; i++)
        {
            io.AddMousePosEvent(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            Frame(draw);
        }
        io.AddMouseButtonEvent(button, false);
        Frame(draw);
        Frame(draw);
    }

    /// <summary>Presses and releases <paramref name="key"/> with the modifiers held, over two frames.</summary>
    public void Press(Action draw, ImGuiKey key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var io = ImGui.GetIO();
        void Mods(bool down)
        {
            if (ctrl) io.AddKeyEvent(ImGuiKey.Mod_Ctrl, down);
            if (shift) io.AddKeyEvent(ImGuiKey.Mod_Shift, down);
            if (alt) io.AddKeyEvent(ImGuiKey.Mod_Alt, down);
        }
        Mods(true);
        io.AddKeyEvent(key, true);
        Frame(draw);
        io.AddKeyEvent(key, false);
        Mods(false);
        Frame(draw);
        Frame(draw);
    }

    /// <summary>Types <paramref name="text"/> into the focused field, then runs a frame.</summary>
    public void Type(Action draw, string text)
    {
        var io = ImGui.GetIO();
        foreach (var rune in text.EnumerateRunes()) io.AddInputCharacter((uint)rune.Value);
        Frame(draw);
    }

    /// <summary>Saves the last frame as a PNG.</summary>
    public void Save(string path) => File.WriteAllBytes(path, Png.Encode(Width, Height, Pixels));

    /// <summary>How many pixels of the last frame differ from the background: a quick "something was drawn here" check.</summary>
    public int Covered(int x0 = 0, int y0 = 0, int x1 = int.MaxValue, int y1 = int.MaxValue)
    {
        var n = 0;
        for (var y = Math.Max(0, y0); y < Math.Min(Height, y1); y++)
            for (var x = Math.Max(0, x0); x < Math.Min(Width, x1); x++)
                if (Pixels[y * Width + x] != Background) n++;
        return n;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ImGui.DestroyContext(context);
        Marshal.FreeCoTaskMem(clipboard);
        clipboard = 0;
    }

    // The clipboard's text, as UTF-8 that ImGui reads in place. Only one host exists at a time, so one buffer serves.
    private static nint clipboard;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte* GetClipboard(nint context) => (byte*)clipboard;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetClipboard(nint context, byte* text)
    {
        var copy = Marshal.StringToCoTaskMemUTF8(Marshal.PtrToStringUTF8((nint)text) ?? "");
        Marshal.FreeCoTaskMem(clipboard);
        clipboard = copy;
    }

    // ---- the rasteriser ----

    private void Rasterise(ImDrawDataPtr data)
    {
        UpdateTextures(data);
        Array.Fill(Pixels, Background);
        var origin = data.DisplayPos;
        for (var l = 0; l < data.CmdListsCount; l++)
        {
            var list = data.CmdLists[l];
            var vtx = list.VtxBuffer;
            var idx = list.IdxBuffer;
            for (var c = 0; c < list.CmdBuffer.Count; c++)
            {
                var cmd = list.CmdBuffer[c];
                if (cmd.UserCallback != 0) continue;
                var clip = cmd.ClipRect;
                var cx0 = (int)MathF.Max(0, clip.X - origin.X);
                var cy0 = (int)MathF.Max(0, clip.Y - origin.Y);
                var cx1 = (int)MathF.Min(Width, clip.Z - origin.X);
                var cy1 = (int)MathF.Min(Height, clip.W - origin.Y);
                if (cx1 <= cx0 || cy1 <= cy0) continue;
                textures.TryGetValue((ulong)cmd.GetTexID().Value, out var tex);
                for (var e = 0; e < cmd.ElemCount; e += 3)
                {
                    var a = vtx[(int)(cmd.VtxOffset + idx[(int)(cmd.IdxOffset + e)])];
                    var b = vtx[(int)(cmd.VtxOffset + idx[(int)(cmd.IdxOffset + e + 1)])];
                    var d = vtx[(int)(cmd.VtxOffset + idx[(int)(cmd.IdxOffset + e + 2)])];
                    Triangle(a, b, d, origin, cx0, cy0, cx1, cy1, tex.Rgba is null ? default : tex);
                }
            }
        }
    }

    private void Triangle(ImDrawVert a, ImDrawVert b, ImDrawVert c, float2 o, int cx0, int cy0, int cx1, int cy1,
        (int Width, int Height, byte[] Rgba) tex)
    {
        float ax = a.pos.X - o.X, ay = a.pos.Y - o.Y, bx = b.pos.X - o.X, by = b.pos.Y - o.Y, qx = c.pos.X - o.X, qy = c.pos.Y - o.Y;
        var area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
        if (MathF.Abs(area) < 1e-6f) return;
        if (area < 0)   // one winding, so the fill rule below means the same edges for every triangle
        {
            (b, c) = (c, b);
            (bx, by, qx, qy) = (qx, qy, bx, by);
            area = -area;
        }
        // The top-left rule, as GPUs use: a pixel exactly on an edge belongs to the triangle that has it as a top or left
        // edge, so two triangles sharing an edge never both blend it (found by A067/A070: translucent pixels on a
        // rectangle's diagonal were drawn twice, darkening the BRUTAL logo's soft edges).
        static bool TopLeft(float px, float py, float qx2, float qy2) => qy2 < py || (qy2 == py && qx2 > px);
        bool e0 = TopLeft(bx, by, qx, qy), e1 = TopLeft(qx, qy, ax, ay), e2 = TopLeft(ax, ay, bx, by);
        var x0 = Math.Max(cx0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, qx))));
        var x1 = Math.Min(cx1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, qx))));
        var y0 = Math.Max(cy0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, qy))));
        var y1 = Math.Min(cy1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, qy))));
        var flat = a.col == b.col && b.col == c.col;
        var flatColor = Unpack(a.col);
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                // Sample just off the pixel centre: an edge lying exactly on a centre (a 55.5-pixel square, say) would
                // otherwise fall in or out by rounding noise, pixel by pixel, and look jagged. GPUs break such ties
                // with a fixed rule too (the top-left rule).
                float px = x + 0.5f + 1f / 512, py = y + 0.5f + 1f / 512;
                var w0 = ((bx - px) * (qy - py) - (by - py) * (qx - px)) / area;
                var w1 = ((qx - px) * (ay - py) - (qy - py) * (ax - px)) / area;
                var w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0 || w0 == 0 && !e0 || w1 == 0 && !e1 || w2 == 0 && !e2) continue;
                var (r, g, bl, al) = flat ? flatColor : Mix(a.col, b.col, c.col, w0, w1, w2);
                if (tex.Rgba is { } t)
                {
                    var u = a.uv.X * w0 + b.uv.X * w1 + c.uv.X * w2;
                    var v = a.uv.Y * w0 + b.uv.Y * w1 + c.uv.Y * w2;
                    var tx = Math.Clamp((int)(u * tex.Width), 0, tex.Width - 1);
                    var ty = Math.Clamp((int)(v * tex.Height), 0, tex.Height - 1);
                    var i = (ty * tex.Width + tx) * 4;
                    r *= t[i] / 255f; g *= t[i + 1] / 255f; bl *= t[i + 2] / 255f; al *= t[i + 3] / 255f;
                }
                if (al <= 0) continue;
                ref var dst = ref Pixels[y * Width + x];
                // Opaque coverage replaces the destination. Keep the same channel packing while
                // avoiding a read/unpack and six blend operations for solid UI surfaces/glyphs.
                if (al == 1) dst = Pack(r, g, bl, 1);
                else
                {
                    var (dr, dg, db, _) = Unpack(dst);
                    dst = Pack(r * al + dr * (1 - al), g * al + dg * (1 - al), bl * al + db * (1 - al), 1);
                }
            }
    }

    private static (float R, float G, float B, float A) Unpack(uint c) =>
        ((c & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, ((c >> 16) & 0xFF) / 255f, (c >> 24) / 255f);

    private static (float, float, float, float) Mix(uint a, uint b, uint c, float w0, float w1, float w2)
    {
        var (ar, ag, ab, aa) = Unpack(a);
        var (br, bg, bb, ba) = Unpack(b);
        var (cr, cg, cb, ca) = Unpack(c);
        return (ar * w0 + br * w1 + cr * w2, ag * w0 + bg * w1 + cg * w2, ab * w0 + bb * w1 + cb * w2, aa * w0 + ba * w1 + ca * w2);
    }

    private static uint Pack(float r, float g, float b, float a) =>
        (uint)(Math.Clamp(r, 0, 1) * 255 + 0.5f) | (uint)(Math.Clamp(g, 0, 1) * 255 + 0.5f) << 8
        | (uint)(Math.Clamp(b, 0, 1) * 255 + 0.5f) << 16 | (uint)(Math.Clamp(a, 0, 1) * 255 + 0.5f) << 24;

    // ImGui asks the renderer to create, update and destroy textures (the font atlas among them); keep CPU copies.
    private void UpdateTextures(ImDrawDataPtr data)
    {
        if (data.Textures.IsNull()) return;
        ref var list = ref data.Textures.Value;
        for (var i = 0; i < list.Count; i++)
        {
            var t = list[i].Ptr;
            switch (t->Status)
            {
                case ImTextureStatus.WantCreate:
                case ImTextureStatus.WantUpdates:
                    var id = t->Status == ImTextureStatus.WantCreate ? nextTexture++ : (ulong)t->TexID.Value;
                    textures[id] = (t->Width, t->Height, Copy(t));
                    t->TexID = new ImTextureID((nint)id);
                    t->Status = ImTextureStatus.OK;
                    break;
                case ImTextureStatus.WantDestroy when t->UnusedFrames > 0:
                    textures.Remove((ulong)t->TexID.Value);
                    t->TexID = default;
                    t->Status = ImTextureStatus.Destroyed;
                    break;
            }
        }
    }

    private static byte[] Copy(ImTextureData* t)
    {
        var n = t->Width * t->Height;
        var rgba = new byte[n * 4];
        var src = (byte*)t->Pixels.Pointer;
        if (t->Format == ImTextureFormat.RGBA32) new ReadOnlySpan<byte>(src, n * 4).CopyTo(rgba);
        else
            for (var i = 0; i < n; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255; rgba[i * 4 + 3] = src[i]; }
        return rgba;
    }
}

/// <summary>A minimal PNG writer (8-bit RGBA, no filtering), so frames can be saved with no image library.</summary>
public static class Png
{
    private static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Encodes 0xAABBGGRR pixels, row by row from the top, as a PNG file.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<uint> pixels)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + width * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = pixels[y * width + x];
                    row[1 + x * 4] = (byte)p; row[2 + x * 4] = (byte)(p >> 8); row[3 + x * 4] = (byte)(p >> 16); row[4 + x * 4] = (byte)(p >> 24);
                }
                z.Write(row);
            }
        }
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        var header = new byte[13];
        BigEndian(header, 0, (uint)width);
        BigEndian(header, 4, (uint)height);
        header[8] = 8; header[9] = 6;   // 8 bits, RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, (uint)data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        s.Write(body);
        var c = 0xFFFFFFFFu;
        foreach (var b in body) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        var crc = new byte[4];
        BigEndian(crc, 0, c ^ 0xFFFFFFFF);
        s.Write(crc);
    }

    private static void BigEndian(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }
}
