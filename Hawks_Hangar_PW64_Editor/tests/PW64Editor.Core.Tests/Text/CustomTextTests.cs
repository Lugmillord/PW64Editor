using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Text;

public class CustomTextRuleTests
{
    [Theory]
    [InlineData("MY_TEXT", null)]
    [InlineData("A1_2", null)]
    [InlineData("", "empty")]
    [InlineData("my_text", "capital")]
    [InlineData("MY-TEXT", "capital")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXY", "at most 24")]
    [InlineData("MINI_USA", "only once")]
    public void CheckName_FollowsTheRules(string name, string? problemPart)
    {
        string? problem = CustomTexts.CheckName(name, ["MINI_USA", "OK"]);

        if (problemPart is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.Contains(problemPart, problem);
        }
    }

    [Fact]
    public void NextFreeIndex_FillsGapsFirst_AndStopsAtCapacity()
    {
        Assert.Equal(439, CustomTexts.NextFreeIndex(Enumerable.Range(0, 439), 439));
        Assert.Equal(440, CustomTexts.NextFreeIndex([.. Enumerable.Range(0, 440), 441], 439));
        Assert.Null(CustomTexts.NextFreeIndex(Enumerable.Range(0, CustomTexts.Capacity), 439));
    }

    [Fact]
    public void CountProblem_NeedsTheFixForMoreThanTheOriginalTexts()
    {
        Assert.Null(CustomTexts.CountProblem(439, 439, expandedTablesApplied: false));
        Assert.Contains("crash", CustomTexts.CountProblem(440, 439, expandedTablesApplied: false));
        Assert.Null(CustomTexts.CountProblem(1024, 439, expandedTablesApplied: true));
        Assert.Contains("at most 1024", CustomTexts.CountProblem(1025, 439, expandedTablesApplied: true));
    }
}

public class TextNumberSlotTests
{
    private static readonly TextCodec Codec = new(TextFontTests.SmallFont);

    [Fact]
    public void Validate_SlotMustBeOnTheFirstLine()
    {
        // LEFT_SHT: one digit at position 14 (the 15th value).
        ushort[] longEnough = [.. Enumerable.Repeat((ushort)0, 15), TextCodec.CodeLineBreak, TextCodec.CodeEnd];
        ushort[] tooShort = [.. Enumerable.Repeat((ushort)0, 10), TextCodec.CodeLineBreak, TextCodec.CodeEnd];

        Assert.Empty(TextNumberSlots.Validate("LEFT_SHT", longEnough));
        Assert.Contains("character 15", Assert.Single(TextNumberSlots.Validate("LEFT_SHT", tooShort)).Message);
        Assert.Empty(TextNumberSlots.Validate("SOMETHING_ELSE", tooShort));
    }

    [Fact]
    public void WithExampleNumber_WritesBoldDigitsIntoTheSlot()
    {
        ushort[] codes = [.. Enumerable.Repeat((ushort)0x42, 16), TextCodec.CodeLineBreak, TextCodec.CodeEnd];

        IReadOnlyList<ushort> shown = TextNumberSlots.WithExampleNumber("STRACK", codes);

        Assert.Equal((ushort)0x61, shown[13]); // '1' bold
        Assert.Equal((ushort)0x62, shown[14]); // '2' bold
        Assert.Equal((ushort)0x42, shown[12]);
    }

    [Fact]
    public void Validator_ReportsABrokenSlot_OnlyWithTheName()
    {
        Assert.False(TextValidator.Validate(Codec, "AB", "AB").HasErrors);
        Assert.True(TextValidator.Validate(Codec, "AB", "AB", name: "STRACK").HasErrors);
    }

    [RealRomFact]
    public void RealRom_OriginalNumberTextsHaveValidSlots()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), GameTextFile.FindFile(fs).Data, false);

        foreach (string name in new[] { "LEFT_CNT", "STRACK", "LEFT_SHT", "C_POINTS" })
        {
            GameText text = library.Texts.Single(t => t.Name == name);
            Assert.Empty(TextNumberSlots.Validate(name, library.Codec.Encode(text.Markup).Codes));
        }
    }
}

public class CustomTextFileTests
{
    private static GameTextFile RealFile(out byte[] data, out TextCodec codec)
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        data = GameTextFile.FindFile(fs).Data;
        codec = new TextCodec(TextFont.Load(fs));
        return GameTextFile.Parse(data);
    }

    [RealRomFact]
    public void Build_WithUnchangedEntries_ReproducesTheFile()
    {
        GameTextFile file = RealFile(out byte[] data, out _);

        Assert.Equal(data, file.Build(file.ToEntries()));
    }

    [RealRomFact]
    public void Build_WithAddedAndFreeEntries_RoundTrips()
    {
        GameTextFile file = RealFile(out _, out TextCodec codec);
        List<TextFileEntry> texts = file.ToEntries();
        texts.Add(TextFileEntry.FreeSlot);
        texts.Add(new TextFileEntry("MY_TEXT", TextCodec.ToChunkData(codec.Encode("Hello").Codes, 0)));

        GameTextFile reread = GameTextFile.Parse(file.Build(texts));

        Assert.Equal(441, reread.Entries.Count);
        Assert.Equal(441, reread.DeclaredCount);
        Assert.True(reread.Entries[439].IsFree);
        Assert.Equal("MY_TEXT", reread.Entries[440].Name);
        Assert.Equal("Hello", codec.Decode(reread.Entries[440].Data));
        Assert.Equal(file.Entries[438].Data, reread.Entries[438].Data);
    }

    [RealRomFact]
    public void Library_MarksTextsBehindTheOriginalsAsCustom()
    {
        GameTextFile file = RealFile(out _, out TextCodec codec);
        List<TextFileEntry> texts = file.ToEntries();
        texts.Add(new TextFileEntry("A_HG_9_M", TextCodec.ToChunkData(codec.Encode("x").Codes, 0))); // looks like a mission text
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), file.Build(texts), true);

        GameText custom = library.Texts[439];
        Assert.True(custom.IsCustom);
        Assert.Equal(CustomTexts.AreaName, custom.Category.Area);
        Assert.False(library.Texts[438].IsCustom);
    }
}

public class SessionCustomTextTests
{
    private static EditorSession NewSession(TempDirectory temp, bool withFix = true)
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("p"), "Texts", false, rom, TestRomLocator.RomPath!);
        if (!withFix)
        {
            session.Project.Settings.AppliedCodeFixes.Remove(CodeFixes.ExpandedTexts.Id);
        }

        return session;
    }

    [RealRomFact]
    public void AddRemove_KeepsNumbersStable_AndDropsFreeSlotsAtTheEnd()
    {
        using var temp = new TempDirectory();
        EditorSession session = NewSession(temp);

        session.SaveTexts(session.LoadTexts(), new Dictionary<int, string>(),
            [new NewText(439, "FIRST", "One"), new NewText(440, "SECOND", "Two"), new NewText(441, "THIRD", "Three")], []);
        session.SaveTexts(session.LoadTexts(), new Dictionary<int, string>(), [], [440]);
        GameTextLibrary afterMiddle = session.LoadTexts();

        Assert.Equal(442, afterMiddle.Texts.Count);
        Assert.True(afterMiddle.Texts[440].IsFree);
        Assert.Equal("THIRD", afterMiddle.Texts[441].Name); // keeps its number
        Assert.Equal(440, CustomTexts.NextFreeIndex(afterMiddle.Texts.Where(t => !t.IsFree).Select(t => t.Index), afterMiddle.OriginalCount));

        session.SaveTexts(afterMiddle, new Dictionary<int, string>(), [], [441]);
        GameTextLibrary afterLast = session.LoadTexts();
        Assert.Equal(440, afterLast.Texts.Count); // trailing free slots dropped
        Assert.Equal("FIRST", afterLast.Texts[439].Name);
        Assert.True(afterLast.Texts[439].IsCustom);
    }

    [RealRomFact]
    public void Rules_AreEnforcedWhenSaving()
    {
        using var temp = new TempDirectory();
        EditorSession session = NewSession(temp);
        GameTextLibrary library = session.LoadTexts();
        var none = new Dictionary<int, string>();

        Assert.Contains("only once", Assert.Throws<ProjectException>(() =>
            session.SaveTexts(library, none, [new NewText(439, "MINI_USA", "x")], [])).Message);
        Assert.Contains("cannot be removed", Assert.Throws<ProjectException>(() =>
            session.SaveTexts(library, none, [], [12])).Message);
        Assert.Contains("after the 439", Assert.Throws<ProjectException>(() =>
            session.SaveTexts(library, none, [new NewText(100, "NEW", "x")], [])).Message);
    }

    [RealRomFact]
    public void WithoutTheFix_MoreTextsAreRefused_AlsoWhenBuilding()
    {
        using var temp = new TempDirectory();
        EditorSession withFix = NewSession(temp);
        withFix.SaveTexts(withFix.LoadTexts(), new Dictionary<int, string>(), [new NewText(439, "EXTRA", "x")], []);
        withFix.Build(createRestorePoint: false); // fine with the fix

        withFix.Project.Settings.AppliedCodeFixes.Remove(CodeFixes.ExpandedTexts.Id);

        var ex = Assert.Throws<ProjectException>(() => withFix.Build(createRestorePoint: false));
        Assert.Contains(CodeFixes.ExpandedTexts.Name, ex.Message);
        Assert.False(withFix.CanAddTexts);
    }
}
