using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Code;

/// <summary>Stage 8: ten photos per flight, the photo grid with pages, "Please wait...".</summary>
public class FlightPhotosTests
{
    [Fact]
    public void Table_LiesBehindTheLargestObjectLists()
    {
        Dictionary<MissionObjectKind, int> caps = ObjectLimits.Lists.Where(l => l.CanRaise).ToDictionary(l => l.Kind, l => l.Cap);
        uint end = HighDataArea.ObjectLists(caps).Max(l => l.Value + (uint)(ObjectLimits.Of(l.Key)!.ElementSize * caps[l.Key]));
        Assert.True(end <= HighDataArea.FlightPhotoTable, $"object lists end at 0x{end:X8}");
        Assert.True(HighDataArea.FlightPhotoTable + (FlightPhotos.Count * FlightPhotos.PhotoSize) <= HighDataArea.End);
        Assert.Equal(0u, HighDataArea.FlightPhotoTable % 8);
    }

    [RealRomFact]
    public void Patches_FitTheCleanCode_AndNoOtherPatch()
    {
        byte[] rom = N64Rom.Load(TestRomLocator.RomPath!).Data;
        var others = new HashSet<int>();
        var own = FlightPhotos.Patches().Select(p => (p.RomOffset, p.Description)).ToHashSet();
        foreach (WordPatch patch in GameModule.Patches(new GameModuleOptions(true, true, true), 0x7F0000, 0x2710)
            .Where(p => !own.Contains((p.RomOffset, p.Description))))
        {
            for (int i = 0; i < patch.Original.Length; i++)
            {
                others.Add(patch.RomOffset + (i * 4));
            }
        }

        foreach (WordPatch patch in FlightPhotos.Patches())
        {
            for (int i = 0; i < patch.Original.Length; i++)
            {
                int offset = patch.RomOffset + (i * 4);
                Assert.Equal(patch.Original[i], BinaryPrimitives.ReadUInt32BigEndian(rom.AsSpan(offset)));
                Assert.False(others.Contains(offset), $"0x{offset:X} is also patched by another change");
            }
        }

        // Every reference of the old list is moved (all of them point into 0x80373390-0x8037362F).
        Assert.Equal(16, FlightPhotos.Patches().Count(p => p.Description.StartsWith("flight photos, ", StringComparison.Ordinal)
            && !p.Description.Contains("count", StringComparison.Ordinal)));
    }

    [RealRomFact]
    public void Build_HasTenPhotos_AndTheText()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Photos", false, rom, TestRomLocator.RomPath!);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;

        Assert.Equal(0x28A1000Au, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xBFFB8))); // snapPhoto: count < 10
        Assert.Equal(0x240F000Au, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xC6B50))); // snapGetPhotoCount
        Assert.Equal(CodeArea.Jal(GameModuleImage.PhotoGridAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xC5B30)));

        // "Please wait..." in the module's header (+0x180), one line, ending with 0xFFFF.
        int module = GameModule.ReadHeader(built)!.RomOffset;
        var text = new List<ushort>();
        for (int i = 0; i < 24; i++)
        {
            ushort code = BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(module + 0x180 + (i * 2)));
            text.Add(code);
            if (code == 0xFFFF)
            {
                break;
            }
        }

        Assert.Equal(ProjectBuilder.BusyText.Length + 1, text.Count);
        Assert.DoesNotContain((ushort)0x0FFE, text);
    }
}
