namespace PW64Editor.Core.Textures;

/// <summary>
/// An image with 8 bits each for red, green, blue and alpha (not premultiplied), row by row from
/// the top left. The common format between the game's textures and image files.
/// </summary>
public sealed class RgbaImage
{
    /// <summary>Creates an image from pixel data (4 bytes per pixel: R, G, B, A).</summary>
    /// <exception cref="ArgumentException">The data does not fit the size.</exception>
    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("An image must be at least 1 × 1 pixels.");
        }

        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException($"An image of {width} × {height} pixels needs {width * height * 4} bytes, not {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Creates an empty (fully transparent black) image.</summary>
    public RgbaImage(int width, int height)
        : this(width, height, new byte[Math.Max(width, 1) * Math.Max(height, 1) * 4])
    {
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixels: R, G, B, A for each pixel, rows from top to bottom.</summary>
    public byte[] Pixels { get; }

    /// <summary>The pixel at (x, y).</summary>
    public (byte R, byte G, byte B, byte A) this[int x, int y]
    {
        get
        {
            int i = (y * Width + x) * 4;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }

        set
        {
            int i = (y * Width + x) * 4;
            Pixels[i] = value.R;
            Pixels[i + 1] = value.G;
            Pixels[i + 2] = value.B;
            Pixels[i + 3] = value.A;
        }
    }

    /// <summary>True if both images have the same size and the same pixels.</summary>
    public bool SamePixels(RgbaImage other) =>
        Width == other.Width && Height == other.Height && Pixels.AsSpan().SequenceEqual(other.Pixels);

    /// <summary>
    /// True if both images look the same: same size, and every pixel is equal or fully
    /// transparent in both (the color of an invisible pixel does not matter; many image
    /// programs do not keep it).
    /// </summary>
    public bool LooksLike(RgbaImage other)
    {
        if (Width != other.Width || Height != other.Height)
        {
            return false;
        }

        for (int i = 0; i < Pixels.Length; i += 4)
        {
            bool bothInvisible = Pixels[i + 3] == 0 && other.Pixels[i + 3] == 0;
            if (!bothInvisible && !Pixels.AsSpan(i, 4).SequenceEqual(other.Pixels.AsSpan(i, 4)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A smaller (or equally large) copy: every new pixel is the average of the pixels it covers.
    /// Colors are weighted by their alpha, so transparent pixels do not darken the edges.
    /// </summary>
    public RgbaImage Downscale(int width, int height)
    {
        var result = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int y0 = y * Height / height;
            int y1 = Math.Max(y0 + 1, (y + 1) * Height / height);
            for (int x = 0; x < width; x++)
            {
                int x0 = x * Width / width;
                int x1 = Math.Max(x0 + 1, (x + 1) * Width / width);

                long r = 0, g = 0, b = 0, a = 0, plainR = 0, plainG = 0, plainB = 0;
                int count = 0;
                for (int sy = y0; sy < y1 && sy < Height; sy++)
                {
                    for (int sx = x0; sx < x1 && sx < Width; sx++)
                    {
                        (byte pr, byte pg, byte pb, byte pa) = this[sx, sy];
                        r += pr * pa;
                        g += pg * pa;
                        b += pb * pa;
                        a += pa;
                        plainR += pr;
                        plainG += pg;
                        plainB += pb;
                        count++;
                    }
                }

                result[x, y] = a > 0
                    ? ((byte)((r + a / 2) / a), (byte)((g + a / 2) / a), (byte)((b + a / 2) / a), (byte)((a + count / 2) / count))
                    : ((byte)(plainR / count), (byte)(plainG / count), (byte)(plainB / count), (byte)0);
            }
        }

        return result;
    }
}
