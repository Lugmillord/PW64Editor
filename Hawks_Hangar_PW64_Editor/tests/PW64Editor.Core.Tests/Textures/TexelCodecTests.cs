using PW64Editor.Core.Textures;

namespace PW64Editor.Core.Tests.Textures;

public class TexelCodecTests
{
    private static readonly TextureFormat[] Formats =
        [TextureFormat.Rgba16, TextureFormat.Ia4, TextureFormat.Ia8, TextureFormat.Ia16, TextureFormat.I4, TextureFormat.I8];

    // A test image with all kinds of colors and alpha values.
    private static RgbaImage Pattern(int width, int height)
    {
        var image = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = ((byte)(x * 37 + y * 11), (byte)(x * 5 + y * 71), (byte)(x * y * 13), (byte)((x + y) * 29));
            }
        }

        return image;
    }

    // Rows padded to whole 64-bit words, as the game stores them.
    private static TextureTile Tile(TextureFormat format, int width, int height, int tmem = 0)
    {
        int rowBytes = (width * TextureFormats.BitsPerTexel(format) / 8 + 7) / 8 * 8;
        return new TextureTile(0, format, rowBytes / 8, tmem, width, height);
    }

    [Fact]
    public void EncodeThenDecode_GivesTheQuantizedImage()
    {
        RgbaImage image = Pattern(16, 6);
        foreach (TextureFormat format in Formats)
        {
            TextureTile tile = Tile(format, 16, 6);
            byte[] data = new byte[tile.RowBytes * 6];
            TexelCodec.Encode(image, data, tile);

            Assert.True(TexelCodec.Quantize(image, format).SamePixels(TexelCodec.Decode(data, tile)), format.ToString());
        }
    }

    [Fact]
    public void Quantize_OfAStoredImage_ChangesNothing()
    {
        RgbaImage image = Pattern(16, 4);
        foreach (TextureFormat format in Formats)
        {
            RgbaImage once = TexelCodec.Quantize(image, format);
            Assert.True(once.SamePixels(TexelCodec.Quantize(once, format)), format.ToString());
        }
    }

    [Fact]
    public void AllStoredValues_SurviveDecodingAndEncoding()
    {
        // Every possible 16-bit value of an RGBA16 texel and every byte of the 8-bit formats.
        foreach ((TextureFormat format, int width) in new[] { (TextureFormat.Rgba16, 256), (TextureFormat.Ia8, 256), (TextureFormat.I8, 256), (TextureFormat.Ia4, 512), (TextureFormat.I4, 512) })
        {
            int height = format == TextureFormat.Rgba16 ? 256 : 1;
            TextureTile tile = Tile(format, width, height);
            byte[] data = new byte[tile.RowBytes * height];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(format == TextureFormat.Rgba16 ? (i % 2 == 0 ? i / 512 : i / 2) : i);
            }

            byte[] copy = new byte[data.Length];
            TexelCodec.Encode(TexelCodec.Decode(data, tile), copy, tile);

            Assert.Equal(data, copy);
        }
    }

    [Fact]
    public void OddRows_HaveTheirWordHalvesSwapped()
    {
        // 4 × 2 texels RGBA16: one 8-byte row each. The second row is stored with its 4-byte halves swapped.
        var image = new RgbaImage(4, 2);
        for (int x = 0; x < 4; x++)
        {
            image[x, 0] = (255, 255, 255, 255);
        }

        image[0, 1] = (255, 0, 0, 255);
        TextureTile tile = new(0, TextureFormat.Rgba16, 1, 0, 4, 2);
        byte[] data = new byte[16];
        TexelCodec.Encode(image, data, tile);

        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], data[..8]);
        Assert.Equal([0, 0, 0, 0, 0xF8, 0x01, 0, 0], data[8..]);
    }

    [Fact]
    public void Encode_LeavesBytesOutsideTheTileAlone()
    {
        TextureTile tile = new(0, TextureFormat.I8, 1, 1, 4, 1); // 4 texels at byte 8, row of 8 bytes
        byte[] data = Enumerable.Repeat((byte)0xAA, 24).ToArray();
        TexelCodec.Encode(new RgbaImage(4, 1, Enumerable.Repeat((byte)0, 16).ToArray()), data, tile);

        Assert.All(data[..8], b => Assert.Equal(0xAA, b));
        Assert.All(data[8..12], b => Assert.Equal(0, b));
        Assert.All(data[12..], b => Assert.Equal(0xAA, b));
    }

    [Fact]
    public void Greyscale_UsesLuminanceAndKeepsAlpha()
    {
        var image = new RgbaImage(1, 1, [255, 0, 0, 200]);

        Assert.Equal((76, 76, 76, 200), ToTuple(TexelCodec.Quantize(image, TextureFormat.Ia16)));
        Assert.Equal((76, 76, 76, 255), ToTuple(TexelCodec.Quantize(image, TextureFormat.I8)));
        Assert.Equal((255, 0, 0, 255), ToTuple(TexelCodec.Quantize(image, TextureFormat.Rgba16)));
        Assert.Equal(0, TexelCodec.Quantize(new RgbaImage(1, 1, [255, 0, 0, 127]), TextureFormat.Rgba16).Pixels[3]);
    }

    [Fact]
    public void Downscale_AveragesAndIgnoresColorOfTransparentPixels()
    {
        // 2 × 1: an opaque red pixel and a transparent green one.
        var image = new RgbaImage(2, 1, [255, 0, 0, 255, 0, 255, 0, 0]);
        Assert.Equal((255, 0, 0, 128), ToTuple(image.Downscale(1, 1)));
    }

    [Fact]
    public void LooksLike_IgnoresTheColorOfInvisiblePixels()
    {
        var a = new RgbaImage(2, 1, [1, 2, 3, 0, 9, 9, 9, 255]);
        Assert.True(a.LooksLike(new RgbaImage(2, 1, [0, 0, 0, 0, 9, 9, 9, 255])));
        Assert.False(a.LooksLike(new RgbaImage(2, 1, [1, 2, 3, 1, 9, 9, 9, 255])));
        Assert.False(a.LooksLike(new RgbaImage(2, 1, [1, 2, 3, 0, 9, 9, 8, 255])));
    }

    private static (int, int, int, int) ToTuple(RgbaImage image) => (image.Pixels[0], image.Pixels[1], image.Pixels[2], image.Pixels[3]);
}
