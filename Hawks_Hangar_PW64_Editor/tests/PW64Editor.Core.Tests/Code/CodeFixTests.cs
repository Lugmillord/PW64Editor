using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Code;

public class CodeFixTests
{
    private static readonly CodeFix TestFix = new("test", "Test fix", "Problem.", "Solution.",
    [
        CodePatch.FromWords("first", 0x100, [0x11111111, 0x22222222], [0x11111111, 0x33333333]),
        CodePatch.FromWords("second", 0x200, [0x44444444, 0x55555555], [0x66666666, 0x55555555]),
    ]);

    private static byte[] RomWithOriginalCode()
    {
        byte[] rom = new byte[0x1000];
        TestFix.Patches[0].Original.CopyTo(rom, 0x100);
        TestFix.Patches[1].Original.CopyTo(rom, 0x200);
        return rom;
    }

    [Fact]
    public void Apply_ReplacesTheCode_AndStateFollows()
    {
        byte[] rom = RomWithOriginalCode();
        Assert.Equal(CodeFixState.NotApplied, TestFix.GetState(rom, 0, rom.Length));

        TestFix.Apply(rom, 0, rom.Length);

        Assert.Equal(CodeFixState.Applied, TestFix.GetState(rom, 0, rom.Length));
        Assert.Equal(TestFix.Patches[0].Replacement, rom[0x100..0x108]);
        Assert.Equal(TestFix.Patches[1].Replacement, rom[0x200..0x208]);
    }

    [Fact]
    public void Apply_Twice_ChangesNothingMore()
    {
        byte[] rom = RomWithOriginalCode();
        TestFix.Apply(rom, 0, rom.Length);
        byte[] once = rom.ToArray();

        TestFix.Apply(rom, 0, rom.Length);

        Assert.Equal(once, rom);
    }

    [Fact]
    public void Apply_FindsCodeThatWasMoved()
    {
        byte[] rom = new byte[0x1000];
        TestFix.Patches[0].Original.CopyTo(rom, 0x500); // not at 0x100 any more
        TestFix.Patches[1].Original.CopyTo(rom, 0x200);

        TestFix.Apply(rom, 0, rom.Length);

        Assert.Equal(TestFix.Patches[0].Replacement, rom[0x500..0x508]);
    }

    [Fact]
    public void Apply_UnknownCode_ThrowsAndChangesNothing()
    {
        byte[] rom = RomWithOriginalCode();
        rom[0x204] = 0x99; // the second piece is not recognizable any more
        byte[] before = rom.ToArray();

        var ex = Assert.Throws<InvalidDataException>(() => TestFix.Apply(rom, 0, rom.Length));

        Assert.Contains("second", ex.Message);
        Assert.Equal(before, rom); // the first piece was not patched either
        Assert.Equal(CodeFixState.Unrecognized, TestFix.GetState(rom, 0, rom.Length));
    }

    [Fact]
    public void Apply_CodeFoundTwiceElsewhere_IsNotGuessed()
    {
        byte[] rom = new byte[0x1000];
        TestFix.Patches[0].Original.CopyTo(rom, 0x400);
        TestFix.Patches[0].Original.CopyTo(rom, 0x600);
        TestFix.Patches[1].Original.CopyTo(rom, 0x200);

        Assert.Throws<InvalidDataException>(() => TestFix.Apply(rom, 0, rom.Length));
    }

    [Fact]
    public void GetState_SomePiecesReplaced_IsPartlyApplied()
    {
        byte[] rom = RomWithOriginalCode();
        TestFix.Patches[0].Replacement.CopyTo(rom, 0x100);

        Assert.Equal(CodeFixState.PartlyApplied, TestFix.GetState(rom, 0, rom.Length));
    }

    [Fact]
    public void Catalog_IdsAreUnique_AndPatchesHaveEqualLengths()
    {
        Assert.Equal(CodeFixes.All.Count, CodeFixes.All.Select(f => f.Id).Distinct().Count());
        Assert.All(CodeFixes.All.SelectMany(f => f.Patches), p => Assert.Equal(p.Original.Length, p.Replacement.Length));
        Assert.Same(CodeFixes.SafeText, CodeFixes.Find("safe-text-v1"));
        Assert.Null(CodeFixes.Find("does-not-exist"));
    }

    [RealRomFact]
    public void SafeText_OnRealRom_ChangesExactlyFourInstructions()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        int codeEnd = CodeFixes.CodeEnd(RomLayout.PilotwingsUsa);
        Assert.Equal(CodeFixState.NotApplied, CodeFixes.SafeText.GetState(rom.Data, CodeFixes.CodeStart, codeEnd));

        byte[] patched = rom.Data.ToArray();
        CodeFixes.SafeText.Apply(patched, CodeFixes.CodeStart, codeEnd);

        Assert.Equal(CodeFixState.Applied, CodeFixes.SafeText.GetState(patched, CodeFixes.CodeStart, codeEnd));
        int[] changedWords = Enumerable.Range(0, patched.Length / 4)
            .Where(w => !patched.AsSpan(w * 4, 4).SequenceEqual(rom.Data.AsSpan(w * 4, 4)))
            .Select(w => w * 4)
            .ToArray();
        Assert.Equal([0x1A904, 0x1A918, 0x1AA14, 0xC95B0], changedWords);
    }

    [RealRomFact]
    public void RomBuilder_WithFix_DiffersOnlyInTheFixAndTheChecksum()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);

        RomBuildResult result = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa,
            new RomBuildOptions(CodeFixes: [CodeFixes.SafeText]));

        int[] changedWords = Enumerable.Range(0, rom.Size / 4)
            .Where(w => !result.Rom.Data.AsSpan(w * 4, 4).SequenceEqual(rom.Data.AsSpan(w * 4, 4)))
            .Select(w => w * 4)
            .ToArray();
        Assert.Equal([0x10, 0x14, 0x1A904, 0x1A918, 0x1AA14, 0xC95B0], changedWords); // 0x10/0x14 = CRC1/CRC2
        Assert.True(result.BootChecksumUpdated);
    }
}

public class ProjectCodeFixTests
{
    [Fact]
    public void NewProject_GetsAllFixes()
    {
        using var temp = new TempDirectory();

        HackProject project = HackProject.Create(temp.Combine("p"), "Fixed", "00", null);

        Assert.Equal(CodeFixes.AllIds, project.Settings.AppliedCodeFixes);
        Assert.Empty(project.MissingCodeFixes);
        Assert.Equal(CodeFixes.All, project.BuildOptions.CodeFixes);
    }

    [Fact]
    public void OldProject_WithoutFixes_IsOfferedThem()
    {
        using var temp = new TempDirectory();
        HackProject.Create(temp.Combine("p"), "Old", "00", null);
        string file = temp.Combine("p", HackProject.ProjectFileName);
        // A project from before code fixes existed has no "AppliedCodeFixes" entry at all.
        string json = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), @",\s*""AppliedCodeFixes"":\s*\[[^\]]*\]", string.Empty);
        Assert.DoesNotContain("AppliedCodeFixes", json);
        File.WriteAllText(file, json);

        HackProject project = HackProject.Load(temp.Combine("p"));

        Assert.Contains(CodeFixes.SafeText, project.MissingCodeFixes);
        project.AddCodeFix(CodeFixes.SafeText);
        project.AddCodeFix(CodeFixes.SafeText);
        Assert.Single(project.Settings.AppliedCodeFixes);
    }

    [Fact]
    public void Project_WithUnknownFix_IsRefused()
    {
        using var temp = new TempDirectory();
        HackProject.Create(temp.Combine("p"), "Future", "00", null);
        string file = temp.Combine("p", HackProject.ProjectFileName);
        File.WriteAllText(file, File.ReadAllText(file).Replace("safe-text-v1", "from-the-future"));

        var ex = Assert.Throws<ProjectException>(() => HackProject.Load(temp.Combine("p")));
        Assert.Contains("from-the-future", ex.Message);
    }

    [RealRomFact]
    public void ProjectBuild_AppliesTheProjectsFixes()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Fixed", rom.ComputeSha1(), null);

        ProjectBuildResult result = ProjectBuilder.Build(project, rom, RomLayout.PilotwingsUsa);

        Assert.Equal(CodeFixState.Applied, CodeFixes.SafeText.GetState(result.RomBuild.Rom.Data,
            CodeFixes.CodeStart, CodeFixes.CodeEnd(RomLayout.PilotwingsUsa)));
    }

    [RealRomFact]
    public void Session_ApplyCodeFixes_BuildsAndSaves()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        var session = PW64Editor.Core.Workspace.EditorSession.Create(temp.Combine("p"), "Session", false, rom, TestRomLocator.RomPath!);
        session.Project.Settings.AppliedCodeFixes.Clear();
        session.Project.Save();

        (_, string romPath) = session.ApplyCodeFixes(session.Project.MissingCodeFixes);

        Assert.Empty(HackProject.Load(temp.Combine("p")).MissingCodeFixes); // saved
        Assert.Equal(CodeFixState.Applied, CodeFixes.SafeText.GetState(File.ReadAllBytes(romPath),
            CodeFixes.CodeStart, CodeFixes.CodeEnd(RomLayout.PilotwingsUsa)));
    }
}

public class ExpandedTextFixTests
{
    [RealRomFact]
    public void ExpandedTexts_OnRealRom_ChangesOnlyTheListedPlaces()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        int codeEnd = CodeFixes.CodeEnd(RomLayout.PilotwingsUsa);
        Assert.Equal(CodeFixState.NotApplied, CodeFixes.ExpandedTexts.GetState(rom.Data, CodeFixes.CodeStart, codeEnd));

        byte[] patched = rom.Data.ToArray();
        CodeFixes.ExpandedTexts.Apply(patched, CodeFixes.CodeStart, codeEnd);

        Assert.Equal(CodeFixState.Applied, CodeFixes.ExpandedTexts.GetState(patched, CodeFixes.CodeStart, codeEnd));
        int[] changedWords = Enumerable.Range(0, patched.Length / 4)
            .Where(w => !patched.AsSpan(w * 4, 4).SequenceEqual(rom.Data.AsSpan(w * 4, 4)))
            .Select(w => w * 4)
            .ToArray();
        int[] expected =
        [
            0x1AB6C, 0x1AB78, 0x1AB7C,                    // ASCII line limit
            0x2B3B4, 0x2B3E0, 0x2B450,                    // reserved memory for the tables
            0xA4E94, 0xA4F6C,                             // flight message copy
            0xC94B8, 0xC94BC, 0xC9504, 0xC9514,           // tables in textLoadBlock
            0xC965C, 0xC9660, 0xC9678, 0xC9680,           // tables in textGetDataByName
            0xC96D8, 0xC96EC, 0xC96F0, 0xC96F8,           // textGetDataByIdx
            0xC9934, 0xC9938, 0xC993C, 0xC9940, 0xC9944, 0xC9948, 0xC994C, 0xC9950, 0xC9954, 0xC9958, // textFmtIntAt
            0xDD190,                                      // empty text
        ];
        Assert.Equal(expected, changedWords);
    }

    [RealRomFact]
    public void BothTextFixes_ApplyTogether()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        byte[] patched = rom.Data.ToArray();
        int codeEnd = CodeFixes.CodeEnd(RomLayout.PilotwingsUsa);

        foreach (CodeFix fix in CodeFixes.All)
        {
            fix.Apply(patched, CodeFixes.CodeStart, codeEnd);
        }

        Assert.All(CodeFixes.All, f => Assert.Equal(CodeFixState.Applied, f.GetState(patched, CodeFixes.CodeStart, codeEnd)));
    }
}
