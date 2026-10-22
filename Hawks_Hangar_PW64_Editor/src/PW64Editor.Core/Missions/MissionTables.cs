using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>Medal limits of one entry of the medal table.</summary>
public readonly record struct MedalLimits(int Bronze, int Silver, int Gold);

/// <summary>
/// The mission tables in the game code (see <see cref="MissionLayout"/>), read from a ROM.
/// </summary>
/// <remarks>
/// The game indexes the result tables as [vehicle][class][test] without checking the test number.
/// For Cannonball, whose levels have four "tests" (the targets), the fourth target therefore reads
/// the first entry of the next class. The lookups here compute the position the same way, so they
/// return exactly what the game uses.
/// </remarks>
public sealed class MissionTables
{
    private readonly byte[] _rom;
    private readonly MissionLayout _layout;
    private readonly bool _bonusGames;
    private readonly IReadOnlySet<TestId> _birdmanPoints;

    /// <summary>Reads the tables from a ROM (a copy of the needed bytes is not made; do not change the array).</summary>
    /// <param name="rom">The ROM (the clean one: the editor's changes are not read back from a built ROM).</param>
    /// <param name="layout">Where the tables are.</param>
    /// <param name="bonusGames">The game patch <see cref="Code.GamePatches.BonusGames"/> is applied: the levels of Sky
    /// Diving and the Jumble Hopper have texts of their own and the Birdman has medal limits.</param>
    /// <param name="birdmanPoints">With the patch: the Birdman missions with points (texts of their own).</param>
    public MissionTables(byte[] rom, MissionLayout layout, bool bonusGames = false, IReadOnlySet<TestId>? birdmanPoints = null)
    {
        _rom = rom;
        _layout = layout;
        _bonusGames = bonusGames;
        _birdmanPoints = bonusGames ? birdmanPoints ?? new HashSet<TestId>() : new HashSet<TestId>();
    }

    /// <summary>
    /// Name of the text of the mission screen ("…_S1", points without slash), or null if the game
    /// has none for this test.
    /// </summary>
    public string? MissionScreenTextName(TestId test) =>
        (_bonusGames ? OwnName(test, false) : null) ?? ReadName(_layout.MissionScreenNamesOffset, test);

    /// <summary>Name of the text of the result sheet ("…_S2", with slash), or null if the game has none.</summary>
    public string? ResultSheetTextName(TestId test) =>
        (_bonusGames ? OwnName(test, true) : null) ?? ReadName(_layout.ResultSheetNamesOffset, test);

    private string? OwnName(TestId test, bool resultSheet) =>
        ResultTables.LevelTextName(test, resultSheet)
        ?? (test.Vehicle != Vehicle.Birdman ? null
            : _birdmanPoints.Contains(test) ? ResultTables.BirdmanTextName(test, resultSheet)
            : resultSheet ? "BD_ALL_S2" : "BD_ALL_S1"); // the patched resultInit reads the free flight's names from the tables

    /// <summary>
    /// The point rows of the result sheet, bottom row first, as stored. Empty rows are 0; the list
    /// is empty if the test is outside the table (Birdman).
    /// </summary>
    public IReadOnlyList<int> ResultRows(TestId test)
    {
        int? index = FlatIndex(test);
        if (index is not { } i)
        {
            return [];
        }

        int offset = _layout.ResultRowsOffset + (i * MissionLayout.ResultRows);
        return _rom.AsSpan(offset, MissionLayout.ResultRows).ToArray().Select(b => (int)b).ToList();
    }

    /// <summary>ROM offset of the 4 result rows of a test (bottom row first), or null outside the table.</summary>
    public int? ResultRowsOffset(TestId test) =>
        FlatIndex(test) is { } index ? _layout.ResultRowsOffset + (index * MissionLayout.ResultRows) : null;

    /// <summary>The island the class menu shows for a vehicle and class (0-3), or null outside the table.</summary>
    public int? ClassIsland(Vehicle vehicle, int classIndex) =>
        (int)vehicle is >= 0 and < Vehicles.Count && classIndex is >= 0 and < 4
            ? _rom[_layout.ClassIslandsOffset + ((int)vehicle * 4) + classIndex]
            : null;

    /// <summary>Entry of the Birdman in the medal table (medal index = vehicle + 1 for all but the main vehicles).</summary>
    public const int BirdmanMedalEntry = 7;

    /// <summary>The Birdman's medal limits with the game patch <see cref="Code.GamePatches.BonusGames"/> (the game has 0/0/0).</summary>
    public static MedalLimits BirdmanMedalLimits { get; } = new(280, 320, 360);

    /// <summary>The eight entries of the medal table (with the game patch <see cref="Code.GamePatches.BonusGames"/>: as it sets them).</summary>
    public IReadOnlyList<MedalLimits> MedalLimits()
    {
        var limits = new List<MedalLimits>(MissionLayout.MedalEntries);
        for (int i = 0; i < MissionLayout.MedalEntries; i++)
        {
            ReadOnlySpan<byte> entry = _rom.AsSpan(_layout.MedalLimitsOffset + (i * 6), 6);
            limits.Add(new MedalLimits(
                BinaryPrimitives.ReadUInt16BigEndian(entry),
                BinaryPrimitives.ReadUInt16BigEndian(entry[2..]),
                BinaryPrimitives.ReadUInt16BigEndian(entry[4..])));
        }

        if (_bonusGames && limits.Count > BirdmanMedalEntry)
        {
            limits[BirdmanMedalEntry] = BirdmanMedalLimits;
        }

        return limits;
    }

    /// <summary>Position in the [6][4][3] tables, computed like the game; null outside the tables.</summary>
    private static int? FlatIndex(TestId test)
    {
        int vehicle = (int)test.Vehicle;
        if (vehicle is < 0 or >= MissionLayout.ResultVehicles || test.Class is < 0 or >= MissionLayout.ResultClasses || test.Test < 0)
        {
            return null;
        }

        int index = (((vehicle * MissionLayout.ResultClasses) + test.Class) * MissionLayout.ResultTests) + test.Test;
        return index < MissionLayout.ResultVehicles * MissionLayout.ResultClasses * MissionLayout.ResultTests ? index : null;
    }

    private string? ReadName(int tableOffset, TestId test)
    {
        if (FlatIndex(test) is not { } index)
        {
            return null;
        }

        uint pointer = BinaryPrimitives.ReadUInt32BigEndian(_rom.AsSpan(tableOffset + (index * 4)));
        if (pointer == 0 || pointer < _layout.DataRamDelta)
        {
            return null;
        }

        long offset = pointer - _layout.DataRamDelta;
        if (offset >= _rom.Length)
        {
            return null;
        }

        int end = Array.IndexOf(_rom, (byte)0, (int)offset);
        return end < 0 ? null : Encoding.ASCII.GetString(_rom, (int)offset, end - (int)offset);
    }
}
