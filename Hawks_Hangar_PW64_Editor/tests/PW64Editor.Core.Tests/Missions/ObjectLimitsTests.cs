using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

/// <summary>Stage 8: raised limits of the object lists.</summary>
public class ObjectLimitsTests
{
    private static Dictionary<MissionObjectKind, int> AllCaps() =>
        ObjectLimits.Lists.Where(l => l.CanRaise).ToDictionary(l => l.Kind, l => l.Cap);

    [Fact]
    public void Raise_InSmallSteps_UpToTheCap()
    {
        var raised = new Dictionary<string, int>();
        Assert.Equal(30, ObjectLimits.Limit(raised, MissionObjectKind.Ring));
        Assert.Equal(40, ObjectLimits.NextMaximum(raised, MissionObjectKind.Ring));
        Assert.Throws<ArgumentException>(() => ObjectLimits.Raise(raised, MissionObjectKind.Ring, 41));
        ObjectLimits.Raise(raised, MissionObjectKind.Ring, 40);
        Assert.Equal(50, ObjectLimits.NextMaximum(raised, MissionObjectKind.Ring));
        ObjectLimits.Raise(raised, MissionObjectKind.Ring, 50);
        ObjectLimits.Raise(raised, MissionObjectKind.Ring, 60);
        Assert.Equal(64, ObjectLimits.NextMaximum(raised, MissionObjectKind.Ring));
        Assert.Throws<ArgumentException>(() => ObjectLimits.Raise(raised, MissionObjectKind.Ring, 59));
        Assert.Throws<ArgumentException>(() => ObjectLimits.Raise(raised, MissionObjectKind.LandingPad, 15));
        Assert.Equal(20, ObjectLimits.NextMaximum(raised, MissionObjectKind.Thermal)); // the radar has room for 20
        Assert.Equal(new Dictionary<MissionObjectKind, int> { [MissionObjectKind.Ring] = 60 }, ObjectLimits.Raised(raised));
        Assert.Null(ObjectLimits.Limit(raised, MissionObjectKind.Photo));
    }

    [Fact]
    public void Budget_Warnings()
    {
        Assert.Equal((int)((0x800DA800u - 0x8004181Cu) + (0x801F1000u - 0x80125800u) + (0x803DA800u - 0x803905E0u) + (HighDataArea.Start - 0x80400000u)),
            IslandBudget.Heap);
        Assert.All(IslandBudget.Game, b => Assert.True(b.HeapInFlight < IslandBudget.Heap));

        MissionComm comm = MissionComm.Parse(new byte[MissionComm.Size]);
        comm.Island = 3;
        comm.SetObjectCount(MissionObjectKind.Ring, 40);
        comm.SetObjectCount(MissionObjectKind.Balloon, 20);
        Assert.Empty(MissionBudget.Warnings(comm));
        comm.SetObjectCount(MissionObjectKind.Ring, 64);
        Assert.Single(MissionBudget.Warnings(comm)); // 84 on the radar
        comm.SetObjectCount(MissionObjectKind.Target, 64);
        comm.SetObjectCount(MissionObjectKind.HoverPad, 40);
        comm.SetObjectCount(MissionObjectKind.Balloon, 40);
        Assert.Single(MissionBudget.Warnings(comm)); // 208 + 42 display objects still fit
        comm.SetObjectCount(MissionObjectKind.LandingPad, 14);
        Assert.Equal(2, MissionBudget.Warnings(comm).Count); // 222 + 42
    }

    [Fact]
    public void Lists_FitTheHighDataArea()
    {
        IReadOnlyDictionary<MissionObjectKind, uint> lists = HighDataArea.ObjectLists(AllCaps());
        Assert.Equal(HighDataArea.ObjectListsStart, lists[MissionObjectKind.Ring]);
        Assert.Equal(6, lists.Count);
        Assert.Empty(HighDataArea.ObjectLists(new Dictionary<MissionObjectKind, int>()));
        Assert.Empty(HighDataArea.ObjectListPatches(new Dictionary<MissionObjectKind, int>()));
    }

    [RealRomFact]
    public void Patches_FitTheCleanCode()
    {
        byte[] rom = N64Rom.Load(TestRomLocator.RomPath!).Data;
        var words = new HashSet<int>();
        foreach (WordPatch patch in HighDataArea.Patches().Concat(HighDataArea.ObjectListPatches(AllCaps())))
        {
            for (int i = 0; i < patch.Original.Length; i++)
            {
                Assert.Equal(patch.Original[i], BinaryPrimitives.ReadUInt32BigEndian(rom.AsSpan(patch.RomOffset + (i * 4))));
                Assert.True(words.Add(patch.RomOffset + (i * 4)), $"0x{patch.RomOffset + (i * 4):X} is patched twice");
            }
        }

        Assert.Null(HighDataArea.ReadObjectLimits(rom));
    }

    [RealRomFact]
    public void FortyRings_Build_AndImport()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Rings", false, rom, TestRomLocator.RomPath!);
        MissionInfo mission = session.LoadMissions().Missions.Single(m => m.Id == new TestId(1, Vehicle.Gyrocopter, 0));
        byte[] rings = Rings(mission.File, 40);
        GameFile original = session.CleanFileSystem.Files.Single(f => f.TableIndex == mission.TableIndex);

        // 40 rings: too many for the game's list.
        session.Project.WriteOverride(original, mission.File.WithObjectData(MissionObjectKind.Ring, rings));
        ProjectException tooMany = Assert.Throws<ProjectException>(() => session.Build(false));
        Assert.Contains("rings", tooMany.Message, StringComparison.Ordinal);

        session.Project.Settings.ObjectLimits = new Dictionary<string, int> { ["Ring"] = 40 };
        session.Project.Save();
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x28A10029u, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xAAF40))); // ringsLoad: count > 40
        DataImages images = GameModule.ReadDataImages(built, GameModule.ReadHeader(built)!)!;
        Assert.Equal(HighDataArea.ObjectListsStart, BinaryPrimitives.ReadUInt32BigEndian(images.Main.AsSpan(DataArea.RingListOffset)));
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32BigEndian(images.Main.AsSpan(DataArea.RingListOffset + 4)));

        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.Equal(new Dictionary<string, int> { ["Ring"] = 40 }, import.Settings.ObjectLimits);
        (EditorSession _, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, N64Rom.FromBytes(built), temp.Combine("imported"), "Rings", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
    }

    /// <summary>The mission's rings repeated (each copy 60 units higher) up to this count.</summary>
    internal static byte[] Rings(MissionFile mission, int count)
    {
        ReadOnlySpan<byte> rings = mission.GetObjectData(MissionObjectKind.Ring);
        int size = MissionObjects.Size(MissionObjectKind.Ring), have = rings.Length / size;
        byte[] result = new byte[count * size];
        for (int i = 0; i < count; i++)
        {
            Span<byte> ring = result.AsSpan(i * size, size);
            rings.Slice(i % have * size, size).CopyTo(ring);
            float z = BinaryPrimitives.ReadSingleBigEndian(ring[8..]) + (60 * (i / have));
            BinaryPrimitives.WriteSingleBigEndian(ring[8..], z);
            ring[0x1D] = 0; // no children (the copies would point to the originals)
            ring[0x55] = 1; // active
        }

        return result;
    }
}
