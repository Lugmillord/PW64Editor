using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Text;

/// <summary>One element of the text markup, for the reference shown to users.</summary>
/// <param name="Syntax">How it is written, e.g. "[b]...[/b]".</param>
/// <param name="Meaning">What it does in the game.</param>
/// <param name="StoredAs">How it is stored in the ROM (16-bit codes).</param>
public sealed record TextMarkupElement(string Syntax, string Meaning, string StoredAs);

/// <summary>One 16-bit text code and what it does.</summary>
/// <param name="Code">The code as stored in the ROM.</param>
/// <param name="WrittenAs">How the editor shows it.</param>
/// <param name="Meaning">What the game does with it.</param>
/// <param name="Usable">False for codes that should not be used in texts.</param>
public sealed record TextCodeInfo(int Code, string WrittenAs, string Meaning, bool Usable)
{
    public string CodeHex => $"0x{Code:X2}";
}

/// <summary>
/// Describes the markup produced by <see cref="TextCodec"/> and every single text code, for help screens.
/// </summary>
public static class TextMarkup
{
    /// <summary>The elements of the markup, with descriptions in the editor's language.</summary>
    public static IReadOnlyList<TextMarkupElement> Elements =>
    [
        new("[b]Text[/b]", CoreText.T("Shows the text in the bold variant of the font. Spaces inside stay regular " +
            "spaces, as in the original game."), CoreText.T("glyph number + 0x60")),
        new(CoreText.T("(line break)"), CoreText.T("Starts a new line. Every text also ends with an invisible line break that the editor adds automatically."), "0xFE"),
        new("[x=212]", CoreText.T("Continues the line at horizontal position 212. Used to line up columns, e.g. " +
            "points in score sheets."), "0xFD, 212, 0"),
        new("[x=212,y=5]",
            CoreText.T("Same, with a non-zero third value. Does not occur in the original game."), "0xFD, 212, 5"),
        new("[#5C]", CoreText.T("A single code without a character of its own, by its hex number. The list below " +
            "shows which codes this applies to."), CoreText.T("the given number")),
        new("[nonl]", CoreText.T("Only at the very end: the text has no final line break. Does not occur in the " +
            "original game."), CoreText.T("final 0xFE left out")),
    ];

    /// <summary>
    /// Lists every code from 0x00 to 0xFF with what it does, based on the given font.
    /// </summary>
    public static IReadOnlyList<TextCodeInfo> DescribeCodes(TextFont font)
    {
        var codes = new List<TextCodeInfo>(256);
        for (int code = 0; code <= 0xFF; code++)
        {
            codes.Add(DescribeCode(font, code));
        }

        return codes;
    }

    private static TextCodeInfo DescribeCode(TextFont font, int code)
    {
        switch (code)
        {
            case TextCodec.CodePosition:
                return new(code, "[x=…]", CoreText.T("Continue at a horizontal position. The next two codes are the " +
                    "position and a second value (always 0)."), true);
            case TextCodec.CodeLineBreak:
                return new(code, CoreText.T("(line break)"), CoreText.T("New line."), true);
            case TextCodec.CodeEnd:
                return new(code, CoreText.T("(end)"), CoreText.T("End of the text. Added automatically."), true);
        }

        string raw = $"[#{code:X2}]";
        if (code >= font.GlyphCount)
        {
            return new(code, raw, CoreText.T("Outside the font. The game would draw invalid data. Do not use."), false);
        }

        bool bold = code >= font.BoldOffset;
        string style = bold ? CoreText.T("bold") : CoreText.T("regular");
        char fontCharacter = font.Characters[code];

        if (font.TryGetCharacter(code, out char character, out _))
        {
            if (character == ' ')
            {
                return bold
                    ? new(code, raw, CoreText.T("Space in bold. Looks like a regular space; the editor always writes " +
                        "spaces as 0x42."), true)
                    : new(code, CoreText.T("(space)"), CoreText.T("Space."), true);
            }

            return new(code, bold ? $"[b]{character}[/b]" : character.ToString(), CoreText.F("Character '{0}' ({1}).",
                character, style), true);
        }

        return fontCharacter == '\\'
            ? new(code, raw, CoreText.F("Empty position in the font ({0} half). Draws nothing useful.", style), false)
            : new(code, raw, CoreText.F("Second copy of '{0}' ({1}). Probably looks the same as the first one.",
                fontCharacter, style), true);
    }
}
