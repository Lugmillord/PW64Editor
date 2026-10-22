using System.Buffers.Binary;

namespace PW64Editor.Core.Code;

/// <summary>
/// The editor's data area: RAM in front of the game's program block for new tables (US version).
/// So far it holds the mission settings table (one entry per mission with settings), which the game
/// module copies from the ROM at the start.
/// </summary>
/// <remarks>
/// <para><b>Memory</b>: the program block of the memory manager started at 0x80200000 (since the code
/// fix <see cref="CodeFixes.ExpandedTexts"/> at 0x801FE000, in front of the text tables). It now starts
/// at <see cref="Start"/>, so the heap ends there (uvMemInitBlocks), and the clearing of free memory
/// at every level start stops there (uvMemClearRegions). New data grows downwards from here.</para>
/// <para><b>Layout</b>: a header of <see cref="HeaderSize"/> bytes (magic 'HHMD', version, number of
/// missions, entry size), then one entry of <see cref="MissionEntrySize"/> bytes per mission:</para>
/// <code>
/// +0x00 u8  vehicle (COMM byte 1)      +0x01 u8 class (COMM byte 0)   +0x02 u8 test (COMM byte 2)
/// +0x03 u8  flags: 1 crash gives 0, 2 minimum altitude, 4 minimum distance, 8 unlimited fuel,
///           16 hard end time scored (like a flight ending in the air), 32 hard end time gives 0 points
/// +0x04 u8[7] required objects per type (0 = no, 0xFF = all, otherwise at least this many)
/// +0x0C f32 minimum altitude (game units)  +0x10 f32 minimum distance (game units)
/// +0x03 flags (continued): 64 time points after a crash, 128 own curve of the Gyrocopter's touchdown point
/// +0x14 u8  jingle when the hard end time is reached (music number, 0 = none)
/// +0x15 u8  landing flags: 1 pad radius, 2 speed limits of the vehicle
/// +0x18 curve (0x54 bytes, as in the mission file): the Gyrocopter's touchdown point
/// +0x6C f32 radius of the landing pads (game units)
/// +0x70 f32[4] speed limits of the vehicle (LandingValues.SpeedsOf; HG km/h, GC and RB units per second)
/// +0x80 ... reserved (0)
/// </code>
/// <para>Header: +0x00 'HHMD', +0x04 version, +0x08 missions, +0x0C entry size, +0x10 size of the result tables
/// (0 = none), +0x14 u16 the Birdman missions with points (bit class × 4 + test), +0x18 RAM address of the class table
/// (medal limits per vehicle and class, <see cref="Classes.ClassLimits"/>; 0 = none). At +0x50 the Birdman's two landing
/// limits, which its patched code reads. At +0x40 the values the game code reads while flying (written by the module in every flight frame,
/// see <see cref="LiveLandingAddress"/>).</para>
/// <para>Behind the room for <see cref="MissionCapacity"/> entries (at <see cref="Missions.ResultTables.Address"/>)
/// the result tables of the game patch <see cref="GamePatches.BonusGames"/>, when it is applied; the image then
/// reaches up to their end.</para>
/// <para><b>ROM</b>: the image lies in the <see cref="RomReserve"/> bytes in front of the game module
/// at the end of the ROM; the module header holds its offset and size.</para>
/// </remarks>
public static class DataArea
{
    /// <summary>Start of the data area (new start of the program block).</summary>
    public const uint Start = 0x801F5000;

    /// <summary>End of the data area: the text tables of <see cref="CodeFixes.ExpandedTexts"/>.</summary>
    public const uint End = CodeFixes.TextNameTableAddress;

    /// <summary>Header: magic number 'HHMD'.</summary>
    public const uint Magic = 0x48484D44;

    /// <summary>Layout version of the data area.</summary>
    public const uint Version = 1;

    /// <summary>Size of the header in front of the mission entries.</summary>
    public const int HeaderSize = 0x100;

    /// <summary>Bytes per mission entry (room for the settings of later patches).</summary>
    public const int MissionEntrySize = 0x100;

    /// <summary>Number of missions the table has room for.</summary>
    public const int MissionCapacity = 128;

    /// <summary>Room in the ROM for the image, in front of the game module.</summary>
    public const int RomReserve = 0x10000;

    /// <summary>
    /// RAM address of the Hang Glider's four current speed limits (f32: landing vertical, landing forward,
    /// good landing vertical, good landing forward, km/h), which its patched landing code loads instead of
    /// the immediates 50 and 15. The module writes the mission's values (or the original ones) there.
    /// </summary>
    public const uint LiveLandingAddress = Start + 0x40;

    /// <summary>Offset of the touchdown curve in an entry.</summary>
    public const int TouchdownCurveOffset = 0x18;

    /// <summary>RAM address of the first mission entry.</summary>
    public const uint MissionTableAddress = Start + HeaderSize;

    static DataArea()
    {
        if (HeaderSize + (MissionCapacity * MissionEntrySize) > End - Start)
        {
            throw new InvalidOperationException("The mission settings table does not fit into the data area.");
        }
    }

    /// <summary>Header offset of the size of the result tables (0 = none).</summary>
    public const int ResultTablesSizeOffset = 0x10;

    /// <summary>Header offset of the mask of the Birdman missions with points (u16, <see cref="Missions.ResultTables.BirdmanPointsMask"/>).</summary>
    public const int BirdmanPointsOffset = 0x14;

    /// <summary>Header: RAM address of the class table (<see cref="Classes.ClassLimits"/>), 0 = none.</summary>
    public const int ClassTableOffset = 0x18;

    /// <summary>
    /// RAM address of the Birdman's two current landing limits (f32 km/h: vertical, forward), which its patched landing
    /// code loads instead of the immediates 50 (module: the mission's values or the original ones).
    /// </summary>
    public const uint LiveBirdmanAddress = Start + 0x50;

    /// <summary>
    /// The image as the module copies it into the data area: header, the used entries and, if given, the
    /// result tables at their place behind the room for all entries.
    /// </summary>
    /// <param name="missions">Missions with settings.</param>
    /// <param name="resultTables">The result tables of <see cref="GamePatches.BonusGames"/> (<see cref="Missions.ResultTables.Create"/>), or null.</param>
    /// <exception cref="InvalidDataException">More missions than the table has room for, or the tables do not fit.</exception>
    /// <param name="birdmanPoints">The mask of the Birdman missions with points (header +0x14).</param>
    public static byte[] CreateImage(IReadOnlyList<GameModuleCondition> missions, byte[]? resultTables = null, ushort birdmanPoints = 0,
        byte[]? classTable = null)
    {
        if (missions.Count > MissionCapacity)
        {
            throw new InvalidDataException($"The mission settings table has room for {MissionCapacity} missions.");
        }

        int tablesOffset = (int)(Missions.ResultTables.Address - Start);
        if (resultTables is not null && (tablesOffset + resultTables.Length > End - Start || tablesOffset + resultTables.Length > RomReserve))
        {
            throw new InvalidDataException("The result tables do not fit into the data area.");
        }

        int classOffset = (int)(Classes.ClassLimits.TableAddress - Start);
        if (classTable is not null && (classTable.Length != Classes.ClassLimits.TableSize
            || !Classes.ClassLimits.FitsBehind(Missions.ResultTables.Address + (uint)(resultTables?.Length ?? 0))))
        {
            throw new InvalidDataException("The class table does not fit into the data area.");
        }

        byte[] image = new byte[classTable is not null ? classOffset + classTable.Length
            : resultTables is null ? HeaderSize + (missions.Count * MissionEntrySize) : tablesOffset + resultTables.Length];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x00), Magic);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x04), Version);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x08), (uint)missions.Count);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x0C), MissionEntrySize);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(BirdmanPointsOffset), birdmanPoints);
        if (resultTables is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ResultTablesSizeOffset), (uint)resultTables.Length);
            resultTables.CopyTo(image, tablesOffset);
        }

        if (classTable is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ClassTableOffset), Classes.ClassLimits.TableAddress);
            classTable.CopyTo(image, classOffset);
        }

        for (int i = 0; i < missions.Count; i++)
        {
            WriteEntry(image.AsSpan(HeaderSize + (i * MissionEntrySize), MissionEntrySize), missions[i]);
        }

        return image;
    }

    private static void WriteEntry(Span<byte> entry, GameModuleCondition c)
    {
        entry[0] = c.Vehicle;
        entry[1] = c.Class;
        entry[2] = c.Test;
        entry[3] = (byte)((c.CrashGivesZero ? 1 : 0) | (c.MinimumAltitude is not null ? 2 : 0)
            | (c.MinimumDistance is not null ? 4 : 0) | (c.UnlimitedFuel ? 8 : 0)
            | (c.HardEnd == HardEndScoring.Scored ? 16 : 0) | (c.HardEnd == HardEndScoring.Zero ? 32 : 0)
            | (c.CrashTimePoints ? 64 : 0) | (c.TouchdownCurve is not null ? 128 : 0));
        entry[0x15] = (byte)((c.PadRadius is not null ? 1 : 0) | (c.VehicleLimits.Count > 0 ? 2 : 0));
        if (c.TouchdownCurve is { } curve)
        {
            if (curve.Count is < 1 or > Missions.ScoreCurve.MaxPoints)
            {
                throw new InvalidDataException("A touchdown curve needs 1 to 10 points.");
            }

            BinaryPrimitives.WriteInt32BigEndian(entry[TouchdownCurveOffset..], curve.Count);
            for (int i = 0; i < curve.Count; i++)
            {
                BinaryPrimitives.WriteSingleBigEndian(entry[(TouchdownCurveOffset + 4 + (i * 8))..], curve[i].X);
                BinaryPrimitives.WriteSingleBigEndian(entry[(TouchdownCurveOffset + 8 + (i * 8))..], curve[i].Y);
            }
        }

        BinaryPrimitives.WriteSingleBigEndian(entry[0x6C..], c.PadRadius ?? 0);
        if (c.VehicleLimits.Count > 4)
        {
            throw new InvalidDataException("A vehicle has at most four speed limits.");
        }

        for (int i = 0; i < c.VehicleLimits.Count; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(entry[(0x70 + (i * 4))..], c.VehicleLimits[i]);
        }
        for (int t = 0; t < GameModule.ObjectTypes; t++)
        {
            entry[4 + t] = t < c.Required.Count ? c.Required[t] : (byte)0;
        }

        BinaryPrimitives.WriteSingleBigEndian(entry[0x0C..], c.MinimumAltitude ?? 0);
        BinaryPrimitives.WriteSingleBigEndian(entry[0x10..], c.MinimumDistance ?? 0);
        entry[0x14] = c.HardEndMusic;
    }

    /// <summary>The result tables of an image made by <see cref="CreateImage"/>, or null if it has none.</summary>
    public static byte[]? ReadResultTables(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(image) != Magic)
        {
            return null;
        }

        int size = (int)BinaryPrimitives.ReadUInt32BigEndian(image[ResultTablesSizeOffset..]);
        int offset = (int)(Missions.ResultTables.Address - Start);
        return size > 0 && offset + (long)size <= image.Length ? image.Slice(offset, size).ToArray() : null;
    }

    /// <summary>The class table of an image made by <see cref="CreateImage"/>, or null if it has none.</summary>
    public static byte[]? ReadClassTable(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(image) != Magic
            || BinaryPrimitives.ReadUInt32BigEndian(image[ClassTableOffset..]) != Classes.ClassLimits.TableAddress)
        {
            return null;
        }

        int offset = (int)(Classes.ClassLimits.TableAddress - Start);
        return offset + Classes.ClassLimits.TableSize <= image.Length ? image.Slice(offset, Classes.ClassLimits.TableSize).ToArray() : null;
    }

    /// <summary>The mask of the Birdman missions with points of an image (0 for none or not an image).</summary>
    public static ushort ReadBirdmanPoints(ReadOnlySpan<byte> image) =>
        image.Length >= HeaderSize && BinaryPrimitives.ReadUInt32BigEndian(image) == Magic
            ? BinaryPrimitives.ReadUInt16BigEndian(image[BirdmanPointsOffset..])
            : (ushort)0;

    /// <summary>Reads the mission entries of an image made by <see cref="CreateImage"/>.</summary>
    /// <exception cref="InvalidDataException">Not a data area image.</exception>
    public static IReadOnlyList<GameModuleCondition> ReadImage(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(image) != Magic)
        {
            throw new InvalidDataException("This is not an image of the data area.");
        }

        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(image[0x08..]);
        int entrySize = (int)BinaryPrimitives.ReadUInt32BigEndian(image[0x0C..]);
        if (count < 0 || count > MissionCapacity || entrySize < 0x15 || HeaderSize + ((long)count * entrySize) > image.Length)
        {
            throw new InvalidDataException("The mission table of the data area is damaged.");
        }

        var result = new List<GameModuleCondition>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(ReadEntry(image.Slice(HeaderSize + (i * entrySize), entrySize)));
        }

        return result;
    }

    /// <summary>
    /// Reads one mission entry (the reverse of WriteEntry). Also reads the shorter entries of older module
    /// versions (0x14 bytes, without the jingle).
    /// </summary>
    public static GameModuleCondition ReadEntry(ReadOnlySpan<byte> entry)
    {
        byte flags = entry[3];
        byte[] required = entry.Slice(4, GameModule.ObjectTypes).ToArray();
        float altitude = BinaryPrimitives.ReadSingleBigEndian(entry[0x0C..]);
        float distance = BinaryPrimitives.ReadSingleBigEndian(entry[0x10..]);
        HardEndScoring hardEnd = (flags & 32) != 0 ? HardEndScoring.Zero : (flags & 16) != 0 ? HardEndScoring.Scored : HardEndScoring.Retail;
        bool full = entry.Length >= 0x80; // entries of module versions before 6 end at 0x14
        byte landing = full ? entry[0x15] : (byte)0;
        List<Missions.CurvePoint>? curve = null;
        if (full && (flags & 128) != 0)
        {
            int count = Math.Clamp(BinaryPrimitives.ReadInt32BigEndian(entry[TouchdownCurveOffset..]), 0, Missions.ScoreCurve.MaxPoints);
            curve = [];
            for (int i = 0; i < count; i++)
            {
                curve.Add(new Missions.CurvePoint(BinaryPrimitives.ReadSingleBigEndian(entry[(TouchdownCurveOffset + 4 + (i * 8))..]),
                    BinaryPrimitives.ReadSingleBigEndian(entry[(TouchdownCurveOffset + 8 + (i * 8))..])));
            }
        }

        var limits = new List<float>();
        if ((landing & 2) != 0)
        {
            int count = Missions.LandingValues.SpeedsOf((SaveGame.Vehicle)entry[0]).Count;
            for (int i = 0; i < count; i++)
            {
                limits.Add(BinaryPrimitives.ReadSingleBigEndian(entry[(0x70 + (i * 4))..]));
            }
        }

        return new GameModuleCondition(entry[1], entry[0], entry[2], (flags & 1) != 0,
            (flags & 2) != 0 ? altitude : null, required, (flags & 4) != 0 ? distance : null,
            UnlimitedFuel: (flags & 8) != 0, HardEnd: hardEnd, HardEndMusic: entry.Length > 0x14 ? entry[0x14] : (byte)0)
        {
            CrashTimePoints = full && (flags & 64) != 0,
            TouchdownCurve = curve,
            PadRadius = (landing & 1) != 0 ? BinaryPrimitives.ReadSingleBigEndian(entry[0x6C..]) : null,
            VehicleLimits = limits,
        };
    }

    /// <summary>
    /// The changes of the memory manager that keep the heap out of the data area. They work on the
    /// original game code and on the code of <see cref="CodeFixes.ExpandedTexts"/>.
    /// </summary>
    public static IReadOnlyList<WordPatch> Patches()
    {
        uint hi = (Start + 0x8000) >> 16;   // lui/addiu pair: addiu adds a signed value
        uint lo = Start & 0xFFFF;
        return
        [
            // uvMemInitBlocks: start of the program block. The original loads 0x80200000 with one lui and needs
            // "lui t3, 0x8000" for block 5; ExpandedTexts already freed that instruction (block 5 starts at 0,
            // "sw zero"). Here the same is done with the start of the data area.
            new WordPatch("data area, program block start in uvMemInitBlocks", 0x2B3AC,
                [0x3C098020, 0x254A05E0, 0x3C0B8000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC4B0028, 0xAC45002C],
                [0x3C090000 | hi, 0x254A05E0, 0x25290000 | lo, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC400028, 0xAC45002C],
                [0x3C098020, 0x254A05E0, 0x2529E000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC400028, 0xAC45002C]),
            // uvMemClearRegions: the clearing of free memory stops in front of the data area.
            new WordPatch("data area, memory clearing in uvMemClearRegions", 0x2B44C,
                [0x3C038020, 0x246300A0, 0x3C028012, 0x10600005, 0x34425800],
                [0x3C030000 | hi, 0x24630000 | lo, 0x3C028012, 0x10600005, 0x34425800],
                [0x3C038020, 0x2463E000, 0x3C028012, 0x10600005, 0x34425800]),
        ];
    }
}
