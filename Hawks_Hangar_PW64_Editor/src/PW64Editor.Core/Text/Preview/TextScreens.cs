using System.Globalization;
using System.Text.RegularExpressions;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Text.Preview;

/// <summary>Everything a screen needs to draw itself.</summary>
internal sealed class PreviewContext
{
    public required ScreenCanvas Canvas { get; init; }

    public required FontPrinter Printer { get; init; }

    /// <summary>The text font (number 6).</summary>
    public required FontGraphics TextFont { get; init; }

    /// <summary>Other fonts by number (0 = mission numbers, 3 = points), if they could be loaded.</summary>
    public required IReadOnlyDictionary<int, FontGraphics> OtherFonts { get; init; }

    /// <summary>The edited text.</summary>
    public required GameTextMemory Text { get; init; }

    /// <summary>Looks up another text by name (in its current, possibly edited version).</summary>
    public required Func<string, GameTextMemory?> OtherText { get; init; }

    public List<TextIssue> Issues { get; } = [];

    /// <summary>Where the edited text has room. Parts outside are marked red.</summary>
    public ScreenRect? TextArea { get; set; }
}

/// <summary>
/// A screen of the game on which texts appear, rebuilt from the drawing code of the
/// decompilation (positions, colors, boxes and the way the lines are printed).
/// </summary>
internal abstract class TextScreen
{
    /// <summary>
    /// Border that old TVs may cut off (overscan). Texts without a box should stay inside it.
    /// </summary>
    protected const int SafeMargin = 16;

    /// <summary>Text color of most screens (0xD2 gray).</summary>
    protected static readonly ScreenColor TextColor = ScreenColor.Gray(0xD2);

    /// <summary>The screen's name, shown above the preview.</summary>
    public abstract string Title { get; }

    /// <summary>What the preview shows and how exact it is.</summary>
    public abstract string Note { get; }

    public abstract void Draw(PreviewContext context);

    /// <summary>Picks the screen on which a text appears.</summary>
    public static TextScreen For(string name, TextCategory category)
    {
        // Custom texts are not shown anywhere yet; their name may look like any other.
        if (category.Area == CustomTexts.AreaName)
        {
            return new GenericScreen(custom: true);
        }

        if (MissionBriefingScreen.TryCreate(name) is { } briefing)
        {
            return briefing;
        }

        if (ResultsScreen.TryCreate(name) is { } results)
        {
            return results;
        }

        if (category.Section == "In-flight messages" || category.Area == "In flight")
        {
            return new InFlightScreen();
        }

        return new GenericScreen();
    }

    /// <summary>A sky with a horizon, standing in for the 3D scene behind the texts.</summary>
    protected static void DrawScenery(ScreenCanvas canvas)
    {
        canvas.VerticalGradient(new ScreenRect(0, 0, ScreenCanvas.Width, 150),
            new ScreenColor(0x2E, 0x5C, 0xB4), new ScreenColor(0xA6, 0xC6, 0xE6));
        canvas.VerticalGradient(new ScreenRect(0, 150, ScreenCanvas.Width, ScreenCanvas.Height),
            new ScreenColor(0x6A, 0x92, 0x52), new ScreenColor(0x34, 0x5E, 0x2C));
    }

    /// <summary>
    /// screenDrawBox2 (src/app/code_66160.c) without a title: a translucent black box with a
    /// 2-pixel blue frame and a thin black edge. Coordinates as in the game (y from the bottom).
    /// </summary>
    /// <returns>The inside of the frame.</returns>
    protected static ScreenRect DrawBox(ScreenCanvas canvas, int x, int y, int width, int height)
    {
        const int border = 2;
        ScreenRect outer = ScreenRect.FromGame(x, y, width, height);
        ScreenRect inner = ScreenRect.FromGame(x + border, y + border, width - 2 * border, height - 2 * border);

        canvas.Fill(inner, new ScreenColor(0, 0, 0, 0xA0));
        canvas.Outline(outer.Inflate(1), new ScreenColor(0, 0, 0));
        // The frame is shaded from light blue (0x14,0xAA,0xFF) outside to dark blue (0x00,0x0F,0xA0) inside.
        canvas.Outline(outer, new ScreenColor(0x0F, 0x84, 0xE7));
        canvas.Outline(outer.Inflate(-1), new ScreenColor(0x05, 0x34, 0xB8));
        canvas.Outline(inner, new ScreenColor(0, 0, 0, 0x60));
        return inner;
    }

    /// <summary>
    /// Prints a text line by line, the way most screens do:
    /// <c>do { i += uvFontPrintStr16(x, y, &amp;text[i], 255, 0xFFE); y -= step; } while (text[i] != -1);</c>
    /// </summary>
    /// <remarks>
    /// When the text does not end with a line break, the print function returns -1 at the end,
    /// the index goes back by one and the loop never ends: the game hangs.
    /// </remarks>
    protected static void PrintUntilEnd(PreviewContext context, GameTextMemory text, int x, int y, int step)
    {
        const int MaxRounds = 60;
        int index = 0;
        int rounds = 0;
        do
        {
            index += context.Printer.PrintStr16(x, y, text, index, 255);
            y -= step;
            rounds++;
        }
        while (text[index] != GameTextMemory.End && rounds < MaxRounds && index >= 0);

        if (rounds >= MaxRounds || index < 0)
        {
            context.Issues.Add(new TextIssue(true,
                CoreText.T("On this screen the game prints lines until it reaches the end of the text. Without a " +
                    "line break at the very end ([nonl]) it never finds the end and prints the last line again and " +
                    "again, or reads memory in front of the text: the game hangs or crashes. Remove [nonl].")));
        }
    }

    /// <summary>A short string the game builds from glyph numbers, ended with a line break.</summary>
    protected static GameTextMemory Glyphs(string name, params short[] glyphs) =>
        GameTextMemory.FromValues(name, [.. glyphs, GameTextMemory.LineBreak, GameTextMemory.End]);

    /// <summary>Bold digits as the game writes numbers (textFmtInt: digit + 0x60, -3 = empty space).</summary>
    protected static short[] BoldNumber(int value, int length)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        var result = new List<short>();
        for (int i = digits.Length; i < length; i++)
        {
            result.Add(-3);
        }

        result.AddRange(digits.Select(d => (short)(d - '0' + 0x60)));
        return [.. result];
    }
}

/// <summary>
/// The mission screen before a flight: name box at the top, description (or hint) box below
/// (test_menu.c, the function that draws the "test" menu).
/// </summary>
internal sealed partial class MissionBriefingScreen : TextScreen
{
    private readonly string _prefix;
    private readonly string _vehicle;
    private readonly string _number;
    private readonly string _kind;

    private MissionBriefingScreen(string cls, string vehicle, string number, string kind)
    {
        _prefix = $"{cls}_{vehicle}_{number}_";
        _vehicle = vehicle;
        _number = number;
        _kind = kind;
    }

    public override string Title => _kind == "H" ? CoreText.T("Mission screen with hint") : CoreText.T("Mission screen");

    public override string Note =>
        CoreText.T("Name box at the top, description (or hint, after pressing the button for it) in the large box. " +
            "Mission number and points are examples; the 3D scene behind is only suggested.");

    public static MissionBriefingScreen? TryCreate(string name)
    {
        Match m = Pattern().Match(name);
        return m.Success ? new MissionBriefingScreen(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value) : null;
    }

    public override void Draw(PreviewContext context)
    {
        ScreenCanvas canvas = context.Canvas;
        FontPrinter printer = context.Printer;
        DrawScenery(canvas);

        // Boxes (US version): description, name, points.
        ScreenRect bodyBox = DrawBox(canvas, 38, 80, 244, 110);
        ScreenRect nameBox = DrawBox(canvas, 38, 195, 191, 25);
        bool hasPoints = _vehicle != "BD";
        if (hasPoints)
        {
            DrawBox(canvas, 235, 195, 48, 25);
        }

        GameTextMemory? name = _kind == "N" ? context.Text : context.OtherText(_prefix + "N");
        GameTextMemory? body = _kind is "M" or "H" ? context.Text : context.OtherText(_prefix + "M");
        // The room is the inside of the box plus the dark inner pixel of the frame: one original
        // hint (B_BD_3_H) touches it, all others stay inside.
        context.TextArea = (_kind == "N" ? nameBox : bodyBox).Inflate(1);

        // Mission number in font 0, squeezed to 80 % height.
        if (context.OtherFonts.TryGetValue(0, out FontGraphics? numberFont))
        {
            printer.Font = numberFont;
            printer.ScaleX = 1f;
            printer.ScaleY = 0.8f;
            printer.Color = ScreenColor.Gray(0xBE);
            printer.PrintStr(67, 200, _number.TrimStart('0'));
        }

        // Mission name: one call, so only the first line is shown.
        printer.Font = context.TextFont;
        printer.ScaleX = 1f;
        printer.ScaleY = 1f;
        printer.Color = ScreenColor.Gray(0xBE);
        if (name is not null)
        {
            printer.PrintStr16(79, 196, name, 0, 255);
        }

        // Points (example value) in font 3: "100" and a narrower "PTS".
        if (hasPoints && context.OtherFonts.TryGetValue(3, out FontGraphics? pointsFont))
        {
            printer.Font = pointsFont;
            printer.ScaleX = 1f;
            int numberWidth = printer.StrWidth("100");
            printer.ScaleX = 0.7f;
            int ptsWidth = printer.StrWidth("PTS");
            int x = 258 - (ptsWidth + numberWidth) / 2;
            printer.ScaleX = 1f;
            printer.PrintStr(x, 196, "100");
            printer.Color = ScreenColor.Gray(0xAA);
            printer.ScaleX = 0.7f;
            printer.PrintStr(x + numberWidth + 1, 196, "PTS");
        }

        // Description or hint: one line every 14 pixels, at most 20 lines.
        printer.Font = context.TextFont;
        printer.ScaleX = 1f;
        printer.ScaleY = 1f;
        printer.Color = TextColor;
        if (body is not null)
        {
            int index = 0;
            bool reachedEnd = false;
            for (int y = 166; y > -114; y -= 14)
            {
                int used = printer.PrintStr16(46, y, body, index, 255);
                if (used == -1)
                {
                    reachedEnd = true;
                    break;
                }

                index += used;
            }

            if (!reachedEnd && body.IsEdited && body[index] != GameTextMemory.End)
            {
                context.Issues.Add(new TextIssue(false, CoreText.T("This screen prints at most 20 lines; the rest of " +
                    "the text is not shown.")));
            }
        }

        if (_kind == "N" && name is not null && HasMoreLines(name))
        {
            context.Issues.Add(new TextIssue(false, CoreText.T("Only the first line of a mission name is shown on " +
                "this screen.")));
        }
    }

    /// <summary>True if anything visible follows the first line break.</summary>
    private static bool HasMoreLines(GameTextMemory text)
    {
        int i = 0;
        while (text[i] != GameTextMemory.LineBreak && text[i] != GameTextMemory.End)
        {
            i++;
        }

        return text[i] == GameTextMemory.LineBreak && text[i + 1] != GameTextMemory.End;
    }

    [GeneratedRegex(@"^(E|B|A|P)_(HG|RP|GC|BD|EX)_(\d+)_(N|M|H)$")]
    private static partial Regex Pattern();
}

/// <summary>
/// The screen after a flight (results.c): the 3D scene darkened, with a score sheet or a tip.
/// </summary>
internal sealed partial class ResultsScreen : TextScreen
{
    private readonly bool _isTip;
    private readonly bool _isBirdman;
    private readonly int _sheet;
    private readonly string _vehicle;

    private ResultsScreen(bool isTip, bool isBirdman, int sheet, string vehicle)
    {
        _isTip = isTip;
        _isBirdman = isBirdman;
        _sheet = sheet;
        _vehicle = vehicle;
    }

    public override string Title =>
        _isTip ? CoreText.T("Tip screen after a flight") : CoreText.F("Results screen, sheet {0}", _sheet);

    public override string Note => _isTip
        ? CoreText.T("Shown on the results screen after asking for a tip. The menu (Replay, Next, ...) is not shown.")
        : _sheet == 2 && !_isBirdman
            ? CoreText.T("Points on the right are example values. The menu (Photo, Replay, Next) is not shown.")
            : CoreText.T("The menu (Photo, Replay, Next) is not shown.");

    public static ResultsScreen? TryCreate(string name)
    {
        Match m;
        if ((m = TipPattern().Match(name)).Success)
        {
            return new ResultsScreen(true, false, 0, m.Groups[1].Value);
        }

        if ((m = SheetPattern().Match(name)).Success)
        {
            return new ResultsScreen(false, false, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), m.Groups[1].Value);
        }

        if ((m = BirdmanPattern().Match(name)).Success)
        {
            return new ResultsScreen(false, true, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), "BD");
        }

        return null;
    }

    public override void Draw(PreviewContext context)
    {
        ScreenCanvas canvas = context.Canvas;
        FontPrinter printer = context.Printer;
        DrawScenery(canvas);
        canvas.Fill(new ScreenRect(0, 0, ScreenCanvas.Width, ScreenCanvas.Height), new ScreenColor(0, 0, 0, 130));

        if (!_isTip && !_isBirdman)
        {
            // BOX_X0..BOX_X1 at BOX_Y1..BOX_Y0: a one pixel line.
            canvas.Fill(ScreenRect.FromGame(36, 119, 236, 1), TextColor);
        }

        int x = _isTip ? 44 : 28;
        int y = _isTip ? 168 : 180;
        // No box: the room is the screen minus the border old TVs may cut off.
        context.TextArea = new ScreenRect(x, SafeMargin, ScreenCanvas.Width - SafeMargin, ScreenCanvas.Height - SafeMargin);

        printer.Font = context.TextFont;
        printer.ScaleX = 1f;
        printer.ScaleY = 1f;
        printer.Color = TextColor;
        PrintUntilEnd(context, context.Text, x, y, 16);

        if (_sheet == 2 && !_isBirdman)
        {
            DrawExamplePoints(context);
        }
    }

    /// <summary>
    /// The points the game prints next to sheet 2 (X_TOTAL = 180): up to four values from
    /// y = 116 upwards, and the total at y = 100 (Hang Glider, Cannonball, Sky Diving) or 84
    /// (the others, which show the deducted points at y = 100).
    /// </summary>
    private void DrawExamplePoints(PreviewContext context)
    {
        FontPrinter printer = context.Printer;
        int[] lineHasText = LineLengths(context.Text);

        for (int i = 0; i < 4; i++)
        {
            int y = 116 + 16 * i;
            int line = (180 - y) / 16; // 0-based line of the sheet at this height
            if (line < lineHasText.Length && lineHasText[line] > 0)
            {
                printer.PrintStr16(180, y, Glyphs("points", BoldNumber(20, 3)), 0, 3);
            }
        }

        bool totalHigh = _vehicle is "HG" or "CB" or "SD";
        int totalY = totalHigh ? 100 : 84;
        printer.PrintStr16(180, totalY, Glyphs("total", BoldNumber(100, 3)), 0, 3);
        if (context.OtherText("PTS") is { } label)
        {
            printer.PrintStr16(180 + 36, totalY, label, 0, 4);
        }

        if (!totalHigh)
        {
            printer.PrintStr16(180, 100, Glyphs("deducted", [-3, -3, (short)(0 + 0x60)]), 0, 4);
        }
    }

    /// <summary>Number of visible values per line.</summary>
    private static int[] LineLengths(GameTextMemory text)
    {
        var lengths = new List<int> { 0 };
        for (int i = 0; text[i] != GameTextMemory.End && i < 2000; i++)
        {
            if (text[i] == GameTextMemory.LineBreak)
            {
                lengths.Add(0);
            }
            else if (text[i] != 0x42)
            {
                lengths[^1]++;
            }
        }

        return [.. lengths];
    }

    [GeneratedRegex(@"^(HG|RP|GC)_\d+_A$")]
    private static partial Regex TipPattern();

    [GeneratedRegex(@"^(HG|RP|GC|SD|CB|HP)_(?:[EBAP]\d?|L\d+)_S([12])$")]
    private static partial Regex SheetPattern();

    [GeneratedRegex(@"^BD_ALL_S([12])$")]
    private static partial Regex BirdmanPattern();
}

/// <summary>
/// A message during the flight (hud.c): one line, centered, white.
/// </summary>
internal sealed class InFlightScreen : TextScreen
{
    public override string Title => CoreText.T("Message during the flight");

    public override string Note =>
        CoreText.T("Centered like the game's flight messages (hudDrawStartText). Some messages appear a little " +
            "higher or lower, or in another color. Only the first line is shown.");

    public override void Draw(PreviewContext context)
    {
        DrawScenery(context.Canvas);
        FontPrinter printer = context.Printer;
        printer.Font = context.TextFont;
        printer.ScaleX = 1f;
        printer.ScaleY = 1f;
        printer.Color = ScreenColor.Gray(0xFF);
        context.TextArea = new ScreenRect(SafeMargin, SafeMargin, ScreenCanvas.Width - SafeMargin, ScreenCanvas.Height - SafeMargin);

        // The game measures up to 44 values across line breaks and subtracts 16 (two codes without glyph).
        int width = printer.Str16Width(context.Text, 0);
        int x = ScreenCanvas.Width / 2 - (width - 16) / 2;
        int used = printer.PrintStr16(x, 140, context.Text, 0, 0x28);
        if (used != -1 && context.Text[used] != GameTextMemory.End)
        {
            context.Issues.Add(new TextIssue(false,
                CoreText.T("Flight messages are printed with a single call: only the first line is shown, and " +
                    "further lines also shift the first one to the left, because the game measures the whole text " +
                    "to center it.")));
        }
    }
}

/// <summary>Texts whose exact place on the screen is not known: shown line by line at the left.</summary>
internal sealed class GenericScreen : TextScreen
{
    private readonly bool _custom;

    public GenericScreen(bool custom = false)
    {
        _custom = custom;
    }

    public override string Title => _custom ? CoreText.T("New text") : CoreText.T("General preview");

    public override string Note => _custom
        ? CoreText.T("This text is not shown anywhere in the game yet. It is drawn with the game's font and line " +
            "spacing; where you use it later, less room may be available.")
        : CoreText.T("The exact place of this text is not known; it is shown with the game's font and line spacing " +
            "at a typical position. Widths are exact.");

    public override void Draw(PreviewContext context)
    {
        DrawScenery(context.Canvas);
        context.Canvas.Fill(new ScreenRect(0, 0, ScreenCanvas.Width, ScreenCanvas.Height), new ScreenColor(0, 0, 0, 130));
        FontPrinter printer = context.Printer;
        printer.Font = context.TextFont;
        printer.ScaleX = 1f;
        printer.ScaleY = 1f;
        printer.Color = TextColor;
        context.TextArea = new ScreenRect(SafeMargin, SafeMargin, ScreenCanvas.Width - SafeMargin, ScreenCanvas.Height - SafeMargin);

        // Line by line, but without the endless loop (the real screen is unknown).
        int index = 0;
        int y = 196;
        for (int line = 0; line < 30; line++, y -= 16)
        {
            int used = printer.PrintStr16(28, y, context.Text, index, 255);
            if (used == -1)
            {
                break;
            }

            index += used;
            if (context.Text[index] == GameTextMemory.End)
            {
                break;
            }
        }
    }
}
