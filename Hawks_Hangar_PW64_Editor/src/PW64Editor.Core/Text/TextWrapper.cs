namespace PW64Editor.Core.Text;

/// <summary>
/// One change made by <see cref="TextWrapper.Wrap"/>: a line break at a markup position.
/// </summary>
/// <param name="Index">Position in the markup at the time the change is applied (changes are applied in order).</param>
/// <param name="ReplacesSpace">True if the line break replaces a space (same length);
/// false if it is inserted (the text gets one character longer).</param>
public sealed record WrapOperation(int Index, bool ReplacesSpace);

/// <summary>
/// Automatic line breaking for edited texts, so no line gets longer than the game can draw.
/// </summary>
/// <remarks>
/// Works on the editor markup. Tags like [b] take no room; a raw code [#5C] counts as one
/// character; [x=...] starts a new piece, just like a line break (see <see cref="TextValidator"/>).
/// A too long piece is broken at its last space (word wrap); if it has no space, it is broken
/// right before the character that does not fit any more.
/// </remarks>
public static class TextWrapper
{
    /// <summary>Number of lines of a markup text.</summary>
    public static int CountLines(string markup) => 1 + markup.Count(c => c == '\n');

    /// <summary>
    /// Breaks every piece longer than <paramref name="maxPerPiece"/> characters.
    /// </summary>
    /// <returns>The wrapped markup and the changes, in the order they must be applied.</returns>
    public static (string Text, IReadOnlyList<WrapOperation> Operations) Wrap(string markup, int maxPerPiece = TextValidator.MaxCharactersPerPiece)
    {
        var operations = new List<WrapOperation>();
        string text = markup.Replace("\r\n", "\n");

        // Fix one piece per pass; texts are short, so the repeated scan costs nothing.
        while (FindBreak(text, maxPerPiece) is { } fix)
        {
            operations.Add(fix);
            text = fix.ReplacesSpace
                ? text[..fix.Index] + "\n" + text[(fix.Index + 1)..]
                : text.Insert(fix.Index, "\n");
        }

        return (text, operations);
    }

    /// <summary>Finds the first place that needs a line break, or null if every piece fits.</summary>
    private static WrapOperation? FindBreak(string text, int maxPerPiece)
    {
        int count = 0;          // characters in the current piece
        int lastSpace = -1;     // markup index of the last space in the current piece

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                count = 0;
                lastSpace = -1;
                continue;
            }

            if (c == '[' && text.IndexOf(']', i) is int close and > 0)
            {
                string tag = text[(i + 1)..close];
                if (tag.StartsWith("x=", StringComparison.Ordinal))
                {
                    // A position starts a new piece.
                    count = 0;
                    lastSpace = -1;
                }
                else if (tag.StartsWith('#'))
                {
                    if (++count > maxPerPiece)
                    {
                        return BreakBefore(i, lastSpace);
                    }
                }

                // [b], [/b], [nonl] take no room.
                i = close;
                continue;
            }

            if (++count > maxPerPiece)
            {
                return BreakBefore(i, lastSpace);
            }

            if (c == ' ')
            {
                lastSpace = i;
            }
        }

        return null;
    }

    private static WrapOperation BreakBefore(int index, int lastSpace) =>
        lastSpace >= 0 ? new WrapOperation(lastSpace, ReplacesSpace: true) : new WrapOperation(index, ReplacesSpace: false);
}
