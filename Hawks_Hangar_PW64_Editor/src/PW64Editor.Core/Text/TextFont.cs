using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Text;

/// <summary>
/// The character set of the font the game uses for its 16-bit texts.
/// </summary>
/// <remarks>
/// <para>
/// Game texts do not store ASCII codes but glyph numbers: the index of a character image in the
/// font. Which character each glyph shows is listed in the font's "STRG" chunk, one character per
/// glyph, in glyph order. The game itself uses this list to print ASCII strings
/// (uvFontPrintStr in src/kernel/font.c of the decompilation).
/// </para>
/// <para>
/// The text font (number 6) has 192 glyphs: 96 regular ones followed by the same 96 in bold.
/// Some positions are unused and filled with a backslash; a few characters appear twice
/// (e.g. the digits). For those, only the first occurrence counts as "the" character, so that
/// decoding and encoding are exact inverses.
/// </para>
/// </remarks>
public sealed class TextFont
{
    /// <summary>The font used for 16-bit texts (uvFontSet(6) in the decompilation).</summary>
    public const int TextFontIndex = 6;

    /// <summary>Placeholder used in the STRG list for unused glyph positions.</summary>
    private const char UnusedGlyph = '\\';

    private readonly string _characters;
    private readonly int _boldOffset;

    public TextFont(string characters)
    {
        if (characters.Length == 0 || characters.Length % 2 != 0)
        {
            throw new ArgumentException("The character list must contain a regular and a bold half of equal length.", nameof(characters));
        }

        _characters = characters;
        _boldOffset = characters.Length / 2;
    }

    /// <summary>Number of glyphs in the font (regular and bold together).</summary>
    public int GlyphCount => _characters.Length;

    /// <summary>
    /// Glyph number of the first bold character. Bold glyph = regular glyph + this value.
    /// </summary>
    public int BoldOffset => _boldOffset;

    /// <summary>The raw character list as stored in the font.</summary>
    public string Characters => _characters;

    /// <summary>
    /// Loads the text font from the game's file system.
    /// </summary>
    /// <exception cref="InvalidDataException">The font or its character list is missing.</exception>
    public static TextFont Load(GameFileSystem fileSystem)
    {
        GameFile file = fileSystem.Find("UVFT", TextFontIndex)
            ?? throw new InvalidDataException($"The game has no font number {TextFontIndex}.");
        return FromFontFile(file.Data);
    }

    /// <summary>Reads the character list from a font file (FORM "UVFT").</summary>
    public static TextFont FromFontFile(byte[] fontFile)
    {
        IffForm form = IffForm.Parse(fontFile);
        foreach (IffChunk chunk in form.Chunks)
        {
            (string tag, byte[] data) = GzipChunk.ReadChunkData(fontFile, chunk);
            if (tag == "STRG")
            {
                // Only printable ASCII is used; Latin-1 maps every byte to one char, so nothing gets lost.
                return new TextFont(System.Text.Encoding.Latin1.GetString(data));
            }
        }

        throw new InvalidDataException("The font has no character list (STRG chunk).");
    }

    /// <summary>
    /// Returns the character a glyph shows and whether it is bold.
    /// Returns false for unused glyphs and for duplicates of an earlier glyph.
    /// </summary>
    public bool TryGetCharacter(int glyph, out char character, out bool bold)
    {
        character = '\0';
        bold = glyph >= _boldOffset;

        if (glyph < 0 || glyph >= _characters.Length)
        {
            return false;
        }

        char c = _characters[glyph];
        if (c == UnusedGlyph || FindGlyph(c, bold) != glyph)
        {
            return false;
        }

        character = c;
        return true;
    }

    /// <summary>
    /// Returns the glyph for a character in regular or bold style.
    /// </summary>
    public bool TryGetGlyph(char character, bool bold, out int glyph)
    {
        glyph = character == UnusedGlyph ? -1 : FindGlyph(character, bold);
        return glyph >= 0;
    }

    /// <summary>
    /// All characters the font can show in one style, in font order, without duplicates.
    /// </summary>
    public string GetCharacters(bool bold)
    {
        var characters = new System.Text.StringBuilder();
        int start = bold ? _boldOffset : 0;
        for (int glyph = start; glyph < start + _boldOffset; glyph++)
        {
            if (TryGetCharacter(glyph, out char c, out _))
            {
                characters.Append(c);
            }
        }

        return characters.ToString();
    }

    /// <summary>True if the font can show the character (in regular style).</summary>
    public bool Contains(char character) => TryGetGlyph(character, false, out _);

    private int FindGlyph(char character, bool bold)
    {
        int start = bold ? _boldOffset : 0;
        int index = _characters.IndexOf(character, start, _boldOffset);
        return index;
    }
}
