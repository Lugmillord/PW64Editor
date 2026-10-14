using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Code;

/// <summary>
/// Exchanged instructions at a fixed ROM offset. Unlike <see cref="CodePatch"/> it is never searched
/// elsewhere: the words at the offset must be the original or already the replacement.
/// </summary>
/// <param name="Description">What is changed, for error messages.</param>
/// <param name="RomOffset">ROM offset of the first word.</param>
/// <param name="Original">The words of the original game (or of an applied code fix, see <paramref name="AlsoAccepted"/>).</param>
/// <param name="Replacement">The new words.</param>
/// <param name="AlsoAccepted">Other words that may be found instead of the original ones.</param>
public sealed record WordPatch(string Description, int RomOffset, uint[] Original, uint[] Replacement, uint[]? AlsoAccepted = null)
{
    /// <summary>Checks that the ROM holds the expected words.</summary>
    /// <exception cref="InvalidDataException">Other words were found.</exception>
    public void Check(ReadOnlySpan<byte> rom)
    {
        if (RomOffset < 0 || RomOffset + (Replacement.Length * 4) > rom.Length || Original.Length != Replacement.Length)
        {
            throw new InvalidDataException(CoreText.F("The game code cannot be changed for {0}.", Description));
        }

        uint[] current = Read(rom);
        if (!current.SequenceEqual(Original) && !current.SequenceEqual(Replacement)
            && (AlsoAccepted is null || !current.SequenceEqual(AlsoAccepted)))
        {
            throw new InvalidDataException(CoreText.F(
                "The game code at 0x{0:X6} is not the expected one ({1}). The ROM is not the game version the editor was made for.",
                RomOffset, Description));
        }
    }

    /// <summary>Writes the new words.</summary>
    public void Write(Span<byte> rom)
    {
        for (int i = 0; i < Replacement.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(rom[(RomOffset + (i * 4))..], Replacement[i]);
        }
    }

    private uint[] Read(ReadOnlySpan<byte> rom)
    {
        uint[] words = new uint[Replacement.Length];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32BigEndian(rom[(RomOffset + (i * 4))..]);
        }

        return words;
    }
}

/// <summary>
/// The editor's code area: RAM for new program code directly behind the game's variables, and the
/// loader that fills it at the start (US version).
/// </summary>
/// <remarks>
/// <para><b>Memory</b>: the game's variables end at 0x803805E0; the memory up to 0x803DA800 is heap.
/// The code area takes 0x803805E0–<see cref="End"/> (64 KiB) away from the heap: the program
/// block of the memory manager now ends at <see cref="End"/> (uvMemInitBlocks), and the clearing of
/// free memory at every level start begins there (uvMemClearRegions). Without these two changes
/// the game would allocate and clear this memory.</para>
/// <para><b>Loader</b>: Thread_App copies the game program from the ROM, clears its variables and
/// calls app_entrypoint. That call now goes to the loader in the code cave (see
/// <see cref="CodeFixes.CodeCaveStart"/>): it copies the module from the ROM into the code area
/// (_uvMediaCopy), calls its init function (header +8) and continues with app_entrypoint.</para>
/// <para><b>Gate</b>: a small function in the code cave that passes a call on to the module's printf
/// (header +4) once the module is loaded (header +0 holds the magic number), and returns otherwise.
/// The game's own debug calls go through it, also those that come before the module is loaded.</para>
/// <para>The code cave is the body of uvMemScanBlocks, which only reports overlapping memory
/// blocks through the empty _uvDebugPrintf; it returns at once (the same change as in
/// <see cref="CodeFixes.PhotoAlbum"/>). Loader and gate use 0x2B4D4–0x2B548, behind the photo
/// helpers.</para>
/// </remarks>
public static class CodeArea
{
    /// <summary>Start of the code area (end of the game's variables).</summary>
    public const uint Start = 0x803805E0;

    /// <summary>End of the code area (start of the remaining heap): 64 KiB behind <see cref="Start"/>.</summary>
    public const uint End = 0x803905E0;

    /// <summary>ROM offset of the loader in the code cave (behind the photo helpers).</summary>
    public const int LoaderRomOffset = 0x2B4D4;

    /// <summary>RAM address of the loader.</summary>
    public const uint LoaderAddress = 0x8022A524;

    /// <summary>RAM address of the printf gate (behind the loader).</summary>
    public const uint PrintfGateAddress = LoaderAddress + (LoaderWords * 4);

    private const int LoaderWords = 18;
    private const int GateWords = 11;

    /// <summary>ROM offset of "jal app_entrypoint" in Thread_App.</summary>
    public const int AppEntryCallRomOffset = 0x2F918;

    private const uint AppEntrypoint = 0x8030FE20;
    private const uint MediaCopy = 0x8022A760;

    /// <summary>A "jal" to an address.</summary>
    public static uint Jal(uint address) => 0x0C000000 | ((address >> 2) & 0x03FFFFFF);

    /// <summary>A "j" to an address.</summary>
    public static uint J(uint address) => 0x08000000 | ((address >> 2) & 0x03FFFFFF);

    private static uint Hi(uint value) => value >> 16;

    private static uint Lo(uint value) => value & 0xFFFF;

    /// <summary>The words of the original game in the code cave where loader and gate go.</summary>
    private static readonly uint[] OriginalCave =
    [
        0x3C158025, 0x3C148025, 0x3C12802C, 0x26528830, 0x2694E400, 0x26B5E434, 0x18400020, 0x00003025, 0x1266001A,
        0x000670C0, 0x001378C0, 0x024F8821, 0x024E8021, 0x8E180000, 0x8E390004, 0x0319082B, 0x50200013, 0x24C60001,
        0x8E280000, 0x8E090004, 0x0109082B, 0x1020000D, 0x02802025, 0x0C08BB45, 0x02602825, 0x8E0A0004, 0x02A02025,
        0x8E250000, 0x8E260004,
    ];

    /// <summary>
    /// All changes for the code area: memory reservation, uvMemScanBlocks returning at once, loader,
    /// gate and the call of the loader.
    /// </summary>
    /// <param name="moduleRomOffset">Where the module lies in the ROM.</param>
    /// <param name="moduleSize">Bytes to load (a multiple of 8).</param>
    public static IReadOnlyList<WordPatch> Patches(int moduleRomOffset, int moduleSize)
    {
        uint rom = (uint)moduleRomOffset;
        uint size = (uint)moduleSize;
        if (Start + size > End || size % 8 != 0)
        {
            throw new ArgumentException("The module does not fit into the code area.", nameof(moduleSize));
        }

        uint[] loader =
        [
            0x27BDFFE0,                      // addiu sp, sp, -0x20
            0xAFBF001C,                      // sw    ra, 0x1C(sp)
            0xAFA40018,                      // sw    a0, 0x18(sp)
            0x3C040000 | Hi(Start),          // lui   a0, hi(Start)
            0x34840000 | Lo(Start),          // ori   a0, a0, lo(Start)
            0x3C050000 | Hi(rom),            // lui   a1, hi(rom)
            0x34A50000 | Lo(rom),            // ori   a1, a1, lo(rom)
            0x3C060000 | Hi(size),           // lui   a2, hi(size)
            Jal(MediaCopy),                  // jal   _uvMediaCopy(Start, rom, size)
            0x34C60000 | Lo(size),           // ori  a2, a2, lo(size)
            0x3C080000 | Hi(Start),          // lui   t0, hi(Start)
            0x8D190000 | Lo(Start + 8),      // lw    t9, lo(Start + 8)(t0): module_init
            0x0320F809,                      // jalr  t9
            0x8FA40018,                      // lw    a0, 0x18(sp)
            0x8FA40018,                      // lw    a0, 0x18(sp)
            0x8FBF001C,                      // lw    ra, 0x1C(sp)
            J(AppEntrypoint),                // j     app_entrypoint
            0x27BD0020,                      // addiu sp, sp, 0x20
        ];

        uint[] gate =
        [
            0x3C080000 | Hi(Start),          // lui   t0, hi(Start)
            0x8D090000 | Lo(Start),          // lw    t1, lo(Start)(t0): magic
            0x3C0A0000 | Hi(GameModule.Magic), // lui t2, hi(magic)
            0x354A0000 | Lo(GameModule.Magic), // ori t2, t2, lo(magic)
            0x152A0003,                      // bne   t1, t2, +3 (return)
            0x00000000,                      // nop
            0x8D190000 | Lo(Start + 4),      // lw    t9, lo(Start + 4)(t0): module_game_printf
            0x03200008,                      // jr    t9
            0x00000000,                      // nop
            0x03E00008,                      // jr    ra
            0x00000000,                      // nop
        ];

        return
        [
            // Only the upper halves change (0x8038 -> 0x8039), so the code fix ExpandedTexts, which also
            // contains the lower half 0x05E0 of uvMemInitBlocks, is not touched.
            new WordPatch("code area, program block end in uvMemInitBlocks", 0x2B394,
                [0x3C0A8038], [0x3C0A0000 | Hi(End)]),
            new WordPatch("code area, memory clearing in uvMemClearRegions", 0x2B478,
                [0x3C028038], [0x3C020000 | Hi(End)]),
            new WordPatch("code cave, uvMemScanBlocks returns at once", CodeFixes.CodeCaveStart,
                [0x27BDFFC8, 0x3C02802C], [0x03E00008, 0x00000000]),
            new WordPatch("code area loader and gate, code cave", LoaderRomOffset,
                OriginalCave, [.. loader, .. gate]),
            new WordPatch("code area loader call, Thread_App", AppEntryCallRomOffset,
                [Jal(AppEntrypoint)], [Jal(LoaderAddress)]),
        ];
    }
}
