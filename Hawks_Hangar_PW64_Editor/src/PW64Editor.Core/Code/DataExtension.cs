using System.Buffers.Binary;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Code;

/// <summary>
/// The lower part of the editor's data area (US version): RAM 0x801F1000–0x801F5000 in front of the data area of
/// <see cref="DataArea"/>, taken from the heap as well. It holds the tables of up to 128 missions and of up to eight
/// classes per vehicle, which the game module copies from the ROM at the start like the data area.
/// </summary>
/// <remarks>
/// <para><b>Layout</b>:</para>
/// <code>
/// +0x0000 u32 'HHMX'
/// +0x0004 u32 number of missions in the mission list (+0x80)
/// +0x0008 u8[8] Birdman missions with points: per stage a bit per test (bit t = test t)
/// +0x0010 u8[7][8] photo missions ("Shutter Bug"): per vehicle and class a bit per test
/// +0x0080 u8[128] the mission list: the user file number of each mission, in the order taskInit loads them
///         (sTaskUserFileIdxLookup of task.c)
/// +0x0100 the result tables ([7 vehicles][8 classes][5 tests], <see cref="Missions.ResultTables"/>), when the game patch
///         "Bonus games and Birdman" is applied
/// +0x2800 the class table ([7][8], <see cref="Classes.ClassLimits"/>), when a class has own limits
/// </code>
/// <para><b>ROM</b>: the image lies <see cref="RomOffset"/> bytes behind the image of the data area (which has at most
/// 0x9000 bytes); its size is in the header of the data area (+0x24).</para>
/// <para>Names of the game's tables from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class DataExtension
{
    /// <summary>Start of the lower data area (new start of the program block).</summary>
    public const uint Start = 0x801F1000;

    /// <summary>End of the lower data area: the data area.</summary>
    public const uint End = DataArea.Start;

    /// <summary>Magic number 'HHMX'.</summary>
    public const uint Magic = 0x48484D58;

    /// <summary>Where the image lies in the ROM, counted from the image of the data area.</summary>
    public const int RomOffset = 0x9000;

    /// <summary>Most missions the game can hold: the mission list, the settings table and the saved game.</summary>
    public const int MaxMissions = 128;

    /// <summary>Vehicles of the tables.</summary>
    public const int Vehicles = 7;

    /// <summary>Classes (levels, stages) per vehicle the tables have room for (MAX_CLASSES of task.c).</summary>
    public const int Classes = 8;

    /// <summary>Tests per class (MAX_TESTS of task.c).</summary>
    public const int Tests = 5;

    /// <summary>Offset of the number of missions.</summary>
    public const int MissionCountOffset = 0x04;

    /// <summary>Offset of the Birdman missions with points.</summary>
    public const int BirdmanOffset = 0x08;

    /// <summary>Offset of the photo missions.</summary>
    public const int PhotoOffset = 0x10;

    /// <summary>Offset of the mission list.</summary>
    public const int MissionListOffset = 0x80;

    /// <summary>RAM address of the mission list (taskLoadCommObj reads it).</summary>
    public const uint MissionListAddress = Start + MissionListOffset;

    /// <summary>RAM address of the result tables.</summary>
    public const uint ResultTablesAddress = Start + 0x100;

    /// <summary>RAM address of the class table.</summary>
    public const uint ClassTableAddress = Start + 0x2800;

    /// <summary>Room for the result tables.</summary>
    public const int ResultTablesRoom = (int)(ClassTableAddress - ResultTablesAddress);

    /// <summary>
    /// The mission list of the game (sTaskUserFileIdxLookup in task.c, the 61 missions): Hang Glider, Rocket Belt and
    /// Gyrocopter first, then Cannonball, Sky Diving, Jumble Hopper and Birdman.
    /// </summary>
    public static IReadOnlyList<int> GameMissionFiles { get; } =
    [
        0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D,
        0x3E, 0x3F, 0x40, 0x41, 0x11, 0x12, 0x13, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26,
    ];

    /// <summary>
    /// The photo missions of the game: Hang Glider Class A test 1, Class B test 2, Pilot test 3 (testMenuShutterBug). snapPhoto
    /// checks only class and test, so the Birdman has them at the same places (the HUD says "Photo" there).
    /// </summary>
    public static IReadOnlyList<TestId> GamePhotoMissions { get; } =
    [
        new(1, Vehicle.HangGlider, 0), new(2, Vehicle.HangGlider, 1), new(3, Vehicle.HangGlider, 2),
        new(1, Vehicle.Birdman, 0), new(2, Vehicle.Birdman, 1), new(3, Vehicle.Birdman, 2),
    ];

    /// <summary>The image.</summary>
    /// <param name="missionFiles">The user file number of every mission, in the order the game loads them.</param>
    /// <param name="birdmanPoints">The Birdman missions with points.</param>
    /// <param name="photoMissions">The photo missions.</param>
    /// <param name="resultTables">The result tables, or null.</param>
    /// <param name="classTable">The class table, or null.</param>
    /// <exception cref="InvalidDataException">Too many missions, or a table does not fit.</exception>
    public static byte[] Create(IReadOnlyList<int> missionFiles, IEnumerable<TestId> birdmanPoints, IEnumerable<TestId> photoMissions,
        byte[]? resultTables = null, byte[]? classTable = null)
    {
        if (missionFiles.Count > MaxMissions)
        {
            throw new InvalidDataException($"The game has room for {MaxMissions} missions.");
        }

        if (missionFiles.Any(f => f is < 0 or > 255))
        {
            throw new InvalidDataException("A mission file has a number the mission list cannot hold.");
        }

        if (resultTables is not null && resultTables.Length > ResultTablesRoom)
        {
            throw new InvalidDataException("The result tables do not fit into the data area.");
        }

        int size = classTable is not null ? (int)(ClassTableAddress - Start) + classTable.Length
            : resultTables is not null ? (int)(ResultTablesAddress - Start) + resultTables.Length
            : MissionListOffset + MaxMissions;
        if (size > End - Start)
        {
            throw new InvalidDataException("The tables do not fit into the data area.");
        }

        byte[] image = new byte[(size + 7) & ~7];
        BinaryPrimitives.WriteUInt32BigEndian(image, Magic);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(MissionCountOffset), (uint)missionFiles.Count);
        foreach (TestId m in birdmanPoints)
        {
            if (m.Vehicle == Vehicle.Birdman && m.Class is >= 0 and < Classes && m.Test is >= 0 and < Tests)
            {
                image[BirdmanOffset + m.Class] |= (byte)(1 << m.Test);
            }
        }

        foreach (TestId m in photoMissions)
        {
            if ((int)m.Vehicle is >= 0 and < Vehicles && m.Class is >= 0 and < Classes && m.Test is >= 0 and < Tests)
            {
                image[PhotoOffset + ((int)m.Vehicle * Classes) + m.Class] |= (byte)(1 << m.Test);
            }
        }

        for (int i = 0; i < missionFiles.Count; i++)
        {
            image[MissionListOffset + i] = (byte)missionFiles[i];
        }

        resultTables?.CopyTo(image, (int)(ResultTablesAddress - Start));
        classTable?.CopyTo(image, (int)(ClassTableAddress - Start));
        return image;
    }

    /// <summary>True if the image is one of <see cref="Create"/>.</summary>
    public static bool IsImage(ReadOnlySpan<byte> image) =>
        image.Length >= MissionListOffset + MaxMissions && BinaryPrimitives.ReadUInt32BigEndian(image) == Magic;

    /// <summary>The mission list of an image (user file numbers).</summary>
    public static IReadOnlyList<int> ReadMissionFiles(ReadOnlySpan<byte> image)
    {
        if (!IsImage(image))
        {
            return [];
        }

        int count = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(image[MissionCountOffset..]), MaxMissions);
        return [.. image.Slice(MissionListOffset, count).ToArray().Select(b => (int)b)];
    }

    /// <summary>The Birdman missions with points of an image.</summary>
    public static IReadOnlyList<TestId> ReadBirdmanPoints(ReadOnlySpan<byte> image)
    {
        var result = new List<TestId>();
        if (!IsImage(image))
        {
            return result;
        }

        for (int c = 0; c < Classes; c++)
        {
            for (int t = 0; t < Tests; t++)
            {
                if ((image[BirdmanOffset + c] & (1 << t)) != 0)
                {
                    result.Add(new TestId(c, Vehicle.Birdman, t));
                }
            }
        }

        return result;
    }

    /// <summary>The photo missions of an image.</summary>
    public static IReadOnlyList<TestId> ReadPhotoMissions(ReadOnlySpan<byte> image)
    {
        var result = new List<TestId>();
        if (!IsImage(image))
        {
            return result;
        }

        for (int v = 0; v < Vehicles; v++)
        {
            for (int c = 0; c < Classes; c++)
            {
                for (int t = 0; t < Tests; t++)
                {
                    if ((image[PhotoOffset + (v * Classes) + c] & (1 << t)) != 0)
                    {
                        result.Add(new TestId(c, (Vehicle)v, t));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>A table of the image at its address, or null if the image does not reach it.</summary>
    public static byte[]? ReadTable(ReadOnlySpan<byte> image, uint address, int size)
    {
        int offset = (int)(address - Start);
        return IsImage(image) && size > 0 && offset + size <= image.Length ? image.Slice(offset, size).ToArray() : null;
    }
}
