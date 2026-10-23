using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Classes;

/// <summary>
/// The medal limits of the classes (bronze, silver, gold: points of the whole class) and the class table the game module
/// reads (data area, <see cref="TableAddress"/>).
/// </summary>
/// <remarks>
/// <para>The game has one table for eight entries (gMedalPointRequirements, ROM 0xD7104): the license classes Beginner …
/// Pilot (shared by the three license vehicles), three equal entries for the bonus games (every level 70/80/90) and the
/// Birdman's (0/0/0, 280/320/360 with the game patch "Bonus games and Birdman"). The game reads it in eleven places and
/// picks the bonus entry in five different ways, so limits per vehicle and class need the module: with a class table the
/// level select (func_8030D9C8, func_8030DED0), the file menu (fileMenu_802E8AF0), the points under the menu fields
/// (func_8030F818), "points to the next medal" (levelSetPointsToNextMedal) and the ending (func_8032C3C4) go through
/// it (see Menu_Unlocks.md).</para>
/// <para>Table (lower data area, <see cref="DataExtension"/>): u16 limits[7 vehicles][8 classes][3] (bronze, silver, gold),
/// then u8 flags[7][8]: 1 own limits, 2 own medal texts on the total screen (<see cref="ClassTexts"/>). Until Step 40 it
/// had [7][4] and lay at 0x801FDC00 in the data area. Entries without own limits hold the original
/// ones; a Birdman stage with points and without own limits keeps 70/80/90 per mission with points (computed by the
/// module).</para>
/// <para>Names of the game's functions and tables from the decompilation
/// (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class ClassLimits
{
    /// <summary>RAM address of the class table (lower data area).</summary>
    public const uint TableAddress = DataExtension.ClassTableAddress;

    /// <summary>Size of the class table in bytes.</summary>
    public const int TableSize = (Vehicles * Classes * 3 * 2) + (Vehicles * Classes);

    /// <summary>Offset of the flags in the table.</summary>
    public const int FlagsOffset = Vehicles * Classes * 3 * 2;

    /// <summary>Flag: the class has own limits.</summary>
    public const byte FlagOwnLimits = 1;

    /// <summary>Flag: the class has own medal texts.</summary>
    public const byte FlagOwnTexts = 2;

    /// <summary>Flag: the class has own texts for the perfect score too (another number of tests than in the game).</summary>
    public const byte FlagAllTexts = 4;

    private const int Vehicles = 7;

    private const int Classes = DataExtension.Classes;

    /// <summary>Size of a class table of layout version 1 ([7][4]).</summary>
    public const int Version1TableSize = (Vehicles * 4 * 3 * 2) + (Vehicles * 4);

    /// <summary>Highest limit the table can hold.</summary>
    public const int MaxLimit = 9999;

    /// <summary>The original limits of the license classes (gMedalPointRequirements entries 0-3).</summary>
    private static readonly int[][] LicenseLimits = [[70, 80, 90], [140, 160, 180], [210, 240, 270], [210, 240, 270]];

    /// <summary>Points per medal and mission with points of a Birdman stage without own limits (70/80/90).</summary>
    public static IReadOnlyList<int> BirdmanPerMission { get; } = [70, 80, 90];

    /// <summary>All classes in the order of the Classes tab: per vehicle (game order) its classes, levels or stages.</summary>
    public static IReadOnlyList<(Vehicle Vehicle, int Class)> All { get; } =
    [
        .. Enum.GetValues<Vehicle>().SelectMany(v => Enumerable.Range(0, ClassCount(v)).Select(c => (v, c))),
    ];

    /// <summary>Classes of a vehicle: 4 for the license vehicles and the Birdman, 3 levels for the bonus games.</summary>
    public static int ClassCount(Vehicle vehicle) => vehicle is Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper ? 3 : 4;

    /// <summary>The original limits of a class.</summary>
    /// <param name="vehicle">The vehicle.</param>
    /// <param name="classIndex">Class, level or stage.</param>
    /// <param name="bonusPatch">True if the game patch "Bonus games and Birdman" is applied (Birdman 280/320/360).</param>
    public static IReadOnlyList<int> Original(Vehicle vehicle, int classIndex, bool bonusPatch) => vehicle switch
    {
        Vehicle.HangGlider or Vehicle.RocketBelt or Vehicle.Gyrocopter => LicenseLimits[Math.Clamp(classIndex, 0, 3)],
        Vehicle.Birdman => bonusPatch ? [280, 320, 360] : [0, 0, 0],
        _ => [70, 80, 90],
    };

    /// <summary>The automatic limits of a Birdman stage with this many missions with points (70/80/90 each).</summary>
    public static IReadOnlyList<int> BirdmanAutomatic(int missionsWithPoints) => [.. BirdmanPerMission.Select(l => l * missionsWithPoints)];

    /// <summary>
    /// The limits that count: the class's own, a Birdman stage's automatic ones (with missions with points), or the
    /// original ones.
    /// </summary>
    public static IReadOnlyList<int> Effective(ClassSettings? settings, Vehicle vehicle, int classIndex, bool bonusPatch, int birdmanPointMissions) =>
        settings?.Medals is { Count: 3 } own ? own
        : vehicle == Vehicle.Birdman && bonusPatch && birdmanPointMissions > 0 ? BirdmanAutomatic(birdmanPointMissions)
        : Original(vehicle, classIndex, bonusPatch);

    /// <summary>
    /// Points of a perfect score: 100 per test (a bonus game level 100; a Birdman stage with points 100 per mission with
    /// points).
    /// </summary>
    public static int Perfect(Vehicle vehicle, int testCount, int birdmanPointMissions) => vehicle switch
    {
        Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper => 100,
        Vehicle.Birdman when birdmanPointMissions > 0 => 100 * birdmanPointMissions,
        _ => 100 * testCount,
    };

    /// <summary>What is wrong with limits, or null if they can be used.</summary>
    /// <param name="medals">Bronze, silver, gold.</param>
    /// <param name="perfect">Points of a perfect score of the class.</param>
    public static string? Problem(IReadOnlyList<int> medals, int perfect)
    {
        if (medals.Count != 3 || medals.Any(m => m is < 0 or > MaxLimit))
        {
            return CoreText.F("The limits are whole numbers from 0 to {0}.", MaxLimit);
        }

        if (medals[0] > medals[1] || medals[1] > medals[2])
        {
            return CoreText.T("The limits must not fall from bronze to gold.");
        }

        return medals[2] > perfect
            ? CoreText.F("Gold needs more points than the class can give ({0} for a perfect score).", perfect)
            : null;
    }

    /// <summary>
    /// The class table for the data area, or null if no class has own limits (then the game's table counts).
    /// </summary>
    /// <param name="settings">The settings of the classes.</param>
    /// <param name="bonusPatch">True if the game patch "Bonus games and Birdman" is applied.</param>
    /// <param name="ownTexts">True if the classes with own limits get own medal texts (<see cref="ClassTexts"/>).</param>
    /// <param name="missions">The missions of the game (null: the game's own): with <paramref name="ownTexts"/> the classes with
    /// another number of tests get all their medal texts (flags 2 and 4, <see cref="ClassTexts.ChangedClasses"/>).</param>
    public static byte[]? Table(IEnumerable<ClassSettings> settings, bool bonusPatch, bool ownTexts, IEnumerable<TestId>? missions = null)
    {
        List<ClassSettings> own = [.. settings.Where(s => s.Medals is { Count: 3 } && s.Vehicle is >= 0 and < Vehicles && s.Class is >= 0 and < Classes)];
        IReadOnlyList<(Vehicle Vehicle, int Class, int Tests)> changed = ownTexts ? ClassTexts.ChangedClasses(missions) : [];
        // Classes 4-7 have no missions yet; their entries hold the limits of class 3 (Pilot) or the bonus entries.
        if (own.Count == 0 && changed.Count == 0)
        {
            return null;
        }

        byte[] table = new byte[TableSize];
        for (int v = 0; v < Vehicles; v++)
        {
            for (int c = 0; c < Classes; c++)
            {
                ClassSettings? setting = own.FirstOrDefault(s => s.Vehicle == v && s.Class == c);
                IReadOnlyList<int> limits = setting?.Medals ?? Original((Vehicle)v, c, bonusPatch);
                for (int i = 0; i < 3; i++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan((((v * Classes) + c) * 3 + i) * 2), (ushort)Math.Clamp(limits[i], 0, MaxLimit));
                }

                if (setting is not null)
                {
                    table[FlagsOffset + (v * Classes) + c] = (byte)(FlagOwnLimits
                        | (ownTexts && ClassTexts.HasOwnTexts((Vehicle)v) ? FlagOwnTexts : 0));
                }

                if (changed.Any(x => (int)x.Vehicle == v && x.Class == c))
                {
                    table[FlagsOffset + (v * Classes) + c] |= FlagOwnTexts | FlagAllTexts;
                }
            }
        }

        return table;
    }

    /// <summary>The own limits in a class table (classes with the flag <see cref="FlagOwnLimits"/>).</summary>
    public static IReadOnlyList<(Vehicle Vehicle, int Class, IReadOnlyList<int> Medals)> ReadTable(ReadOnlySpan<byte> table)
    {
        var result = new List<(Vehicle, int, IReadOnlyList<int>)>();
        if (table.Length < TableSize)
        {
            return result;
        }

        for (int v = 0; v < Vehicles; v++)
        {
            for (int c = 0; c < Classes; c++)
            {
                if ((table[FlagsOffset + (v * Classes) + c] & FlagOwnLimits) == 0)
                {
                    continue;
                }

                int[] limits = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    limits[i] = BinaryPrimitives.ReadUInt16BigEndian(table[((((v * Classes) + c) * 3 + i) * 2)..]);
                }

                result.Add(((Vehicle)v, c, limits));
            }
        }

        return result;
    }

    /// <summary>A class table of layout version 1 ([7][4]) in the current layout ([7][8]).</summary>
    public static byte[] FromVersion1(ReadOnlySpan<byte> old)
    {
        byte[] table = new byte[TableSize];
        for (int v = 0; v < Vehicles; v++)
        {
            for (int c = 0; c < 4; c++)
            {
                old.Slice(((v * 4) + c) * 6, 6).CopyTo(table.AsSpan(((v * Classes) + c) * 6));
                table[FlagsOffset + (v * Classes) + c] = old[(Vehicles * 4 * 6) + (v * 4) + c];
            }
        }

        return table;
    }
}
