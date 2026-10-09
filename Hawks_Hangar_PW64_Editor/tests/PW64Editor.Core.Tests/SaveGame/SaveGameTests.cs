using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.SaveGame;

public class SaveGameLayoutTests
{
    [Fact]
    public void Retail_HasTheGamesOrderAndSize()
    {
        SaveGameLayout layout = SaveGameLayout.Retail;

        Assert.Equal(57, layout.SavedTests.Count);
        Assert.Equal(new TestId(0, Vehicle.HangGlider, 0), layout.SavedTests[0]);
        Assert.Equal(new TestId(1, Vehicle.HangGlider, 0), layout.SavedTests[1]);
        Assert.Equal(new TestId(0, Vehicle.RocketBelt, 0), layout.SavedTests[9]);
        Assert.Equal(new TestId(0, Vehicle.Cannonball, 0), layout.SavedTests[27]);
        Assert.Equal(new TestId(2, Vehicle.Birdman, 3), layout.SavedTests[56]);
        Assert.Equal(16 + (57 * 7), layout.PhotosStartBit);
        Assert.Equal(1447, layout.DataEndBit);

        // The game saves only three classes of the bonus vehicles: Birdman stage 4 is not saved.
        Assert.Equal(4, layout.TestCount(3, Vehicle.Birdman));
        Assert.False(layout.IsSaved(new TestId(3, Vehicle.Birdman, 0)));
    }

    [RealRomFact]
    public void FromGameFiles_OnRealRom_EqualsRetail()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        SaveGameLayout layout = SaveGameLayout.FromGameFiles(fs);

        Assert.Equal(SaveGameLayout.Retail.SavedTests, layout.SavedTests);
    }
}

public class SaveSlotTests
{
    private static readonly TestId HangGliderBeginner = new(0, Vehicle.HangGlider, 0);

    private static SaveSlot NewSlot()
    {
        var slot = new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail);
        slot.StartNew();
        return slot;
    }

    [Fact]
    public void NewSlot_HasNoResults()
    {
        SaveSlot slot = NewSlot();

        Assert.Equal(SaveSlotState.InUse, slot.State);
        Assert.All(SaveGameLayout.Retail.SavedTests, t => Assert.Equal(SaveSlot.NoResult, slot.GetResult(t)));
        Assert.All(slot.Photos, p => Assert.True(p.IsEmpty));
    }

    [Fact]
    public void Results_AreSevenBitValuesFromBit16()
    {
        SaveSlot slot = NewSlot();
        slot.SetResult(HangGliderBeginner, 100);

        byte[] data = slot.ToBytes();

        Assert.Equal((byte)'P', data[0]);
        Assert.Equal((byte)'W', data[1]);
        // Bits 16-22: 100; bit 23 is bit 0 of the next result (127).
        Assert.Equal(100 | 0x80, data[2]);
        Assert.Equal(SaveSlot.Checksum(data), data[255]);
    }

    [Fact]
    public void ZeroAndNoResult_StayDifferent()
    {
        SaveSlot slot = NewSlot();
        slot.SetResult(HangGliderBeginner, 0);

        var read = new SaveSlot(slot.ToBytes(), SaveGameLayout.Retail);

        Assert.Equal(0, read.GetResult(HangGliderBeginner));
        Assert.Equal(SaveSlot.NoResult, read.GetResult(new TestId(0, Vehicle.RocketBelt, 0)));
    }

    [Fact]
    public void UnchangedSlot_IsWrittenByteForByte()
    {
        var data = new byte[SaveSlot.Size];
        new Random(5).NextBytes(data);
        data[0] = (byte)'P';
        data[1] = (byte)'W';

        var slot = new SaveSlot(data, SaveGameLayout.Retail);

        Assert.Equal(data, slot.ToBytes());
    }

    [Fact]
    public void ChangedSlot_KeepsUnknownBytesAndGetsANewChecksum()
    {
        var data = new byte[SaveSlot.Size];
        new Random(7).NextBytes(data);
        data[0] = (byte)'P';
        data[1] = (byte)'W';
        var slot = new SaveSlot(data, SaveGameLayout.Retail);

        slot.SetResult(HangGliderBeginner, slot.GetResult(HangGliderBeginner) == 5 ? 6 : 5);
        byte[] written = slot.ToBytes();

        int dataEnd = (SaveGameLayout.Retail.DataEndBit + 7) / 8;
        Assert.Equal(data[dataEnd..255], written[dataEnd..255]);
        Assert.Equal(SaveSlot.Checksum(written), written[255]);
        var read = new SaveSlot(written, SaveGameLayout.Retail);
        Assert.Equal(slot.GetResult(HangGliderBeginner), read.GetResult(HangGliderBeginner));
        Assert.Equal(slot.GetResult(new TestId(2, Vehicle.Birdman, 3)), read.GetResult(new TestId(2, Vehicle.Birdman, 3)));
    }

    [Fact]
    public void Photos_AreReadAndDeleted()
    {
        SaveSlot slot = NewSlot();
        byte[] data = slot.ToBytes();
        int bit = SaveGameLayout.Retail.PhotosStartBit;
        for (int p = 0; p < 2; p++)
        {
            var record = new byte[SavePhoto.RecordBytes];
            record[1] = (byte)(10 + p);                 // x = 10 + p
            record[9] = 0x0A;                           // first object type 10 (low 4 bits of byte 9)
            record[19] = 0x40;                          // object count 1 (bits 6-8 of the word at byte 16)
            for (int j = 0; j < SaveGameLayout.PhotoBits; j++)
            {
                SaveSlot.WriteBits(data, ref bit, (record[j / 8] >> (j % 8)) & 1, 1);
            }
        }

        var read = new SaveSlot(data, SaveGameLayout.Retail);
        Assert.Equal(1, read.Photos[0].ObjectCount);
        Assert.Equal([10], read.Photos[0].ObjectTypes);
        Assert.Equal((short)11, read.Photos[1].Position.X);
        Assert.True(read.Photos[2].IsEmpty);

        read.DeletePhoto(0);

        Assert.Equal((short)11, read.Photos[0].Position.X);
        Assert.True(read.Photos[1].IsEmpty);
        Assert.Equal(6, read.Photos.Count);
        var again = new SaveSlot(read.ToBytes(), SaveGameLayout.Retail);
        Assert.Equal((short)11, again.Photos[0].Position.X);
    }

    [Fact]
    public void Erase_WritesTheGamesEmptySlot()
    {
        SaveSlot slot = NewSlot();
        slot.SetResult(HangGliderBeginner, 90);

        slot.Erase();
        byte[] data = slot.ToBytes();

        Assert.Equal(SaveSlotState.Empty, slot.State);
        Assert.Equal((byte)'p', data[0]);
        Assert.Equal((byte)'w', data[1]);
        Assert.All(data[2..], b => Assert.Equal(0, b));
    }
}

public class EepromFileTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(511)]
    [InlineData(4096)]
    public void Read_RefusesOtherSizes(int size)
    {
        Assert.Throws<InvalidDataException>(() => EepromFile.Read(new byte[size], SaveGameLayout.Retail));
    }

    [Fact]
    public void Read_KeepsTheBytesBehindTheTwoSlots()
    {
        var data = new byte[2048];
        data[0] = (byte)'P';
        data[1] = (byte)'W';
        data[256] = (byte)'p';
        data[257] = (byte)'w';
        data[1000] = 0x55;

        EepromFile file = EepromFile.Read(data, SaveGameLayout.Retail);

        Assert.Equal(SaveSlotState.InUse, file.Slots[0].State);
        Assert.Equal(SaveSlotState.Empty, file.Slots[1].State);
        Assert.Equal(data, file.ToBytes());
    }
}

public class SaveProgressTests
{
    private static SaveProgress NewProgress()
    {
        var slot = new SaveSlot(new byte[SaveSlot.Size], SaveGameLayout.Retail);
        slot.StartNew();
        return new SaveProgress(slot);
    }

    private static void SetClass(SaveProgress progress, Vehicle vehicle, int classIndex, params int[] values)
    {
        for (int t = 0; t < values.Length; t++)
        {
            progress.Slot.SetResult(new TestId(classIndex, vehicle, t), values[t]);
        }
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(55, 55)]
    [InlineData(101, 100)]
    [InlineData(126, 100)]
    public void NearestPoints_KeepsValuesInRange(int input, int expected)
    {
        Assert.Equal(expected, SaveProgress.NearestPoints(input));
    }

    [Fact]
    public void NewGame_HasOnlyBeginnerOpen()
    {
        SaveProgress progress = NewProgress();

        Assert.All(Vehicles.Main, v => Assert.True(progress.IsClassOpen(v, 0)));
        Assert.All(Vehicles.Main, v => Assert.False(progress.IsClassOpen(v, 1)));
        Assert.True(progress.CanOpenClassA);
        Assert.False(progress.IsBonusGameOpen(Vehicle.Cannonball));
        Assert.False(progress.IsBirdmanStageOpen(0));
    }

    [Fact]
    public void ClassA_NeedsABronzeMedalWithAllThreeVehicles()
    {
        SaveProgress progress = NewProgress();
        SetClass(progress, Vehicle.HangGlider, 0, 100);
        SetClass(progress, Vehicle.RocketBelt, 0, 70);
        SetClass(progress, Vehicle.Gyrocopter, 0, 69);

        Assert.False(progress.IsClassOpen(Vehicle.HangGlider, 1));

        SetClass(progress, Vehicle.Gyrocopter, 0, 70);
        Assert.True(progress.IsClassOpen(Vehicle.HangGlider, 1));
        Assert.False(progress.CanOpenClassA);
    }

    [Fact]
    public void OpenClassA_RaisesBeginnerTo70AndGivesClassAZeroPoints()
    {
        SaveProgress progress = NewProgress();
        SetClass(progress, Vehicle.HangGlider, 0, 100);

        progress.OpenClassA();

        Assert.Equal(100, progress.Slot.GetResult(new TestId(0, Vehicle.HangGlider, 0)));
        Assert.Equal(70, progress.Slot.GetResult(new TestId(0, Vehicle.RocketBelt, 0)));
        Assert.Equal(70, progress.Slot.GetResult(new TestId(0, Vehicle.Gyrocopter, 0)));
        Assert.All(Vehicles.Main, v => Assert.Equal(0, progress.Slot.GetResult(new TestId(1, v, 1))));
        Assert.All(Vehicles.Main, v => Assert.True(progress.IsClassOpen(v, 1)));
        // Class B stays closed: Class A has 0 points.
        Assert.False(progress.IsClassOpen(Vehicle.HangGlider, 2));
    }

    [Fact]
    public void OpenClassB_RaisesClassAInOrderTo140()
    {
        SaveProgress progress = NewProgress();
        progress.OpenClassA();
        SetClass(progress, Vehicle.RocketBelt, 1, 30, 90);

        Assert.True(progress.CanOpenClass(Vehicle.RocketBelt, 2));
        progress.OpenClass(Vehicle.RocketBelt, 2);

        Assert.Equal(50, progress.Slot.GetResult(new TestId(1, Vehicle.RocketBelt, 0)));
        Assert.Equal(90, progress.Slot.GetResult(new TestId(1, Vehicle.RocketBelt, 1)));
        Assert.True(progress.IsClassOpen(Vehicle.RocketBelt, 2));
        Assert.False(progress.IsClassOpen(Vehicle.HangGlider, 2));
        Assert.Equal(0, progress.Slot.GetResult(new TestId(2, Vehicle.RocketBelt, 2)));

        // Pilot: 210 in Class B, raised test by test up to 100.
        progress.OpenClass(Vehicle.RocketBelt, 3);
        Assert.Equal(100, progress.Slot.GetResult(new TestId(2, Vehicle.RocketBelt, 0)));
        Assert.Equal(100, progress.Slot.GetResult(new TestId(2, Vehicle.RocketBelt, 1)));
        Assert.Equal(10, progress.Slot.GetResult(new TestId(2, Vehicle.RocketBelt, 2)));
        Assert.True(progress.IsClassOpen(Vehicle.RocketBelt, 3));
    }

    [Fact]
    public void ClassMedal_NeedsAResultInEveryTest()
    {
        SaveProgress progress = NewProgress();
        progress.OpenClassA();
        SetClass(progress, Vehicle.HangGlider, 1, 100, 100);
        progress.Slot.SetResult(new TestId(1, Vehicle.HangGlider, 1), SaveSlot.NoResult);
        SetClass(progress, Vehicle.HangGlider, 1, 100);

        // 100 points, but the second test has no result.
        Assert.Equal(Medal.None, progress.ClassMedal(Vehicle.HangGlider, 1));
    }

    [Fact]
    public void Cannonball_SpreadsALevelOverItsTargets()
    {
        SaveProgress progress = NewProgress();

        progress.SetLevelValue(Vehicle.Cannonball, 1, 70);

        Assert.Equal(70, progress.LevelValue(Vehicle.Cannonball, 1));
        Assert.Equal(25, progress.Slot.GetResult(new TestId(1, Vehicle.Cannonball, 0)));
        Assert.Equal(20, progress.Slot.GetResult(new TestId(1, Vehicle.Cannonball, 2)));
        Assert.Equal(0, progress.Slot.GetResult(new TestId(1, Vehicle.Cannonball, 3)));
        Assert.Equal(SaveSlot.NoResult, progress.LevelValue(Vehicle.Cannonball, 0));

        progress.SetLevelValue(Vehicle.Cannonball, 1, SaveSlot.NoResult);
        Assert.Equal(SaveSlot.NoResult, progress.LevelValue(Vehicle.Cannonball, 1));
    }

    [Fact]
    public void BonusGames_OpenWithSilverMedalsAndLevelByLevel()
    {
        SaveProgress progress = NewProgress();
        progress.OpenClassA();
        Assert.True(progress.CanOpenLevel(Vehicle.Cannonball, 0));
        Assert.False(progress.CanOpenLevel(Vehicle.SkyDiving, 0));

        progress.OpenLevel(Vehicle.Cannonball, 0);

        Assert.All(Vehicles.Main, v => Assert.Equal(Medal.Silver, progress.ClassMedal(v, 1)));
        Assert.True(progress.IsLevelOpen(Vehicle.Cannonball, 0));
        Assert.Equal(0, progress.LevelValue(Vehicle.Cannonball, 0));
        Assert.False(progress.IsLevelOpen(Vehicle.Cannonball, 1));

        progress.OpenLevel(Vehicle.Cannonball, 1);
        Assert.Equal(70, progress.LevelValue(Vehicle.Cannonball, 0));
        Assert.True(progress.IsLevelOpen(Vehicle.Cannonball, 1));
        Assert.Equal(0, progress.LevelValue(Vehicle.Cannonball, 1));
    }

    [Fact]
    public void Birdman_StageOneOpensWithSilverInBeginner()
    {
        SaveProgress progress = NewProgress();
        foreach (Vehicle v in Vehicles.Main)
        {
            SetClass(progress, v, 0, 80);
        }

        Assert.True(progress.IsBirdmanStageOpen(0));
        Assert.False(progress.IsBirdmanStageOpen(1));
    }

    [Fact]
    public void OpenBirdmanStage_RaisesClassAndBonusLevels()
    {
        SaveProgress progress = NewProgress();
        Assert.True(progress.CanOpenBirdmanStage(0));
        Assert.False(progress.CanOpenBirdmanStage(1));

        progress.OpenBirdmanStage(0);
        Assert.True(progress.IsBirdmanStageOpen(0));
        Assert.All(Vehicles.Main, v => Assert.Equal(80, progress.Slot.GetResult(new TestId(0, v, 0))));

        // Stage 2 needs Class A, which is open now (80 points in Beginner are a medal).
        Assert.True(progress.CanOpenBirdmanStage(1));
        progress.OpenBirdmanStage(1);

        Assert.True(progress.IsBirdmanStageOpen(1));
        Assert.All(Vehicles.Main, v => Assert.Equal(Medal.Silver, progress.ClassMedal(v, 1)));
        Assert.Equal(80, progress.LevelValue(Vehicle.Cannonball, 2));
        Assert.Equal(5, progress.Slot.GetResult(new TestId(2, Vehicle.Cannonball, 3)));
        Assert.False(progress.IsBirdmanStageOpen(2));
    }

    [Fact]
    public void SetPerfectScore_OpensEverything()
    {
        SaveProgress progress = NewProgress();

        progress.SetPerfectScore();

        Assert.All(Vehicles.Main, v => Assert.Equal(300, progress.ClassTotal(v, 3)));
        Assert.Equal(25, progress.Slot.GetResult(new TestId(0, Vehicle.Cannonball, 3)));
        Assert.All(Vehicles.BonusGames, g => Assert.Equal(100, progress.LevelValue(g, 2)));
        Assert.All(Vehicles.BonusGames, g => Assert.True(progress.IsLevelOpen(g, 2)));
        Assert.All(Enumerable.Range(0, SaveProgress.BirdmanStages), s => Assert.True(progress.IsBirdmanStageOpen(s)));
        Assert.Equal(SaveSlot.NoResult, progress.Slot.GetResult(new TestId(0, Vehicle.Birdman, 0)));
    }
}
