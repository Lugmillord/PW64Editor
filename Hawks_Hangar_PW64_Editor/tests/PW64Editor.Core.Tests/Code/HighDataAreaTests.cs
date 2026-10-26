using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Code;

/// <summary>Stage 8: the high data area (display list, object memory, radar goals).</summary>
public class HighDataAreaTests
{
    [Fact]
    public void Layout_FitsTheArea()
    {
        Assert.Equal(0x80780000u, HighDataArea.DisplayList);
        Assert.Equal(0x807A0000u, HighDataArea.ObjectMemory);
        Assert.Equal(0x807A8000u, HighDataArea.Objects);
        Assert.Equal(0x807B9000u, HighDataArea.MatrixStack);
        Assert.True(HighDataArea.FreeStart <= HighDataArea.End);
        Assert.Equal(0u, HighDataArea.Drawn % 4);
        Assert.True(HighDataArea.ObjectMemoryLimit < HighDataArea.ObjectMemorySize);
        Assert.True(HighDataArea.ObjectMemoryLimit < 0x8000); // sltiu takes a signed 16-bit value
    }

    [RealRomFact]
    public void Patches_FitTheCleanCode()
    {
        byte[] rom = N64Rom.Load(TestRomLocator.RomPath!).Data;
        var words = new HashSet<int>();
        foreach (WordPatch patch in HighDataArea.Patches())
        {
            for (int i = 0; i < patch.Original.Length; i++)
            {
                Assert.Equal(patch.Original[i], BinaryPrimitives.ReadUInt32BigEndian(rom.AsSpan(patch.RomOffset + (i * 4))));
                Assert.True(words.Add(patch.RomOffset + (i * 4)), $"0x{patch.RomOffset + (i * 4):X} is patched twice");
            }
        }

        // None of them is changed by another part of the module's patches.
        foreach (WordPatch other in MissionCapacity.Patches(61))
        {
            Assert.DoesNotContain(other.RomOffset, words);
        }
    }

    [RealRomFact]
    public void ObjectMemory_OfTheGamesMissions()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        List<MissionComm> comms = [.. fs.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data).Comm)];

        // Measured in the emulator (mem_remaining after loading): Gyrocopter Pilot test 1 uses 3256 bytes, the most.
        MissionComm gcP1 = comms.Single(c => c.Id == new TestId(3, Vehicle.Gyrocopter, 0));
        Assert.Equal(3256, MissionObjects.MemoryUsed(gcP1));
        Assert.Equal(3256, comms.Max(MissionObjects.MemoryUsed));
        Assert.All(comms, c => Assert.Null(MissionObjects.Problem(c, HighDataArea.GameObjectMemoryLimit)));

        // 30 rings and 20 balloons do not fit the game's 5000 bytes, but the 32 KiB.
        MissionComm many = gcP1.Clone();
        many.SetObjectCount(MissionObjectKind.Ring, 30);
        many.SetObjectCount(MissionObjectKind.Balloon, 20);
        Assert.NotNull(MissionObjects.Problem(many, HighDataArea.GameObjectMemoryLimit));
        Assert.Null(MissionObjects.Problem(many));
    }
}
