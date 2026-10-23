using System.Buffers.Binary;
using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Code;

public class GameModuleTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    [Fact]
    public void Image_HasHeaderAndFitsIntoTheCodeArea()
    {
        byte[] image = GameModuleImage.Image;
        Assert.Equal(GameModule.Magic, Word(image, 0));
        Assert.InRange(Word(image, 4), CodeArea.Start, CodeArea.End);   // printf
        Assert.InRange(Word(image, 8), CodeArea.Start, CodeArea.End);   // init
        Assert.True(GameModuleImage.EndAddress <= CodeArea.End);
        Assert.True(image.Length <= GameModule.RomReserve);
        Assert.Equal(CodeArea.Start, GameModuleImage.LoadAddress);
        Assert.All(GameModuleImage.StartupSites, s => Assert.InRange(s.HookAddress, CodeArea.Start, GameModuleImage.EndAddress));
    }

    [Fact]
    public void CreateImage_FillsInTheHeader()
    {
        byte[] image = GameModule.CreateImage(new GameModuleOptions(true, StartupInfo: true, GameMessages: true), 0x7F0000,
            "project \"Ä Test\" 1.0");
        Assert.Equal(7u, Word(image, 0x0C)); // startup, developers' messages, debug output
        Assert.Equal(0x7F0000u, Word(image, 0x10));
        Assert.Equal((uint)GameModule.AlignedSize(image.Length), Word(image, 0x14));
        Assert.Equal("project \" Test\" 1.0", System.Text.Encoding.ASCII.GetString(image, 0x30, 96).TrimEnd('\0')); // only ASCII

        byte[] quiet = GameModule.CreateImage(new GameModuleOptions(true), 0x7F0000, string.Empty);
        Assert.Equal(4u, Word(quiet, 0x0C)); // debug output only
    }

    [Fact]
    public void Loader_IsEncodedAsPlanned()
    {
        IReadOnlyList<WordPatch> patches = CodeArea.Patches(0x7F0000, 0x2710);
        WordPatch loader = patches.Single(p => p.RomOffset == CodeArea.LoaderRomOffset);
        Assert.Equal(29, loader.Replacement.Length);
        Assert.Equal(0x3C05007Fu, loader.Replacement[5]);  // lui a1, 0x007F
        Assert.Equal(0x34A50000u, loader.Replacement[6]);  // ori a1, a1, 0
        Assert.Equal(0x0C08A9D8u, loader.Replacement[8]);  // jal _uvMediaCopy
        Assert.Equal(0x34C62710u, loader.Replacement[9]);  // ori a2, a2, 0x2710
        Assert.Equal(0x080C3F88u, loader.Replacement[16]); // j app_entrypoint
        Assert.Equal(0x0C08A949u, patches.Single(p => p.RomOffset == CodeArea.AppEntryCallRomOffset).Replacement[0]);
        Assert.Equal(0x3C0A8039u, patches.Single(p => p.RomOffset == 0x2B394).Replacement[0]); // block end 0x803905E0
        Assert.Throws<ArgumentException>(() => CodeArea.Patches(0x7F0000, 0x10001));
    }

    [RealRomFact]
    public void StartupSites_MatchTheGame()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.All(GameModuleImage.StartupSites, s => Assert.Equal(s.OriginalWord, Word(rom.Data, s.RomOffset)));
        foreach (WordPatch patch in GameModule.Patches(new GameModuleOptions(true, true, true), 0x7F0000, 0x2710))
        {
            patch.Check(rom.Data);
        }
    }

    [RealRomFact]
    public void Build_WithDebugOutput_PlacesTheModuleAndChangesOnlyItsCode()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);

        // With and without the code fixes (the code cave is shared with the photo album fix).
        foreach (IReadOnlyList<CodeFix> fixes in new[] { (IReadOnlyList<CodeFix>)[], CodeFixes.All })
        {
            RomBuildResult plain = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa, new RomBuildOptions(CodeFixes: fixes));
            RomBuildResult debug = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa, new RomBuildOptions(CodeFixes: fixes)
            {
                GameModule = new GameModuleOptions(true, StartupInfo: true),
                GameModuleInfo = "test",
            });

            int moduleOffset = rom.Size - GameModule.RomReserve;
            Assert.Equal(GameModule.Magic, Word(debug.Rom.Data, moduleOffset));
            Assert.Equal(rom.Size, debug.Rom.Size);
            Assert.Equal(plain.FreeSpace - GameModule.TotalRomReserve, debug.FreeSpace);
            Assert.Equal(DataArea.Magic, Word(debug.Rom.Data, moduleOffset - DataArea.RomReserve));

            // Inside the game code, only the planned words differ.
            var expected = new HashSet<int>(GameModule.Patches(new GameModuleOptions(true, true), moduleOffset, GameModuleImage.Image.Length)
                .SelectMany(p => Enumerable.Range(0, p.Replacement.Length).Select(i => p.RomOffset + (i * 4))));
            int[] changed = Enumerable.Range(0x1000 / 4, (RomLayout.PilotwingsUsa.FileTableOffset - 0x1000) / 4)
                .Select(w => w * 4)
                .Where(o => Word(plain.Rom.Data, o) != Word(debug.Rom.Data, o))
                .ToArray();
            Assert.All(changed, o => Assert.Contains(o, expected));
            Assert.Contains(CodeArea.AppEntryCallRomOffset, changed);
            Assert.Contains(GameModuleImage.StartupSites[0].RomOffset, changed);
        }
    }

    [RealRomFact]
    public void Project_AlwaysBuildsTheModuleWithTheExpansionPakCheck()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Features", false, rom, TestRomLocator.RomPath!);

        // Every hack ROM: 16 MiB, the module at its end with the Expansion Pak check and the goal patch, no debug hooks.
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(ProjectBuilder.HackRomSize, built.Length);
        int moduleOffset = built.Length - GameModule.RomReserve;
        Assert.Equal(GameModule.Magic, Word(built, moduleOffset));
        Assert.Equal(96u, Word(built, moduleOffset + 0x0C));
        Assert.Equal(GameModuleImage.StartupSites[0].OriginalWord, Word(built, GameModuleImage.StartupSites[0].RomOffset));
        Assert.Equal(CodeArea.Jal(CodeArea.LoaderAddress), Word(built, CodeArea.AppEntryCallRomOffset));

        session.SaveFeatures(new GameFeatureSettings { StartupInfo = true }); // without DebugOutput: no debug flags
        Assert.Equal(96u, Word(session.Build(false).Result.RomBuild.Rom.Data, moduleOffset + 0x0C));

        session.SaveFeatures(new GameFeatureSettings { DebugOutput = true, GameMessages = true });
        EditorSession reopened = EditorSession.Open(temp.Combine("hack"), rom, TestRomLocator.RomPath!);
        Assert.True(reopened.Project.Settings.Features.DebugOutput);
        built = reopened.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(GameModule.Magic, Word(built, moduleOffset));
        Assert.Equal(110u, Word(built, moduleOffset + 0x0C)); // goal patch, Pak check, developers' messages, debug output, on screen (default)
        Assert.Equal(GameModuleImage.StartupSites[0].OriginalWord, Word(built, GameModuleImage.StartupSites[0].RomOffset));
    }

    [RealRomFact]
    public void DataArea_ReservesItsMemoryWithAndWithoutExpandedTexts()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        foreach (IReadOnlyList<CodeFix> fixes in new[] { (IReadOnlyList<CodeFix>)[], CodeFixes.All })
        {
            byte[] built = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa, new RomBuildOptions(CodeFixes: fixes)
            {
                GameModule = new GameModuleOptions(false) { RequireExpansionPak = true },
            }).Rom.Data;

            // Program block from 0x801F1000 (lui 0x801F + addiu 0x1000: the lower data area), block 5 from 0, clearing up to
            // 0x801F1000.
            Assert.Equal(0x3C09801Fu, Word(built, 0x2B3AC));
            Assert.Equal(0x25291000u, Word(built, 0x2B3B4));
            Assert.Equal(0xAC400028u, Word(built, 0x2B3E0));
            Assert.Equal(0x3C03801Fu, Word(built, 0x2B44C));
            Assert.Equal(0x24631000u, Word(built, 0x2B450));

            // The data area image (header only) in front of the module, and its place in the module header.
            int module = built.Length - GameModule.RomReserve;
            int data = module - DataArea.RomReserve;
            Assert.Equal(DataArea.Magic, Word(built, data));
            Assert.Equal(0u, Word(built, data + 8));
            Assert.Equal((uint)data, Word(built, module + 0xE8));
            Assert.Equal((uint)DataArea.HeaderSize, Word(built, module + 0xEC));
            Assert.Equal(32u, Word(built, module + 0x0C));
        }
    }

    [RealRomFact]
    public void MinimumRomSize_GrowsTheRomAndKeepsThePatchSmall()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        RomBuildResult result = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa, new RomBuildOptions()
        {
            MinimumRomSize = ProjectBuilder.HackRomSize,
        });

        Assert.Equal(ProjectBuilder.HackRomSize, result.Rom.Size);
        Assert.True(result.Expanded);
        Assert.True(result.Rom.Data.AsSpan(0, rom.Size).SequenceEqual(rom.Data)); // nothing else changes
        Assert.All(result.Rom.Data.AsSpan(rom.Size).ToArray(), b => Assert.Equal(0xFF, b));
        byte[] patch = PW64Editor.Core.Patching.BpsWriter.Create(rom.Data, result.Rom.Data, string.Empty);
        Assert.True(patch.Length < 1024, $"patch has {patch.Length} bytes");
    }
}
