using System.Buffers.Binary;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Text.Preview;

/// <summary>
/// One character image of a font, as described by the font's "BITM" chunk.
/// </summary>
/// <param name="Width">Width in pixels. This is also how far the next character moves to the right.
/// A width of 0 marks an unused glyph.</param>
/// <param name="Image">Number of the "IMAG" chunk that holds the pixels.</param>
/// <param name="S">Left edge of the character inside that image.</param>
/// <param name="T">Top edge of the character inside that image.</param>
public sealed record FontGlyph(int Width, int Image, int S, int T);

/// <summary>
/// The pixel data of a font (FORM "UVFT"): character images, widths and character list.
/// </summary>
/// <remarks>
/// <para>Layout of a font file (see uvParseTopUVFT in src/kernel/font.c of the decompilation):</para>
/// <code>
/// STRG  the character of each glyph, one ASCII byte per glyph (used to print ASCII strings)
/// BITM  one 16-byte libultra "Bitmap" per glyph:
///         s16 width        width of the character (and advance to the next one)
///         s16 width_img    width of the image that holds it
///         s16 s, t         position of the character inside the image
///         u32 buf          number of the IMAG chunk (replaced by a pointer when loading)
///         s16 actualHeight height of the character
///         s16 LUToffset    (unused for fonts)
/// FRMT  two 32-bit values: image format and pixel size (G_IM_FMT_*, G_IM_SIZ_*)
/// IMAG  the images, one per chunk, all characters of a row side by side
/// </code>
/// <para>
/// All fonts of the game use the format IA 4-bit: each pixel is one nibble (high nibble first),
/// 3 bits of brightness and 1 bit "visible". When drawing, the brightness is multiplied by the
/// text color.
/// </para>
/// </remarks>
public sealed class FontGraphics
{
    /// <summary>libultra G_IM_FMT_IA.</summary>
    private const int FormatIA = 3;

    /// <summary>libultra G_IM_FMT_I.</summary>
    private const int FormatI = 4;

    /// <summary>libultra G_IM_SIZ_4b.</summary>
    private const int Size4Bit = 0;

    private const int BitmapEntrySize = 16;

    private readonly FontImage[] _images;

    private FontGraphics(int index, string characters, IReadOnlyList<FontGlyph> glyphs, int height, FontImage[] images)
    {
        Index = index;
        Characters = characters;
        Glyphs = glyphs;
        Height = height;
        _images = images;
    }

    /// <summary>The number of the font in the game (uvFontSet).</summary>
    public int Index { get; }

    /// <summary>The character of each glyph (STRG chunk).</summary>
    public string Characters { get; }

    public IReadOnlyList<FontGlyph> Glyphs { get; }

    /// <summary>
    /// Height of the font. The game takes it from glyph 1 ("actualHeight") and uses it to
    /// place text vertically (uvFontPrintStr16, uvFontHeight).
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Width used for codes that have no glyph of their own, for example the line break code
    /// when measuring a text (sFontCurWidth in font.c: the width of glyph 0).
    /// </summary>
    public int DefaultWidth => Glyphs.Count > 0 ? Glyphs[0].Width : 8;

    /// <summary>Loads a font from the game's file system (uvFontSet number, e.g. 6 for texts).</summary>
    /// <exception cref="InvalidDataException">The font is missing or damaged.</exception>
    public static FontGraphics Load(GameFileSystem fileSystem, int fontIndex)
    {
        GameFile file = fileSystem.Find("UVFT", fontIndex)
            ?? throw new InvalidDataException($"The game has no font number {fontIndex}.");
        return FromFontFile(file.Data, fontIndex);
    }

    /// <summary>Reads a font file (FORM "UVFT").</summary>
    /// <exception cref="InvalidDataException">The file is not a font the editor can read.</exception>
    public static FontGraphics FromFontFile(byte[] fontFile, int fontIndex)
    {
        IffForm form = IffForm.Parse(fontFile);
        string? characters = null;
        byte[]? bitmaps = null;
        int format = FormatIA, size = Size4Bit;
        var imageData = new List<byte[]>();

        foreach (IffChunk chunk in form.Chunks)
        {
            (string tag, byte[] data) = GzipChunk.ReadChunkData(fontFile, chunk);
            switch (tag)
            {
                case "STRG":
                    characters = System.Text.Encoding.Latin1.GetString(data);
                    break;
                case "BITM":
                    bitmaps = data;
                    break;
                case "FRMT" when data.Length >= 8:
                    format = BinaryPrimitives.ReadInt32BigEndian(data);
                    size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                    break;
                case "IMAG":
                    imageData.Add(data);
                    break;
            }
        }

        if (bitmaps is null || bitmaps.Length < BitmapEntrySize)
        {
            throw new InvalidDataException($"Font {fontIndex} has no character table (BITM chunk).");
        }

        if (format is not (FormatIA or FormatI) || size != Size4Bit)
        {
            throw new InvalidDataException($"Font {fontIndex} uses image format {format}/{size}; only 4-bit IA and I are supported.");
        }

        var glyphs = new List<FontGlyph>();
        var imageWidths = new int[imageData.Count];
        int height = 0;
        for (int i = 0; i + BitmapEntrySize <= bitmaps.Length; i += BitmapEntrySize)
        {
            ReadOnlySpan<byte> entry = bitmaps.AsSpan(i, BitmapEntrySize);
            int width = BinaryPrimitives.ReadInt16BigEndian(entry);
            int imageWidth = BinaryPrimitives.ReadInt16BigEndian(entry[2..]);
            int s = BinaryPrimitives.ReadInt16BigEndian(entry[4..]);
            int t = BinaryPrimitives.ReadInt16BigEndian(entry[6..]);
            int image = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            int actualHeight = BinaryPrimitives.ReadInt16BigEndian(entry[12..]);

            if (image < 0 || image >= imageData.Count)
            {
                throw new InvalidDataException($"Font {fontIndex}: glyph {glyphs.Count} refers to a missing image.");
            }

            imageWidths[image] = imageWidth;
            if (glyphs.Count == 1)
            {
                height = actualHeight;
            }

            glyphs.Add(new FontGlyph(width, image, s, t));
        }

        var images = new FontImage[imageData.Count];
        for (int i = 0; i < images.Length; i++)
        {
            images[i] = FontImage.Decode4Bit(imageData[i], imageWidths[i], format == FormatIA);
        }

        return new FontGraphics(fontIndex, characters ?? string.Empty, glyphs, height, images);
    }

    /// <summary>
    /// Returns the glyph the game uses for an ASCII character (uvFontPrintStr), or -2 if the
    /// font does not have it (the game then leaves an empty space).
    /// </summary>
    public int GlyphOf(char character)
    {
        int index = Characters.IndexOf(character);
        return index >= 0 ? index : -2;
    }

    /// <summary>
    /// Reads one pixel of a glyph. Returns false if the pixel is transparent.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="x">Column inside the glyph.</param>
    /// <param name="y">Row inside the glyph.</param>
    /// <param name="intensity">Brightness 0-255 of a visible pixel.</param>
    public bool TryGetPixel(FontGlyph glyph, int x, int y, out byte intensity)
    {
        FontImage image = _images[glyph.Image];
        int px = glyph.S + x;
        int py = glyph.T + y;
        if (px < 0 || py < 0 || px >= image.Width || py >= image.Height)
        {
            intensity = 0;
            return false;
        }

        int index = py * image.Width + px;
        intensity = image.Intensity[index];
        return image.Visible[index];
    }

    /// <summary>Decoded pixels of one IMAG chunk.</summary>
    private sealed record FontImage(int Width, int Height, byte[] Intensity, bool[] Visible)
    {
        public static FontImage Decode4Bit(byte[] data, int width, bool withAlpha)
        {
            if (width <= 0)
            {
                return new FontImage(0, 0, [], []);
            }

            int height = data.Length * 2 / width;
            var intensity = new byte[width * height];
            var visible = new bool[width * height];
            for (int i = 0; i < intensity.Length; i++)
            {
                int nibble = (i & 1) == 0 ? data[i >> 1] >> 4 : data[i >> 1] & 0x0F;
                if (withAlpha)
                {
                    // IA4: iii a
                    intensity[i] = (byte)((nibble >> 1) * 255 / 7);
                    visible[i] = (nibble & 1) != 0;
                }
                else
                {
                    // I4: the brightness is also the coverage.
                    intensity[i] = (byte)(nibble * 17);
                    visible[i] = nibble != 0;
                }
            }

            return new FontImage(width, height, intensity, visible);
        }
    }
}
