using System.Buffers.Binary;
using PW64Editor.Core.Classes;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

/// <summary>Stage 7: up to 128 missions, five tests per class, missions added and removed.</summary>
public class MissionCountTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static DataImages Images(byte[] built) => GameModule.ReadDataImages(built, GameModule.ReadHeader(built)!)!;

    [Fact]
    public void Places_AndTheirNames()
    {
        Assert.Equal("B_HG_4", MissionPositions.StatementBase(new TestId(2, Vehicle.HangGlider, 3)));
        Assert.Equal("P_BD_5", MissionPositions.StatementBase(new TestId(3, Vehicle.Birdman, 4)));
        Assert.Null(MissionPositions.StatementBase(new TestId(0, Vehicle.SkyDiving, 0)));
        Assert.Equal("HG_B4_S1", MissionPositions.NewPointsTextName(new TestId(2, Vehicle.HangGlider, 3), false));
        Assert.Equal("RP_E2_S2", MissionPositions.NewPointsTextName(new TestId(0, Vehicle.RocketBelt, 1), true));
        Assert.Null(MissionPositions.NewPointsTextName(new TestId(2, Vehicle.HangGlider, 2), false)); // the game's HG_B3_S1
        Assert.Equal("GC_A1_S1", MissionPositions.FirstTestPointsTextName(new TestId(1, Vehicle.Gyrocopter, 2), false));
        Assert.Equal(3 * (4 + 3 + 2 + 2), MissionPositions.NewPointsPlaces.Count());
        Assert.True(MissionPositions.IsNewPointsText("GC_P5_S2"));
        Assert.False(MissionPositions.IsNewPointsText("GC_P3_S2"));

        Assert.Null(ProjectMissions.Problem([new(2, Vehicle.HangGlider, 0), new(2, Vehicle.HangGlider, 1)]));
        Assert.NotNull(ProjectMissions.Problem([new(2, Vehicle.HangGlider, 0), new(2, Vehicle.HangGlider, 2)])); // a gap
        Assert.NotNull(ProjectMissions.Problem([new(2, Vehicle.HangGlider, 0), new(2, Vehicle.HangGlider, 0)]));
        Assert.NotNull(ProjectMissions.Problem([.. Enumerable.Range(0, 6).Select(t => new TestId(2, Vehicle.HangGlider, t))]));
    }

    [Fact]
    public void Patches_FitTheCleanCode_AndTheLowerDataArea()
    {
        Assert.Equal(0x803901E0u, MissionCapacity.UserFilesAddress);
        Assert.True(GameModuleImage.EndAddress <= MissionCapacity.UserFilesAddress);
        Assert.True(ClassLimits.TableAddress + ClassLimits.TableSize <= DataExtension.End);
        IReadOnlyList<WordPatch> patches = MissionCapacity.Patches(70);
        Assert.Equal(0x24010046u, patches.Single(p => p.RomOffset == MissionCapacity.MissionCountRomOffset).Replacement[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionCapacity.Patches(129));

        byte[] ext = DataExtension.Create([0x27, 0x6F], [new(0, Vehicle.Birdman, 4)], [new(2, Vehicle.HangGlider, 0)]);
        Assert.Equal([0x27, 0x6F], DataExtension.ReadMissionFiles(ext));
        Assert.Equal([new TestId(2, Vehicle.HangGlider, 0)], DataExtension.ReadPhotoMissions(ext));
        Assert.Equal(1, ext[DataExtension.PhotoOffset + (0 * 8) + 2]);
        Assert.Throws<InvalidDataException>(() => DataExtension.Create([.. Enumerable.Range(0, 129)], [], []));
    }

    [RealRomFact]
    public void GameList_IsTheGamesOrder()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);

        // sTaskUserFileIdxLookup (task.c, ROM 0xD7CDC = 0x803507AC): the same 61 numbers.
        byte[] table = rom.Data.AsSpan((int)(0x803507AC - 0x80278AD0), 61).ToArray();
        Assert.Equal(DataExtension.GameMissionFiles, table.Select(b => (int)b));
        Assert.Equal(61, DataExtension.GameMissionFiles.Distinct().Count());
        Assert.All(DataExtension.GameMissionFiles, n => Assert.Equal(MissionFile.FormType, fs.Files.Single(f => f.Group == FileTypeLimits.UserFileGroup && f.GroupIndex == n).FileType));

        // The photo missions of the game are missions of the game.
        List<TestId> ids = [.. fs.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data).Comm.Id)];
        Assert.All(DataExtension.GamePhotoMissions, id => Assert.Contains(id, ids));
    }

    [RealRomFact]
    public void Add_Build_Remove()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Missions", false, rom, TestRomLocator.RomPath!);

        // A plain build: the game's 61 missions, the user files in the code area.
        byte[] plain = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x2401003Du, Word(plain, MissionCapacity.MissionCountRomOffset));
        Assert.Equal(0x3C018039u, Word(plain, 0x25980));
        Assert.Equal(DataExtension.GameMissionFiles, DataExtension.ReadMissionFiles(Images(plain).Extension!));
        Assert.Equal(6, DataExtension.ReadPhotoMissions(Images(plain).Extension!).Count);

        // Not for Cannonball; a fourth test for the Hang Glider's Class B (a copy of its third).
        Assert.NotNull(session.CannotAddMission(Vehicle.Cannonball, 0));
        MissionInfo b3 = session.LoadMissions().Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 2));
        TestId b4 = session.AddMission(b3, 2);
        Assert.Equal(new TestId(2, Vehicle.HangGlider, 3), b4);
        MissionCatalogResult catalog = session.LoadMissions();
        MissionInfo added = catalog.Missions.Single(m => m.Id == b4);
        Assert.Equal(62, catalog.Missions.Count);
        Assert.Equal("B_HG_4", added.File.TextBase);
        Assert.Equal("HG_B4_S1", added.MissionScreenTextName);
        Assert.Equal(111, added.FileNumber);
        GameTextLibrary texts = session.LoadTexts();
        string Markup(string name) => texts.Texts.Single(t => !t.IsFree && t.Name == name).Markup;
        Assert.Equal(Markup("B_HG_3_N"), Markup("B_HG_4_N"));
        Assert.Equal(Markup("HG_B3_S2"), Markup("HG_B4_S2"));
        Assert.Equal("[b]4 tests to go[/b]", Markup("TRY4"));
        Assert.Contains("Test 4", Markup("HG_B_S3_GOLD"), StringComparison.Ordinal); // the class's own medal texts
        Assert.Equal(4, session.CurrentSaveLayout().TestCount(2, Vehicle.HangGlider));

        // The Classes tab offers to scale the limits of the class (210/240/270 for 3 tests → 280/320/360 for 4).
        ClassInfo classB = session.LoadClasses().Single(c => c.Vehicle == Vehicle.HangGlider && c.Class == 2);
        Assert.True(classB.LimitsNeedScaling);
        Assert.Equal(3, classB.LimitsForTests);
        Assert.Equal([280, 320, 360], classB.ScaledMedals());
        Assert.False(session.LoadClasses().Single(c => c.Vehicle == Vehicle.HangGlider && c.Class == 1).LimitsNeedScaling);
        Assert.False(session.LoadClasses().Single(c => c.Vehicle == Vehicle.Cannonball && c.Class == 0).LimitsNeedScaling);

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x2401003Eu, Word(built, MissionCapacity.MissionCountRomOffset));
        DataImages images = Images(built);
        Assert.Equal([.. DataExtension.GameMissionFiles, 111], DataExtension.ReadMissionFiles(images.Extension!));
        GameFileSystem fs = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        GameFile file = fs.Files.Single(f => f.Group == FileTypeLimits.UserFileGroup && f.GroupIndex == 111);
        Assert.Equal(b4, MissionFile.Parse(file.Data).Comm.Id);
        byte[] table = ClassesTable(images);
        Assert.Equal(ClassLimits.FlagOwnTexts | ClassLimits.FlagAllTexts, table[ClassLimits.FlagsOffset + (0 * 8) + 2]);
        var builtTexts = GameTextFile.Parse(fs.Files.Single(f => f.FileType == GameTextFile.FormType).Data);
        Assert.Contains(builtTexts.Entries, e => e.Name == "HG_B4_S1");
        Assert.Contains(builtTexts.Entries, e => e.Name == "HG_B_S3_PERFECT");
        byte[] tables = DataArea.ReadResultTables(images)!;
        int entry = (((0 * 8) + 2) * 5) + 3;
        uint pointer = Word(tables, entry * 4);
        Assert.Equal("HG_B4_S1", System.Text.Encoding.ASCII.GetString(tables, (int)(pointer - ResultTables.Address), 8));

        // Removing test 1 of Class B (a mission of the game): the others move up with their texts; the photo mission (test 2)
        // stays one.
        string oldTitle2 = Markup("B_HG_2_N"), oldTitle4 = Markup("B_HG_4_N");
        MissionInfo b1 = session.LoadMissions().Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 0));
        Assert.Null(session.CannotRemoveMission(b1));
        SavePlaceChanges places = session.RemoveMission(b1);
        Assert.Equal([b1.Id], places.Removed);
        Assert.Equal(new TestId(2, Vehicle.HangGlider, 0), places.Moved[new TestId(2, Vehicle.HangGlider, 1)]);
        Assert.Equal(3, places.Moved.Count);
        catalog = session.LoadMissions();
        Assert.Equal(61, catalog.Missions.Count);
        Assert.Equal([0, 1, 2], catalog.Missions.Where(m => m.Id.Vehicle == Vehicle.HangGlider && m.Id.Class == 2).Select(m => m.Id.Test));
        Assert.Equal("B_HG_1", catalog.Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 0)).File.TextBase);
        texts = session.LoadTexts();
        Assert.Equal(oldTitle2, Markup("B_HG_1_N"));
        Assert.Equal(oldTitle4, Markup("B_HG_3_N"));
        Assert.DoesNotContain(texts.Texts, t => !t.IsFree && t.Name == "B_HG_4_N"); // the free place's added texts are gone
        Assert.Equal([b1.FileNumber], session.Project.Settings.RemovedMissions);
        Assert.True(session.IsPhotoMission(catalog.Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 0))));

        built = session.Build(false).Result.RomBuild.Rom.Data;
        images = Images(built);
        IReadOnlyList<int> list = DataExtension.ReadMissionFiles(images.Extension!);
        Assert.Equal(61, list.Count);
        Assert.DoesNotContain(b1.FileNumber, list);
        Assert.Contains(new TestId(2, Vehicle.HangGlider, 0), DataExtension.ReadPhotoMissions(images.Extension!));
        Assert.DoesNotContain(new TestId(2, Vehicle.HangGlider, 1), DataExtension.ReadPhotoMissions(images.Extension!));
        fs = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        Assert.Equal(MissionComm.NoVehicle, (byte)MissionFile.Parse(fs.Files.Single(f => f.Group == FileTypeLimits.UserFileGroup && f.GroupIndex == b1.FileNumber).Data).Comm.Id.Vehicle);

        // The import finds the added and the removed mission and builds the same ROM.
        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.Equal([b1.FileNumber], import.Settings.RemovedMissions);
        Assert.Contains(import.ChangedFiles, f => f.TableIndex == session.CleanFileSystem.Files.Count && f.GroupIndex == 111);
        Assert.DoesNotContain(import.ChangedFiles, f => f.GroupIndex == b1.FileNumber && f.FileType == MissionFile.FormType);
        Assert.DoesNotContain(import.Notes, n => n.Kind == ImportNoteKind.Warning);
        foreach (TestId moved in Enumerable.Range(0, 3).Select(t => new TestId(2, Vehicle.HangGlider, t)))
        {
            Assert.Equal(session.Project.Settings.Missions.FirstOrDefault(m => m.Id == moved)?.ResultRows,
                import.Settings.Missions.FirstOrDefault(m => m.Id == moved)?.ResultRows);
        }

        (EditorSession copy, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, N64Rom.FromBytes(built), temp.Combine("imported"), "Missions", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
        Assert.Equal(61, copy.LoadMissions().Missions.Count);

        // Removing the added mission (now test 3) deletes its file.
        MissionInfo last = session.LoadMissions().Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 2));
        Assert.Equal(111, last.FileNumber);
        session.RemoveMission(last);
        Assert.DoesNotContain(session.Project.GetOverrides(), o => o.TableIndex >= session.CleanFileSystem.Files.Count);
        Assert.Equal(60, session.LoadMissions().Missions.Count);
        Assert.Equal(2, session.CurrentSaveLayout().TestCount(2, Vehicle.HangGlider));
    }

    private static byte[] ClassesTable(DataImages images) => DataArea.ReadClassTable(images)!;
}
