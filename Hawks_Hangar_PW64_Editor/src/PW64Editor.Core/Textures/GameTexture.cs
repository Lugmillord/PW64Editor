using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Textures;

/// <summary>
/// One texture of the game: the content of the COMM chunk of a UVTX file.
/// </summary>
/// <remarks>
/// <para>Layout (see _uvExpandTexture in the decompilation):</para>
/// <code>
/// 0x00  u16      size of the image data in bytes (at most 4096, the size of TMEM)
/// 0x02  u16      number of display list commands
/// 0x04  4 × f32  two texture scroll settings
/// 0x14  ...      image data: the content of TMEM after loading
/// ...   8 × n    display list: loads the image and describes its tiles (G_SETTILE, G_SETTILESIZE)
/// ...            width (u16), height (u16) and further settings
/// </code>
/// <para>The main image is the tile that starts at TMEM address 0. Many textures also have
/// smaller copies for distant surfaces (mipmaps): further tiles of the same format behind it.
/// Some textures are combined with a second texture that the game loads behind the image data;
/// those tiles belong to the other texture and are ignored here.</para>
/// </remarks>
public sealed class GameTexture
{
    private const int HeaderSize = 0x14;
    private const int LoadTile = 7;

    private readonly byte[] _data;
    private readonly int _imageSize;

    private GameTexture(byte[] data, int imageSize, int width, int height, IReadOnlyList<TextureTile> levels, TextureFormat format)
    {
        _data = data;
        _imageSize = imageSize;
        Width = width;
        Height = height;
        Levels = levels;
        Format = format;
    }

    /// <summary>Width of the main image in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the main image in pixels.</summary>
    public int Height { get; }

    /// <summary>The texel format.</summary>
    public TextureFormat Format { get; }

    /// <summary>The main image (first) and its smaller copies, as tiles in TMEM.</summary>
    public IReadOnlyList<TextureTile> Levels { get; }

    /// <summary>Number of smaller copies (mipmaps) behind the main image.</summary>
    public int MipmapCount => Levels.Count - 1;

    /// <summary>True if the editor can read and write the texture's format.</summary>
    public bool IsSupported => Format != TextureFormat.Unsupported && Levels.Count > 0;

    /// <summary>The COMM chunk's content as it is.</summary>
    public byte[] Data => _data.ToArray();

    private ReadOnlySpan<byte> Image => _data.AsSpan(HeaderSize, _imageSize);

    /// <summary>Reads a texture from the content of its COMM chunk.</summary>
    /// <exception cref="InvalidDataException">The data is too short or damaged.</exception>
    public static GameTexture Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(CoreText.T("The texture data is too short."));
        }

        int imageSize = BinaryPrimitives.ReadUInt16BigEndian(data);
        int commandCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        int commands = HeaderSize + imageSize;
        int trailer = commands + commandCount * 8;
        if (trailer + 4 > data.Length)
        {
            throw new InvalidDataException(CoreText.T("The texture data is too short."));
        }

        int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(trailer));
        int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(trailer + 2));

        // The tile descriptors as the display list sets them (the first setting of each tile counts).
        var formats = new Dictionary<int, (TextureFormat Format, int Line, int Tmem)>();
        var sizes = new Dictionary<int, (int Width, int Height)>();
        for (int i = 0; i < commandCount; i++)
        {
            ulong command = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(commands + i * 8));
            int tile = (int)((command >> 24) & 7);
            switch (command >> 56)
            {
                case 0xF5 when tile != LoadTile: // G_SETTILE
                    formats.TryAdd(tile, (TextureFormats.FromRdp((int)((command >> 53) & 7), (int)((command >> 51) & 3)),
                        (int)((command >> 41) & 0x1FF), (int)((command >> 32) & 0x1FF)));
                    break;
                case 0xF2 when tile != LoadTile: // G_SETTILESIZE, coordinates in 10.2 fixed point
                    int uls = (int)((command >> 44) & 0xFFF), ult = (int)((command >> 32) & 0xFFF);
                    int lrs = (int)((command >> 12) & 0xFFF), lrt = (int)(command & 0xFFF);
                    sizes.TryAdd(tile, (((lrs - uls) >> 2) + 1, ((lrt - ult) >> 2) + 1));
                    break;
            }
        }

        var levels = new List<TextureTile>();
        TextureFormat format = TextureFormat.Unsupported;
        int[] own = formats.Keys.Where(t => formats[t].Tmem == 0).Order().ToArray();
        if (own.Length > 0 && width > 0 && height > 0)
        {
            int main = own[0];
            (format, int line, _) = formats[main];
            levels.Add(new TextureTile(main, format, line, 0, width, height));

            // Smaller copies: later tiles of the same format inside the image data, each smaller than the one before.
            foreach (int tile in formats.Keys.Where(t => t > main).Order())
            {
                (TextureFormat tileFormat, int tileLine, int tmem) = formats[tile];
                if (tileFormat != format || tmem * 8 >= imageSize || !sizes.TryGetValue(tile, out var size))
                {
                    continue;
                }

                TextureTile previous = levels[^1];
                if (tmem <= previous.Tmem || size.Width > previous.Width || size.Height > previous.Height)
                {
                    continue;
                }

                levels.Add(new TextureTile(tile, format, tileLine, tmem, size.Width, size.Height));
            }
        }

        return new GameTexture(data, imageSize, width, height, levels, format);
    }

    /// <summary>The main image.</summary>
    /// <exception cref="InvalidOperationException">The format is not supported.</exception>
    public RgbaImage DecodeImage() => DecodeLevel(0);

    /// <summary>The image of a level (0 = main image, then the smaller copies).</summary>
    public RgbaImage DecodeLevel(int level)
    {
        EnsureSupported();
        return TexelCodec.Decode(Image, Levels[level]);
    }

    /// <summary>The image as it would be stored in this texture's format (what the game shows).</summary>
    public RgbaImage Quantize(RgbaImage image) => TexelCodec.Quantize(image, Format);

    /// <summary>
    /// The texture with a new main image. The smaller copies are computed from it; everything
    /// else (display list, settings) stays as it is.
    /// </summary>
    /// <returns>The new content of the COMM chunk.</returns>
    /// <exception cref="ArgumentException">The image does not have the texture's size.</exception>
    public byte[] WithImage(RgbaImage image)
    {
        EnsureSupported();
        if (image.Width != Width || image.Height != Height)
        {
            throw new ArgumentException(CoreText.F("Wrong size: {0} × {1} instead of {2} × {3}",
                image.Width, image.Height, Width, Height), nameof(image));
        }

        byte[] result = _data.ToArray();
        Span<byte> target = result.AsSpan(HeaderSize, _imageSize);
        foreach (TextureTile level in Levels)
        {
            RgbaImage levelImage = level.Width == image.Width && level.Height == image.Height
                ? image
                : image.Downscale(level.Width, level.Height);
            TexelCodec.Encode(levelImage, target, level);
        }

        return result;
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(CoreText.T("The editor cannot read the format of this texture."));
        }
    }
}
