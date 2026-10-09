namespace PW64Editor.Core.Textures;

/// <summary>
/// Reads and writes texels in the layout of the N64's texture memory (TMEM).
/// </summary>
/// <remarks>
/// <para>Pilotwings loads each texture into TMEM in one piece (G_LOADBLOCK without line
/// swapping), so the image data in the file already has the TMEM layout: every row starts at a
/// multiple of 8 bytes (<see cref="TextureTile.Line"/>), and in every odd row the two 4-byte
/// halves of each 8-byte word are swapped. The graphics chip swaps them back when it reads odd
/// rows (this lets it read two rows at once).</para>
/// <para>Expanding fewer bits to 8 repeats the bit pattern (e.g. 5 bits <c>abcde</c> become
/// <c>abcdeabc</c>), so white stays white and black stays black. Reducing rounds to the nearest
/// value. Greyscale is computed from color with the usual weights (0.299 R + 0.587 G + 0.114 B).
/// Pixels with less than half alpha become transparent in formats with 1 bit of transparency.</para>
/// </remarks>
public static class TexelCodec
{
    /// <summary>Reads the image of a tile. Texels beyond the end of the data are transparent black.</summary>
    public static RgbaImage Decode(ReadOnlySpan<byte> data, TextureTile tile)
    {
        var image = new RgbaImage(tile.Width, tile.Height);
        int bits = TextureFormats.BitsPerTexel(tile.Format);
        for (int y = 0; y < tile.Height; y++)
        {
            for (int x = 0; x < tile.Width; x++)
            {
                int address = Address(tile, bits, x, y);
                if (address < 0 || address + Math.Max(bits / 8, 1) > data.Length)
                {
                    continue;
                }

                image[x, y] = DecodeTexel(tile.Format, data, address, x);
            }
        }

        return image;
    }

    /// <summary>
    /// Writes an image into the data of a tile. The image must have the tile's size. Bytes
    /// outside the tile (row padding, other tiles) are left as they are.
    /// </summary>
    /// <exception cref="ArgumentException">Wrong size or unsupported format.</exception>
    public static void Encode(RgbaImage image, Span<byte> data, TextureTile tile)
    {
        if (image.Width != tile.Width || image.Height != tile.Height)
        {
            throw new ArgumentException($"The image has {image.Width} × {image.Height} pixels, the tile {tile.Width} × {tile.Height}.", nameof(image));
        }

        int bits = TextureFormats.BitsPerTexel(tile.Format);
        if (bits == 0)
        {
            throw new ArgumentException("This texel format cannot be written.", nameof(tile));
        }

        for (int y = 0; y < tile.Height; y++)
        {
            for (int x = 0; x < tile.Width; x++)
            {
                int address = Address(tile, bits, x, y);
                if (address < 0 || address + Math.Max(bits / 8, 1) > data.Length)
                {
                    continue;
                }

                EncodeTexel(tile.Format, data, address, x, image[x, y]);
            }
        }
    }

    /// <summary>The image as the game shows it after storing it in <paramref name="format"/>.</summary>
    public static RgbaImage Quantize(RgbaImage image, TextureFormat format)
    {
        var result = new RgbaImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                result[x, y] = QuantizeTexel(format, image[x, y]);
            }
        }

        return result;
    }

    private static int Address(TextureTile tile, int bits, int x, int y)
    {
        int address = tile.ByteOffset + y * tile.RowBytes + x * bits / 8;
        return (y & 1) == 1 ? address ^ 4 : address;
    }

    private static (byte, byte, byte, byte) DecodeTexel(TextureFormat format, ReadOnlySpan<byte> data, int address, int x)
    {
        switch (format)
        {
            case TextureFormat.Rgba16:
            {
                int value = (data[address] << 8) | data[address + 1];
                return (Expand5(value >> 11), Expand5(value >> 6), Expand5(value >> 1), (value & 1) != 0 ? (byte)255 : (byte)0);
            }

            case TextureFormat.Ia16:
                return Grey(data[address], data[address + 1]);
            case TextureFormat.Ia8:
                return Grey((byte)((data[address] >> 4) * 17), (byte)((data[address] & 15) * 17));
            case TextureFormat.Ia4:
            {
                int value = Nibble(data[address], x);
                return Grey(Expand3(value >> 1), (value & 1) != 0 ? (byte)255 : (byte)0);
            }

            case TextureFormat.I8:
                return Grey(data[address], 255);
            case TextureFormat.I4:
                return Grey((byte)(Nibble(data[address], x) * 17), 255);
            default:
                return (0, 0, 0, 0);
        }
    }

    private static void EncodeTexel(TextureFormat format, Span<byte> data, int address, int x, (byte R, byte G, byte B, byte A) pixel)
    {
        int grey = Luminance(pixel);
        switch (format)
        {
            case TextureFormat.Rgba16:
            {
                int value = (Reduce(pixel.R, 31) << 11) | (Reduce(pixel.G, 31) << 6) | (Reduce(pixel.B, 31) << 1) | (pixel.A >= 128 ? 1 : 0);
                data[address] = (byte)(value >> 8);
                data[address + 1] = (byte)value;
                break;
            }

            case TextureFormat.Ia16:
                data[address] = (byte)grey;
                data[address + 1] = pixel.A;
                break;
            case TextureFormat.Ia8:
                data[address] = (byte)((Reduce(grey, 15) << 4) | Reduce(pixel.A, 15));
                break;
            case TextureFormat.Ia4:
                SetNibble(data, address, x, (Reduce(grey, 7) << 1) | (pixel.A >= 128 ? 1 : 0));
                break;
            case TextureFormat.I8:
                data[address] = (byte)grey;
                break;
            case TextureFormat.I4:
                SetNibble(data, address, x, Reduce(grey, 15));
                break;
        }
    }

    private static (byte, byte, byte, byte) QuantizeTexel(TextureFormat format, (byte R, byte G, byte B, byte A) pixel)
    {
        int grey = Luminance(pixel);
        return format switch
        {
            TextureFormat.Rgba16 => (Expand5(Reduce(pixel.R, 31)), Expand5(Reduce(pixel.G, 31)), Expand5(Reduce(pixel.B, 31)),
                pixel.A >= 128 ? (byte)255 : (byte)0),
            TextureFormat.Ia16 => Grey((byte)grey, pixel.A),
            TextureFormat.Ia8 => Grey((byte)(Reduce(grey, 15) * 17), (byte)(Reduce(pixel.A, 15) * 17)),
            TextureFormat.Ia4 => Grey(Expand3(Reduce(grey, 7)), pixel.A >= 128 ? (byte)255 : (byte)0),
            TextureFormat.I8 => Grey((byte)grey, 255),
            TextureFormat.I4 => Grey((byte)(Reduce(grey, 15) * 17), 255),
            _ => pixel,
        };
    }

    private static (byte, byte, byte, byte) Grey(byte intensity, byte alpha) => (intensity, intensity, intensity, alpha);

    private static int Luminance((byte R, byte G, byte B, byte A) pixel) =>
        (pixel.R * 299 + pixel.G * 587 + pixel.B * 114 + 500) / 1000;

    /// <summary>Reduces 0-255 to 0-<paramref name="max"/>, rounded.</summary>
    private static int Reduce(int value, int max) => (value * max + 127) / 255;

    private static byte Expand5(int value)
    {
        value &= 31;
        return (byte)((value << 3) | (value >> 2));
    }

    private static byte Expand3(int value)
    {
        value &= 7;
        return (byte)((value << 5) | (value << 2) | (value >> 1));
    }

    /// <summary>4-bit texels: the even one is in the high nibble.</summary>
    private static int Nibble(byte value, int x) => (x & 1) == 0 ? value >> 4 : value & 15;

    private static void SetNibble(Span<byte> data, int address, int x, int value) =>
        data[address] = (x & 1) == 0
            ? (byte)((data[address] & 0x0F) | (value << 4))
            : (byte)((data[address] & 0xF0) | (value & 15));
}
