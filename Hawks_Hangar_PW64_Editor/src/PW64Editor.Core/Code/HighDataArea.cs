namespace PW64Editor.Core.Code;

/// <summary>
/// The high data area at the top of the Expansion Pak's memory (stage 8): room for tables of the game that become larger
/// in a hack, built into every hack with the game module.
/// </summary>
/// <remarks>
/// <para>The game's heap (_uvMemAlloc) grows from 0x8004181C up to the end of the memory (D_802B8930 = osMemSize +
/// 0x80000000, set by uvLevelInit) and jumps over the reserved blocks. In the measured flights it never went above
/// 0x80402000 (Little States 1.7 MB of 5.8 MB), so its end moves down to <see cref="Start"/>: the last 512 KiB belong to
/// the editor's tables. Every hack needs the Expansion Pak anyway.</para>
/// <list type="number">
///   <item><b>Display list</b> (graphics.c): gGfxDisplayListBase[2][4200] becomes [2][8192] at <see cref="DisplayList"/>.
///         uvGfxInit, uvGfxEnd (the count, its check and the task's data pointer) and the start of the next frame compute
///         "base + frame × 4200 × 8" with a chain of shifts and adds; the chain becomes one shift by 16 (× 65536) and
///         nops. The developers' warning (DL_ELEMENTS, sDisplayListNumElements) starts at 7800 instead of 4000.</item>
///   <item><b>Object memory of a mission</b> (mem.c): sLevelObjHeap, 5000 bytes, becomes 32 KiB at
///         <see cref="ObjectMemory"/>; mem_get checks 0x7FF8 instead of 5000, mem_init clears 32 KiB.</item>
///   <item><b>Radar goals</b> (hud.c): hud_8031A378 writes its goal list into the game module (room for 32) instead of
///         the radar's goals[10]; the calls of hudDrawRadar go to the module, which shows the 10 nearest.</item>
/// </list>
/// <para>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class HighDataArea
{
    /// <summary>First address of the area (the heap ends here).</summary>
    public const uint Start = 0x80780000;

    /// <summary>End of the area (the end of the Expansion Pak's memory).</summary>
    public const uint End = 0x80800000;

    /// <summary>Commands of one display list (the game: 4200).</summary>
    public const int DisplayListCommands = 8192;

    /// <summary>The two display lists (frame 0, frame 1), 8 bytes per command.</summary>
    public const uint DisplayList = Start;

    /// <summary>The object memory of a mission (mem.c).</summary>
    public const uint ObjectMemory = DisplayList + (2 * DisplayListCommands * 8);

    /// <summary>Size of the object memory.</summary>
    public const int ObjectMemorySize = 0x8000;

    /// <summary>
    /// Bytes a mission's objects may use: mem_get refuses a block whose end is not below this (the game: 5000), so the
    /// sum of the blocks (each rounded up to 8) must stay below it.
    /// </summary>
    public const int ObjectMemoryLimit = 0x7FF8;

    /// <summary>The game's object memory (sLevelObjHeap) and its limit, for projects without the area.</summary>
    public const int GameObjectMemoryLimit = 5000;

    /// <summary>First free address behind the fixed tables.</summary>
    public const uint FreeStart = ObjectMemory + ObjectMemorySize;

    private const uint Nop = 0;

    private static uint Hi(uint address) => (address + 0x8000) >> 16;

    private static uint Lo(uint address) => address & 0xFFFF;

    /// <summary>The changes of the game code (kernel and app).</summary>
    public static IReadOnlyList<WordPatch> Patches()
    {
        uint list = DisplayList, pool = ObjectMemory, goals = GameModuleImage.RadarGoalsAddress;
        uint radarJal = 0x0C0C6B5A; // jal hudDrawRadar (0x8031AD68)
        var patches = new List<WordPatch>
        {
            // The heap ends at the area: uvLevelInit "addu t8, t7, at" (osMemSize + 0x80000000) becomes "lui t8, 0x8078".
            new("high data area, end of the heap (uvLevelInit)", 0x2BEFC, [0x01E1C021], [0x3C180000 | (Start >> 16)]),

            // 1. Display list. uvGfxInit (0x80220960): frame 0, only the base.
            new("display list, base (uvGfxInit)", 0x219AC, [0x3C18802A], [0x3C180000 | Hi(list)]),
            new("display list, base (uvGfxInit)", 0x219B4, [0x27189280], [0x27180000 | Lo(list)]),
            // uvGfxEnd (0x8022217C): count = (head - base[frame]) / 8, with "negu at, frame" × 4200 × 8 as a chain.
            new("display list, base (uvGfxEnd)", 0x231A4, [0x3C0B802A], [0x3C0B0000 | Hi(list)]),
            new("display list, frame offset (uvGfxEnd)", 0x231AC,
                [0x00017940, 0x01E17821, 0x000F7880, 0x01E17823, 0x000F7880],
                [0x00017C00, Nop, Nop, Nop, Nop]),
            new("display list, base (uvGfxEnd)", 0x231C0, [0x256B9280, 0x01E17821, 0x000F7980],
                [0x256B0000 | Lo(list), Nop, Nop]),
            new("display list, size check (uvGfxEnd)", 0x231E0, [0x2B211068], [0x2B210000 | DisplayListCommands]),
            new("display list, size in the message (uvGfxEnd)", 0x231FC, [0x24061068], [0x24060000 | DisplayListCommands]),
            // The task's data pointer: base + frame × 4200 × 8 (t3 = base from above).
            new("display list, task data (uvGfxEnd)", 0x232F8,
                [0x00196140, 0x01996021, 0x000C6080, 0x01996023, 0x000C6080, 0x01996021, 0x000C6180],
                [0x00196400, Nop, Nop, Nop, Nop, Nop, Nop]),
            // The next frame's head: base + frame × 4200 × 8 (frame in v1).
            new("display list, next frame (uvGfxEnd)", 0x23820, [0x00036940], [0x00036C00]),
            new("display list, next frame (uvGfxEnd)", 0x23834, [0x01A36821], [Nop]),
            new("display list, next frame (uvGfxEnd)", 0x23844, [0x000D6880], [Nop]),
            new("display list, next frame (uvGfxEnd)", 0x23850, [0x01A36823, 0x000D6880], [Nop, Nop]),
            new("display list, next frame (uvGfxEnd)", 0x2385C, [0x01A36821, 0x3C0F802A], [Nop, 0x3C0F0000 | Hi(list)]),
            new("display list, next frame (uvGfxEnd)", 0x2386C, [0x25EF9280, 0x000D6980], [0x25EF0000 | Lo(list), Nop]),
            // The developers' warning threshold (sDisplayListNumElements, 0x80249234, starts at 4000).
            new("display list, developers' warning", 0x4A1E4, [4000], [7800]),

            // 2. Object memory: mem_get (0x80312740), mem_init (0x803127BC), mem_remaining (0x803127F0) and the start
            //    value of sLevelObjHeapPtr (0x8034F840).
            new("object memory, mem_get", 0x99CA4, [0x3C198037], [0x3C190000 | Hi(pool)]),
            new("object memory, mem_get", 0x99CAC, [0x2739AA60], [0x27390000 | Lo(pool)]),
            new("object memory, size (mem_get)", 0x99CB4, [0x2D011388], [0x2D010000 | (uint)ObjectMemoryLimit]),
            new("object memory, mem_init", 0x99CF0, [0x3C048037, 0x2484AA60], [0x3C040000 | Hi(pool), 0x24840000 | Lo(pool)]),
            new("object memory, size (mem_init)", 0x99D0C, [0x24061388], [0x34060000 | (uint)ObjectMemorySize]),
            new("object memory, mem_remaining", 0x99D28, [0x3C0F8037, 0x25EFAA60], [0x3C0F0000 | Hi(pool), 0x25EF0000 | Lo(pool)]),
            new("object memory, start pointer", 0xD6D70, [0x8036AA60], [pool]),

            // 3. Radar goals: hud_8031A378 (0x8031A378) fills the module's list (its four bases and the count).
            new("radar goals, hopper goals (hud_8031A378)", 0xA18F4, [0x3C0F8037, 0x25EFC638],
                [0x3C0F0000 | Hi(goals), 0x25EF0000 | Lo(goals)]),
            new("radar goals, ball targets (hud_8031A378)", 0xA1964, [0x3C0B8037, 0x256BC638],
                [0x3C0B0000 | Hi(goals), 0x256B0000 | Lo(goals)]),
            new("radar goals, landing pads (hud_8031A378)", 0xA19E0, [0x3C058037, 0x24A5C638],
                [0x3C050000 | Hi(goals), 0x24A50000 | Lo(goals)]),
            new("radar goals, landing strips (hud_8031A378)", 0xA1A78, [0x3C0E8037, 0x25CEC638],
                [0x3C0E0000 | Hi(goals), 0x25CE0000 | Lo(goals)]),
            new("radar goals, count (hud_8031A378)", 0xA1B04, [0x3C018037], [0x3C010000 | Hi(goals)]),
            new("radar goals, count (hud_8031A378)", 0xA1B0C, [0xAC33C638], [0xAC330000 | Lo(goals)]),
        };

        // The six calls of hudDrawRadar (hudDrawHangGlider, hudDrawRocketPack, hudDrawGyrocopter, …).
        foreach (int site in (int[])[0x9FCC0, 0x9FD60, 0xA10F8, 0xA1358, 0xA1400, 0xA16D4])
        {
            patches.Add(new WordPatch("radar goals, hudDrawRadar", site, [radarJal], [CodeArea.Jal(GameModuleImage.HudRadarAddress)]));
        }

        return patches;
    }
}
