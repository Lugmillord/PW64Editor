using System.Buffers.Binary;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Text.Preview;

namespace PW64Editor.Core.Tests.Text;

/// <summary>A tiny font in the game's format, for tests without the ROM.</summary>
internal static class SyntheticFont
{
    // Glyphs: 0 = 'A' (10 px), 1 = 'B' (5 px), 2 = unused (0 px), 3 = ' ' (4 px).
    public const short A = 0, B = 1, Unused = 2;

    public static FontGraphics Create()
    {
        const int imageWidth = 32, height = 17;
        (int Width, int S)[] glyphs = [(10, 0), (5, 10), (0, 15), (4, 15)];

        byte[] bitmaps = new byte[glyphs.Length * 16];
        for (int i = 0; i < glyphs.Length; i++)
        {
            Span<byte> entry = bitmaps.AsSpan(i * 16, 16);
            BinaryPrimitives.WriteInt16BigEndian(entry, (short)glyphs[i].Width);
            BinaryPrimitives.WriteInt16BigEndian(entry[2..], imageWidth);
            BinaryPrimitives.WriteInt16BigEndian(entry[4..], (short)glyphs[i].S);
            BinaryPrimitives.WriteInt16BigEndian(entry[6..], 0);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], 0);
            BinaryPrimitives.WriteInt16BigEndian(entry[12..], height);
        }

        byte[] format = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(format, 3); // G_IM_FMT_IA
        byte[] image = Enumerable.Repeat((byte)0xFF, imageWidth * height / 2).ToArray(); // all pixels white and visible

        byte[] file = IffBuilder.Form("UVFT",
            IffBuilder.Chunk("STRG", "AB\\ "u8.ToArray()),
            IffBuilder.GzipChunk("BITM", bitmaps),
            IffBuilder.Chunk("FRMT", format),
            IffBuilder.GzipChunk("IMAG", image));
        return FontGraphics.FromFontFile(file, 6);
    }

    /// <summary>Encoded text (ROM codes) from glyph numbers and control codes.</summary>
    public static ushort[] Codes(params int[] values) => values.Select(v => (ushort)v).ToArray();
}

public class FontGraphicsTests
{
    [Fact]
    public void FromFontFile_ReadsGlyphTableAndImages()
    {
        FontGraphics font = SyntheticFont.Create();

        Assert.Equal(4, font.Glyphs.Count);
        Assert.Equal([10, 5, 0, 4], font.Glyphs.Select(g => g.Width).ToArray());
        Assert.Equal(17, font.Height);
        Assert.Equal(10, font.DefaultWidth);
        Assert.Equal(1, font.GlyphOf('B'));
        Assert.Equal(-2, font.GlyphOf('Z'));
        Assert.True(font.TryGetPixel(font.Glyphs[1], 0, 0, out byte intensity));
        Assert.Equal(255, intensity);
        Assert.False(font.TryGetPixel(font.Glyphs[1], 40, 0, out _)); // outside the image
    }

    [RealRomFact]
    public void RealTextFont_HasProportionalGlyphs()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        FontGraphics font = FontGraphics.Load(fs, TextFont.TextFontIndex);

        Assert.Equal(192, font.Glyphs.Count);
        Assert.Equal(17, font.Height);
        Assert.Equal(15, font.Glyphs[font.GlyphOf('W')].Width);
        Assert.Equal(3, font.Glyphs[font.GlyphOf('i')].Width);
        Assert.Equal(4, font.Glyphs[0x42].Width); // the space
        Assert.Equal(0, font.Glyphs[0x5A].Width);  // an unused glyph
    }
}

public class FontPrinterTests
{
    private static readonly FontGraphics Font = SyntheticFont.Create();

    private static GameTextMemory Memory(params int[] codes) =>
        GameTextMemory.FromCodes("TEST", SyntheticFont.Codes(codes), isEdited: true);

    [Fact]
    public void FromCodes_ConvertsLikeTheGamesLoader_AlsoInsidePositionCodes()
    {
        // textLoadBlock turns every 0x00FE into 0x0FFE and every 0x00FF into -1, even the x of a position.
        GameTextMemory text = Memory(0xFD, 254, 0, SyntheticFont.A, 0xFE, 0xFF);

        Assert.Equal(GameTextMemory.Position, text[0]);
        Assert.Equal(GameTextMemory.LineBreak, text[1]);
        Assert.Equal(GameTextMemory.LineBreak, text[4]);
        Assert.Equal(GameTextMemory.End, text[5]);
    }

    [Fact]
    public void PrintStr16_SplitsAtPositionCodes_AndReturnsTheUsedValues()
    {
        var printer = new FontPrinter(Font);
        GameTextMemory text = Memory(SyntheticFont.A, 0xFD, 100, 0, SyntheticFont.B, 0xFE, SyntheticFont.A, 0xFE, 0xFF);

        int first = printer.PrintStr16(28, 180, text, 0, 255);
        int second = printer.PrintStr16(28, 164, text, first, 255);
        int third = printer.PrintStr16(28, 148, text, first + second, 255);

        Assert.Equal(6, first);   // A, position (3 values), B, line break
        Assert.Equal(2, second);  // A, line break
        Assert.Equal(-1, third);  // the end: the game still adds an (empty) piece
        Assert.Equal(4, printer.Messages.Count);
        Assert.Equal(28, printer.Messages[0].X);
        Assert.Equal(100, printer.Messages[1].X);
        Assert.Equal(240 - (180 + 17), printer.Messages[0].Top);
        Assert.Equal(2, printer.Messages[2].Line);
    }

    [Theory]
    [InlineData(42, false, false)]
    [InlineData(43, false, true)]  // the original writes the end marker behind the buffer (checked in a MIPS emulator)
    [InlineData(44, false, true)]
    [InlineData(43, true, false)]  // with the code fix: no overflow, the line is cut instead
    [InlineData(50, true, false)]
    public void PrintStr16_LongPieces_OverflowOnlyWithoutTheFix(int length, bool fix, bool overflow)
    {
        var printer = new FontPrinter(Font) { SafeTextFix = fix };
        GameTextMemory text = Memory([.. Enumerable.Repeat(0, length), 0xFE, 0xFF]);

        int used = printer.PrintStr16(28, 180, text, 0, 255);

        Assert.Equal(overflow, printer.Messages[0].Overflow);
        if (fix && length >= 43)
        {
            Assert.Equal(43, used);
            Assert.True(printer.Messages[0].Truncated);
        }
    }

    [Fact]
    public void PrintStr16_LongPieceAfterPosition_HasNoEndMarkerInTheOriginal()
    {
        GameTextMemory text = Memory([SyntheticFont.A, 0xFD, 100, 0, .. Enumerable.Repeat(0, 44), 0xFE, 0xFF]);
        var original = new FontPrinter(Font);
        var fixedPrinter = new FontPrinter(Font) { SafeTextFix = true };

        original.PrintStr16(28, 180, text, 0, 255);
        fixedPrinter.PrintStr16(28, 180, text, 0, 255);

        Assert.True(original.Messages[1].Overflow);
        Assert.False(fixedPrinter.Messages[1].Overflow);
    }

    [Fact]
    public void PrintStr16_WithShortLimit_CutsThePieceCleanly()
    {
        var printer = new FontPrinter(Font);
        GameTextMemory text = Memory([.. Enumerable.Repeat(0, 50), 0xFE, 0xFF]);

        int used = printer.PrintStr16(28, 180, text, 0, 40);

        Assert.Equal(40, used);
        Assert.True(printer.Messages[0].Truncated);
        Assert.False(printer.Messages[0].Overflow);
    }

    [Fact]
    public void Str16Width_CountsCodesWithoutGlyphWithTheDefaultWidth()
    {
        var printer = new FontPrinter(Font);

        // A (10) + B (5) + line break (default width = glyph 0 = 10); the end stops the count.
        Assert.Equal(25, printer.Str16Width(Memory(SyntheticFont.A, SyntheticFont.B, 0xFE, 0xFF), 0));
    }
}

public class TextPreviewRendererTests
{
    private static readonly TextPreviewRenderer Renderer = new(SyntheticFont.Create());

    private static TextPreview Render(string name, params int[] codes) =>
        Renderer.Render(name, TextCatalog.Categorize(name), SyntheticFont.Codes(codes));

    private static (byte B, byte G, byte R) PixelAt(TextPreview preview, int x, int y)
    {
        int i = (y * TextPreview.Width + x) * 4;
        return (preview.Pixels[i], preview.Pixels[i + 1], preview.Pixels[i + 2]);
    }

    [Fact]
    public void Render_MeasuresLinesWithTheGlyphWidths()
    {
        TextPreview preview = Render("FILE_SEL", SyntheticFont.A, SyntheticFont.B, 0xFE, SyntheticFont.B, 0xFE, 0xFF);

        Assert.Equal(TextPreview.Width * TextPreview.Height * 4, preview.Pixels.Length);
        Assert.Equal(2, preview.Lines.Count);
        Assert.Equal(new TextPreviewLine(1, 28, 43, 27, 42), preview.Lines[0]);
        Assert.Equal(5, preview.Lines[1].Width);
        Assert.Equal(16, preview.Lines[1].Top - preview.Lines[0].Top); // line spacing
        Assert.Empty(preview.Issues);
    }

    [Fact]
    public void Render_TooWideLine_IsReportedAndDrawnInRed()
    {
        TextPreview preview = Render("FILE_SEL", [.. Enumerable.Repeat(0, 30), 0xFE, 0xFF]);

        Assert.Contains(preview.Issues, i => !i.IsError && i.Message.Contains("too wide"));
        TextPreviewLine line = preview.Lines[0];
        Assert.Equal((0x30, 0x30, 0xFF), PixelAt(preview, 310, line.Top + 2));   // outside the room (x >= 304): red
        Assert.Equal((0xD2, 0xD2, 0xD2), PixelAt(preview, 100, line.Top + 2));   // inside: the normal gray
    }

    [Fact]
    public void Render_ResultsScreen_WithoutFinalLineBreak_ReportsAHang()
    {
        TextPreview preview = Render("HG_3_A", SyntheticFont.A, 0xFE, SyntheticFont.B, 0xFF);

        Assert.Equal("Tip screen after a flight", preview.ScreenTitle);
        Assert.Contains(preview.Issues, i => i.IsError && i.Message.Contains("hangs"));
    }

    [Fact]
    public void Render_MissionName_ShowsOnlyTheFirstLine()
    {
        TextPreview preview = Render("A_HG_1_N", SyntheticFont.A, 0xFE, SyntheticFont.B, 0xFE, 0xFF);

        Assert.Equal("Mission screen", preview.ScreenTitle);
        Assert.Single(preview.Lines);
        Assert.Contains(preview.Issues, i => i.Message.Contains("first line"));
    }

    [Fact]
    public void Render_MissionDescription_EighthLineIsBelowTheBox()
    {
        int[] eightLines = [.. Enumerable.Range(0, 8).SelectMany(_ => new[] { 0, 0xFE }), 0xFF];

        TextPreview preview = Render("A_HG_1_M", eightLines);

        Assert.Equal(8, preview.Lines.Count);
        Assert.Equal(46, preview.Lines[0].Left);
        Assert.Equal(14, preview.Lines[1].Top - preview.Lines[0].Top);
        string issue = Assert.Single(preview.Issues).Message;
        Assert.StartsWith("Line 8 lies outside", issue);
    }

    [Fact]
    public void Render_UnusedGlyph_StopsTheLine()
    {
        TextPreview preview = Render("FILE_SEL", SyntheticFont.A, SyntheticFont.Unused, SyntheticFont.A, 0xFE, 0xFF);

        Assert.Equal(10, preview.Lines[0].Width);
        Assert.Contains(preview.Issues, i => i.Message.Contains("has no image"));
    }

    [Fact]
    public void Render_Position254_EndsUpOutsideTheScreen()
    {
        // 254 = 0x00FE is changed to 0x0FFE by the loader.
        TextPreview preview = Render("FILE_SEL", SyntheticFont.A, 0xFD, 254, 0, SyntheticFont.B, 0xFE, 0xFF);

        Assert.Contains(preview.Issues, i => i.Message.Contains("x = 4094"));
    }

    [Fact]
    public void Render_TooManyPieces_IsAnError()
    {
        // 31 lines on a screen without its own limit: more pieces than the game's 30.
        int[] lines = [.. Enumerable.Range(0, 31).SelectMany(_ => new[] { 1, 0xFE }), 0xFF];

        TextPreview preview = Render("HG_B1_S1", lines);

        Assert.Contains(preview.Issues, i => i.IsError && i.Message.Contains("room for 30"));
    }

    [RealRomFact]
    public void RealRom_EveryOriginalText_FitsItsScreen()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), GameTextFile.FindFile(fs).Data, fromProject: false);
        TextPreviewRenderer renderer = TextPreviewRenderer.Load(fs);
        Dictionary<string, GameText> byName = library.Texts.ToDictionary(t => t.Name);
        IReadOnlyList<ushort>? Other(string name) => byName.TryGetValue(name, out GameText? t) ? library.Codec.Encode(t.Markup).Codes : null;

        foreach (GameText text in library.Texts)
        {
            TextPreview preview = renderer.Render(text.Name, text.Category, library.Codec.Encode(text.Markup).Codes, Other);

            Assert.True(preview.Issues.Count == 0, $"{text.Name}: {string.Join(" / ", preview.Issues.Select(i => i.Message))}");
            Assert.InRange(preview.PiecesOnScreen, 1, 30);
        }

        TextPreview mission = renderer.Render("A_HG_1_M", byName["A_HG_1_M"].Category, library.Codec.Encode(byName["A_HG_1_M"].Markup).Codes, Other);
        Assert.Equal("Mission screen", mission.ScreenTitle);
        Assert.Equal(4, mission.Lines.Count);
        Assert.Equal(new ScreenRect(39, 51, 281, 159), mission.TextArea);
        Assert.Equal(46, mission.Lines[0].Left);
        Assert.Equal(57, mission.Lines[0].Top);
    }
}
