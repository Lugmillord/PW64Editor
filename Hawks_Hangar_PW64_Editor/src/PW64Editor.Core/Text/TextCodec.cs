using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PW64Editor.Core.Text;

/// <summary>A problem found while encoding a text.</summary>
/// <param name="Position">Character position in the markup text.</param>
/// <param name="Message">Explanation for the user.</param>
public sealed record TextEncodeError(int Position, string Message);

/// <summary>The outcome of <see cref="TextCodec.Encode"/>.</summary>
/// <param name="Codes">The encoded 16-bit codes including the end code, or empty if there were errors.</param>
/// <param name="Errors">Everything that prevented encoding.</param>
public sealed record TextEncodeResult(IReadOnlyList<ushort> Codes, IReadOnlyList<TextEncodeError> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Converts the game's 16-bit text codes to an editable "markup" string and back.
/// </summary>
/// <remarks>
/// <para>Text format in the ROM (US version), one big-endian 16-bit code per entry:</para>
/// <code>
/// 0x0000-0x00BF  glyph of the text font (see TextFont; 0x60 and above are bold)
/// 0x00FD x 0     continue at horizontal position x (used to line up columns)
/// 0x00FE         line break
/// 0x00FF         end of text
/// </code>
/// <para>Markup used in the editor:</para>
/// <code>
/// [b]...[/b]     bold
/// [x=212]        continue at position 212 (0x00FD 212 0)
/// [#A2]          a raw code that has no character of its own (unused or duplicate glyph)
/// line break     a real line break
/// [nonl]         at the very end: the text does not end with a line break
/// </code>
/// <para>
/// Every text of the retail game ends with a line break right before the end code. That final
/// line break is hidden in the markup and added again when encoding.
/// </para>
/// <para>
/// Spaces are always stored with the regular space glyph, even inside bold text (the retail
/// game never uses the bold space). So a space never interrupts a bold section.
/// </para>
/// <para>
/// Decoding and encoding are exact inverses: Encode(Decode(x)) reproduces x code for code.
/// </para>
/// </remarks>
public sealed class TextCodec
{
    public const ushort CodePosition = 0xFD;
    public const ushort CodeLineBreak = 0xFE;
    public const ushort CodeEnd = 0xFF;

    private const string BoldOn = "[b]";
    private const string BoldOff = "[/b]";
    private const string NoFinalLineBreak = "[nonl]";

    private readonly TextFont _font;
    private readonly ushort _space;

    public TextCodec(TextFont font)
    {
        _font = font;
        if (!font.TryGetGlyph(' ', false, out int space))
        {
            throw new ArgumentException("The font has no space character.", nameof(font));
        }

        _space = (ushort)space;
    }

    /// <summary>Reads 16-bit codes from raw text data, up to and including the end code.</summary>
    public static ushort[] ReadCodes(ReadOnlySpan<byte> data)
    {
        var codes = new List<ushort>(data.Length / 2);
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            ushort code = BinaryPrimitives.ReadUInt16BigEndian(data[i..]);
            codes.Add(code);
            if (code == CodeEnd)
            {
                break;
            }
        }

        return codes.ToArray();
    }

    /// <summary>Decodes raw text data (as stored in a DATA chunk) into markup.</summary>
    public string Decode(ReadOnlySpan<byte> data) => DecodeCodes(ReadCodes(data));

    /// <summary>Decodes 16-bit codes into markup.</summary>
    public string DecodeCodes(IReadOnlyList<ushort> codes)
    {
        // Ignore everything from the end code on.
        int length = 0;
        while (length < codes.Count && codes[length] != CodeEnd)
        {
            length++;
        }

        bool endsWithLineBreak = length > 0 && codes[length - 1] == CodeLineBreak;
        if (endsWithLineBreak)
        {
            length--;
        }

        var text = new StringBuilder();
        bool bold = false;
        int pendingSpaces = 0;

        void FlushSpaces()
        {
            text.Append(' ', pendingSpaces);
            pendingSpaces = 0;
        }

        void EndBold()
        {
            if (bold)
            {
                text.Append(BoldOff);
                bold = false;
            }

            FlushSpaces();
        }

        for (int i = 0; i < length; i++)
        {
            ushort code = codes[i];

            if (code == _space)
            {
                pendingSpaces++;
            }
            else if (code == CodeLineBreak)
            {
                EndBold();
                text.Append('\n');
            }
            else if (code == CodePosition && i + 2 < length)
            {
                EndBold();
                ushort x = codes[i + 1];
                ushort y = codes[i + 2];
                text.Append(y == 0 ? $"[x={x}]" : $"[x={x},y={y}]");
                i += 2;
            }
            else if (_font.TryGetCharacter(code, out char character, out bool isBold) && character != ' ')
            {
                if (isBold && !bold)
                {
                    FlushSpaces();
                    text.Append(BoldOn);
                    bold = true;
                }
                else if (!isBold && bold)
                {
                    EndBold();
                }
                else
                {
                    FlushSpaces();
                }

                text.Append(character);
            }
            else
            {
                // No character of its own (or the bold space, which the encoder never writes,
                // because spaces are always stored as the regular space): keep the exact code.
                FlushSpaces();
                text.Append(CultureInfo.InvariantCulture, $"[#{code:X2}]");
            }
        }

        EndBold();
        if (!endsWithLineBreak)
        {
            text.Append(NoFinalLineBreak);
        }

        return text.ToString();
    }

    /// <summary>
    /// Encodes markup into 16-bit codes, including the final line break and the end code.
    /// </summary>
    public TextEncodeResult Encode(string markup)
    {
        var codes = new List<ushort>();
        var errors = new List<TextEncodeError>();
        bool bold = false;
        bool finalLineBreak = true;
        string text = markup.Replace("\r\n", "\n").Replace('\r', '\n');

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '[')
            {
                int close = text.IndexOf(']', i);
                if (close < 0)
                {
                    errors.Add(new(i, "'[' starts a tag, but the closing ']' is missing."));
                    break;
                }

                string tag = text[(i + 1)..close];
                if (!ApplyTag(tag, i, close == text.Length - 1, codes, errors, ref bold, ref finalLineBreak))
                {
                    errors.Add(new(i, $"Unknown tag [{tag}]. Allowed: [b], [/b], [x=number], [#hex code], [nonl] at the end."));
                }

                i = close;
            }
            else if (c == '\n')
            {
                codes.Add(CodeLineBreak);
            }
            else if (c == ' ')
            {
                codes.Add(_space);
            }
            else if (_font.TryGetGlyph(c, bold, out int glyph))
            {
                codes.Add((ushort)glyph);
            }
            else
            {
                errors.Add(new(i, _font.TryGetGlyph(c, !bold, out _)
                    ? $"'{c}' exists only in {(bold ? "regular" : "bold")} style in the game's font."
                    : $"'{c}' is not in the game's font."));
            }
        }

        if (finalLineBreak)
        {
            codes.Add(CodeLineBreak);
        }

        codes.Add(CodeEnd);
        return errors.Count == 0 ? new TextEncodeResult(codes, errors) : new TextEncodeResult([], errors);
    }

    /// <summary>
    /// Encodes markup into the bytes of a DATA chunk. If the result fits into
    /// <paramref name="minimumSize"/> bytes, it is padded with zeros to exactly that size
    /// (the original size, which keeps patches small); otherwise it is padded to a multiple of 4.
    /// </summary>
    public static byte[] ToChunkData(IReadOnlyList<ushort> codes, int minimumSize)
    {
        int size = Math.Max(minimumSize, (codes.Count * 2 + 3) / 4 * 4);
        byte[] data = new byte[size];
        for (int i = 0; i < codes.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), codes[i]);
        }

        return data;
    }

    private bool ApplyTag(string tag, int position, bool atEnd, List<ushort> codes, List<TextEncodeError> errors,
        ref bool bold, ref bool finalLineBreak)
    {
        switch (tag)
        {
            case "b":
                if (bold)
                {
                    errors.Add(new(position, "[b] inside bold text: close the previous one with [/b] first."));
                }

                bold = true;
                return true;

            case "/b":
                if (!bold)
                {
                    errors.Add(new(position, "[/b] without a matching [b]."));
                }

                bold = false;
                return true;

            case "nonl":
                if (!atEnd)
                {
                    errors.Add(new(position, "[nonl] is only allowed at the very end of the text."));
                }

                finalLineBreak = false;
                return true;
        }

        if (tag.StartsWith('#'))
        {
            if (ushort.TryParse(tag.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort raw)
                && raw is not (CodePosition or CodeLineBreak or CodeEnd))
            {
                codes.Add(raw);
            }
            else
            {
                errors.Add(new(position, $"[{tag}] is not a valid raw code."));
            }

            return true;
        }

        if (tag.StartsWith("x=", StringComparison.Ordinal))
        {
            string[] parts = tag[2..].Split(",y=");
            if (parts.Length is 1 or 2
                && ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ushort x)
                && (parts.Length == 1 || ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                ushort y = parts.Length == 2 ? ushort.Parse(parts[1], CultureInfo.InvariantCulture) : (ushort)0;
                codes.Add(CodePosition);
                codes.Add(x);
                codes.Add(y);
            }
            else
            {
                errors.Add(new(position, $"[{tag}] is not a valid position. Example: [x=212]"));
            }

            return true;
        }

        return false;
    }
}
