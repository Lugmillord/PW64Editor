using PW64Editor.Core.FileSystem;

namespace PW64Editor.Core.Text.Preview;

/// <summary>Where one line of the edited text ends up on the screen.</summary>
/// <param name="Line">Line number, counting from 1.</param>
/// <param name="Left">First column of the line's first piece.</param>
/// <param name="Right">First column right of the line's last visible pixel.</param>
/// <param name="Top">Top row.</param>
/// <param name="Bottom">First row below the line.</param>
public sealed record TextPreviewLine(int Line, int Left, int Right, int Top, int Bottom)
{
    public int Width => Right - Left;
}

/// <summary>The result of <see cref="TextPreviewRenderer.Render"/>.</summary>
/// <param name="ScreenTitle">Name of the screen the text appears on.</param>
/// <param name="ScreenNote">What the preview shows and how exact it is.</param>
/// <param name="Pixels">The picture: 320 × 240 pixels, 4 bytes each (B, G, R, A).</param>
/// <param name="TextArea">The room the text has on this screen, if known.</param>
/// <param name="Lines">The drawn lines of the edited text.</param>
/// <param name="Issues">Problems found while drawing.</param>
/// <param name="PiecesOnScreen">Separately drawn pieces on the whole screen (the game can draw 30).</param>
public sealed record TextPreview(
    string ScreenTitle,
    string ScreenNote,
    byte[] Pixels,
    ScreenRect? TextArea,
    IReadOnlyList<TextPreviewLine> Lines,
    IReadOnlyList<TextIssue> Issues,
    int PiecesOnScreen)
{
    public const int Width = ScreenCanvas.Width;
    public const int Height = ScreenCanvas.Height;

    /// <summary>The widest line, or null if nothing was drawn.</summary>
    public TextPreviewLine? WidestLine => Lines.Count == 0 ? null : Lines.MaxBy(l => l.Right);
}

/// <summary>Options for <see cref="TextPreviewRenderer.Render"/>.</summary>
/// <param name="ShowTextArea">Draws a dashed frame around the room the text has.</param>
/// <param name="MarkOverflow">Draws the parts of the text that leave their room in red.</param>
/// <param name="SafeTextFix">Draw like a ROM with the code fix "Safe text loading and drawing".</param>
public sealed record TextPreviewOptions(bool ShowTextArea = true, bool MarkOverflow = true, bool SafeTextFix = false);

/// <summary>
/// Draws a text the way the game shows it: with the game's own font images, on a rebuilt
/// version of the screen it appears on, and with the game's own rules for splitting it into
/// pieces. This shows how much room a text really takes, because the font is proportional
/// (an "i" is 3 pixels wide, a "W" 15).
/// </summary>
public sealed class TextPreviewRenderer
{
    /// <summary>Fonts the screens use besides the text font: 0 (mission numbers) and 3 (points).</summary>
    private static readonly int[] OtherFontNumbers = [0, 3];

    private static readonly ScreenColor OverflowColor = new(0xFF, 0x30, 0x30);
    private static readonly ScreenColor AreaColor = new(0xFF, 0xD8, 0x40, 0xB0);

    private readonly FontGraphics _textFont;
    private readonly IReadOnlyDictionary<int, FontGraphics> _otherFonts;

    public TextPreviewRenderer(FontGraphics textFont, IReadOnlyDictionary<int, FontGraphics>? otherFonts = null)
    {
        _textFont = textFont;
        _otherFonts = otherFonts ?? new Dictionary<int, FontGraphics>();
    }

    /// <summary>Loads the fonts from the game.</summary>
    /// <exception cref="InvalidDataException">The text font is missing or damaged.</exception>
    public static TextPreviewRenderer Load(GameFileSystem fileSystem)
    {
        FontGraphics textFont = FontGraphics.Load(fileSystem, TextFont.TextFontIndex);
        var others = new Dictionary<int, FontGraphics>();
        foreach (int number in OtherFontNumbers)
        {
            try
            {
                others[number] = FontGraphics.Load(fileSystem, number);
            }
            catch (InvalidDataException)
            {
                // Only used for decoration (numbers and points); the preview works without them.
            }
        }

        return new TextPreviewRenderer(textFont, others);
    }

    /// <summary>
    /// Draws a text on its screen.
    /// </summary>
    /// <param name="name">The text's name, e.g. "A_HG_1_M"; it decides the screen.</param>
    /// <param name="category">The text's category (used for texts without a known name pattern).</param>
    /// <param name="codes">The encoded text, including the end code (see <see cref="TextCodec.Encode"/>).</param>
    /// <param name="otherTexts">Returns other texts shown on the same screen (encoded), or null.</param>
    /// <param name="options">Drawing options; default: frame and red marks on.</param>
    public TextPreview Render(string name, TextCategory category, IReadOnlyList<ushort> codes,
        Func<string, IReadOnlyList<ushort>?>? otherTexts = null, TextPreviewOptions? options = null)
    {
        options ??= new TextPreviewOptions();
        TextScreen screen = TextScreen.For(name, category);

        // Texts into which the game writes a number are shown with an example number, like in the game.
        if (category.Area != CustomTexts.AreaName)
        {
            codes = TextNumberSlots.WithExampleNumber(name, codes);
        }

        GameTextMemory text = GameTextMemory.FromCodes(name, codes, isEdited: true);

        var context = new PreviewContext
        {
            Canvas = new ScreenCanvas(),
            Printer = new FontPrinter(_textFont) { SafeTextFix = options.SafeTextFix },
            TextFont = _textFont,
            OtherFonts = _otherFonts,
            Text = text,
            OtherText = other => otherTexts?.Invoke(other) is { } found
                ? GameTextMemory.FromCodes(other, found, isEdited: false)
                : null,
        };

        screen.Draw(context);

        if (options.ShowTextArea && context.TextArea is { } area)
        {
            context.Canvas.Outline(area, AreaColor, dashed: true);
        }

        List<TextPreviewLine> lines = DrawMessages(context, options.MarkOverflow);
        CheckLimits(context, lines);

        return new TextPreview(screen.Title, screen.Note, context.Canvas.Pixels, context.TextArea, lines,
            context.Issues.DistinctBy(i => i.Message).ToList(), context.Printer.Messages.Count);
    }

    /// <summary>
    /// Draws all pieces (uvFontGenDlist / spDraw) and collects the extent of the edited text's lines.
    /// </summary>
    private static List<TextPreviewLine> DrawMessages(PreviewContext context, bool markOverflow)
    {
        var extents = new SortedDictionary<int, TextPreviewLine>();
        var stoppedAt = new HashSet<string>();

        foreach (FontMessage message in context.Printer.Messages)
        {
            bool edited = message.Source?.IsEdited == true;
            FontGraphics font = message.Font;
            ScreenRect? area = edited && markOverflow ? context.TextArea : null;
            float x = 0;
            int left = message.X;
            int right = message.X;
            int top = message.Top;
            int bottom = message.Top + (int)Math.Ceiling(FontPrinter.GlyphRows * message.ScaleY);

            for (int i = 0; i < message.Codes.Count; i++)
            {
                short code = message.Codes[i];
                FontGlyph glyph;
                bool blank = code is -2 or -3;
                if (blank)
                {
                    glyph = font.Glyphs[0];
                }
                else if (code >= 0 && code < font.Glyphs.Count)
                {
                    glyph = font.Glyphs[code];
                }
                else
                {
                    if (edited)
                    {
                        context.Issues.Add(new TextIssue(true,
                            $"Line {message.Line}: the code [#{(ushort)code:X2}] is not a character of the font. The game " +
                            "would draw whatever lies behind the font table in memory."));
                    }

                    break;
                }

                // spDraw stops at the first character image with width 0 (unused glyphs).
                if (glyph.Width <= 0)
                {
                    if (edited && i < message.Codes.Count && stoppedAt.Add($"{message.Line}:{code}"))
                    {
                        context.Issues.Add(new TextIssue(false,
                            $"Line {message.Line}: [#{(ushort)code:X2}] has no image in the font. The game stops drawing the " +
                            "line there; everything behind it on this line (or up to the next [x=...]) is invisible."));
                    }

                    break;
                }

                int glyphLeft = (int)(message.X + x * message.ScaleX + 0.9999f);
                int glyphRight = (int)(message.X + (x + glyph.Width) * message.ScaleX + 0.9999f);
                if (!blank)
                {
                    int ink = DrawGlyph(context.Canvas, font, glyph, glyphLeft, glyphRight, message, area);
                    right = Math.Max(right, ink);
                }

                x += glyph.Width;
            }

            // The empty piece the game prints for the end of the text is no line of its own.
            bool endPiece = message.Codes.Count == 0 && message.Source?[message.SourceIndex] == GameTextMemory.End;
            if (edited && !endPiece)
            {
                if (message.Overflow)
                {
                    context.Issues.Add(new TextIssue(true,
                        $"Line {message.Line}: the piece is too long for the game's buffer of {FontPrinter.MaxMessageLength} values. " +
                        "The game writes past the buffer and crashes (black screen). The code fix \"Safe text loading and drawing\" prevents this."));
                }

                if (message.X >= ScreenCanvas.Width)
                {
                    context.Issues.Add(new TextIssue(false,
                        $"Line {message.Line}: a part starts at x = {message.X}, outside the screen (320 pixels wide). " +
                        "It is not visible."));
                }

                int line = message.Line;
                extents[line] = extents.TryGetValue(line, out TextPreviewLine? known)
                    ? known with { Left = Math.Min(known.Left, left), Right = Math.Max(known.Right, right) }
                    : new TextPreviewLine(line, left, right, top, bottom);
            }
        }

        return [.. extents.Values];
    }

    /// <summary>Draws one character.</summary>
    /// <returns>The first column right of the character's visible pixels (or <paramref name="left"/> if it has none).</returns>
    private static int DrawGlyph(ScreenCanvas canvas, FontGraphics font, FontGlyph glyph, int left, int right,
        FontMessage message, ScreenRect? area)
    {
        int inkRight = left;
        int rows = (int)Math.Ceiling(FontPrinter.GlyphRows * message.ScaleY);
        for (int sy = 0; sy < rows; sy++)
        {
            int ty = (int)(sy / message.ScaleY);
            for (int sx = left; sx < right; sx++)
            {
                int tx = (int)((sx - left) / message.ScaleX);
                if (tx >= glyph.Width || !font.TryGetPixel(glyph, tx, ty, out byte intensity))
                {
                    continue;
                }

                int py = message.Top + sy;
                inkRight = Math.Max(inkRight, sx + 1);
                ScreenColor color = area is { } room && !room.Contains(sx, py) ? OverflowColor : message.Color;
                canvas.Blend(sx, py, new ScreenColor(
                    (byte)(color.R * intensity / 255),
                    (byte)(color.G * intensity / 255),
                    (byte)(color.B * intensity / 255),
                    color.A));
            }
        }

        return inkRight;
    }

    /// <summary>Compares the drawn lines with the room on the screen.</summary>
    private static void CheckLimits(PreviewContext context, List<TextPreviewLine> lines)
    {
        if (context.TextArea is { } area)
        {
            foreach (TextPreviewLine line in lines.Where(l => l.Width > 0))
            {
                if (line.Right > area.Right)
                {
                    context.Issues.Add(new TextIssue(false,
                        $"Line {line.Line} is {line.Right - area.Right} pixel(s) too wide for its room on this screen."));
                }

                if (line.Bottom > area.Bottom || line.Top < area.Top)
                {
                    context.Issues.Add(new TextIssue(false,
                        $"Line {line.Line} lies outside its room on this screen (too far {(line.Top < area.Top ? "up" : "down")})."));
                }
            }
        }

        int pieces = context.Printer.Messages.Count;
        if (pieces > FontPrinter.MaxMessages)
        {
            context.Issues.Add(new TextIssue(true,
                $"This screen would draw {pieces} pieces of text, but the game has room for {FontPrinter.MaxMessages}. " +
                "More pieces overwrite other memory and can crash the game."));
        }
    }
}
