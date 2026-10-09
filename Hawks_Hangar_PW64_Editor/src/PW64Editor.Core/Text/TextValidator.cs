using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Text;

/// <summary>A problem with an edited text.</summary>
/// <param name="IsError">True if the text cannot be saved; false for a warning.</param>
/// <param name="Message">Explanation for the user.</param>
public sealed record TextIssue(bool IsError, string Message);

/// <summary>Line statistics of a text, as the game draws it.</summary>
/// <param name="Lines">Number of lines.</param>
/// <param name="Pieces">Number of separately drawn pieces: one per line plus one per [x=...] position.</param>
/// <param name="LongestPiece">Characters in the longest piece.</param>
public sealed record TextLayoutInfo(int Lines, int Pieces, int LongestPiece);

/// <summary>The outcome of <see cref="TextValidator.Validate"/>.</summary>
public sealed record TextValidation(IReadOnlyList<TextIssue> Issues, TextLayoutInfo? Layout, TextLayoutInfo? OriginalLayout)
{
    public bool HasErrors => Issues.Any(i => i.IsError);
}

/// <summary>
/// Checks an edited text against the limits of the game's text drawing.
/// </summary>
/// <remarks>
/// <para>What the game does when drawing a text (uvFontPrintStr16 in src/kernel/font.c):</para>
/// <list type="bullet">
///   <item>A text is drawn in pieces: each line, and each part after an [x=...] position, is one
///         piece. The buffer for a piece holds 44 values (FONT_MAX_MSG_LEN) including the end
///         marker. A bug in the original writes the marker behind the buffer for pieces of 43
///         characters followed by a line break, or of 44 and more: the game crashes (black
///         screen, confirmed in an emulator). The code fix "Safe text loading and drawing"
///         (Code.CodeFixes.SafeText) removes the crash. The editor allows at most 40 characters
///         per piece either way; the longest original piece has 39.</item>
///   <item>At most 30 pieces can be drawn per frame on the whole screen (FONT_MSG_COUNT). The game
///         does not check this; more pieces overwrite other memory and can crash the game.</item>
///   <item>There is no automatic line wrapping, and the boxes on screen are sized for the original
///         texts. Longer lines can run past their box, extra lines can overlap other things.</item>
///   <item>The loader (textLoadBlock in src/app/text_data.c) replaces every 16-bit value 254 and 255
///         with its line break and end codes, without skipping the numbers of [x=...] codes. So
///         positions 254 and 255 are destroyed while loading.</item>
/// </list>
/// <para>
/// Hard limits are errors; going beyond the original text's size is a warning, because it may or
/// may not fit depending on the screen. The longest original piece has 39 characters.
/// </para>
/// </remarks>
public static class TextValidator
{
    /// <summary>Size of the game's buffer for one piece (FONT_MAX_MSG_LEN, US version).</summary>
    /// <remarks>Only for documentation: without the code fix, 43 characters plus a line break already crash the game.</remarks>
    public const int GameBufferCharactersPerPiece = 44;

    /// <summary>
    /// Characters per piece the editor allows. Safely below <see cref="GameBufferCharactersPerPiece"/>,
    /// because a full 44-character line kept the game from starting. Longer lines are wrapped.
    /// </summary>
    public const int MaxCharactersPerPiece = 40;

    /// <summary>Pieces the game can draw per frame on the whole screen (FONT_MSG_COUNT).</summary>
    public const int MaxPiecesPerFrame = 30;

    /// <param name="maxLines">Most lines the text may have (see <see cref="TextLimits"/>). More is an error.
    /// If null, more lines than the original text only give a warning.</param>
    /// <param name="name">The text's name, to check texts into which the game writes numbers (<see cref="TextNumberSlots"/>).</param>
    public static TextValidation Validate(TextCodec codec, string markup, string originalMarkup, int? maxLines = null, string? name = null)
    {
        var issues = new List<TextIssue>();
        TextEncodeResult encoded = codec.Encode(markup);
        issues.AddRange(encoded.Errors.Select(e => new TextIssue(true, $"{DescribePosition(markup, e.Position)}: {e.Message}")));

        TextEncodeResult original = codec.Encode(originalMarkup);
        TextLayoutInfo? originalLayout = original.Success ? Measure(original.Codes) : null;

        if (!encoded.Success)
        {
            return new TextValidation(issues, null, originalLayout);
        }

        TextLayoutInfo layout = Measure(encoded.Codes);
        IReadOnlyList<int> pieces = PieceLengths(encoded.Codes);

        if (name is not null)
        {
            issues.AddRange(TextNumberSlots.Validate(name, encoded.Codes));
        }

        foreach (int value in PositionValues(encoded.Codes).Where(v => v is TextCodec.CodeLineBreak or TextCodec.CodeEnd).Distinct())
        {
            issues.Add(new TextIssue(true,
                CoreText.F("A position of {0} cannot be used: while loading the texts, the game changes every value " +
                    "254 and 255 into its line break and end codes, also inside [x=...]. Use {1} or {2} instead.",
                    value, value - 2, value + 2)));
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] > MaxCharactersPerPiece)
            {
                issues.Add(new TextIssue(true,
                    CoreText.F("Piece {0} has {1} characters. At most {2} are allowed per line (or per part after an " +
                        "[x=...] position); longer lines can crash the game. Add a line break.",
                        i + 1, pieces[i], MaxCharactersPerPiece)));
            }
        }

        if (layout.Pieces > MaxPiecesPerFrame)
        {
            issues.Add(new TextIssue(true,
                CoreText.F("The text is drawn in {0} pieces (lines and [x=...] parts). The game can draw at most {1} " +
                    "per screen and may crash with more.", layout.Pieces, MaxPiecesPerFrame)));
        }

        if (maxLines is { } limit && layout.Lines > limit)
        {
            issues.Add(new TextIssue(true,
                CoreText.F("{0} lines, but the screen has room for {1}. Remove {2} line(s).",
                    layout.Lines, limit, layout.Lines - limit)));
        }

        if (originalLayout is not null)
        {
            if (maxLines is null && layout.Lines > originalLayout.Lines)
            {
                issues.Add(new TextIssue(false,
                    CoreText.F("{0} lines instead of {1}. The screen only has room for the original number of lines; " +
                        "extra lines may overlap other things or not be visible.", layout.Lines, originalLayout.Lines)));
            }

            if (layout.LongestPiece > originalLayout.LongestPiece)
            {
                issues.Add(new TextIssue(false,
                    CoreText.F("The longest line has {0} characters, the original {1}. It may run past the edge of " +
                        "its box, because characters have different widths.",
                        layout.LongestPiece, originalLayout.LongestPiece)));
            }

            if (layout.Pieces > originalLayout.Pieces && layout.Pieces <= MaxPiecesPerFrame)
            {
                issues.Add(new TextIssue(false,
                    CoreText.F("The text is drawn in {0} pieces instead of {1}. Together with other texts on the " +
                        "same screen, the game's limit of {2} could be exceeded.",
                        layout.Pieces, originalLayout.Pieces, MaxPiecesPerFrame)));
            }
        }

        return new TextValidation(issues, layout, originalLayout);
    }

    /// <summary>Counts lines, pieces and the longest piece of encoded text (final line break and end code ignored).</summary>
    public static TextLayoutInfo Measure(IReadOnlyList<ushort> codes)
    {
        IReadOnlyList<int> pieces = PieceLengths(codes);
        int lines = 1 + Content(codes).Count(c => c == TextCodec.CodeLineBreak);
        return new TextLayoutInfo(lines, pieces.Count, pieces.Count == 0 ? 0 : pieces.Max());
    }

    /// <summary>Number of characters in each piece, in order.</summary>
    private static IReadOnlyList<int> PieceLengths(IReadOnlyList<ushort> codes)
    {
        List<ushort> content = Content(codes);
        var pieces = new List<int> { 0 };

        for (int i = 0; i < content.Count; i++)
        {
            switch (content[i])
            {
                case TextCodec.CodeLineBreak:
                    pieces.Add(0);
                    break;
                case TextCodec.CodePosition:
                    // A position at the very start only moves the first piece (the game handles
                    // that case separately); anywhere else it starts a new piece.
                    if (i > 0)
                    {
                        pieces.Add(0);
                    }

                    i += 2; // the two position values are not characters
                    break;
                default:
                    pieces[^1]++;
                    break;
            }
        }

        return pieces;
    }

    /// <summary>The numbers of all position codes ([x=...,y=...]).</summary>
    private static IEnumerable<int> PositionValues(IReadOnlyList<ushort> codes)
    {
        // Walk the codes themselves: a position number of 255 must not be taken for the end code.
        for (int i = 0; i < codes.Count && codes[i] != TextCodec.CodeEnd; i++)
        {
            if (codes[i] == TextCodec.CodePosition && i + 2 < codes.Count)
            {
                yield return codes[i + 1];
                yield return codes[i + 2];
                i += 2;
            }
        }
    }

    /// <summary>The codes without the end code and the final line break.</summary>
    private static List<ushort> Content(IReadOnlyList<ushort> codes)
    {
        var content = codes.TakeWhile(c => c != TextCodec.CodeEnd).ToList();
        if (content.Count > 0 && content[^1] == TextCodec.CodeLineBreak)
        {
            content.RemoveAt(content.Count - 1);
        }

        return content;
    }

    private static string DescribePosition(string markup, int position)
    {
        int line = 1 + markup.Take(Math.Min(position, markup.Length)).Count(c => c == '\n');
        return CoreText.F("Line {0}", line);
    }
}
