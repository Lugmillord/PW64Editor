using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.SaveGame;

/// <summary>The vehicles, numbered as in the game (VehicleId in src/app/task.h).</summary>
public enum Vehicle
{
    HangGlider = 0,
    RocketBelt = 1,
    Gyrocopter = 2,
    Cannonball = 3,
    SkyDiving = 4,
    JumbleHopper = 5,
    Birdman = 6,
}

/// <summary>Names and groups of the vehicles.</summary>
public static class Vehicles
{
    /// <summary>Number of vehicles (VEHICLE_COUNT).</summary>
    public const int Count = 7;

    /// <summary>The three vehicles of the license tests, in the order of the game's tables.</summary>
    public static IReadOnlyList<Vehicle> Main { get; } = [Vehicle.HangGlider, Vehicle.RocketBelt, Vehicle.Gyrocopter];

    /// <summary>The three bonus games with points, in the order of the game's tables.</summary>
    public static IReadOnlyList<Vehicle> BonusGames { get; } = [Vehicle.Cannonball, Vehicle.SkyDiving, Vehicle.JumbleHopper];

    /// <summary>Hang Glider, Rocket Belt and Gyrocopter (IS_MAIN_VEHICLE).</summary>
    public static bool IsMain(Vehicle vehicle) => vehicle <= Vehicle.Gyrocopter;

    public static string Name(Vehicle vehicle) => vehicle switch
    {
        Vehicle.HangGlider => "Hang Glider",
        Vehicle.RocketBelt => "Rocket Belt",
        Vehicle.Gyrocopter => "Gyrocopter",
        Vehicle.Cannonball => "Cannonball",
        Vehicle.SkyDiving => "Sky Diving",
        Vehicle.JumbleHopper => "Jumble Hopper",
        Vehicle.Birdman => "Birdman",
        _ => $"Vehicle {(int)vehicle}",
    };

    /// <summary>Name of a class of the license tests (index 0-3).</summary>
    public static string ClassName(int classIndex) => classIndex switch
    {
        0 => "Beginner",
        1 => "Class A",
        2 => "Class B",
        3 => "Pilot",
        _ => $"Class {classIndex}",
    };
}

/// <summary>
/// One test of the game: class (for bonus games the level), vehicle and test number, all counting from 0.
/// For Cannonball, the "tests" of a level are its four targets.
/// </summary>
public readonly record struct TestId(int Class, Vehicle Vehicle, int Test);

/// <summary>
/// Where the results lie in a saved game. The game stores one 7-bit value per test, in this order:
/// for every vehicle, for every class (3 for the bonus vehicles, 4 for the others), for every test.
/// How many tests a class has comes from the mission files (UPWT), so a hack with other missions
/// has another layout (saveFileWrite / saveFileLoad in src/app/save.c).
/// </summary>
public sealed class SaveGameLayout
{
    /// <summary>Bit where the results start (behind the 2 magic bytes).</summary>
    public const int ResultsStartBit = 16;

    /// <summary>Bits per result.</summary>
    public const int ResultBits = 7;

    /// <summary>Photos in the album.</summary>
    public const int PhotoCount = 6;

    /// <summary>Bits per photo.</summary>
    public const int PhotoBits = 172;

    /// <summary>Bytes of a saved game that hold data; the last byte of the 256 is the checksum.</summary>
    public const int DataBytes = 255;

    /// <summary>Highest class index and test index the game's tables allow (MAX_CLASSES, MAX_TESTS).</summary>
    private const int MaxClasses = 8;
    private const int MaxTests = 5;

    private readonly int[,] _testCounts = new int[MaxClasses, Vehicles.Count];
    private readonly Dictionary<TestId, int> _index = new();

    private SaveGameLayout(IEnumerable<TestId> tasks)
    {
        var defined = new bool[MaxClasses, MaxTests, Vehicles.Count];
        foreach (TestId task in tasks)
        {
            if (task.Class is < 0 or >= MaxClasses || task.Test is < 0 or >= MaxTests || (int)task.Vehicle is < 0 or >= Vehicles.Count)
            {
                continue;
            }

            defined[task.Class, task.Test, (int)task.Vehicle] = true;
        }

        // taskGetTestCount: the number of defined tests of a class.
        for (int c = 0; c < MaxClasses; c++)
        {
            for (int v = 0; v < Vehicles.Count; v++)
            {
                for (int t = 0; t < MaxTests; t++)
                {
                    if (defined[c, t, v])
                    {
                        _testCounts[c, v]++;
                    }
                }
            }
        }

        var saved = new List<TestId>();
        for (int v = 0; v < Vehicles.Count; v++)
        {
            int classes = Vehicles.IsMain((Vehicle)v) ? 4 : 3;
            for (int c = 0; c < classes; c++)
            {
                for (int t = 0; t < _testCounts[c, v]; t++)
                {
                    var id = new TestId(c, (Vehicle)v, t);
                    _index[id] = saved.Count;
                    saved.Add(id);
                }
            }
        }

        SavedTests = saved;
        if (DataEndBit > DataBytes * 8)
        {
            throw new InvalidDataException(
                $"The missions of this ROM need {DataEndBit} bits in a saved game, but only {DataBytes * 8} fit.");
        }
    }

    /// <summary>The tests whose results are saved, in their order in the saved game.</summary>
    public IReadOnlyList<TestId> SavedTests { get; }

    /// <summary>Bit where the photo album starts.</summary>
    public int PhotosStartBit => ResultsStartBit + (SavedTests.Count * ResultBits);

    /// <summary>First bit behind the photo album; the rest up to the checksum is unused.</summary>
    public int DataEndBit => PhotosStartBit + (PhotoCount * PhotoBits);

    /// <summary>Number of tests of a class (for bonus games: of a level).</summary>
    public int TestCount(int classIndex, Vehicle vehicle) =>
        classIndex is >= 0 and < MaxClasses ? _testCounts[classIndex, (int)vehicle] : 0;

    /// <summary>Position of a test in <see cref="SavedTests"/>, or -1 if its result is not saved.</summary>
    public int IndexOf(TestId test) => _index.TryGetValue(test, out int index) ? index : -1;

    /// <summary>True if the result of the test is saved.</summary>
    public bool IsSaved(TestId test) => _index.ContainsKey(test);

    /// <summary>The layout of the retail game (US): 57 results, among them no Birdman stage 4.</summary>
    public static SaveGameLayout Retail { get; } = new(RetailTasks());

    /// <summary>Builds the layout from a list of tests, as the game builds its task table.</summary>
    public static SaveGameLayout FromTasks(IEnumerable<TestId> tasks) => new(tasks);

    /// <summary>
    /// Builds the layout from the mission files (UPWT, block COMM: class, vehicle, test) of a ROM.
    /// </summary>
    /// <exception cref="InvalidDataException">A mission file is damaged, or the results do not fit.</exception>
    public static SaveGameLayout FromGameFiles(GameFileSystem fileSystem)
    {
        var tasks = new List<TestId>();
        foreach (GameFile file in fileSystem.Files.Where(f => f.FileType == "UPWT"))
        {
            IffForm form = IffForm.Parse(file.Data);
            IffChunk? comm = form.FindChunk("COMM");
            if (comm is null || comm.DataSize < 3)
            {
                throw new InvalidDataException($"The mission file {file.GroupIndex} has no COMM block.");
            }

            byte[] data = file.Data;
            tasks.Add(new TestId(data[comm.DataOffset], (Vehicle)data[comm.DataOffset + 1], data[comm.DataOffset + 2]));
        }

        return new SaveGameLayout(tasks);
    }

    /// <summary>The 61 missions of the retail game (class, vehicle, number of tests).</summary>
    private static IEnumerable<TestId> RetailTasks()
    {
        (int Class, Vehicle Vehicle, int Tests)[] classes =
        [
            (0, Vehicle.HangGlider, 1), (1, Vehicle.HangGlider, 2), (2, Vehicle.HangGlider, 3), (3, Vehicle.HangGlider, 3),
            (0, Vehicle.RocketBelt, 1), (1, Vehicle.RocketBelt, 2), (2, Vehicle.RocketBelt, 3), (3, Vehicle.RocketBelt, 3),
            (0, Vehicle.Gyrocopter, 1), (1, Vehicle.Gyrocopter, 2), (2, Vehicle.Gyrocopter, 3), (3, Vehicle.Gyrocopter, 3),
            (0, Vehicle.Cannonball, 4), (1, Vehicle.Cannonball, 4), (2, Vehicle.Cannonball, 4),
            (0, Vehicle.SkyDiving, 1), (1, Vehicle.SkyDiving, 1), (2, Vehicle.SkyDiving, 1),
            (0, Vehicle.JumbleHopper, 1), (1, Vehicle.JumbleHopper, 1), (2, Vehicle.JumbleHopper, 1),
            (0, Vehicle.Birdman, 4), (1, Vehicle.Birdman, 4), (2, Vehicle.Birdman, 4), (3, Vehicle.Birdman, 4),
        ];
        foreach ((int c, Vehicle v, int tests) in classes)
        {
            for (int t = 0; t < tests; t++)
            {
                yield return new TestId(c, v, t);
            }
        }
    }
}
