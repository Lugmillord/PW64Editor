using System.Buffers.Binary;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Tests.Text;

public class TextFontTests
{
    /// <summary>A small font with the same structure as the real one: regular half, bold half.</summary>
    internal static readonly TextFont SmallFont = new("AB #\\" + "AB }\\");

    [Fact]
    public void Glyphs_MapToCharactersAndStyle()
    {
        Assert.True(SmallFont.TryGetCharacter(1, out char regular, out bool regularBold));
        Assert.True(SmallFont.TryGetCharacter(6, out char bold, out bool boldBold));

        Assert.Equal(('B', false), (regular, regularBold));
        Assert.Equal(('B', true), (bold, boldBold));
    }

    [Fact]
    public void UnusedGlyphs_HaveNoCharacter()
    {
        Assert.False(SmallFont.TryGetCharacter(4, out _, out _)); // backslash placeholder
        Assert.False(SmallFont.TryGetCharacter(99, out _, out _)); // beyond the font
    }

    [Fact]
    public void TryGetGlyph_FindsGlyphPerStyle()
    {
        Assert.True(SmallFont.TryGetGlyph('A', true, out int glyph));
        Assert.Equal(5, glyph);
        Assert.False(SmallFont.TryGetGlyph('}', false, out _)); // only in bold
        Assert.False(SmallFont.TryGetGlyph('\\', false, out _));
    }

    [RealRomFact]
    public void RealFont_HasExpectedLayout()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        TextFont font = TextFont.Load(fs);

        Assert.Equal(192, font.GlyphCount);
        Assert.Equal(0x60, font.BoldOffset);
        Assert.True(font.TryGetGlyph(' ', false, out int space));
        Assert.Equal(0x42, space);
        Assert.True(font.TryGetGlyph('A', true, out int boldA));
        Assert.Equal(0x6A, boldA);
    }
}

public class TextCodecTests
{
    private static readonly TextCodec Codec = new(TextFontTests.SmallFont);

    // SmallFont glyphs: 0 'A', 1 'B', 2 ' ', 3 '#', 5 bold 'A', 6 bold 'B', 8 bold '}'.

    [Fact]
    public void Decode_PlainText_HidesFinalLineBreak()
    {
        Assert.Equal("AB", Codec.DecodeCodes(Codes(0, 1, 0xFE, 0xFF)));
    }

    [Fact]
    public void Decode_Bold_KeepsSpacesInsideRun()
    {
        // bold A, space, bold B, regular A
        Assert.Equal("[b]A B[/b]A", Codec.DecodeCodes(Codes(5, 2, 6, 0, 0xFE, 0xFF)));
    }

    [Fact]
    public void Decode_SpacesAroundBold_StayOutside()
    {
        Assert.Equal("A [b]B[/b] A", Codec.DecodeCodes(Codes(0, 2, 6, 2, 0, 0xFE, 0xFF)));
    }

    [Fact]
    public void Decode_LineBreakPositionAndRawCodes()
    {
        Assert.Equal("A\n[x=212]B[#04]", Codec.DecodeCodes(Codes(0, 0xFE, 0xFD, 212, 0, 1, 4, 0xFE, 0xFF)));
    }

    [Fact]
    public void Decode_MarksMissingFinalLineBreak()
    {
        Assert.Equal("AB[nonl]", Codec.DecodeCodes(Codes(0, 1, 0xFF)));
    }

    [Fact]
    public void Decode_IgnoresPaddingAfterEnd()
    {
        byte[] data = [0x00, 0x00, 0x00, 0xFE, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        Assert.Equal("A", Codec.Decode(data));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("[b]A B[/b]A")]
    [InlineData("A [b]B[/b] A")]
    [InlineData("A\n[x=212]B[#04]")]
    [InlineData("AB[nonl]")]
    [InlineData("\n\nA\n")]
    [InlineData("[x=10,y=3]A")]
    public void Encode_ThenDecode_RoundTrips(string markup)
    {
        TextEncodeResult result = Codec.Encode(markup);

        Assert.True(result.Success);
        Assert.Equal(markup, Codec.DecodeCodes(result.Codes.ToArray()));
    }

    [Fact]
    public void Encode_AddsFinalLineBreakAndEnd()
    {
        Assert.Equal(Codes(5, 0, 0xFE, 0xFF), Codec.Encode("[b]A[/b]A").Codes.ToArray());
    }

    [Fact]
    public void Encode_AcceptsWindowsLineBreaks()
    {
        Assert.Equal(Codes(0, 0xFE, 1, 0xFE, 0xFF), Codec.Encode("A\r\nB").Codes.ToArray());
    }

    [Theory]
    [InlineData("Ä", "not in the game's font")]
    [InlineData("}", "only in bold")]
    [InlineData("A[/b]", "without a matching [b]")]
    [InlineData("[b]A[b]", "inside bold text")]
    [InlineData("[foo]", "Unknown tag")]
    [InlineData("A[nonl]B", "only allowed at the very end")]
    [InlineData("[#FE]", "not a valid raw code")]
    [InlineData("[x=abc]", "not a valid position")]
    [InlineData("A[b", "closing ']' is missing")]
    public void Encode_ReportsErrors(string markup, string expectedMessagePart)
    {
        TextEncodeResult result = Codec.Encode(markup);

        Assert.False(result.Success);
        Assert.Empty(result.Codes);
        Assert.Contains(expectedMessagePart, result.Errors[0].Message);
    }

    [Fact]
    public void ToChunkData_KeepsOriginalSizeWhenItFits_OtherwiseAlignsTo4()
    {
        ushort[] codes = Codes(0, 0xFE, 0xFF); // 6 bytes

        Assert.Equal(16, TextCodec.ToChunkData(codes, 16).Length);
        Assert.Equal(8, TextCodec.ToChunkData(codes, 4).Length);
        Assert.Equal(new byte[] { 0, 0, 0, 0xFE, 0, 0xFF, 0, 0 }, TextCodec.ToChunkData(codes, 0));
    }

    private static ushort[] Codes(params int[] values) => values.Select(v => (ushort)v).ToArray();
}

public class GameTextFileTests
{
    [Fact]
    public void Parse_PairsNamesWithData()
    {
        byte[] file = BuildTextFile(("FIRST", [0, 0xFE, 0, 0xFF]), ("SECOND", [0, 1, 0, 0xFE, 0, 0xFF, 0, 0]));

        GameTextFile parsed = GameTextFile.Parse(file);

        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(2, parsed.DeclaredCount);
        Assert.Equal(("SECOND", 1), (parsed.Entries[1].Name, parsed.Entries[1].Index));
    }

    [Fact]
    public void Build_WithoutChanges_IsIdentical_AndReplacesOnlyGivenTexts()
    {
        byte[] file = BuildTextFile(("FIRST", [0, 0xFE, 0, 0xFF]), ("SECOND", [0, 1, 0, 0xFE, 0, 0xFF, 0, 0]));
        GameTextFile parsed = GameTextFile.Parse(file);

        Assert.Equal(file, parsed.Build(new Dictionary<int, byte[]>()));

        byte[] changed = parsed.Build(new Dictionary<int, byte[]> { [0] = [0, 1, 0, 1, 0, 0xFE, 0, 0xFF] });
        GameTextFile reparsed = GameTextFile.Parse(changed);
        Assert.Equal(new byte[] { 0, 1, 0, 1, 0, 0xFE, 0, 0xFF }, reparsed.Entries[0].Data);
        Assert.Equal(parsed.Entries[1].Data, reparsed.Entries[1].Data);
    }

    [Fact]
    public void Build_Throws_ForUnalignedData()
    {
        GameTextFile parsed = GameTextFile.Parse(BuildTextFile(("ONE", [0, 0xFE, 0, 0xFF])));

        Assert.Throws<ArgumentException>(() => parsed.Build(new Dictionary<int, byte[]> { [0] = [0, 0xFF] }));
    }

    [Fact]
    public void Parse_Throws_ForOtherFileType()
    {
        byte[] other = IffWriter.BuildForm("UPWT", [IffWriter.BuildChunk("COMM", new byte[4])]);

        Assert.Throws<InvalidDataException>(() => GameTextFile.Parse(other));
    }

    internal static byte[] BuildTextFile(params (string Name, byte[] Data)[] texts)
    {
        var chunks = new List<byte[]> { IffWriter.BuildChunk("PAD ", new byte[4]) };
        byte[] size = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)texts.Length);
        chunks.Add(IffWriter.BuildChunk("SIZE", size));

        foreach ((string name, byte[] data) in texts)
        {
            byte[] nameBytes = new byte[(name.Length + 8) / 8 * 8];
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
            chunks.Add(IffWriter.BuildChunk("NAME", nameBytes));
            chunks.Add(IffWriter.BuildChunk("DATA", data));
        }

        return IffWriter.BuildForm(GameTextFile.FormType, chunks);
    }
}

public class TextCatalogTests
{
    [Theory]
    [InlineData("A_HG_2_M", "Hang Glider", "Class A", "Mission 2 description")]
    [InlineData("E_RP_1_N", "Rocket Belt", "Beginner class", "Mission 1 name")]
    [InlineData("P_BD_4_H", "Birdman", "Pilot class", "Mission 4 hint")]
    [InlineData("A_EX_3_N", "Cannonball", "Levels", "Level 3 name")]
    [InlineData("B_EX_1_M", "Sky Diving", "Levels", "Level 1 description")]
    [InlineData("P_EX_2_H", "Jumble Hopper", "Levels", "Level 2 hint")]
    [InlineData("GC_10_A", "Gyrocopter", "Tutorial", "Tutorial page 10")]
    [InlineData("HG_B3_S1", "Hang Glider", "Score sheets", "Class B mission 3, sheet 1")]
    [InlineData("GC_E_S2", "Gyrocopter", "Score sheets", "Beginner class, sheet 2")]
    [InlineData("SD_L123_S1", "Sky Diving", "Score sheets", "Score sheet 1, all levels")]
    [InlineData("A_S3_GOLD", "Badges", "Class A", "Gold badge requirement")]
    [InlineData("BONUS_S3_BLONDS", "Badges", "Bonus games", "Bronze badge requirement")]
    [InlineData("HANG", "Menus and screens", "Vehicle names", "Hang Glider")]
    public void Categorize_ByNamingScheme(string name, string area, string section, string description)
    {
        TextCategory category = TextCatalog.Categorize(name);

        Assert.Equal((area, section, description), (category.Area, category.Section, category.Description));
    }

    [Theory]
    [InlineData("FILE_ERASE", "Menus and screens", "File select")]
    [InlineData("BGM_V3", "Menus and screens", "Options")]
    [InlineData("STALL_WA", "Hang Glider", "In-flight messages")]
    [InlineData("C_PERFECT", "Cannonball", "In-flight messages")]
    [InlineData("R_PASS_N", "In flight", "Rings, targets and balloons")]
    [InlineData("FUEL_OUT", "In flight", "General messages")]
    public void Categorize_BySourceFile(string name, string area, string section)
    {
        TextCategory category = TextCatalog.Categorize(name);

        Assert.Equal((area, section), (category.Area, category.Section));
        Assert.NotEmpty(category.SourceFiles);
    }

    [Fact]
    public void Categorize_GivesThePatternOfTheDescription()
    {
        TextCategory mission = TextCatalog.Categorize("A_HG_2_H");
        TextCategory known = TextCatalog.Categorize("HANG");

        Assert.Equal("Mission {0} {1}", mission.DescriptionPattern);
        Assert.Equal(["2", "hint"], mission.DescriptionArgs);
        Assert.Equal(string.Empty, known.DescriptionPattern);
    }

    [Fact]
    public void Categorize_UnknownName_GoesToOther()
    {
        Assert.Equal("Other", TextCatalog.Categorize("SOMETHING_NEW").Area);
    }

    [Fact]
    public void SameSection_AlwaysHasSameSortOrder()
    {
        TextCategory a = TextCatalog.Categorize("FUEL_OUT");
        TextCategory b = TextCatalog.Categorize("CRASH");

        Assert.Equal(a.Section, b.Section);
        Assert.Equal(a.SectionOrder, b.SectionOrder);
    }
}

public class GameTextLibraryTests
{
    private static GameTextLibrary LoadRealLibrary(out byte[] originalFile)
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        originalFile = GameTextFile.FindFile(fs).Data;
        return GameTextLibrary.Create(TextFont.Load(fs), originalFile, fromProject: false);
    }

    [RealRomFact]
    public void RealRom_Has439Texts_WithKnownContent()
    {
        GameTextLibrary library = LoadRealLibrary(out _);

        Assert.Equal(439, library.Texts.Count);
        Assert.Equal(439, library.File.DeclaredCount);
        Assert.Equal("[b]Little States[/b]", library.Texts.Single(t => t.Name == "MINI_USA").Markup);
        Assert.StartsWith("[x=106][b]< Hovering >[/b] \nPress the [b]Z[/b] Button", library.Texts.Single(t => t.Name == "RP_1_A").Markup);
    }

    [RealRomFact]
    public void RealRom_EveryText_EncodesBackToIdenticalBytes()
    {
        // The strongest test of the codec: all 439 retail texts survive decode + encode exactly.
        GameTextLibrary library = LoadRealLibrary(out _);

        foreach (GameText text in library.Texts)
        {
            TextEncodeResult result = library.Codec.Encode(text.Markup);
            Assert.True(result.Success, $"{text.Name}: {(result.Errors.Count > 0 ? result.Errors[0].Message : string.Empty)}");
            Assert.Equal(text.Entry.Data, TextCodec.ToChunkData(result.Codes.ToArray(), text.Entry.Data.Length));
        }
    }

    [RealRomFact]
    public void RealRom_TextFileRebuildsIdentically()
    {
        GameTextLibrary library = LoadRealLibrary(out byte[] original);

        Assert.Equal(original, library.File.Build(new Dictionary<int, byte[]>()));
    }

    [RealRomFact]
    public void RealRom_AlmostAllTextsAreCategorized()
    {
        GameTextLibrary library = LoadRealLibrary(out _);

        Assert.True(library.Texts.Count(t => t.Category.Area == "Other") <= 5);
    }
}

public class SessionTextTests
{
    [RealRomFact]
    public void LoadTexts_UsesProjectCopyOfTextFile()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        var session = PW64Editor.Core.Workspace.EditorSession.Create(temp.Combine("hack"), "Texts", false, rom, TestRomLocator.RomPath!);

        Assert.False(session.LoadTexts().FromProject);

        GameFile textFile = GameTextFile.FindFile(session.CleanFileSystem);
        session.AddFileToProject(textFile.TableIndex);
        GameTextLibrary library = session.LoadTexts();

        Assert.True(library.FromProject);
        Assert.Equal(439, library.Texts.Count);
    }
}

public class TextFontCharacterListTests
{
    [Fact]
    public void GetCharacters_ListsEachStyleWithoutPlaceholders()
    {
        Assert.Equal("AB #", TextFontTests.SmallFont.GetCharacters(bold: false));
        Assert.Equal("AB }", TextFontTests.SmallFont.GetCharacters(bold: true));
    }

    [RealRomFact]
    public void RealFont_HasNoUmlauts_ButAllAsciiLettersAndDigits()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        string regular = TextFont.Load(fs).GetCharacters(bold: false);

        Assert.DoesNotContain("ä", regular);
        foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ")
        {
            Assert.Contains(c.ToString(), regular);
        }
    }
}

public class TextMarkupTests
{
    [Fact]
    public void DescribeCodes_CoversEveryCode()
    {
        IReadOnlyList<TextCodeInfo> codes = TextMarkup.DescribeCodes(TextFontTests.SmallFont);

        Assert.Equal(256, codes.Count);
        Assert.Equal("A", codes[0].WrittenAs);
        Assert.Equal("[b]B[/b]", codes[6].WrittenAs);
        Assert.Equal("(space)", codes[2].WrittenAs);
        Assert.Equal("[#07]", codes[7].WrittenAs); // bold space
        Assert.False(codes[4].Usable);              // empty font position
        Assert.False(codes[0x20].Usable);           // outside the font
        Assert.Equal("(line break)", codes[0xFE].WrittenAs);
    }

    [Fact]
    public void BoldSpace_RoundTripsAsRawCode()
    {
        var codec = new TextCodec(TextFontTests.SmallFont);
        ushort[] codes = [5, 7, 6, 0xFE, 0xFF]; // bold A, bold space, bold B

        string markup = codec.DecodeCodes(codes);

        Assert.Equal("[b]A[#07]B[/b]", markup);
        Assert.Equal(codes, codec.Encode(markup).Codes.ToArray());
    }

    [Fact]
    public void ClassA_IsSortedBeforeClassB()
    {
        Assert.True(TextCatalog.Categorize("A_HG_1_N").SectionOrder < TextCatalog.Categorize("B_HG_1_N").SectionOrder);
        Assert.True(TextCatalog.Categorize("A_S3_GOLD").SectionOrder < TextCatalog.Categorize("B_S3_GOLD").SectionOrder);
    }
}

public class TextValidatorTests
{
    [Theory]
    [InlineData("[x=254]A", true)]
    [InlineData("[x=10,y=255]A", true)]
    [InlineData("[x=253]A", false)]
    [InlineData("[x=256]A", false)]
    public void Validate_Position254Or255_IsAnError(string markup, bool error)
    {
        TextValidation result = TextValidator.Validate(new TextCodec(TextFontTests.SmallFont), markup, "A");

        Assert.Equal(error, result.Issues.Any(i => i.IsError && i.Message.Contains("cannot be used")));
    }

    private static readonly TextCodec Codec = new(TextFontTests.SmallFont);

    [Fact]
    public void Measure_CountsLinesPiecesAndLongestPiece()
    {
        TextLayoutInfo layout = TextValidator.Measure(Codec.Encode("[x=5]AB\nA[x=99]BBB").Codes);

        Assert.Equal(2, layout.Lines);
        Assert.Equal(3, layout.Pieces); // leading position does not start a new piece
        Assert.Equal(3, layout.LongestPiece);
    }

    [Fact]
    public void Validate_SameShape_HasNoIssues()
    {
        TextValidation result = TextValidator.Validate(Codec, "BA\nAB", "AB\nBA");

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_PieceLongerThan40_IsError()
    {
        TextValidation result = TextValidator.Validate(Codec, new string('A', 41), "A");

        Assert.True(result.HasErrors);
        Assert.Contains("41 characters", result.Issues.First(i => i.IsError).Message);
    }

    [Fact]
    public void Validate_PieceOf40_IsNoError()
    {
        TextValidation result = TextValidator.Validate(Codec, new string('A', 40), "A");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Wrap_DefaultLimit_BreaksAfter40Characters()
    {
        (string text, _) = TextWrapper.Wrap(new string('A', 41));

        Assert.Equal(new string('A', 40) + "\n" + "A", text);
    }

    [Fact]
    public void Validate_MoreLinesAndLongerLines_AreWarnings()
    {
        TextValidation result = TextValidator.Validate(Codec, "AAAA\nB\nB", "AA\nB");

        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Message.Contains("3 lines instead of 2"));
        Assert.Contains(result.Issues, i => i.Message.Contains("4 characters, the original 2"));
    }

    [Fact]
    public void Validate_TooManyPieces_IsError()
    {
        string markup = string.Join("\n", Enumerable.Repeat("A", 31));

        Assert.True(TextValidator.Validate(Codec, markup, markup).HasErrors);
    }

    [Fact]
    public void Validate_EncodingErrors_ReportLine()
    {
        TextValidation result = TextValidator.Validate(Codec, "A\nÄ", "A");

        Assert.True(result.HasErrors);
        Assert.StartsWith("Line 2:", result.Issues[0].Message);
    }

    [RealRomFact]
    public void RealRom_NoOriginalTextHasIssues()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), GameTextFile.FindFile(fs).Data, false);

        foreach (GameText text in library.Texts)
        {
            Assert.Empty(TextValidator.Validate(library.Codec, text.Markup, text.Markup).Issues);
        }
    }
}

public class SaveTextsTests
{
    [RealRomFact]
    public void SaveTexts_WritesProjectFile_AndBuildContainsNewText()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        var session = PW64Editor.Core.Workspace.EditorSession.Create(temp.Combine("hack"), "Texts", false, rom, TestRomLocator.RomPath!);
        GameTextLibrary library = session.LoadTexts();
        int index = library.Texts.Single(t => t.Name == "MINI_USA").Index;

        session.SaveTexts(library, new Dictionary<int, string> { [index] = "[b]Tiny States[/b]" });
        GameTextLibrary reloaded = session.LoadTexts();

        Assert.True(reloaded.FromProject);
        Assert.Equal("[b]Tiny States[/b]", reloaded.Texts[index].Markup);
        Assert.Equal(library.Texts[index].Entry.Data.Length, reloaded.Texts[index].Entry.Data.Length); // fits: same size
        Assert.Equal(library.Texts[index + 1].Entry.Data, reloaded.Texts[index + 1].Entry.Data);

        // Saving a second time overwrites the same project file.
        session.SaveTexts(reloaded, new Dictionary<int, string> { [index] = "[b]A much longer name for the states[/b]" });
        Assert.Single(session.Project.GetOverrides());
        Assert.Equal("[b]A much longer name for the states[/b]", session.LoadTexts().Texts[index].Markup);

        // The built ROM contains the text and is still valid.
        var (result, _, _) = session.Build(createRestorePoint: false);
        Assert.True(result.RomBuild.Rom.IsBootChecksumValid());
    }

    [RealRomFact]
    public void SaveTexts_Throws_ForInvalidText()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        var session = PW64Editor.Core.Workspace.EditorSession.Create(temp.Combine("hack"), "Texts", false, rom, TestRomLocator.RomPath!);
        GameTextLibrary library = session.LoadTexts();

        Assert.Throws<PW64Editor.Core.Project.ProjectException>(() =>
            session.SaveTexts(library, new Dictionary<int, string> { [0] = "Ä" }));
        Assert.Empty(session.Project.GetOverrides());
    }
}

public class TextWrapperTests
{
    [Fact]
    public void Wrap_ShortText_IsUnchanged()
    {
        (string text, IReadOnlyList<WrapOperation> ops) = TextWrapper.Wrap("Hello world", 44);

        Assert.Equal("Hello world", text);
        Assert.Empty(ops);
    }

    [Fact]
    public void Wrap_BreaksAtLastSpace()
    {
        (string text, IReadOnlyList<WrapOperation> ops) = TextWrapper.Wrap("aaa bbb ccc", 8);

        Assert.Equal("aaa bbb\nccc", text);
        Assert.Equal(new WrapOperation(7, true), Assert.Single(ops));
    }

    [Fact]
    public void Wrap_LongWordWithoutSpace_IsCut()
    {
        (string text, _) = TextWrapper.Wrap("abcdefghij", 4);

        Assert.Equal("abcd\nefgh\nij", text);
    }

    [Fact]
    public void Wrap_TagsTakeNoRoom_RawCodesCountOne()
    {
        Assert.Equal("[b]abcd[/b]", TextWrapper.Wrap("[b]abcd[/b]", 4).Text);
        Assert.Equal("ab[#5C]c\nd", TextWrapper.Wrap("ab[#5C]cd", 4).Text);
    }

    [Fact]
    public void Wrap_PositionStartsNewPiece()
    {
        Assert.Equal("abcd[x=100]efgh", TextWrapper.Wrap("abcd[x=100]efgh", 4).Text);
    }

    [Fact]
    public void Wrap_OperationsReproduceResult()
    {
        string original = "one two three four five six seven";
        (string expected, IReadOnlyList<WrapOperation> ops) = TextWrapper.Wrap(original, 10);

        string applied = original;
        foreach (WrapOperation op in ops)
        {
            applied = op.ReplacesSpace ? applied[..op.Index] + "\n" + applied[(op.Index + 1)..] : applied.Insert(op.Index, "\n");
        }

        Assert.Equal(expected, applied);
        Assert.All(expected.Split('\n'), line => Assert.True(line.Length <= 10));
    }

    [Fact]
    public void CountLines_CountsLineBreaks()
    {
        Assert.Equal(1, TextWrapper.CountLines("abc"));
        Assert.Equal(3, TextWrapper.CountLines("a\nb\n"));
    }
}

public class TextLimitsTests
{
    [Theory]
    [InlineData("HG_6_A", 7)]
    [InlineData("RP_15_A", 7)]
    [InlineData("A_HG_2_M", 7)]
    [InlineData("B_GC_1_H", 7)]
    [InlineData("P_BD_4_H", 7)]
    [InlineData("A_EX_1_M", 7)]
    [InlineData("HG_B3_S1", 10)]
    [InlineData("GC_E_S1", 10)]
    [InlineData("SD_L123_S1", 10)]
    [InlineData("HG_B3_S2", 7)]
    [InlineData("CB_L123_S2", 7)]
    [InlineData("BD_ALL_S1", 10)]
    [InlineData("BD_ALL_S2", 7)]
    [InlineData("BD_ALL_S3", 1)]
    [InlineData("E_BD_1_M", 7)]
    public void GetRoom_FollowsTextKind(string name, int lines)
    {
        Assert.Equal(lines, TextLimits.GetRoom(name));
    }

    [Theory]
    [InlineData("MINI_USA")]
    [InlineData("A_HG_2_N")] // mission names keep the original line count
    [InlineData("HG_B3_S3")]  // only Birdman has a third sheet text
    public void GetRoom_IsNull_ForOtherTexts(string name)
    {
        Assert.Null(TextLimits.GetRoom(name));
    }

    [Fact]
    public void GetMaxLines_NeverBelowOriginal()
    {
        Assert.Equal(2, TextLimits.GetMaxLines("MINI_USA", "a\nb"));
        Assert.Equal(7, TextLimits.GetMaxLines("HG_6_A", "a"));
    }

    [Fact]
    public void Validate_MoreLinesThanRoom_IsError()
    {
        var codec = new TextCodec(TextFontTests.SmallFont);

        Assert.True(TextValidator.Validate(codec, "A\nA\nA", "A", maxLines: 2).HasErrors);
        Assert.False(TextValidator.Validate(codec, "A\nA", "A", maxLines: 2).HasErrors);
    }

    [RealRomFact]
    public void RealRom_EveryOriginalTextFitsItsLimit()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), GameTextFile.FindFile(fs).Data, false);

        foreach (GameText text in library.Texts)
        {
            Assert.True(TextWrapper.CountLines(text.Markup) <= (TextLimits.GetRoom(text.Name) ?? int.MaxValue), text.Name);
        }
    }
}
