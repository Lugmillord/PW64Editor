namespace PW64Editor.Core.Text.Preview;

/// <summary>
/// A text as the game holds it in memory after loading: 16-bit values, where the loader has
/// replaced the line break code 0x00FE with 0x0FFE and the end code 0x00FF with -1
/// (textLoadBlock in src/app/text_data.c).
/// </summary>
public sealed class GameTextMemory
{
    /// <summary>The line break value in memory.</summary>
    public const short LineBreak = 0xFFE;

    /// <summary>The end value in memory.</summary>
    public const short End = -1;

    /// <summary>The "continue at position x" code (not changed by the loader).</summary>
    public const short Position = 0xFD;

    private readonly short[] _values;

    private GameTextMemory(string name, short[] values, bool isEdited)
    {
        Name = name;
        _values = values;
        IsEdited = isEdited;
    }

    /// <summary>The text's name, e.g. "A_HG_1_M".</summary>
    public string Name { get; }

    /// <summary>True for the text the user is editing (as opposed to other texts on the same screen).</summary>
    public bool IsEdited { get; }

    /// <summary>Number of stored values.</summary>
    public int Length => _values.Length;

    /// <summary>True if the game would have read past the end of the text.</summary>
    public bool ReadPastEnd { get; private set; }

    /// <summary>
    /// Reads a value. Behind the text the real game reads whatever comes next in memory;
    /// the preview stops there, as if an end code followed.
    /// </summary>
    public short this[int index]
    {
        get
        {
            if (index >= 0 && index < _values.Length)
            {
                return _values[index];
            }

            ReadPastEnd = true;
            return End;
        }
    }

    /// <summary>
    /// Converts encoded text (as stored in the ROM) the way the game's loader does. Note that the
    /// loader changes every 0x00FE and 0x00FF, also the numbers of a position code.
    /// </summary>
    public static GameTextMemory FromCodes(string name, IReadOnlyList<ushort> codes, bool isEdited)
    {
        var values = new short[codes.Count];
        for (int i = 0; i < codes.Count; i++)
        {
            values[i] = codes[i] switch
            {
                TextCodec.CodeLineBreak => LineBreak,
                TextCodec.CodeEnd => End,
                _ => unchecked((short)codes[i]),
            };
        }

        return new GameTextMemory(name, values, isEdited);
    }

    /// <summary>A short string the game builds itself, e.g. points ("-3" is an empty space).</summary>
    public static GameTextMemory FromValues(string name, params short[] values) => new(name, values, false);

    /// <summary>Text line (counting from 1) that the value at <paramref name="index"/> belongs to.</summary>
    public int LineOf(int index)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(index, _values.Length); i++)
        {
            if (_values[i] == LineBreak)
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>
/// One separately drawn piece of text (FontMessage in src/kernel/font.c).
/// </summary>
public sealed class FontMessage
{
    public required int X { get; set; }

    /// <summary>Top row on the screen.</summary>
    public required int Top { get; init; }

    public required FontGraphics Font { get; init; }

    public required ScreenColor Color { get; init; }

    public required float ScaleX { get; init; }

    public required float ScaleY { get; init; }

    /// <summary>The glyph numbers (-2 and -3 are empty spaces).</summary>
    public List<short> Codes { get; } = [];

    /// <summary>The text this piece comes from, or null for ASCII strings.</summary>
    public GameTextMemory? Source { get; init; }

    /// <summary>Position of the first character in <see cref="Source"/>.</summary>
    public int SourceIndex { get; set; }

    /// <summary>
    /// True if the piece filled the game's 44-value buffer without an end marker. The game then
    /// writes past the buffer or draws whatever follows it: a crash is likely.
    /// </summary>
    public bool Overflow { get; set; }

    /// <summary>True if the caller's length limit cut the piece off before the end of its line.</summary>
    public bool Truncated { get; set; }

    /// <summary>The text line (from 1) this piece belongs to, or 0 for ASCII strings.</summary>
    public int Line => Source?.LineOf(SourceIndex) ?? 0;
}

/// <summary>
/// Repeats what the game's font functions do (src/kernel/font.c, US version), so a text is
/// split into pieces and placed exactly like in the game.
/// </summary>
/// <remarks>
/// <para>Coordinates follow the game: x from the left, y from the bottom of the screen. The y
/// given to a print function is the bottom edge of the font's height, so the top row on the
/// screen is 240 − (y + font height).</para>
/// <para>Each piece is drawn as a row of character images, 15 rows high (the sprite's bitmap
/// height). The next character starts where the previous one ends (its width), so the font is
/// proportional.</para>
/// </remarks>
public sealed class FontPrinter
{
    /// <summary>FONT_MAX_MSG_LEN, US version: values per piece buffer.</summary>
    public const int MaxMessageLength = 44;

    /// <summary>FONT_MSG_COUNT: pieces per frame.</summary>
    public const int MaxMessages = 30;

    /// <summary>Rows drawn per character (the sprite's bmheight in font.c).</summary>
    public const int GlyphRows = 15;

    public FontPrinter(FontGraphics font)
    {
        Font = font;
    }

    public List<FontMessage> Messages { get; } = [];

    public FontGraphics Font { get; set; }

    public ScreenColor Color { get; set; } = ScreenColor.Gray(255);

    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    /// <summary>
    /// Draw like a ROM with the code fix "Safe text loading and drawing" (<see cref="Code.CodeFixes.SafeText"/>):
    /// at most 43 characters per piece, and the end marker always lands inside the buffer.
    /// </summary>
    public bool SafeTextFix { get; set; }

    /// <summary>uvFontStrLen: values up to the end marker, at most 44.</summary>
    public static int StrLen(GameTextMemory text, int start)
    {
        int length = 0;
        while (length < MaxMessageLength && text[start + length] != GameTextMemory.End)
        {
            length++;
        }

        return length;
    }

    /// <summary>
    /// uvFontStr16Width: width of a 16-bit text. Note that the game measures up to 44 values,
    /// across line breaks, and counts every code without a glyph (also the line break) with
    /// the width of glyph 0.
    /// </summary>
    public int Str16Width(GameTextMemory text, int start)
    {
        int length = StrLen(text, start);
        int width = 0;
        for (int j = 0; j < length; j++)
        {
            short code = text[start + j];
            if (code == GameTextMemory.End)
            {
                break;
            }

            width += code >= 0 && code != 0xFFF && code != GameTextMemory.LineBreak && code < Font.Glyphs.Count
                ? Font.Glyphs[code].Width
                : Font.DefaultWidth;
        }

        return (int)(width * ScaleX);
    }

    /// <summary>uvFontStrWidth: width of an ASCII string.</summary>
    public int StrWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                continue;
            }

            int glyph = Font.GlyphOf(c);
            width += glyph >= 0 ? Font.Glyphs[glyph].Width : Font.DefaultWidth;
        }

        return (int)(width * ScaleX);
    }

    /// <summary>
    /// uvFontPrintStr16 (US version) with the line break as end value: prints one line of a
    /// text, starting at <paramref name="start"/>.
    /// </summary>
    /// <returns>How many values were used (the caller continues there), or -1 at the end of the text.</returns>
    public int PrintStr16(int x, int y, GameTextMemory text, int start, int strLen)
    {
        int top = TopOf(y);
        strLen = Math.Min(strLen, SafeTextFix ? MaxMessageLength - 1 : MaxMessageLength);

        FontMessage message = NewMessage(x, top, text, start);
        int i16 = 0;
        int i = 0;
        bool atEnd = false;
        bool terminated = false;

        if (text[start] == GameTextMemory.Position)
        {
            message.X = text[start + 1];
            i16 = 3;
            message.SourceIndex = start + 3;
        }

        while (i < strLen)
        {
            short code = text[start + i16];
            if (code == GameTextMemory.LineBreak)
            {
                terminated = true;
                i16++;
                i++;
                break;
            }

            if (code == GameTextMemory.Position)
            {
                Messages.Add(message);
                message = NewMessage(text[start + i16 + 1], top, text, start + i16 + 3);
                i = 0;
                i16 += 3;
            }
            else
            {
                if (code == GameTextMemory.End)
                {
                    terminated = true;
                    atEnd = true;
                    break;
                }

                message.Codes.Add(code);
                i16++;
                i++;
            }
        }

        // After the loop the game writes one more end marker at position i, if a counter reached
        // the limit. The original compares the wrong counter (values read, i16); with the fix it
        // compares the characters stored (i), and the limit is one lower.
        bool writesMarker = SafeTextFix ? i == strLen : i16 == strLen;
        if (writesMarker)
        {
            if (i >= MaxMessageLength)
            {
                // Behind the buffer, into the font pointer: crash. In the original this happens
                // with 43 characters and a line break, and with 44 or more characters.
                message.Overflow = true;
            }
            else if (!terminated)
            {
                // The limit cut the piece off cleanly; the rest of the line is not drawn by this call.
                message.Truncated = true;
            }
        }
        else if (!terminated)
        {
            // The loop ended without an end marker (only possible after a position code in the
            // original): the piece runs into whatever the buffer held before.
            message.Overflow = true;
        }

        Messages.Add(message);
        return atEnd ? -1 : i16;
    }

    /// <summary>uvFontPrintStr: prints an ASCII string (characters looked up in the font's list).</summary>
    public void PrintStr(int x, int y, string text)
    {
        FontMessage message = NewMessage(x, TopOf(y), null, 0);
        foreach (char c in text.Length > MaxMessageLength ? text[..MaxMessageLength] : text)
        {
            message.Codes.Add((short)Font.GlyphOf(c));
        }

        Messages.Add(message);
    }

    private int TopOf(int y) => ScreenCanvas.Height - (y + (int)(Font.Height * ScaleY));

    private FontMessage NewMessage(int x, int top, GameTextMemory? source, int sourceIndex) => new()
    {
        X = x,
        Top = top,
        Font = Font,
        Color = Color,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        Source = source,
        SourceIndex = sourceIndex,
    };
}
