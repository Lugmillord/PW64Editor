namespace PW64Editor.Core.Text.Preview;

/// <summary>A color with transparency (0 = invisible, 255 = opaque).</summary>
public readonly record struct ScreenColor(byte R, byte G, byte B, byte A = 255)
{
    public static ScreenColor Gray(byte value, byte alpha = 255) => new(value, value, value, alpha);
}

/// <summary>
/// A rectangle in screen pixels: (0, 0) is the top left corner of the 320 × 240 picture.
/// Right and Bottom are the first column and row outside the rectangle.
/// </summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>
    /// Converts a rectangle given in the game's own coordinates, where y counts upwards from the
    /// bottom of the screen (as used by the box drawing functions of the game).
    /// </summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Bottom edge, counted from the bottom of the screen.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    public static ScreenRect FromGame(int x, int y, int width, int height) =>
        new(x, ScreenCanvas.Height - (y + height), x + width, ScreenCanvas.Height - y);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public ScreenRect Inflate(int amount) => new(Left - amount, Top - amount, Right + amount, Bottom + amount);
}

/// <summary>
/// The picture of the game screen: 320 × 240 pixels, the resolution Pilotwings 64 draws at.
/// Pixels are stored as 32-bit BGRA (blue, green, red, alpha), row by row from the top, which
/// is the layout WPF and most image libraries expect.
/// </summary>
public sealed class ScreenCanvas
{
    public const int Width = 320;
    public const int Height = 240;

    public ScreenCanvas()
    {
        Pixels = new byte[Width * Height * 4];
    }

    /// <summary>The pixels, 4 bytes each (B, G, R, A).</summary>
    public byte[] Pixels { get; }

    /// <summary>Blends one pixel over the picture. Pixels outside the screen are ignored.</summary>
    public void Blend(int x, int y, ScreenColor color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || color.A == 0)
        {
            return;
        }

        int i = (y * Width + x) * 4;
        if (color.A == 255)
        {
            Pixels[i] = color.B;
            Pixels[i + 1] = color.G;
            Pixels[i + 2] = color.R;
        }
        else
        {
            int a = color.A, inv = 255 - a;
            Pixels[i] = (byte)((color.B * a + Pixels[i] * inv) / 255);
            Pixels[i + 1] = (byte)((color.G * a + Pixels[i + 1] * inv) / 255);
            Pixels[i + 2] = (byte)((color.R * a + Pixels[i + 2] * inv) / 255);
        }

        Pixels[i + 3] = 255;
    }

    /// <summary>Fills a rectangle (blended if the color is partly transparent).</summary>
    public void Fill(ScreenRect rect, ScreenColor color)
    {
        for (int y = Math.Max(0, rect.Top); y < Math.Min(Height, rect.Bottom); y++)
        {
            for (int x = Math.Max(0, rect.Left); x < Math.Min(Width, rect.Right); x++)
            {
                Blend(x, y, color);
            }
        }
    }

    /// <summary>Draws the outline of a rectangle, one pixel wide.</summary>
    /// <param name="dashed">Leaves every other pair of pixels out.</param>
    public void Outline(ScreenRect rect, ScreenColor color, bool dashed = false)
    {
        for (int x = rect.Left; x < rect.Right; x++)
        {
            if (!dashed || (x / 2) % 2 == 0)
            {
                Blend(x, rect.Top, color);
                Blend(x, rect.Bottom - 1, color);
            }
        }

        for (int y = rect.Top + 1; y < rect.Bottom - 1; y++)
        {
            if (!dashed || (y / 2) % 2 == 0)
            {
                Blend(rect.Left, y, color);
                Blend(rect.Right - 1, y, color);
            }
        }
    }

    /// <summary>Fills a rectangle with a vertical gradient from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    public void VerticalGradient(ScreenRect rect, ScreenColor top, ScreenColor bottom)
    {
        int rows = Math.Max(1, rect.Height - 1);
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            double f = (double)(y - rect.Top) / rows;
            var color = new ScreenColor(Mix(top.R, bottom.R, f), Mix(top.G, bottom.G, f), Mix(top.B, bottom.B, f), Mix(top.A, bottom.A, f));
            Fill(new ScreenRect(rect.Left, y, rect.Right, y + 1), color);
        }
    }

    private static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
}
