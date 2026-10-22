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

namespace PW64Editor.Core.Tests.Classes;

/// <summary>Medal limits and islands per class (Classes tab).</summary>
public class ClassesTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    [Fact]
    public void WithLimit_ReplacesTheNumber_AndCentresTheHeader()
    {
        Assert.Equal("[x=45][b]< 1000 pts[/b]. [b]for Gold Badge >[/b]\n  Test 1",
            ClassTexts.WithLimit("[x=50][b]< 180 pts[/b]. [b]for Gold Badge >[/b]\n  Test 1", 1000));
        Assert.Equal("[x=50][b]< 120 pts[/b]. [b]for Gold Badge >[/b]", ClassTexts.WithLimit("[x=55][b]< 90 pts[/b]. [b]for Gold Badge >[/b]", 120));
        Assert.Equal("[x=36][b]< 150 pts[/b]. [b]for Bronze Badge[/b]. [b]>[/b]",
            ClassTexts.WithLimit("[x=36][b]< 210 pts[/b]. [b]for Bronze Badge[/b]. [b]>[/b]", 150));
        Assert.Null(ClassTexts.WithLimit("no header", 10));
    }

    [Fact]
    public void Names_AndTemplates()
    {
        Assert.Equal("HG_A_S3_GOLD", ClassTexts.TextName(Vehicle.HangGlider, 1, 2));
        Assert.Equal("SD_L2_S3_BLONDS", ClassTexts.TextName(Vehicle.SkyDiving, 1, 0));
        Assert.Null(ClassTexts.TextName(Vehicle.Birdman, 0, 0));
        Assert.Equal("P_S3_SILVER", ClassTexts.TemplateName(Vehicle.Gyrocopter, 3, 1));
        Assert.Equal("BONUS_S3_GOLD", ClassTexts.TemplateName(Vehicle.JumbleHopper, 2, 2));
        Assert.True(ClassTexts.IsClassText("RP_E_S3_SILVER") && ClassTexts.IsClassText("CB_L3_S3_GOLD"));
        Assert.False(ClassTexts.IsClassText("A_S3_GOLD") || ClassTexts.IsClassText("HG_A_S3_PERFECT") || ClassTexts.IsClassText("BD_E_S3_GOLD"));
        Assert.Equal("Badges", TextCatalog.Categorize("SD_L2_S3_SILVER").Area);
        Assert.Equal("Hang Glider", TextCatalog.Categorize("HG_A_S3_GOLD").Section);
    }

    [Fact]
    public void Limits_Table_AndProblems()
    {
        Assert.Equal([140, 160, 180], ClassLimits.Original(Vehicle.RocketBelt, 1, false));
        Assert.Equal([70, 80, 90], ClassLimits.Original(Vehicle.SkyDiving, 2, true));
        Assert.Equal([280, 320, 360], ClassLimits.Original(Vehicle.Birdman, 0, true));
        Assert.Equal([140, 160, 180], ClassLimits.Effective(null, Vehicle.Birdman, 1, true, 2));
        Assert.Equal(200, ClassLimits.Perfect(Vehicle.Birdman, 4, 2));
        Assert.Equal(100, ClassLimits.Perfect(Vehicle.Cannonball, 4, 0));
        Assert.Equal(300, ClassLimits.Perfect(Vehicle.Gyrocopter, 3, 0));
        Assert.Equal(25, ClassLimits.All.Count);

        Assert.Null(ClassLimits.Problem([100, 120, 150], 200));
        Assert.NotNull(ClassLimits.Problem([130, 120, 150], 200));
        Assert.NotNull(ClassLimits.Problem([100, 120, 250], 200));
        Assert.NotNull(ClassLimits.Problem([-1, 120, 150], 200));

        Assert.Null(ClassLimits.Table([ClassSettings.For(Vehicle.HangGlider, 1)], false, true)); // only an island: no table
        ClassSettings hg = ClassSettings.For(Vehicle.HangGlider, 1);
        hg.Medals = [100, 120, 150];
        ClassSettings bd = ClassSettings.For(Vehicle.Birdman, 2);
        bd.Medals = [10, 20, 30];
        byte[] table = ClassLimits.Table([hg, bd], true, true)!;
        Assert.Equal(ClassLimits.TableSize, table.Length);
        Assert.Equal(150, BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(((0 * 4 + 1) * 3 + 2) * 2)));
        Assert.Equal(180, BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(((1 * 4 + 1) * 3 + 2) * 2))); // Rocket Belt A: original
        Assert.Equal(360, BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(((6 * 4 + 0) * 3 + 2) * 2))); // Birdman stage 1: original
        Assert.Equal(ClassLimits.FlagOwnLimits | ClassLimits.FlagOwnTexts, table[ClassLimits.FlagsOffset + 1]);
        Assert.Equal(ClassLimits.FlagOwnLimits, table[ClassLimits.FlagsOffset + (6 * 4) + 2]); // the Birdman: texts of its own kind
        Assert.Equal(0, table[ClassLimits.FlagsOffset + 2]);
        IReadOnlyList<(Vehicle Vehicle, int Class, IReadOnlyList<int> Medals)> read = ClassLimits.ReadTable(table);
        Assert.Equal(2, read.Count);
        Assert.Equal([100, 120, 150], read[0].Medals);

        // The data area: the table behind the result tables, its address in the header.
        byte[] image = DataArea.CreateImage([], null, 0, table);
        Assert.Equal(ClassLimits.TableAddress, Word(image, DataArea.ClassTableOffset));
        Assert.Equal(table, DataArea.ReadClassTable(image));
        Assert.Null(DataArea.ReadClassTable(DataArea.CreateImage([])));
        IReadOnlyList<TestId> all = [.. Enumerable.Range(0, 16).Select(b => new TestId(b / 4, Vehicle.Birdman, b % 4))];
        Assert.True(ResultTables.Address + ResultTables.SizeFor(all) <= ClassLimits.TableAddress);
    }

    [Fact]
    public void SaveProgress_UsesTheLimitsOfTheProject()
    {
        var slot = new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail);
        slot.SetResult(new TestId(0, Vehicle.HangGlider, 0), 60);
        Assert.Equal(Medal.None, new SaveProgress(slot).ClassMedal(Vehicle.HangGlider, 0));
        var progress = new SaveProgress(slot, (v, c) => v == Vehicle.HangGlider && c == 0 ? [50, 60, 95] : null);
        Assert.Equal(Medal.Silver, progress.ClassMedal(Vehicle.HangGlider, 0));
        Assert.Equal(95, progress.LimitOf(Vehicle.HangGlider, 0, Medal.Gold));
        Assert.Equal(90, progress.LimitOf(Vehicle.RocketBelt, 0, Medal.Gold));
        Assert.Equal(80, progress.LimitOf(Vehicle.SkyDiving, 1, Medal.Silver));
    }

    [RealRomFact]
    public void OwnLimitsAndIsland_AreBuilt_AndImportedAgain()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Classes", false, rom, TestRomLocator.RomPath!);
        ClassSettings hg = ClassSettings.For(Vehicle.HangGlider, 1);
        hg.Medals = [100, 120, 150];
        session.SaveClass(hg);
        ClassSettings sd = ClassSettings.For(Vehicle.SkyDiving, 1);
        sd.Medals = [60, 85, 100];
        session.SaveClass(sd);
        ClassSettings island = ClassSettings.For(Vehicle.RocketBelt, 0);
        island.Island = 2;
        session.SaveClass(island);
        session.SaveClass(ClassSettings.For(Vehicle.Gyrocopter, 0)); // nothing changed: not stored
        Assert.Equal(3, session.Project.Settings.Classes.Count);

        ClassInfo info = session.LoadClasses().Single(c => c.Vehicle == Vehicle.HangGlider && c.Class == 1);
        Assert.True(info.HasOwnMedals && info.OwnTexts);
        Assert.Equal([100, 120, 150], info.Medals);
        Assert.Equal([140, 160, 180], info.DefaultMedals);
        Assert.Equal(200, info.Perfect);
        Assert.Equal(2, session.LoadClasses().Single(c => c.Vehicle == Vehicle.RocketBelt && c.Class == 0).Island);

        // The texts: own bronze, silver and gold texts from the class's texts.
        GameTextLibrary texts = session.LoadTexts();
        Assert.StartsWith("[x=50][b]< 150 pts", texts.Texts.Single(t => t.Name == "HG_A_S3_GOLD").Markup, StringComparison.Ordinal);
        Assert.Contains("Test 2", texts.Texts.Single(t => t.Name == "HG_A_S3_BLONDS").Markup, StringComparison.Ordinal);
        Assert.StartsWith("[x=50][b]< 100 pts", texts.Texts.Single(t => t.Name == "SD_L2_S3_GOLD").Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(texts.Texts, t => t.Name == "RP_A_S3_GOLD" || t.Name == "HG_A_S3_PERFECT");

        // The build: class table, the patched functions, the island in the missions and on the test screen.
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        GameModuleHeader header = GameModule.ReadHeader(built)!;
        byte[] image = built.AsSpan(header.DataRomOffset, header.DataSize).ToArray();
        Assert.Equal(2, ClassLimits.ReadTable(DataArea.ReadClassTable(image)!).Count);
        Assert.Equal(CodeArea.J(GameModuleImage.MenuPage1Address), Word(built, 0x94EF8));
        Assert.Equal(CodeArea.J(GameModuleImage.PointsToMedalAddress), Word(built, 0xB36A4));
        Assert.Equal(CodeArea.Jal(GameModuleImage.BronzeT2Address), Word(built, 0x971F4));
        Assert.Equal(CodeArea.Jal(GameModuleImage.TotalMedalTextAddress), Word(built, 0xCEA44));
        Assert.Equal(2, built[RomLayout.PilotwingsUsa.Missions!.ClassIslandsOffset + (1 * 4) + 0]);
        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.Equal(3, import.Settings.Classes.Count);
        Assert.Equal([100, 120, 150], import.Settings.Classes.Single(c => c.VehicleId == Vehicle.HangGlider).Medals);
        Assert.Equal(2, import.Settings.Classes.Single(c => c.VehicleId == Vehicle.RocketBelt).Island);
        Assert.DoesNotContain(import.ChangedFiles, f => f.FileType == GameTextFile.FormType);

        (EditorSession copy, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, N64Rom.FromBytes(built), temp.Combine("imported"), "Classes", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
        MissionInfo rocketBelt = copy.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0));
        Assert.Equal(2, rocketBelt.Island);
    }

    [RealRomFact]
    public void WithoutOwnLimits_NothingChanges()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Classes", false, rom, TestRomLocator.RomPath!);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x27BDFF48u, Word(built, 0x94EF8));
        Assert.Equal(0x3C0A8035u, Word(built, 0x971F4));
        GameModuleHeader header = GameModule.ReadHeader(built)!;
        Assert.Null(DataArea.ReadClassTable(built.AsSpan(header.DataRomOffset, header.DataSize)));
    }
}
