using System.Buffers.Binary;
using PW64Editor.Core.Classes;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Classes;

public class MenuUnlocksTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static readonly Vehicle[] Games = [Vehicle.Cannonball, Vehicle.SkyDiving, Vehicle.JumbleHopper];

    /// <summary>The game's rules written out by hand (level_select.c, with the limits of Step 37: only the sums count).</summary>
    private static bool ReferenceOpen(SaveProgress p, Vehicle vehicle, int c)
    {
        bool AllMain(int cls, Medal medal) => Vehicles.Main.All(v => p.ClassMedal(v, cls) >= medal);
        if (Vehicles.IsMain(vehicle))
        {
            return c == 0 || (AllMain(0, Medal.Bronze) && Enumerable.Range(1, c - 1).All(k => p.ClassMedal(vehicle, k) >= Medal.Bronze));
        }

        if (vehicle == Vehicle.Birdman)
        {
            return AllMain(c, Medal.Silver) && (c == 0 || Enumerable.Range(0, 3).All(l => p.LevelMedal(Games[c - 1], l) >= Medal.Silver));
        }

        int required = Array.IndexOf(Games, vehicle) + 1;
        return AllMain(required, Medal.Silver) && Enumerable.Range(0, c).All(l => p.LevelMedal(vehicle, l) >= Medal.Bronze);
    }

    [Fact]
    public void Original_IsTheGamesRules_OnRandomSaves()
    {
        var random = new Random(38);
        for (int round = 0; round < 300; round++)
        {
            var slot = new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail);
            var progress = new SaveProgress(slot);
            int high = random.Next(4) switch { 0 => 60, 1 => 85, 2 => 95, _ => 101 };
            foreach (TestId test in slot.Layout.SavedTests.Where(t => t.Vehicle != Vehicle.Birdman))
            {
                if (Vehicles.IsMain(test.Vehicle) && random.Next(8) > 0)
                {
                    slot.SetResult(test, Math.Min(100, random.Next(high - 30, high + 10)));
                }
            }

            foreach (Vehicle game in Games)
            {
                for (int level = 0; level < 3; level++)
                {
                    if (random.Next(5) > 0)
                    {
                        progress.SetLevelValue(game, level, Math.Clamp(random.Next(high - 40, high + 10), 0, 100));
                    }
                }
            }

            foreach ((Vehicle vehicle, int c) in MenuUnlocks.Fields)
            {
                Assert.Equal(ReferenceOpen(progress, vehicle, c), progress.IsFieldOpen(vehicle, c));
            }
        }
    }

    [Fact]
    public void Table_RoundTrip_AndProblems()
    {
        Assert.Null(MenuUnlocks.Table(null, false));
        Assert.True(MenuUnlocks.IsOriginal(MenuUnlocks.ReadTable(MenuUnlocks.Table(MenuUnlocks.Original(), false))!));
        Assert.All(MenuUnlocks.ReadTable(MenuUnlocks.Table(null, true))!, f => Assert.Empty(f.Conditions));

        List<FieldUnlock> rules = MenuUnlocks.Original();
        rules[MenuUnlocks.FieldIndex(Vehicle.RocketBelt, 0)].Conditions.Add(
            UnlockCondition.Of(Vehicle.HangGlider, 1, UnlockKind.Points, 180));
        rules[MenuUnlocks.FieldIndex(Vehicle.Birdman, 3)].Conditions = [UnlockCondition.Of(Vehicle.JumbleHopper, 2, UnlockKind.Medal, 4)];
        Assert.Null(MenuUnlocks.Problem(rules));
        byte[] table = MenuUnlocks.Table(rules, false)!;
        Assert.Equal(MenuUnlocks.TableSize, table.Length);
        Assert.Equal(1, table[MenuUnlocks.FieldIndex(Vehicle.RocketBelt, 0)]);
        int at = 0x20 + (MenuUnlocks.FieldIndex(Vehicle.RocketBelt, 0) * MenuUnlocks.MaxConditions * 3);
        Assert.Equal([0x01, 0x00, 0xB4], table[at..(at + 3)]); // field 1 (Hang Glider Class A), points, 180
        List<FieldUnlock> back = MenuUnlocks.ReadTable(table)!;
        Assert.Equal(UnlockKind.Medal, back[MenuUnlocks.FieldIndex(Vehicle.Birdman, 3)].Conditions.Single().Kind);
        Assert.Equal(4, back[MenuUnlocks.FieldIndex(Vehicle.Birdman, 3)].Conditions.Single().Value);
        Assert.False(MenuUnlocks.IsOriginal(back));

        List<FieldUnlock> self = MenuUnlocks.Original();
        self[0].Conditions.Add(UnlockCondition.Of(Vehicle.HangGlider, 0, UnlockKind.Points, 1));
        Assert.Contains("itself", MenuUnlocks.Problem(self), StringComparison.Ordinal);
        List<FieldUnlock> closed = MenuUnlocks.Original();
        foreach (FieldUnlock field in closed.Where(f => f.Conditions.Count == 0))
        {
            field.Conditions.Add(UnlockCondition.Of(Vehicle.Birdman, 0, UnlockKind.Points, 1));
        }

        Assert.NotNull(MenuUnlocks.Problem(closed));
        List<FieldUnlock> value = MenuUnlocks.Original();
        value[5].Conditions[0].Value = 5;
        Assert.NotNull(MenuUnlocks.Problem(value));
        List<FieldUnlock> many = MenuUnlocks.Original();
        many[5].Conditions.AddRange(Enumerable.Range(0, 9).Select(_ => UnlockCondition.Of(Vehicle.Birdman, 0, UnlockKind.Points, 1)));
        Assert.NotNull(MenuUnlocks.Problem(many));
        List<FieldUnlock> high = MenuUnlocks.Original();
        high[5].Conditions.Add(UnlockCondition.Of(Vehicle.SkyDiving, 0, UnlockKind.Points, 101));
        Assert.Contains("never", MenuUnlocks.Problem(high), StringComparison.Ordinal);
        Assert.Equal(100 * SaveGameLayout.Retail.TestCount(1, Vehicle.HangGlider), MenuUnlocks.HighestPoints(Vehicle.HangGlider, 1));
    }

    [Fact]
    public void SaveProgress_FollowsOwnRules_AndOpensFields()
    {
        List<FieldUnlock> rules = MenuUnlocks.Original();
        // Only Rocket Belt Beginner is open; Hang Glider Beginner needs 80 points in it, Cannonball Level 1 perfect there.
        rules[MenuUnlocks.FieldIndex(Vehicle.HangGlider, 0)].Conditions = [UnlockCondition.Of(Vehicle.RocketBelt, 0, UnlockKind.Points, 80)];
        rules[MenuUnlocks.FieldIndex(Vehicle.Gyrocopter, 0)].Conditions = [UnlockCondition.Of(Vehicle.HangGlider, 0, UnlockKind.Medal, 2)];
        rules[MenuUnlocks.FieldIndex(Vehicle.Cannonball, 0)].Conditions = [UnlockCondition.Of(Vehicle.RocketBelt, 0, UnlockKind.Medal, 4)];
        var slot = new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail);
        var progress = new SaveProgress(slot, null, rules, null);
        Assert.True(progress.IsFieldOpen(Vehicle.RocketBelt, 0));
        Assert.False(progress.IsFieldOpen(Vehicle.HangGlider, 0));
        Assert.False(progress.IsFieldOpen(Vehicle.Gyrocopter, 0));

        progress.OpenField(Vehicle.Gyrocopter, 0); // opens Hang Glider Beginner first
        Assert.True(progress.IsFieldOpen(Vehicle.HangGlider, 0));
        Assert.True(progress.IsFieldOpen(Vehicle.Gyrocopter, 0));
        Assert.True(progress.FieldTotal(Vehicle.RocketBelt, 0) >= 80);
        Assert.True(progress.FieldMedal(Vehicle.HangGlider, 0) >= 2);
        Assert.Single(progress.MissingConditions(Vehicle.Cannonball, 0));

        progress.OpenField(Vehicle.Cannonball, 0);
        Assert.Equal(4, progress.FieldMedal(Vehicle.RocketBelt, 0));
        Assert.True(progress.IsFieldOpen(Vehicle.Cannonball, 0));

        // Conditions in a circle do not hang.
        rules[MenuUnlocks.FieldIndex(Vehicle.SkyDiving, 0)].Conditions = [UnlockCondition.Of(Vehicle.SkyDiving, 1, UnlockKind.Points, 50)];
        rules[MenuUnlocks.FieldIndex(Vehicle.SkyDiving, 1)].Conditions = [UnlockCondition.Of(Vehicle.SkyDiving, 0, UnlockKind.Points, 50)];
        var circle = new SaveProgress(new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail), null, rules, null);
        circle.OpenField(Vehicle.SkyDiving, 0);
        Assert.True(circle.IsFieldOpen(Vehicle.SkyDiving, 0));
    }

    [RealRomFact]
    public void OwnRules_AreBuilt_AndImportedAgain()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Unlocks", false, rom, TestRomLocator.RomPath!);
        List<FieldUnlock> rules = session.LoadUnlocks();
        session.SaveUnlocks(rules);
        Assert.Null(session.Project.Settings.Unlocks); // the game's rules are stored as none

        List<FieldUnlock> broken = session.LoadUnlocks();
        broken[0].Conditions.Add(UnlockCondition.Of(Vehicle.HangGlider, 0, UnlockKind.Points, 1));
        Assert.Throws<ArgumentException>(() => session.SaveUnlocks(broken));

        rules[MenuUnlocks.FieldIndex(Vehicle.RocketBelt, 0)].Conditions = [UnlockCondition.Of(Vehicle.HangGlider, 0, UnlockKind.Points, 85)];
        session.SaveUnlocks(rules);
        Assert.NotNull(session.Project.Settings.Unlocks);

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(CodeArea.J(GameModuleImage.MenuUpdateAddress), Word(built, 0x95F84));
        Assert.Equal(CodeArea.J(GameModuleImage.MenuPage1Address), Word(built, 0x94EF8));
        Assert.Equal(CodeArea.J(GameModuleImage.FileMenuAddress), Word(built, 0x70020));
        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.False(import.Settings.Features.AllFieldsOpen);
        Assert.Equal(85, import.Settings.Unlocks!.Single(f => f.Vehicle == (int)Vehicle.RocketBelt && f.Class == 0).Conditions.Single().Value);

        GameFeatureSettings features = session.Project.Settings.Features.Clone();
        features.AllFieldsOpen = true;
        session.SaveFeatures(features);
        session.SaveUnlocks(null);
        built = session.Build(false).Result.RomBuild.Rom.Data;
        import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.True(import.Settings.Features.AllFieldsOpen);
        Assert.Null(import.Settings.Unlocks);
    }

    [RealRomFact]
    public void WithoutOwnRules_TheCursorStaysTheGames()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Unlocks", false, rom, TestRomLocator.RomPath!);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x27BDFFD0u, Word(built, 0x95F84));
    }
}
