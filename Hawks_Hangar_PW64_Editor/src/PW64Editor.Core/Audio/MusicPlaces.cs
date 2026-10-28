using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Audio;

/// <summary>The four kinds of music a mission can have songs of its own for.</summary>
public enum MissionMusicKind
{
    /// <summary>The music while flying.</summary>
    Flight = 0,

    /// <summary>The jingle of a crash.</summary>
    Crash = 1,

    /// <summary>The jingle of a good landing (Cannonball: hit, Jumble Hopper: goal).</summary>
    LandingOk = 2,

    /// <summary>The jingle of a missed landing (Cannonball: miss; Jumble Hopper: the hard end time's).</summary>
    LandingMissed = 3,
}

/// <summary>
/// Which song plays where. The game has a music number for each place (enum Music of snd.h: the title, the menus, the
/// flight, landings and crashes of each vehicle, the results, ...); every place plays the song of its own number. The
/// game module plays another song at a place (project: <see cref="Project.ProjectSettings.MusicPlaces"/>, a table in
/// the lower data area) and, per mission, other songs for the flight and its jingles (<see cref="Missions.MissionSettings.Music"/>,
/// the mission's entry of the data area).
/// </summary>
/// <remarks>
/// The game keeps the number of the place (gCurrentMusicId) and compares it ("is the results music playing?"), so the
/// module changes only the song that is started: both calls of uvaSeqNew in sndSetMusicState go through it. Names from
/// the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
/// </remarks>
public static class MusicPlaces
{
    /// <summary>Number of places (and of songs).</summary>
    public const int Count = 31;

    /// <summary>
    /// The pseudo place the module uses for the Birdman's landing beside the target (and its hard end jingle "landing
    /// missed"): the game has no number of its own for it (it plays the crash jingle), the module plays the crash's song
    /// unless the mission has a song for it.
    /// </summary>
    public const int BirdmanMissedPlace = 0x1F;

    /// <summary>Size of the table in the lower data area (a byte per place: song + 1, 0 = its own).</summary>
    public const int TableSize = 0x20;

    /// <summary>English name of a place (the original song's name, <see cref="MusicCatalog.SongNames"/>).</summary>
    public static string PlaceName(int place) => MusicCatalog.SongNames[place];

    /// <summary>The place (music number) of a mission's music, or null if the vehicle has none of that kind.</summary>
    public static int? Place(Vehicle vehicle, MissionMusicKind kind) => (vehicle, kind) switch
    {
        (Vehicle.HangGlider, _) => 0x04 + MissionOrder(kind),
        (Vehicle.RocketBelt, _) => 0x08 + MissionOrder(kind),
        (Vehicle.Gyrocopter, _) => 0x0C + MissionOrder(kind),
        (Vehicle.Cannonball, MissionMusicKind.Flight) => 0x10,
        (Vehicle.Cannonball, MissionMusicKind.LandingOk) => 0x11,
        (Vehicle.Cannonball, MissionMusicKind.LandingMissed) => 0x12,
        (Vehicle.SkyDiving, _) => 0x13 + MissionOrder(kind),
        (Vehicle.JumbleHopper, MissionMusicKind.Flight) => 0x17,
        (Vehicle.JumbleHopper, MissionMusicKind.LandingOk) => 0x18,
        (Vehicle.JumbleHopper, MissionMusicKind.LandingMissed) => 0x0A, // its hard end jingle: the Rocket Belt's
        (Vehicle.Birdman, MissionMusicKind.Flight) => 0x19,
        (Vehicle.Birdman, MissionMusicKind.LandingOk) => 0x1A,
        (Vehicle.Birdman, MissionMusicKind.Crash) => 0x1B,
        (Vehicle.Birdman, MissionMusicKind.LandingMissed) => BirdmanMissedPlace,
        _ => null,
    };

    /// <summary>Flight, good landing, missed landing, crash: the order of the numbers of the main vehicles and Sky Diving.</summary>
    private static int MissionOrder(MissionMusicKind kind) => kind switch
    {
        MissionMusicKind.Flight => 0,
        MissionMusicKind.LandingOk => 1,
        MissionMusicKind.LandingMissed => 2,
        _ => 3,
    };

    /// <summary>The kinds of music a mission of a vehicle has, in the order they are shown.</summary>
    public static IReadOnlyList<MissionMusicKind> KindsOf(Vehicle vehicle) =>
        [.. new[] { MissionMusicKind.Flight, MissionMusicKind.Crash, MissionMusicKind.LandingOk, MissionMusicKind.LandingMissed }
            .Where(k => Place(vehicle, k) is not null)];

    /// <summary>English label of a mission's music kind for a vehicle.</summary>
    public static string KindName(Vehicle vehicle, MissionMusicKind kind) => (vehicle, kind) switch
    {
        (_, MissionMusicKind.Flight) => CoreText.K("Flight"),
        (_, MissionMusicKind.Crash) => CoreText.K("Crash"),
        (Vehicle.Cannonball, MissionMusicKind.LandingOk) => CoreText.K("Hit"),
        (Vehicle.Cannonball, MissionMusicKind.LandingMissed) => CoreText.K("Miss"),
        (Vehicle.JumbleHopper, MissionMusicKind.LandingOk) => CoreText.K("Goal"),
        (Vehicle.JumbleHopper, MissionMusicKind.LandingMissed) => CoreText.K("Time is up (jingle)"),
        (Vehicle.Birdman, MissionMusicKind.LandingOk) => CoreText.K("Landing on a pad"),
        (Vehicle.Birdman, MissionMusicKind.LandingMissed) => CoreText.K("Landing beside the target"),
        (_, MissionMusicKind.LandingOk) => CoreText.K("Good landing"),
        _ => CoreText.K("Missed landing"),
    };

    /// <summary>The song a place plays with the project's table (its own if unchanged); the pseudo place plays the crash's.</summary>
    public static int SongAt(IReadOnlyDictionary<int, int> places, int place)
    {
        int real = place == BirdmanMissedPlace ? 0x1B : place;
        return places.TryGetValue(real, out int song) ? song : real;
    }

    /// <summary>The song a mission plays for a kind: its own, or the place's (<see cref="SongAt"/>).</summary>
    public static int? SongOf(IReadOnlyDictionary<int, int> places, Missions.MissionSettings? mission, Vehicle vehicle, MissionMusicKind kind) =>
        Place(vehicle, kind) is not { } place ? null
        : mission?.Music is { } own && own.TryGetValue(kind, out int song) ? song : SongAt(places, place);

    /// <summary>What is wrong with a song at a place, or null: a song without a loop for a place whose original loops (it
    /// ends after a while, then it is silent), or a looping one for a jingle (it plays until the next music).</summary>
    /// <param name="place">The place (the pseudo place counts as the Birdman's crash).</param>
    /// <param name="song">The song.</param>
    /// <param name="loops">Per song: true if it loops (as it is in the project); index = song number.</param>
    /// <param name="originalLoops">Per song: true if the original song loops.</param>
    public static string? Problem(int place, int song, IReadOnlyList<bool> loops, IReadOnlyList<bool> originalLoops)
    {
        int real = place == BirdmanMissedPlace ? 0x1B : place;
        if (real >= originalLoops.Count || song >= loops.Count || loops[song] == originalLoops[real])
        {
            return null;
        }

        return originalLoops[real]
            ? CoreText.T("This song has no loop: it ends after a while, then there is no music here.")
            : CoreText.T("This song loops, but here a jingle plays once: it plays on until the next music starts.");
    }

    /// <summary>The table of the lower data area (+0x48): a byte per place, song + 1, 0 = the place's own song.</summary>
    /// <exception cref="InvalidDataException">A place or song does not exist.</exception>
    public static byte[] CreateTable(IReadOnlyDictionary<int, int> places)
    {
        var table = new byte[TableSize];
        foreach ((int place, int song) in places)
        {
            if (place is < 0 or >= Count || song is < 0 or >= Count)
            {
                throw new InvalidDataException(CoreText.F("Song {0} at place {1} does not exist.", song, place));
            }

            table[place] = place == song ? (byte)0 : (byte)(song + 1);
        }

        return table;
    }

    /// <summary>The places of a table that play another song.</summary>
    public static Dictionary<int, int> ReadTable(ReadOnlySpan<byte> table)
    {
        var places = new Dictionary<int, int>();
        for (int place = 0; place < Count && place < table.Length; place++)
        {
            if (table[place] is > 0 and <= Count && table[place] - 1 != place)
            {
                places[place] = table[place] - 1;
            }
        }

        return places;
    }

    /// <summary>A mission's songs for its entry of the data area (+0x9C, by kind: song + 1, 0 = the place's).</summary>
    public static byte[] MissionBytes(IReadOnlyDictionary<MissionMusicKind, int>? music)
    {
        var bytes = new byte[4];
        foreach ((MissionMusicKind kind, int song) in music ?? new Dictionary<MissionMusicKind, int>())
        {
            if (song is >= 0 and < Count)
            {
                bytes[(int)kind] = (byte)(song + 1);
            }
        }

        return bytes;
    }

    /// <summary>A mission's songs from its entry bytes, or null for none.</summary>
    public static Dictionary<MissionMusicKind, int>? ReadMissionBytes(ReadOnlySpan<byte> bytes)
    {
        var music = new Dictionary<MissionMusicKind, int>();
        for (int i = 0; i < 4 && i < bytes.Length; i++)
        {
            if (bytes[i] is > 0 and <= Count)
            {
                music[(MissionMusicKind)i] = bytes[i] - 1;
            }
        }

        return music.Count > 0 ? music : null;
    }
}
