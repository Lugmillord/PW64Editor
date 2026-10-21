using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Code;

/// <summary>The game patch "Bonus games and Birdman" (<see cref="GamePatches.BonusGames"/>).</summary>
public class BonusGamesTests
{
    private const uint AppRamDelta = 0x80278AD0; // RAM address - ROM offset of the game code and its data

    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static string StringAt(byte[] rom, uint address)
    {
        int offset = (int)(address - AppRamDelta);
        int end = Array.IndexOf(rom, (byte)0, offset);
        return Encoding.ASCII.GetString(rom, offset, end - offset);
    }

    /// <summary>The data area image of a built ROM (the module header names it).</summary>
    private static byte[] DataImage(byte[] built)
    {
        GameModuleHeader header = GameModule.ReadHeader(built)!;
        return built.AsSpan(header.DataRomOffset, header.DataSize).ToArray();
    }

    /// <summary>A name in the tables: a pointer into the game's data, or into the tables (the new names).</summary>
    private static string Name(byte[] rom, byte[] tables, uint pointer) =>
        pointer >= ResultTables.Address && pointer < ResultTables.Address + tables.Length
            ? Encoding.ASCII.GetString(tables, (int)(pointer - ResultTables.Address), Array.IndexOf(tables, (byte)0, (int)(pointer - ResultTables.Address)) - (int)(pointer - ResultTables.Address))
            : StringAt(rom, pointer);

    [RealRomFact]
    public void Patch_FitsTheCleanRom_AndTheBirdmanNamesAreWhereExpected()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.Equal(CodeFixState.NotApplied,
            GamePatches.BonusGames.AsCodeFix.GetState(rom.Data, CodeFixes.CodeStart, CodeFixes.CodeEnd(RomLayout.PilotwingsUsa)));
        Assert.Equal("BD_ALL_S1", StringAt(rom.Data, ResultTables.BirdmanMissionScreenName));
        Assert.Equal("BD_ALL_S2", StringAt(rom.Data, ResultTables.BirdmanResultSheetName));
        Assert.Equal(new MedalLimits(0, 0, 0), new MissionTables(rom.Data, RomLayout.PilotwingsUsa.Missions!).MedalLimits()[7]);

        // The new tables end in front of the text tables of ExpandedTexts.
        Assert.Equal(0x801FD100u, ResultTables.Address);
        Assert.True(ResultTables.Address + ResultTables.Size <= DataArea.End);
    }

    [RealRomFact]
    public void Tables_KeepWhatTheGameRead_AndGiveTheLevelsTheirOwnTexts()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionLayout layout = RomLayout.PilotwingsUsa.Missions!;
        var clean = new MissionTables(rom.Data, layout);
        MissionSettings own = MissionSettings.For(new TestId(1, Vehicle.HangGlider, 1));
        own.ResultRows = [(int)PointSource.Time, (int)PointSource.Rings];
        byte[] tables = ResultTables.Create(rom.Data, layout, [own]);
        Assert.Equal(0, tables.Length % 8);

        for (int v = 0; v < ResultTables.VehicleCount; v++)
        {
            for (int c = 0; c < ResultTables.Classes; c++)
            {
                for (int t = 0; t < ResultTables.Tests; t++)
                {
                    var id = new TestId(c, (Vehicle)v, t);
                    int entry = (((v * 4) + c) * 5) + t;
                    uint screen = Word(tables, entry * 4);
                    uint sheet = Word(tables, 0x230 + (entry * 4));
                    int rows = ResultTables.RowsOffset(id)!.Value;
                    if (id.Vehicle == Vehicle.Birdman)
                    {
                        Assert.Equal(ResultTables.BirdmanMissionScreenName, screen);
                        Assert.Equal(ResultTables.BirdmanResultSheetName, sheet);
                        continue;
                    }

                    string? screenName = ResultTables.LevelTextName(id, false) ?? (t < 3 ? clean.MissionScreenTextName(id) : null);
                    string? sheetName = ResultTables.LevelTextName(id, true) ?? (t < 3 ? clean.ResultSheetTextName(id) : null);
                    if (screenName is not null)
                    {
                        Assert.Equal(screenName, Name(rom.Data, tables, screen));
                    }

                    if (sheetName is not null)
                    {
                        Assert.Equal(sheetName, Name(rom.Data, tables, sheet));
                    }

                    if (t < 3 && id != own.Id)
                    {
                        Assert.Equal(clean.ResultRows(id), tables.AsSpan(rows, 4).ToArray().Select(b => (int)b));
                    }
                }
            }
        }

        // The project's rows, bottom row first.
        Assert.Equal([(int)PointSource.Rings, (int)PointSource.Time, 0, 0],
            tables.AsSpan(ResultTables.RowsOffset(own.Id)!.Value, 4).ToArray().Select(b => (int)b));
        Assert.Equal("SD_L2_S1", Name(rom.Data, tables, Word(tables, ((((4 * 4) + 1) * 5) + 0) * 4)));
        Assert.Equal("HP_L3_S2", Name(rom.Data, tables, Word(tables, 0x230 + (((((5 * 4) + 2) * 5) + 0) * 4))));
        Assert.Equal("HP_L1_S1", Name(rom.Data, tables, Word(tables, ((((5 * 4) + 0) * 5) + 0) * 4))); // already per level
        Assert.Equal("CB_L123_S2", Name(rom.Data, tables, Word(tables, 0x230 + (((((3 * 4) + 1) * 5) + 2) * 4))));
    }

    [RealRomFact]
    public void NewProject_HasTheLevelTexts_AndTheBuildUsesTheTables()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        Assert.True(session.Project.HasPatch(GamePatches.BonusGames));

        // The nine texts, copies of the shared ones.
        GameTextLibrary texts = session.LoadTexts();
        foreach ((string name, string source) in ResultTables.NewTexts)
        {
            GameText text = texts.Texts.Single(t => t.Name == name);
            Assert.True(text.Index >= session.OriginalTextCount);
            Assert.Equal(texts.Texts.Single(t => t.Name == source).Markup, text.Markup);
            Assert.True(text.IsCustom && text.IsPatchText);
            Assert.Equal(texts.Texts.Single(t => t.Name == source).Category.Area, text.Category.Area); // sorted with the shared text
            Assert.Equal("Score sheets", text.Category.Section);
        }

        Assert.Empty(session.Project.GetOverrides()); // only in memory until a text is saved

        // The missions show the texts of their level.
        IReadOnlyList<MissionInfo> missions = session.LoadMissions().Missions;
        MissionInfo sky2 = missions.Single(m => m.Id == new TestId(1, Vehicle.SkyDiving, 0));
        Assert.Equal("SD_L2_S1", sky2.MissionScreenTextName);
        Assert.Equal("SD_L2_S2", sky2.ResultSheetTextName);
        MissionInfo hopper3 = missions.Single(m => m.Id == new TestId(2, Vehicle.JumbleHopper, 0));
        Assert.Equal("HP_L3_S1", hopper3.MissionScreenTextName);
        Assert.Equal("HP_L3_S2", hopper3.ResultSheetTextName);
        Assert.Empty(sky2.SharesResultSheetTextWith);
        Assert.Equal(new MedalLimits(280, 320, 360), session.LoadMedalLimits()[7]);

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        byte[] image = DataImage(built);
        byte[]? tables = DataArea.ReadResultTables(image);
        Assert.NotNull(tables);
        Assert.Equal(ResultTables.Create(rom.Data, RomLayout.PilotwingsUsa.Missions!, session.Project.Settings.Missions), tables);
        Assert.Empty(DataArea.ReadImage(image)); // no mission settings, only the tables

        Assert.Equal(0x3C108020u, Word(built, 0xB563C));                 // lui s0, hi(result sheet names)
        Assert.Equal(0x8E10D330u, Word(built, 0xB5648));                 // lw s0, lo(...)(s0)
        Assert.Equal(0x8E10D100u, Word(built, 0xB567C));                 // mission screen names
        Assert.Equal(0x90C6D560u, Word(built, 0xB57D4));                 // point rows
        Assert.Equal(0x0307C021u, Word(built, 0xB5618));                 // × 5
        Assert.Equal(0x2C210003u, Word(built, 0x6FA00));                 // save: (veh - 3) < 3
        Assert.Equal(0x26E1FFFDu, Word(built, 0x6FE14));                 // load: the delay slot of the vehicle loop
        Assert.Equal(new MedalLimits(280, 320, 360), new MissionTables(built, RomLayout.PilotwingsUsa.Missions!).MedalLimits()[7]);
        foreach (int call in GamePatches.PointsToMedalCalls)
        {
            Assert.Equal(CodeArea.Jal(GameModuleImage.PointsToMedalAddress), Word(built, call));
        }

        // The texts are in the built text file.
        var fileSystem = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        GameTextFile textFile = GameTextFile.Parse(GameTextFile.FindFile(fileSystem).Data);
        Assert.All(ResultTables.NewTexts, t => Assert.Contains(textFile.Entries, e => e.Name == t.Name));
    }

    [RealRomFact]
    public void Undo_RestoresTheCode_AndKeepsTheTexts()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        IReadOnlyList<string> uses = GamePatches.MissionsUsing(GamePatches.BonusGames, session.LoadMissions().Missions);
        Assert.Equal(6, uses.Count); // three Sky Diving and three Jumble Hopper levels

        session.SaveTexts(session.LoadTexts(), new Dictionary<int, string> { [440] = "Changed" }); // now the texts are in the project
        Assert.Equal("SD_L2_S1", session.LoadTexts().Texts[440].Name);
        session.SetGamePatch(GamePatches.BonusGames, false);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x3C108035u, Word(built, 0xB563C));
        Assert.Equal(0x2AC10003u, Word(built, 0x6F9FC));
        Assert.Equal(0x0C0CB05Du, Word(built, GamePatches.PointsToMedalCalls[0]));
        Assert.Null(DataArea.ReadResultTables(DataImage(built)));
        Assert.Equal("SD_L123_S1", session.LoadMissions().Missions.Single(m => m.Id == new TestId(1, Vehicle.SkyDiving, 0)).MissionScreenTextName);
        Assert.Contains(session.LoadTexts().Texts, t => t.Name == "SD_L1_S1"); // stays as a custom text

        // Applying it again: the texts are not added twice.
        int count = session.LoadTexts().Texts.Count;
        session.SetGamePatch(GamePatches.BonusGames, true);
        Assert.Equal(count, session.LoadTexts().Texts.Count);
    }

    [RealRomFact]
    public void ProjectWithoutTheTexts_GetsThemInTheBuild()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("plain"), "Plain", rom.ComputeSha1(), null); // no session: no texts
        Assert.Empty(project.GetOverrides());
        byte[] built = ProjectBuilder.Build(project, rom, RomLayout.PilotwingsUsa).RomBuild.Rom.Data;
        var fileSystem = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        GameTextFile textFile = GameTextFile.Parse(GameTextFile.FindFile(fileSystem).Data);
        GameTextFile original = GameTextFile.Parse(GameTextFile.FindFile(GameFileSystem.Read(rom, RomLayout.PilotwingsUsa)).Data);
        Assert.Equal(original.Entries.Count + 9, textFile.Entries.Count);
        Assert.Equal(original.Entries.Single(e => e.Name == "SD_L123_S2").Data, textFile.Entries.Single(e => e.Name == "SD_L3_S2").Data);
    }

    [RealRomFact]
    public void Patch_NeedsTheExpandedTexts()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        session.SetGamePatch(GamePatches.BonusGames, false);
        session.Project.Settings.AppliedCodeFixes.Remove(CodeFixes.ExpandedTexts.Id);
        Assert.Throws<ProjectException>(() => session.SetGamePatch(GamePatches.BonusGames, true));
        Assert.False(session.Project.HasPatch(GamePatches.BonusGames));
    }

    [Fact]
    public void SaveLayout_KeepsTheBirdmansStage4_WithThePatch()
    {
        SaveGameLayout retail = SaveGameLayout.Retail;
        SaveGameLayout patched = SaveGameLayout.RetailWithBirdmanStage4;
        Assert.Equal(57, retail.SavedTests.Count);
        Assert.Equal(61, patched.SavedTests.Count);
        Assert.False(retail.IsSaved(new TestId(3, Vehicle.Birdman, 0)));
        Assert.True(patched.IsSaved(new TestId(3, Vehicle.Birdman, 3)));
        Assert.Equal(retail.IndexOf(new TestId(2, Vehicle.Birdman, 3)), patched.IndexOf(new TestId(2, Vehicle.Birdman, 3)));
        Assert.Equal(retail.PhotosStartBit + (4 * SaveGameLayout.ResultBits), patched.PhotosStartBit);
        Assert.True(patched.BirdmanStage4);
        Assert.False(retail.BirdmanStage4);
    }
}
