using System.Text;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Tests.Text;

public class TextCsvTests
{
    // A font with the characters A, B, space, ';' and '"' (regular and bold).
    private static readonly TextCodec Codec = new(new TextFont("AB ;\"" + "AB ;\""));

    private static Dictionary<int, TextImportTarget> Targets(params (int Index, string Markup, int MaxLines)[] texts) =>
        texts.ToDictionary(t => t.Index, t => new TextImportTarget(t.Index, $"T{t.Index}", t.Markup, t.Markup, t.MaxLines));

    [Fact]
    public void Export_WritesIdAndTextWithEnterTags()
    {
        string csv = TextCsv.Export([(2, "B"), (0, "AB\nBA")]);

        Assert.Equal("ID;Text\r\n0;AB[enter]BA\r\n2;B\r\n", csv);
    }

    [Fact]
    public void Export_QuotesTextsWithSeparatorsOrQuotes()
    {
        string csv = TextCsv.Export([(1, "A;B \"x\"")]);

        Assert.Equal("ID;Text\r\n1;\"A;B \"\"x\"\"\"\r\n", csv);
    }

    [Fact]
    public void ExportThenImport_ChangesNothing()
    {
        Dictionary<int, TextImportTarget> targets = Targets((0, "AB\nBA", 2), (1, "A B", 1));
        string csv = TextCsv.Export(targets.Values.Select(t => (t.Index, t.CurrentMarkup)));

        TextImportResult result = TextCsv.Import(csv, Codec, targets);

        Assert.Empty(result.Changes);
        Assert.Empty(result.Rejected);
        Assert.Equal(2, result.Unchanged);
    }

    [Fact]
    public void Import_RestoresLineBreaks()
    {
        TextImportResult result = TextCsv.Import("ID;Text\n0;B[ENTER]A[enter]AB\n", Codec, Targets((0, "A", 3)));

        Assert.Equal("B\nA\nAB", result.Changes[0]);
    }

    [Fact]
    public void Import_RejectsBadRowsAndKeepsTheirTexts()
    {
        string csv = string.Join("\n",
            "ID;Text",
            "0;BB",                 // fine
            "x;A",                  // not a number
            "7;A",                  // no such text
            "0;A",                  // second row for 0
            "1;A[enter]A[enter]A",  // too many lines
            "2;AXA",                // character not in the font
            "3",                    // no separator
            "");

        TextImportResult result = TextCsv.Import(csv, Codec, Targets((0, "A", 1), (1, "A", 2), (2, "A", 1), (3, "A", 1)));

        Assert.Equal(new Dictionary<int, string> { [0] = "BB" }, result.Changes);
        Assert.Equal(["x", "7", "0", "1", "2", "3"], result.Rejected.Select(r => r.Id));
        Assert.Contains("not a number", result.Rejected[0].Reason);
        Assert.Contains("no text", result.Rejected[1].Reason);
        Assert.Contains("more than once", result.Rejected[2].Reason);
        Assert.Contains("Too long: 3 lines", result.Rejected[3].Reason);
        Assert.Contains("'X' is not in the game's font", result.Rejected[4].Reason);
        Assert.Contains("No ';'", result.Rejected[5].Reason);
    }

    [Fact]
    public void Import_BreaksLongLinesLikeTheEditor()
    {
        string longLine = string.Join(' ', Enumerable.Repeat("ABAB", 12)); // 59 characters

        TextImportResult tooLong = TextCsv.Import($"0;{longLine}", Codec, Targets((0, "A", 1)));
        TextImportResult fits = TextCsv.Import($"0;{longLine}", Codec, Targets((0, "A", 2)));

        Assert.Contains("Too long: 2 lines", tooLong.Rejected.Single().Reason);
        Assert.Equal(2, TextWrapper.CountLines(fits.Changes[0]));
    }

    [Fact]
    public void Import_ReadsQuotedFieldsAndUnquotedSeparators()
    {
        string csv = "0;\"A;B\"\n1;A;B\n2;\"A\nB\"\n";

        TextImportResult result = TextCsv.Import(csv, Codec, Targets((0, "", 1), (1, "", 1), (2, "", 2)));

        Assert.Empty(result.Rejected);
        Assert.Equal("A;B", result.Changes[0]);
        Assert.Equal("A;B", result.Changes[1]);
        Assert.Equal("A\nB", result.Changes[2]);
    }

    [Fact]
    public void Decode_ReadsUtf8AndLatin1()
    {
        byte[] utf8 = TextCsv.ToFileBytes("0;Ä");
        byte[] latin1 = Encoding.Latin1.GetBytes("0;Ä");

        Assert.Equal("0;Ä", TextCsv.Decode(utf8));
        Assert.Equal("0;Ä", TextCsv.Decode(latin1));
    }

    [RealRomFact]
    public void ExportThenImport_OfAllGameTexts_ChangesNothing()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        GameTextLibrary library = GameTextLibrary.Create(TextFont.Load(fs), GameTextFile.FindFile(fs).Data, false);
        Dictionary<int, TextImportTarget> targets = library.Texts.ToDictionary(
            t => t.Index, t => new TextImportTarget(t.Index, t.Name, t.Markup, t.Markup, TextLimits.GetMaxLines(t.Name, t.Markup)));

        string csv = TextCsv.Decode(TextCsv.ToFileBytes(TextCsv.Export(library.Texts.Select(t => (t.Index, t.Markup)))));
        TextImportResult result = TextCsv.Import(csv, library.Codec, targets);

        Assert.Empty(result.Changes);
        Assert.Empty(result.Rejected);
        Assert.Equal(library.Texts.Count, result.Unchanged);
    }
}
