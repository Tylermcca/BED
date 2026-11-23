using Brutal.OpenGlApi;
using SceneStarter.Garden;
using GL = Brutal.OpenGlApi.OpenGl.Native;

namespace PhysicsStarter;

internal sealed unsafe class ToyControlsOverlay : IDisposable
{
    private static readonly string[] controls =
    [
        "LEFT CLICK: DROP CRATE",
        "RIGHT DRAG OR A/D: TURN VIEW",
        "MOUSE WHEEL: ZOOM",
        "SPACE: JUMP CRATE",
        "R: RESET  ESC: QUIT",
        "F1: HIDE CONTROLS"
    ];
    private static readonly string[] hint = ["F1: SHOW CONTROLS"];
    private readonly GlFramebuffer frame;
    private readonly GlTexture texture;
    private int width, height, drawnScale;
    private bool drawnControls;

    internal ToyControlsOverlay()
    {
        GlFramebuffer newFrame;
        GlTexture newTexture;
        GL.glGenFramebuffers(1, &newFrame);
        GL.glGenTextures(1, &newTexture);
        frame = newFrame;
        texture = newTexture;
        // A texture name must be bound before it has an image target.
        GL.glBindTexture(OpenGl.GL_TEXTURE_2D, texture.Id);
        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER, frame);
        GL.glFramebufferTexture2D(OpenGl.GL_FRAMEBUFFER, OpenGl.GL_COLOR_ATTACHMENT0, OpenGl.GL_TEXTURE_2D, texture.Id, 0);
        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER, default);
    }

    internal void Draw(GlFramebuffer destination, int frameWidth, int frameHeight, bool expanded)
    {
        var lines = expanded ? controls : hint;
        var baseWidth = lines.Max(line => PixelFont.Measure(line, 1)) + 16;
        var baseHeight = lines.Length * 10 + 12;
        var scale = Math.Min(Math.Clamp(frameHeight / 320, 1, 3),
            Math.Min((frameWidth - 16) / baseWidth, (frameHeight - 16) / baseHeight));
        if (scale < 1) return;
        if (scale != drawnScale || expanded != drawnControls)
        {
            width = baseWidth * scale;
            height = baseHeight * scale;
            var pixels = new uint[width * height];
            Array.Fill(pixels, 0xFF17212Bu);
            for (var i = 0; i < lines.Length; i++)
                PixelFont.Draw(pixels, width, height, lines[i], 8 * scale, (6 + i * 10) * scale, scale, 0xFFF5F7FA);
            GL.glBindTexture(OpenGl.GL_TEXTURE_2D, texture.Id);
            fixed (uint* data = pixels)
                GL.glTexImage2D(OpenGl.GL_TEXTURE_2D, 0, (int)OpenGl.GL_RGBA8, width, height, 0,
                    OpenGl.GL_BGRA, OpenGl.GL_UNSIGNED_BYTE, data);
            drawnScale = scale;
            drawnControls = expanded;
        }

        const int inset = 8;
        GL.glBindFramebuffer(OpenGl.GL_READ_FRAMEBUFFER, frame);
        GL.glBindFramebuffer(OpenGl.GL_DRAW_FRAMEBUFFER, destination);
        // PixelFont draws from the top down. Flip its rows when copying onto the game picture.
        GL.glBlitFramebuffer(0, height, width, 0, inset, inset, inset + width, inset + height,
            OpenGl.GL_COLOR_BUFFER_BIT, OpenGl.GL_NEAREST);
        GL.glBindFramebuffer(OpenGl.GL_FRAMEBUFFER, default);
    }

    public void Dispose()
    {
        var ownedFrame = frame;
        var ownedTexture = texture;
        GL.glDeleteFramebuffers(1, &ownedFrame);
        GL.glDeleteTextures(1, &ownedTexture);
    }
}
