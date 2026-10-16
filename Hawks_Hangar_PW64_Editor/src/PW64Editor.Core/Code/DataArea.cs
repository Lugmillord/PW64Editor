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
/// +0x14 u8  jingle when the hard end time is reached (music number, 0 = none)
/// +0x15 ... reserved (0)
/// </code>
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

    /// <summary>RAM address of the first mission entry.</summary>
    public const uint MissionTableAddress = Start + HeaderSize;

    static DataArea()
    {
        if (HeaderSize + (MissionCapacity * MissionEntrySize) > End - Start)
        {
            throw new InvalidOperationException("The mission settings table does not fit into the data area.");
        }
    }

    /// <summary>The image as the module copies it into the data area: header and the used entries.</summary>
    /// <param name="missions">Missions with settings.</param>
    /// <exception cref="InvalidDataException">More missions than the table has room for.</exception>
    public static byte[] CreateImage(IReadOnlyList<GameModuleCondition> missions)
    {
        if (missions.Count > MissionCapacity)
        {
            throw new InvalidDataException($"The mission settings table has room for {MissionCapacity} missions.");
        }

        byte[] image = new byte[HeaderSize + (missions.Count * MissionEntrySize)];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x00), Magic);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x04), Version);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x08), (uint)missions.Count);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x0C), MissionEntrySize);
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
            | (c.HardEnd == HardEndScoring.Scored ? 16 : 0) | (c.HardEnd == HardEndScoring.Zero ? 32 : 0));
        for (int t = 0; t < GameModule.ObjectTypes; t++)
        {
            entry[4 + t] = t < c.Required.Count ? c.Required[t] : (byte)0;
        }

        BinaryPrimitives.WriteSingleBigEndian(entry[0x0C..], c.MinimumAltitude ?? 0);
        BinaryPrimitives.WriteSingleBigEndian(entry[0x10..], c.MinimumDistance ?? 0);
        entry[0x14] = c.HardEndMusic;
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
