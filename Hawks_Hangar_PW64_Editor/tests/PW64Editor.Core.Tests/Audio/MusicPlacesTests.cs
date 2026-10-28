using System.Buffers.Binary;
using PW64Editor.Core.Audio;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Audio;

/// <summary>Which song plays where: the places of the game (Music tab) and the songs of a mission (Missions tab).</summary>
public class MusicPlacesTests
{
    private static readonly TestId HgA1 = new(1, Vehicle.HangGlider, 0);

    [Fact]
    public void Places_OfTheMissionsMusic()
    {
        Assert.Equal(0x04, MusicPlaces.Place(Vehicle.HangGlider, MissionMusicKind.Flight));
        Assert.Equal(0x07, MusicPlaces.Place(Vehicle.HangGlider, MissionMusicKind.Crash));
        Assert.Equal(0x0E, MusicPlaces.Place(Vehicle.Gyrocopter, MissionMusicKind.LandingMissed));
        Assert.Equal(0x16, MusicPlaces.Place(Vehicle.SkyDiving, MissionMusicKind.Crash));
        Assert.Null(MusicPlaces.Place(Vehicle.Cannonball, MissionMusicKind.Crash));
        Assert.Equal([MissionMusicKind.Flight, MissionMusicKind.LandingOk, MissionMusicKind.LandingMissed], MusicPlaces.KindsOf(Vehicle.Cannonball));
        Assert.Equal(MusicPlaces.BirdmanMissedPlace, MusicPlaces.Place(Vehicle.Birdman, MissionMusicKind.LandingMissed));

        // The Music tab's song for a place, the mission's own before it; the Birdman's "beside the target" plays its crash's.
        var places = new Dictionary<int, int> { [0x1B] = 3, [0x04] = 12 };
        Assert.Equal(3, MusicPlaces.SongAt(places, MusicPlaces.BirdmanMissedPlace));
        Assert.Equal(12, MusicPlaces.SongOf(places, null, Vehicle.HangGlider, MissionMusicKind.Flight));
        var mission = MissionSettings.For(HgA1);
        mission.Music = new() { [MissionMusicKind.Flight] = 20 };
        Assert.Equal(20, MusicPlaces.SongOf(places, mission, Vehicle.HangGlider, MissionMusicKind.Flight));
        Assert.Equal(7, MusicPlaces.SongOf(places, mission, Vehicle.HangGlider, MissionMusicKind.Crash));
        Assert.False(mission.IsEmpty);
    }

    [Fact]
    public void Tables_RoundTrip()
    {
        var places = new Dictionary<int, int> { [0] = 30, [4] = 12, [29] = 29 };
        byte[] table = MusicPlaces.CreateTable(places);
        Assert.Equal(MusicPlaces.TableSize, table.Length);
        Assert.Equal([31, 0, 0, 0, 13], table.Take(5));
        Assert.Equal(0, table[29]); // its own song
        Assert.Equal(new Dictionary<int, int> { [0] = 30, [4] = 12 }, MusicPlaces.ReadTable(table));
        Assert.Throws<InvalidDataException>(() => MusicPlaces.CreateTable(new Dictionary<int, int> { [31] = 0 }));

        byte[] bytes = MusicPlaces.MissionBytes(new Dictionary<MissionMusicKind, int> { [MissionMusicKind.Crash] = 0, [MissionMusicKind.LandingMissed] = 30 });
        Assert.Equal([0, 1, 0, 31], bytes);
        Assert.Equal(new Dictionary<MissionMusicKind, int> { [MissionMusicKind.Crash] = 0, [MissionMusicKind.LandingMissed] = 30 },
            MusicPlaces.ReadMissionBytes(bytes));
        Assert.Null(MusicPlaces.ReadMissionBytes(new byte[4]));
    }

    [RealRomFact]
    public void Loops_WarnAboutTheWrongKindOfSong()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        IReadOnlyList<bool> original = session.OriginalSongLoops();
        Assert.True(original[0x04]);  // the Hang Glider's flight loops
        Assert.False(original[0x07]); // its crash jingle does not
        Assert.Equal(original, session.SongLoops());
        Assert.Null(MusicPlaces.Problem(0x04, 0x0C, original, original));    // the Gyrocopter's flight music for the Hang Glider
        Assert.NotNull(MusicPlaces.Problem(0x04, 0x07, original, original)); // a jingle for a flight
        Assert.NotNull(MusicPlaces.Problem(0x07, 0x04, original, original)); // a looping song for a jingle
    }

    [RealRomFact]
    public void Build_WritesBothTables_AndImportTakesThemBack()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        session.SetMusicPlace(0x04, 0x0C);
        session.SetMusicPlace(0x1C, 0x1C); // its own song: no change
        Assert.Equal(new Dictionary<int, int> { [0x04] = 0x0C }, session.Project.Settings.MusicPlaces);

        MissionInfo a1 = session.LoadMissions().Missions.Single(m => m.Id == HgA1);
        var draft = new MissionDraft(a1, null);
        draft.SetMusic(MissionMusicKind.Crash, 0x1D);
        draft.SetMusic(MissionMusicKind.Flight, 0x13);
        draft.SetMusic(MissionMusicKind.Flight, null);
        Assert.Equal(0x1D, draft.MusicOf(MissionMusicKind.Crash));
        Assert.True(draft.IsChanged);
        session.SaveMission(a1, draft.Comm, draft.SettingsToSave());
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;

        foreach (int call in GameModule.MusicStartCallRomOffsets)
        {
            Assert.Equal(CodeArea.Jal(GameModuleImage.SeqNewAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(call)));
        }

        DataImages images = GameModule.ReadDataImages(built, GameModule.ReadHeader(built)!)!;
        Assert.Equal(0x0C + 1, images.Extension![DataExtension.MusicOffset + 0x04]);
        GameModuleCondition entry = GameModule.ReadConditions(built, GameModule.ReadHeader(built)!).Single(c => c.Vehicle == 0 && c.Class == 1 && c.Test == 0);
        Assert.Equal(new Dictionary<MissionMusicKind, int> { [MissionMusicKind.Crash] = 0x1D }, entry.Music);

        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        Assert.Equal(new Dictionary<int, int> { [0x04] = 0x0C }, import.Settings.MusicPlaces);
        Assert.Equal(0x1D, import.Settings.Missions.Single(m => m.Id == HgA1).Music![MissionMusicKind.Crash]);
        (EditorSession _, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, N64Rom.FromBytes(built), temp.Combine("imported"), "Music", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
    }

    private static EditorSession Session(TempDirectory temp)
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        return EditorSession.Create(temp.Combine("hack"), "Music", false, rom, TestRomLocator.RomPath!);
    }
}
