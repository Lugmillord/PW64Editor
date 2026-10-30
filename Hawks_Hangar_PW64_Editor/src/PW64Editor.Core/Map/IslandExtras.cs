using System.Numerics;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Map;

/// <summary>What a code-placed object of an island is.</summary>
public enum IslandExtraKind
{
    /// <summary>A model that goes round a circle (boats, gliders, whales, the ferry, Missi).</summary>
    Mover,

    /// <summary>A model at a fixed place (the fountain, the shuttle, the oil spray).</summary>
    Fixed,

    /// <summary>An invisible sphere that does something when the player enters it.</summary>
    Sphere,

    /// <summary>An invisible box (fuel stations, places without snow).</summary>
    Box,

    /// <summary>Models on a spline path (the planes, the ski lift's chairs).</summary>
    Path,

    /// <summary>A fire: an animated flame drawn as a picture that always faces the camera.</summary>
    Fire,
}

/// <summary>
/// An object the game places in its code rather than in a file: animated models and invisible trigger zones. Read only:
/// changing them means changing the game's code.
/// </summary>
/// <param name="Island">The island number (0 Holiday … 3 Ever-Frost).</param>
/// <param name="Name">The name (a text key).</param>
/// <param name="Note">What it does (a text key).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Position">The centre of its circle, its place, or the centre of its zone.</param>
/// <param name="Model">The model (null for invisible zones).</param>
/// <param name="Radius">The radius of its circle or sphere.</param>
/// <param name="Speed">Degrees per second on its circle (positive: counter-clockwise).</param>
/// <param name="StartAngle">Where on the circle it starts (degrees).</param>
/// <param name="Heading">Heading of a fixed model or a box (degrees).</param>
/// <param name="Roll">Banking of a mover about its own forward axis (degrees).</param>
/// <param name="Size">The size of a box.</param>
/// <param name="Source">Where in the decompilation's code it is.</param>
/// <param name="Area">0 for the island, 1 for its sub-area (cave).</param>
/// <param name="PathFile">The user file of a spline path (SPTH), or −1.</param>
/// <param name="Count">How many models share the path (the ski lift's chairs).</param>
/// <param name="MinHeight">The lowest height on the path (planes).</param>
public sealed record IslandExtra(int Island, string Name, string Note, IslandExtraKind Kind, Vector3 Position, int? Model = null,
    float Radius = 0, float Speed = 0, float StartAngle = 0, float Heading = 0, float Roll = 0, Vector3 Size = default, string Source = "",
    int Area = 0, int PathFile = -1, int Count = 1, float MinHeight = float.MinValue)
{
    /// <summary>Where a mover starts (on its circle at the start angle) or where a fixed thing stands.</summary>
    public Vector3 StartPosition => Kind == IslandExtraKind.Mover
        ? Position + new Vector3(MathF.Cos(StartAngle * MathF.PI / 180), MathF.Sin(StartAngle * MathF.PI / 180), 0) * Radius
        : Position;

    /// <summary>The heading of the model at its start: a mover faces along its circle.</summary>
    public float StartHeading => Kind == IslandExtraKind.Mover ? StartAngle + (Speed < 0 ? 180 : 0) : Heading;

    /// <summary>
    /// All code-placed objects. From boats.c, glider_toys.c, whale.c, whale_pod.c, fountain.c, shuttle.c, ferry.c, missi.c,
    /// oil.c, environment.c, code_66F70.c and snow.c of the decompilation (https://github.com/gcsmith/Pilotwings64Decomp,
    /// MIT license). The gliders' models depend on the chosen pilot (the player's own pilot is left out); shown are the
    /// ones of a game with Lark. The two planes of Little States and Ever-Frost's ski lift follow paths of their own
    /// files (SPTH user files 109, 110 and 4; planes.c, ski_lift.c); the fires come from fire_effects.c and oil_plant.c.
    /// </summary>
    public static IReadOnlyList<IslandExtra> All { get; } =
    [
        // Holiday Island
        Circle(0, CoreText.K("Speedboat"), 0x03, new(-600, -600, 0), 300, -4, roll: -5, source: "boats.c"),
        Circle(0, CoreText.K("Sailboat"), 0x01, new(700, -500, 0), 300, 2, roll: 15, source: "boats.c"),
        Circle(0, CoreText.K("Hang glider"), 0x10B, new(-66, 320, 125), 80, 20, roll: -15, source: "glider_toys.c"),
        Circle(0, CoreText.K("Hang glider"), 0x10C, new(-66, 320, 135), 70, 18, roll: -15, source: "glider_toys.c"),
        Circle(0, CoreText.K("Hang glider"), 0x10D, new(-70, 320, 155), 90, 18, roll: -15, source: "glider_toys.c"),
        new(0, CoreText.K("Night switch"), CoreText.K("Flying into it turns the day into night (once per flight)."), IslandExtraKind.Sphere,
            new(-82, 250, 51), Radius: 6, Source: "environment.c"),

        // Crescent Island
        Circle(1, CoreText.K("Speedboat"), 0x03, new(400, -300, 0), 400, -4, roll: -5, source: "boats.c"),
        Circle(1, CoreText.K("Yacht"), 0x29, new(300, -200, 0), 275, 2, source: "boats.c"),
        Circle(1, CoreText.K("Hang glider"), 0x10B, new(-891.24f, 602.16f, 450), 220, 10, roll: -15, source: "glider_toys.c"),
        Circle(1, CoreText.K("Hang glider"), 0x10C, new(1100.06f, 686.22f, 250), 70, 18, roll: -15, source: "glider_toys.c"),
        Circle(1, CoreText.K("Hang glider"), 0x10D, new(1050.06f, 686.22f, 265), 90, 18, roll: -15, source: "glider_toys.c"),
        Circle(1, CoreText.K("Whale"), 0xD7, new(750, 100, 4.5f), 75, 10, source: "whale.c"),
        new(1, CoreText.K("Fountain"), CoreText.K("Grows out of the water when the player comes near."), IslandExtraKind.Fixed,
            new(-288, -99, 5.75f), 0x26, Source: "fountain.c"),

        // Little States
        Circle(2, CoreText.K("Hang glider"), 0x10B, new(1666.32f, -1099.06f, 100), 30, 17, roll: -15, source: "glider_toys.c"),
        Circle(2, CoreText.K("Hang glider"), 0x10C, new(3293.09f, 931.19f, 150), 60, 13, roll: -15, source: "glider_toys.c"),
        Circle(2, CoreText.K("Hang glider"), 0x10D, new(-2294.23f, -791.48f, 150), 30, 18, roll: -15, source: "glider_toys.c"),
        Circle(2, CoreText.K("Hang glider"), 0x10E, new(-2290.23f, -791.48f, 170), 50, 18, roll: -15, source: "glider_toys.c"),
        Circle(2, CoreText.K("Ferry"), 0x52, new(1150, -2150, 8.8f), 200, 5, source: "ferry.c"),
        Circle(2, CoreText.K("Missi"), 0x58, new(1725, -659, 28), 20, 7, startAngle: 180, source: "missi.c"),
        new(2, CoreText.K("Space shuttle"), CoreText.K("Takes off when the player comes near and disappears high above."), IslandExtraKind.Fixed,
            new(2870, -2230, 57.51f), 0x56, Heading: 180, Source: "shuttle.c"),
        FuelStation(new(1972.1f, -1105.6f, 32), 10.83f),
        FuelStation(new(1954.9f, -1020, 32), 10.83f),
        FuelStation(new(2067.3f, -977.9f, 42), -55.8f),

        // Ever-Frost Island
        Circle(3, CoreText.K("Hang glider"), 0x10B, new(80.03f, -162.16f, 600), 250, 7, roll: -15, source: "glider_toys.c"),
        Circle(3, CoreText.K("Hang glider"), 0x10C, new(745.26f, 1107.29f, 150), 70, 17, roll: -15, source: "glider_toys.c"),
        Circle(3, CoreText.K("Hang glider"), 0x10D, new(800.26f, 1107.29f, 170), 100, 17, roll: -15, source: "glider_toys.c"),
        Circle(3, CoreText.K("Whale"), 0xD7, new(1156.66f, 1770.82f, 0), 195, 10, startAngle: 10, source: "whale_pod.c"),
        Circle(3, CoreText.K("Whale"), 0xD7, new(1156.66f, 1770.82f, 0), 220, 10, startAngle: 20, source: "whale_pod.c"),
        Circle(3, CoreText.K("Whale"), 0xD7, new(1156.66f, 1770.82f, 0), 230, 10, startAngle: 50, source: "whale_pod.c"),
        Circle(3, CoreText.K("Whale"), 0xD7, new(1156.66f, 1770.82f, 0), 180, 10, startAngle: 85, source: "whale_pod.c"),
        Circle(3, CoreText.K("Whale"), 0xD7, new(1156.66f, 1770.82f, 0), 210, 10, startAngle: 110, source: "whale_pod.c"),
        new(3, CoreText.K("Oil spray"), CoreText.K("Grows out of the ground when the player comes near."), IslandExtraKind.Fixed,
            new(-73.8f, 575.7f, 88.3f), 0xA6, Source: "oil.c"),
        NoSnow(new(310, 34, 151), new(350.6f, 62.64f, 167)),
        NoSnow(new(240, 123, 121.47f), new(344.58f, 154.2f, 159.3f)),

        // Spline paths (planes.c, ski_lift.c): the planes fly a loop in 120 s, the 20 chairs start at t = 5 × i.
        new(2, CoreText.K("Plane"), CoreText.K("Flies along its path, a loop in 120 seconds."), IslandExtraKind.Path,
            new(-1225, 454, 161.4f), 0x27, Source: "planes.c", PathFile: 109, MinHeight: 42.43231f),
        new(2, CoreText.K("Plane"), CoreText.K("Flies along its path, a loop in 120 seconds."), IslandExtraKind.Path,
            new(-211.85f, 884.5f, 224.3f), 0x1B, Source: "planes.c", PathFile: 110, MinHeight: 33.757683f),
        new(3, CoreText.K("Ski lift"), CoreText.K("20 chairs go up and down along the path while the player is near."), IslandExtraKind.Path,
            new(-207.927f, -897.804f, 112.643f), 0xA7, Source: "ski_lift.c", PathFile: 4, Count: 20),

        // Fires (fire_effects.c, oil_plant.c): flames that always face the camera, width × height in world units.
        Fire(1, 0, new(-1329.05f, 1232.9f, 6f), 5f, 5f, "fire_effects.c:34"),
        Fire(1, 0, new(-837f, 715.2f, 280.57f), 2f, 2f, "fire_effects.c:35"),
        Fire(1, 0, new(-770.56f, 736.44f, 264.73f), 2f, 2f, "fire_effects.c:36"),
        Fire(1, 0, new(-733.55f, 767.73f, 251.62f), 2f, 2f, "fire_effects.c:37"),
        Fire(1, 0, new(-724.08f, -692.91f, 288.29f), 2f, 2f, "fire_effects.c:38"),
        Fire(1, 0, new(-713.94f, -674.26f, 288.08f), 2f, 2f, "fire_effects.c:39"),
        Fire(1, 0, new(-733.11f, -661f, 290.84f), 2f, 2f, "fire_effects.c:40"),
        Fire(1, 0, new(-746.82f, -672.43f, 292.02f), 2f, 2f, "fire_effects.c:41"),
        Fire(1, 1, new(-837f, 715.2f, 280.57f), 2f, 2f, "fire_effects.c:44"),
        Fire(1, 1, new(-770.56f, 736.44f, 264.73f), 2f, 2f, "fire_effects.c:45"),
        Fire(1, 1, new(-733.55f, 767.73f, 251.62f), 2f, 2f, "fire_effects.c:46"),
        Fire(1, 1, new(-642.13f, 746.5f, 242.78f), 2f, 2f, "fire_effects.c:47"),
        Fire(1, 1, new(-617.17f, 772.03f, 239.67f), 2f, 2f, "fire_effects.c:48"),
        Fire(1, 1, new(-628.55f, 873.54f, 236.18f), 2f, 2f, "fire_effects.c:49"),
        Fire(1, 1, new(-610.74f, 865.28f, 256.96f), 2f, 2f, "fire_effects.c:50"),
        Fire(1, 1, new(-598.96f, 857.81f, 257.23f), 2f, 2f, "fire_effects.c:51"),
        Fire(1, 1, new(-580.66f, 844.82f, 215.96f), 2f, 2f, "fire_effects.c:52"),
        Fire(1, 1, new(-638.68f, 930.47f, 225.59f), 2f, 2f, "fire_effects.c:53"),
        Fire(1, 1, new(-622.2f, 960.48f, 221.53f), 2f, 2f, "fire_effects.c:54"),
        Fire(1, 1, new(-591.5f, 960.13f, 218.54f), 2f, 2f, "fire_effects.c:55"),
        Fire(1, 1, new(-575.53f, 930.53f, 217.59f), 2f, 2f, "fire_effects.c:56"),
        Fire(1, 1, new(-582.33f, 888.75f, 216.78f), 2f, 2f, "fire_effects.c:57"),
        Fire(1, 1, new(-297.95f, 942.72f, 115.58f), 2f, 2f, "fire_effects.c:58"),
        Fire(1, 1, new(-320.42f, 938.52f, 114.73f), 2f, 2f, "fire_effects.c:59"),
        Fire(1, 1, new(-286.23f, 855.1f, 76.46f), 2f, 2f, "fire_effects.c:60"),
        Fire(1, 1, new(-299.11f, 846.29f, 77.05f), 2f, 2f, "fire_effects.c:61"),
        Fire(1, 1, new(-295.22f, 839.95f, 76.8f), 2f, 2f, "fire_effects.c:62"),
        Fire(1, 1, new(-281.23f, 848.97f, 76.37f), 2f, 2f, "fire_effects.c:63"),
        Fire(1, 1, new(-234.07f, 785.09f, 73.3f), 2f, 2f, "fire_effects.c:64"),
        Fire(1, 1, new(-223.94f, 803.74f, 73.08f), 2f, 2f, "fire_effects.c:65"),
        Fire(1, 1, new(-243.11f, 817f, 75.84f), 2f, 2f, "fire_effects.c:66"),
        Fire(1, 1, new(-256.82f, 805.57f, 77.02f), 2f, 2f, "fire_effects.c:67"),
        Fire(1, 1, new(-504.93f, 872.51f, 215.89f), 4f, 4f, "fire_effects.c:68"),
        Fire(1, 1, new(-500.84f, 912.06f, 214.71f), 4f, 4f, "fire_effects.c:69"),
        Fire(1, 1, new(-478.91f, 971.75f, 214.64f), 4f, 4f, "fire_effects.c:70"),
        Fire(1, 1, new(-440.64f, 1002.58f, 217.07f), 4f, 4f, "fire_effects.c:71"),
        Fire(1, 1, new(-361.85f, 1012.32f, 215.73f), 4f, 4f, "fire_effects.c:72"),
        Fire(1, 1, new(-313.42f, 983.83f, 215.65f), 4f, 4f, "fire_effects.c:73"),
        Fire(1, 1, new(-294.02f, 943.14f, 217.08f), 4f, 4f, "fire_effects.c:74"),
        Fire(1, 1, new(-294.24f, 903.16f, 217.11f), 4f, 4f, "fire_effects.c:75"),
        Fire(1, 1, new(-332.27f, 865.19f, 213.61f), 4f, 4f, "fire_effects.c:76"),
        Fire(1, 1, new(-370.13f, 856.82f, 214.84f), 4f, 4f, "fire_effects.c:77"),
        Fire(1, 1, new(-413.83f, 850.75f, 214.1f), 4f, 4f, "fire_effects.c:78"),
        Fire(1, 1, new(-448.45f, 802.51f, 213.5f), 4f, 4f, "fire_effects.c:79"),
        Fire(1, 1, new(-496f, 797.84f, 213.02f), 4f, 4f, "fire_effects.c:80"),
        Fire(2, 0, new(2960.13f, -1086.35f, 90.9f), 10f, 20f, "fire_effects.c:83"),
        Fire(2, 0, new(3337.07f, -548.42f, 90.9f), 10f, 20f, "fire_effects.c:84"),
        Fire(2, 0, new(1955.46f, -2033.42f, 90.9f), 10f, 20f, "fire_effects.c:85"),
        Fire(2, 0, new(3305.1f, 744f, 81f), 4f, 8f, "fire_effects.c:86"),
        Fire(3, 0, new(-888f, -1935.05f, 90.9f), 10f, 20f, "fire_effects.c:89"),
        Fire(3, 0, new(-871.63f, 1427.31f, 90.9f), 10f, 20f, "fire_effects.c:90"),
        Fire(3, 0, new(-368f, 648f, 106f), 25f, 35f, "oil_plant.c:78 (visible only within 500u, else scale 0)"),
    ];

    /// <summary>The code-placed objects of an island or of its sub-area.</summary>
    public static IEnumerable<IslandExtra> Of(int island, int area = 0) => All.Where(e => e.Island == island && e.Area == area);

    private static IslandExtra Circle(int island, string name, int model, Vector3 center, float radius, float speed, float roll = 0,
        float startAngle = 0, string source = "") =>
        new(island, name, CoreText.K("Goes round the circle while the player is near."), IslandExtraKind.Mover, center, model, radius, speed,
            startAngle, Roll: roll, Source: source);

    private static IslandExtra FuelStation(Vector3 center, float heading) =>
        new(2, CoreText.K("Fuel station"), CoreText.K("Fills up the fuel of the Gyrocopter (class B) and the Rocket Belt (class A)."),
            IslandExtraKind.Box, center, Heading: heading, Size: new Vector3(20, 20, 4), Source: "code_66F70.c");

    private static IslandExtra Fire(int island, int area, Vector3 bottom, float width, float height, string source) =>
        new(island, CoreText.K("Fire"), CoreText.K("An animated flame (16 pictures, 20 per second) that always faces the camera."),
            IslandExtraKind.Fire, bottom, Size: new Vector3(width, width, height), Source: source, Area: area);

    private static IslandExtra NoSnow(Vector3 min, Vector3 max) =>
        new(3, CoreText.K("Place without snow"), CoreText.K("While the camera is inside, no snow falls."), IslandExtraKind.Box,
            (min + max) / 2, Size: max - min, Source: "snow.c");
}
