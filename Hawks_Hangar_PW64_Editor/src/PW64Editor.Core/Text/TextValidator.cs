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
///         piece. A piece holds at most 44 characters (FONT_MAX_MSG_LEN); the rest is cut off or
///         pushed into an extra line, depending on the screen.</item>
///   <item>At most 30 pieces can be drawn per frame on the whole screen (FONT_MSG_COUNT). The game
///         does not check this; more pieces overwrite other memory and can crash the game.</item>
///   <item>There is no automatic line wrapping, and the boxes on screen are sized for the original
///         texts. Longer lines can run past their box, extra lines can overlap other things.</item>
/// </list>
/// <para>
/// Hard limits are errors; going beyond the original text's size is a warning, because it may or
/// may not fit depending on the screen. The longest original piece has 39 characters.
/// </para>
/// </remarks>
public static class TextValidator
{
    /// <summary>Characters per piece the game can draw (FONT_MAX_MSG_LEN, US version).</summary>
    public const int MaxCharactersPerPiece = 44;

    /// <summary>Pieces the game can draw per frame on the whole screen (FONT_MSG_COUNT).</summary>
    public const int MaxPiecesPerFrame = 30;

    /// <param name="maxLines">Most lines the text may have (see <see cref="TextLimits"/>). More is an error.
    /// If null, more lines than the original text only give a warning.</param>
    public static TextValidation Validate(TextCodec codec, string markup, string originalMarkup, int? maxLines = null)
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

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] > MaxCharactersPerPiece)
            {
                issues.Add(new TextIssue(true,
                    $"Piece {i + 1} has {pieces[i]} characters. The game draws at most {MaxCharactersPerPiece} per line " +
                    "(or per part after an [x=...] position) and cuts off or moves the rest. Add a line break."));
            }
        }

        if (layout.Pieces > MaxPiecesPerFrame)
        {
            issues.Add(new TextIssue(true,
                $"The text is drawn in {layout.Pieces} pieces (lines and [x=...] parts). The game can draw at most " +
                $"{MaxPiecesPerFrame} per screen and may crash with more."));
        }

        if (maxLines is { } limit && layout.Lines > limit)
        {
            issues.Add(new TextIssue(true,
                $"{layout.Lines} lines, but the screen has room for {limit}. Remove {layout.Lines - limit} line(s)."));
        }

        if (originalLayout is not null)
        {
            if (maxLines is null && layout.Lines > originalLayout.Lines)
            {
                issues.Add(new TextIssue(false,
                    $"{layout.Lines} lines instead of {originalLayout.Lines}. The screen only has room for the original " +
                    "number of lines; extra lines may overlap other things or not be visible."));
            }

            if (layout.LongestPiece > originalLayout.LongestPiece)
            {
                issues.Add(new TextIssue(false,
                    $"The longest line has {layout.LongestPiece} characters, the original {originalLayout.LongestPiece}. " +
                    "It may run past the edge of its box, because characters have different widths."));
            }

            if (layout.Pieces > originalLayout.Pieces && layout.Pieces <= MaxPiecesPerFrame)
            {
                issues.Add(new TextIssue(false,
                    $"The text is drawn in {layout.Pieces} pieces instead of {originalLayout.Pieces}. Together with other " +
                    $"texts on the same screen, the game's limit of {MaxPiecesPerFrame} could be exceeded."));
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
        return $"Line {line}";
    }
}
