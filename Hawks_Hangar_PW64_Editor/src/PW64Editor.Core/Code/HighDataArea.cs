using PW64Editor.Core.Missions;

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
///   <item><b>Display objects</b> (dobj.c): the five tables of the kernel's display objects grow from 100 objects and 300
///         part matrices to <see cref="DisplayObjects"/> and <see cref="PartMatrices"/>. 65 references (lui with addiu, or
///         with an indexed load or store) get the new addresses; the loops that compare with a table's end get the new
///         end; the checks 100 / 101 / 300 become 256 / 768.</item>
///   <item><b>Matrix stack</b> (graphics.c): gGfxMstack[2][350] becomes [2][1024] at <see cref="MatrixStack"/>
///         (uvGfxMstackPushF, uvGfxMstackPushL, uvGfxMstackTop).</item>
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

    /// <summary>Display objects (dobj.c; the game: 100). Their ids are 0–255, which also fits the game's u8 copies of them.</summary>
    public const int DisplayObjects = 256;

    /// <summary>Part matrices of the display objects (the game: 300).</summary>
    public const int PartMatrices = 768;

    /// <summary>Matrices per frame (gGfxMstack, the game: 350).</summary>
    public const int FrameMatrices = 1024;

    /// <summary>The display objects (Unk80263780, 0x40 bytes each).</summary>
    public const uint Objects = ObjectMemory + ObjectMemorySize;

    /// <summary>Their part matrices (Mtx4F, 0x40 bytes each).</summary>
    public const uint PartMatrixTable = Objects + (DisplayObjects * 0x40);

    /// <summary>Which part matrices are in use (u8 each).</summary>
    public const uint PartsUsed = PartMatrixTable + (PartMatrices * 0x40);

    /// <summary>The objects to draw (u16 ids; D_80269CB0).</summary>
    public const uint DrawList = PartsUsed + PartMatrices;

    /// <summary>The objects drawn in this frame (s32 ids; D_80269D78).</summary>
    public const uint Drawn = DrawList + (DisplayObjects * 2);

    /// <summary>The matrix stack of the two frames (Mtx, 0x40 bytes each).</summary>
    public const uint MatrixStack = (Drawn + (DisplayObjects * 4) + 0xFFF) & ~0xFFFu;

    /// <summary>The objects of the map view (sMapObjects, u16 ids; the game: 60, without a check).</summary>
    public const uint MapObjects = MatrixStack + (2 * FrameMatrices * 0x40);

    /// <summary>Entries of the map view's list (its count is a u8).</summary>
    public const int MapObjectCount = 256;

    /// <summary>
    /// The display objects sorted for drawing in a frame (D_802629C8 in chan.c, 0x1C bytes each; the game: 60, more are
    /// not drawn: "_uvSortAdd: dobj list full").
    /// </summary>
    public const uint ObjectSortList = MapObjects + (MapObjectCount * 2);

    /// <summary>Where the raised object lists start (<see cref="ObjectLists"/>).</summary>
    public const uint ObjectListsStart = ObjectSortList + (DisplayObjects * 0x1C);

    /// <summary>First free address behind the fixed tables.</summary>
    public const uint FreeStart = ObjectListsStart;

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

        // 4. Display objects: every reference to the five tables of dobj.c (and the loops of code_8170.c and uvGfxEnd that
        //    count them) gets the new address; the counts and checks 100 / 101 / 300 become 256 / 768.
        foreach (Reference reference in References)
        {
            uint target = reference.Table switch
            {
                Table.Objects => Objects,
                Table.ObjectsEnd => Objects + (DisplayObjects * 0x40),
                Table.PartMatrices => PartMatrixTable,
                Table.PartMatricesEnd => PartMatrixTable + (PartMatrices * 0x40),
                Table.PartsUsed => PartsUsed,
                Table.PartsUsedEnd => PartsUsed + PartMatrices,
                Table.DrawList => DrawList,
                Table.DrawListEnd => DrawList + (DisplayObjects * 2),
                Table.Drawn => Drawn,
                _ => MatrixStack,
            };
            target += reference.Offset;
            string name = $"display objects, {reference.Table}";
            patches.Add(new WordPatch(name, reference.Lui, [reference.OriginalLui], [(reference.OriginalLui & 0xFFFF0000) | Hi(target)]));
            patches.Add(new WordPatch(name, reference.Lo, [reference.OriginalLo], [(reference.OriginalLo & 0xFFFF0000) | Lo(target)]));
        }

        uint objects = DisplayObjects, parts = PartMatrices;
        foreach ((int offset, uint original, uint value) in (ReadOnlySpan<(int, uint, uint)>)
                 [
                     (0x130C8, 0x29C10064, objects), (0x133BC, 0x29610064, objects), // code_8170.c, loops over the objects
                     (0x177E0, 0x28810064, objects), // uvDobjModel: objId >= 100
                     (0x17ADC, 0x2404012C, parts), (0x17AE0, 0x2841012C, parts), // uvDobjModel: 300 part transforms
                     (0x17BD4, 0x28810064, objects), // uvDobjProps
                     (0x17DBC, 0x28810065, objects), (0x17F90, 0x28810065, objects), // uvDobjPosm, uvDobjGetPosm (> 100)
                     (0x180D4, 0x28810065, objects), (0x1821C, 0x28810065, objects), (0x18274, 0x28810065, objects), // uvDobjState …
                     (0x182D8, 0x24050064, objects), // uvDobjAllocIdx
                 ])
        {
            patches.Add(new WordPatch("display objects, count", offset, [original], [(original & 0xFFFF0000) | value]));
        }

        // 5. Matrix stack: uvGfxMstackPushF, uvGfxMstackPushL (the check 350 and frame × 350 × 64) and uvGfxMstackTop.
        patches.Add(new WordPatch("matrix stack, check (uvGfxMstackPushF)", 0x24214, [0x28C1015E], [0x28C10000 | FrameMatrices]));
        patches.Add(new WordPatch("matrix stack, check (uvGfxMstackPushL)", 0x242B4, [0x28A1015E], [0x28A10000 | FrameMatrices]));
        uint[] chain = [0x00084880, 0x01284823, 0x00094880, 0x01284823, 0x00094900, 0x01284823, 0x000949C0];
        patches.Add(new WordPatch("matrix stack, frame offset (uvGfxMstackPushF)", 0x24250, chain, [0x00084C00, Nop, Nop, Nop, Nop, Nop, Nop]));
        patches.Add(new WordPatch("matrix stack, frame offset (uvGfxMstackPushL)", 0x242F8, chain, [0x00084C00, Nop, Nop, Nop, Nop, Nop, Nop]));
        patches.Add(new WordPatch("matrix stack, frame offset (uvGfxMstackTop)", 0x24384,
            [0x000E7880, 0x01EE7823, 0x000F7880, 0x01EE7823, 0x000F7900, 0x01EE7823, 0x000F79C0],
            [0x000E7C00, Nop, Nop, Nop, Nop, Nop, Nop]));

        // The developers' warning thresholds (uvGfxEnd): matrices per frame, part transforms, display objects.
        patches.Add(new WordPatch("matrix stack, developers' warning", 0x4A1E8, [294], [FrameMatrices - 64]));
        patches.Add(new WordPatch("part matrices, developers' warning", 0x4A1EC, [200], [PartMatrices - 68]));
        patches.Add(new WordPatch("display objects, developers' warning", 0x4A1F0, [100], [DisplayObjects - 16]));

        // 6. The map view's objects (map3d.c): 60 entries without a check (more overwrite sMapMessages); now 256. The 11
        //    checks "count >= 60" (only a message) follow.
        foreach ((int lui, int lo, uint originalLui, uint originalLo) in (ReadOnlySpan<(int, int, uint, uint)>)
                 [(0x97734, 0x97738, 0x3C1E8037, 0x27DEA8E8), (0x977E4, 0x977F0, 0x3C1E8037, 0x27DEA8E8), (0x99854, 0x99858, 0x3C108037, 0x2610A8E8)])
        {
            patches.Add(new WordPatch("map view objects (map3d.c)", lui, [originalLui], [(originalLui & 0xFFFF0000) | Hi(MapObjects)]));
            patches.Add(new WordPatch("map view objects (map3d.c)", lo, [originalLo], [(originalLo & 0xFFFF0000) | Lo(MapObjects)]));
        }

        foreach (int offset in (int[])[0x97A74, 0x97BB8, 0x97CC0, 0x97E30, 0x97F40, 0x980F0, 0x981E4, 0x98470, 0x98754, 0x98818, 0x9892C])
        {
            patches.Add(new WordPatch("map view objects, count (map3d.c)", offset, [0x28A1003C], [0x28A10000 | MapObjectCount]));
        }

        // 7. The display objects drawn in a frame (chan.c): _uvSortAdd and uvChan_80205CE4 use the new list; the check 60
        //    (and its message) becomes 256.
        patches.Add(new WordPatch("drawn display objects (_uvSortAdd)", 0x6864, [0x3C098026, 0x252929C8],
            [0x3C090000 | Hi(ObjectSortList), 0x25290000 | Lo(ObjectSortList)]));
        patches.Add(new WordPatch("drawn display objects (uvChan_80205CE4)", 0x6D44, [0x3C0A8026, 0x254A29C8],
            [0x3C0A0000 | Hi(ObjectSortList), 0x254A0000 | Lo(ObjectSortList)]));
        patches.Add(new WordPatch("drawn display objects, check (_uvSortAdd)", 0x6834, [0x2841003C], [0x28410000 | DisplayObjects]));
        patches.Add(new WordPatch("drawn display objects, message (_uvSortAdd)", 0x684C, [0x2405003C], [0x24050000 | DisplayObjects]));

        // The six calls of hudDrawRadar (hudDrawHangGlider, hudDrawRocketPack, hudDrawGyrocopter, …).
        foreach (int site in (int[])[0x9FCC0, 0x9FD60, 0xA10F8, 0xA1358, 0xA1400, 0xA16D4])
        {
            patches.Add(new WordPatch("radar goals, hudDrawRadar", site, [radarJal], [CodeArea.Jal(GameModuleImage.HudRadarAddress)]));
        }

        return patches;
    }

    /// <summary>
    /// Where the raised object lists of a project lie: one after the other from <see cref="ObjectListsStart"/>, each with
    /// room for exactly the project's limit (types without a raised limit keep the game's list).
    /// </summary>
    /// <exception cref="InvalidDataException">The lists do not fit into the area.</exception>
    public static IReadOnlyDictionary<MissionObjectKind, uint> ObjectLists(IReadOnlyDictionary<MissionObjectKind, int> raised)
    {
        var result = new Dictionary<MissionObjectKind, uint>();
        uint next = ObjectListsStart;
        foreach (ObjectList list in ObjectLimits.Lists.Where(l => raised.ContainsKey(l.Kind)))
        {
            result[list.Kind] = next;
            next = (next + (uint)(raised[list.Kind] * list.ElementSize) + 7) & ~7u;
        }

        if (next > End)
        {
            throw new InvalidDataException("The object lists do not fit into the high data area.");
        }

        return result;
    }

    /// <summary>
    /// The changes for a project's raised object limits: every reference to a raised list gets its new address (loops that
    /// run to the list's end get the new end), and the check of the mission's count ("too many … defined in level") the
    /// new limit. Empty without raised limits.
    /// </summary>
    public static IReadOnlyList<WordPatch> ObjectListPatches(IReadOnlyDictionary<MissionObjectKind, int> raised)
    {
        var patches = new List<WordPatch>();
        foreach ((MissionObjectKind kind, uint address) in ObjectLists(raised))
        {
            ObjectList list = ObjectLimits.Of(kind)!;
            uint end = address + (uint)(raised[kind] * list.ElementSize);
            string name = $"object list, {kind}";
            foreach (ListReference reference in ListReferences[kind])
            {
                uint target = reference.IsEnd ? end : address + reference.Offset;
                patches.Add(new WordPatch(name, reference.Lui, [reference.OriginalLui], [(reference.OriginalLui & 0xFFFF0000) | Hi(target)]));
                patches.Add(new WordPatch(name, reference.Lo, [reference.OriginalLo], [(reference.OriginalLo & 0xFFFF0000) | Lo(target)]));
            }

            foreach ((int offset, uint original, int add) in ListCounts[kind])
            {
                patches.Add(new WordPatch(name + ", count", offset, [original], [(original & 0xFFFF0000) | (uint)(raised[kind] + add)]));
            }
        }

        return patches;
    }

    /// <summary>
    /// The raised object limits of a ROM built by the editor (from the loaders' checks), as stored in a project
    /// (<see cref="Project.ProjectSettings.ObjectLimits"/>), or null if it has the game's lists.
    /// </summary>
    public static Dictionary<string, int>? ReadObjectLimits(ReadOnlySpan<byte> rom)
    {
        var result = new Dictionary<string, int>();
        foreach ((MissionObjectKind kind, (int Offset, uint Original, int Add)[] counts) in ListCounts)
        {
            (int offset, uint original, int add) = counts[0];
            if (offset + 4 > rom.Length)
            {
                return null;
            }

            uint word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(rom[offset..]);
            int limit = (int)(word & 0xFFFF) - add;
            if (word != original && (word & 0xFFFF0000) == (original & 0xFFFF0000) && ObjectLimits.Of(kind) is { } list
                && limit > list.GameLimit && limit <= list.Cap)
            {
                result[kind.ToString()] = limit;
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>A lui/addiu pair (or lui with an indexed load or store) of an object list: its start (+ a field) or its end.</summary>
    private sealed record ListReference(int Lui, int Lo, uint OriginalLui, uint OriginalLo, bool IsEnd, uint Offset);

    /// <summary>
    /// All references to the six object lists in the clean ROM (app code; the RAM address in the comment). The words behind a
    /// list belong to other variables (gRingChildIndexes behind gRings, gThermReady behind gThermals, a float behind gBalls);
    /// only the loops of ringsInit, hoverPadInit, targetsInit and thermInit use a list's end.
    /// </summary>
    private static readonly Dictionary<MissionObjectKind, ListReference[]> ListReferences = new()
    {
        [MissionObjectKind.Ring] =
        [
            new(0x97838, 0x97840, 0x3C0D8037, 0x91ADDC2C, false, 0x1B4), // 0x80310310
            new(0x978C0, 0x978C4, 0x3C0D8037, 0x25ADDA78, false, 0x0), // 0x80310394
            new(0x979E0, 0x979E4, 0x3C088037, 0x2508DA78, false, 0x0), // 0x803104B4
            new(0xAA820, 0xAA830, 0x3C038037, 0x2463DA78, false, 0x0), // 0x80323300
            new(0xAA824, 0xAA82C, 0x3C058037, 0x24A51060, true, 0x0), // 0x803232FC
            new(0xAA8D4, 0xAA8E8, 0x3C108037, 0x2610DA78, false, 0x0), // 0x803233B8
            new(0xAAF74, 0xAAF78, 0x3C108037, 0x2610DA78, false, 0x0), // 0x80323A48
            new(0xAAFC8, 0xAAFCC, 0x3C098037, 0x2529DA78, false, 0x0), // 0x80323A9C
            new(0xAB288, 0xAB28C, 0x3C098037, 0x2529DA78, false, 0x0), // 0x80323D5C
            new(0xAB33C, 0xAB340, 0x3C168037, 0x26D6DA78, false, 0x0), // 0x80323E10
            new(0xAB47C, 0xAB480, 0x3C168037, 0x26D6DA78, false, 0x0), // 0x80323F50
            new(0xAB57C, 0xAB580, 0x3C0F8037, 0x25EFDA78, false, 0x0), // 0x80324050
            new(0xAB72C, 0xAB730, 0x3C198037, 0x2739DA78, false, 0x0), // 0x80324200
            new(0xAB960, 0xAB964, 0x3C108037, 0x2610DA78, false, 0x0), // 0x80324434
            new(0xAB9C0, 0xAB9C4, 0x3C098037, 0x2529DA78, false, 0x0), // 0x80324494
            new(0xAB9F4, 0xAB9F8, 0x3C0C8037, 0x258CDA78, false, 0x0), // 0x803244C8
            new(0xABCA4, 0xABCA8, 0x3C088037, 0x2508DA78, false, 0x0), // 0x80324778
            new(0xABCE8, 0xABD00, 0x3C108037, 0x2610DA78, false, 0x0), // 0x803247D0
            new(0xABF9C, 0xABFA0, 0x3C108037, 0x2610DA78, false, 0x0), // 0x80324A70
            new(0xAC02C, 0xAC038, 0x3C0E8037, 0x25C2DA78, false, 0x0), // 0x80324B08
            new(0xAC0B0, 0xAC0BC, 0x3C048037, 0x2484DA78, false, 0x0), // 0x80324B8C
            new(0xAC0E8, 0xAC0EC, 0x3C0F8037, 0x25EFDA78, false, 0x0), // 0x80324BBC
            new(0xAC12C, 0xAC130, 0x3C188037, 0x2718DA78, false, 0x0), // 0x80324C00
        ],
        [MissionObjectKind.Balloon] =
        [
            new(0x52230, 0x52240, 0x3C028035, 0x24427218, false, 0x0), // 0x802CAD10
            new(0x524D0, 0x524DC, 0x3C108035, 0x26107218, false, 0x0), // 0x802CAFAC
            new(0x5266C, 0x52670, 0x3C108035, 0x26107218, false, 0x0), // 0x802CB140
            new(0x526B4, 0x526B8, 0x3C0B8035, 0x256B7218, false, 0x0), // 0x802CB188
            new(0x527C0, 0x527C4, 0x3C0B8035, 0x256B7218, false, 0x0), // 0x802CB294
            new(0x528C0, 0x528C4, 0x3C108035, 0x26107218, false, 0x0), // 0x802CB394
            new(0x52964, 0x52968, 0x3C108035, 0x26107218, false, 0x0), // 0x802CB438
            new(0x529A4, 0x529A8, 0x3C0A8035, 0x254A7218, false, 0x0), // 0x802CB478
            new(0x529D8, 0x529DC, 0x3C0D8035, 0x25AD7218, false, 0x0), // 0x802CB4AC
            new(0x52A44, 0x52A48, 0x3C088035, 0x25087218, false, 0x0), // 0x802CB518
            new(0x52F80, 0x52FCC, 0x3C088035, 0x25087218, false, 0x0), // 0x802CBA9C
            new(0x53458, 0x5345C, 0x3C048035, 0x24847218, false, 0x0), // 0x802CBF2C
            new(0x53504, 0x53508, 0x3C0A8035, 0x254A7218, false, 0x0), // 0x802CBFD8
            new(0x535A4, 0x535B4, 0x3C188035, 0x27027218, false, 0x0), // 0x802CC084
            new(0x53628, 0x5362C, 0x3C068035, 0x24C67218, false, 0x0), // 0x802CC0FC
            new(0x536AC, 0x536B0, 0x3C058035, 0x24A57218, false, 0x0), // 0x802CC180
            new(0x5A1A0, 0x5A1B0, 0x3C108035, 0x26107218, false, 0x0), // 0x802D2C80
            new(0x5A1DC, 0x5A1E0, 0x3C098035, 0x25297218, false, 0x0), // 0x802D2CB0
            new(0x5A30C, 0x5A310, 0x3C0D8035, 0x25AD7218, false, 0x0), // 0x802D2DE0
            new(0x5A434, 0x5A438, 0x3C108035, 0x26107218, false, 0x0), // 0x802D2F08
            new(0x98784, 0x98788, 0x3C118035, 0x26317218, false, 0x0), // 0x80311258
        ],
        [MissionObjectKind.HoverPad] =
        [
            new(0x90D10, 0x90D20, 0x3C038037, 0x24639588, false, 0x0), // 0x803097F0
            new(0x90D14, 0x90D1C, 0x3C048037, 0x24849E48, true, 0x0), // 0x803097EC
            new(0x90DD8, 0x90DEC, 0x3C108037, 0x26109588, false, 0x0), // 0x803098BC
            new(0x91018, 0x9101C, 0x3C108037, 0x26109588, false, 0x0), // 0x80309AEC
            new(0x9118C, 0x91190, 0x3C158037, 0x26B59588, false, 0x0), // 0x80309C60
            new(0x912A8, 0x912B0, 0x3C0F8037, 0x25EF9588, false, 0x0), // 0x80309D80
            new(0x913A0, 0x913A4, 0x3C0F8037, 0x25EF9588, false, 0x0), // 0x80309E74
            new(0x913E0, 0x913E4, 0x3C0F8037, 0x25EF9588, false, 0x0), // 0x80309EB4
            new(0x91480, 0x91488, 0x3C048037, 0x24849588, false, 0x0), // 0x80309F58
            new(0x91554, 0x91558, 0x3C108037, 0x26109588, false, 0x0), // 0x8030A028
            new(0x915B8, 0x915C4, 0x3C0E8037, 0x25C29588, false, 0x0), // 0x8030A094
            new(0x91614, 0x91620, 0x3C0E8037, 0x25C29588, false, 0x0), // 0x8030A0F0
            new(0x97F88, 0x97F90, 0x3C0C8037, 0x918C95F0, false, 0x68), // 0x80310A60
            new(0x97FE8, 0x97FEC, 0x3C0C8037, 0x258C9588, false, 0x0), // 0x80310ABC
        ],
        [MissionObjectKind.Target] =
        [
            new(0x98134, 0x98138, 0x3C118038, 0x26318CF8, false, 0x0), // 0x80310C08
            new(0xCB7C0, 0xCB7CC, 0x3C038038, 0x24638CF8, false, 0x0), // 0x8034429C
            new(0xCB7C4, 0xCB7C8, 0x3C058038, 0x24A598D8, true, 0x0), // 0x80344298
            new(0xCB8BC, 0xCB8C0, 0x3C118038, 0x26318CF8, false, 0x0), // 0x80344390
            new(0xCBAB4, 0xCBAB8, 0x3C108038, 0x26108CF8, false, 0x0), // 0x80344588
            new(0xCBBE4, 0xCBBE8, 0x3C108038, 0x26108CF8, false, 0x0), // 0x803446B8
            new(0xCBC84, 0xCBC88, 0x3C108038, 0x26108CF8, false, 0x0), // 0x80344758
            new(0xCBCA4, 0xCBCA8, 0x3C188038, 0x27188CF8, false, 0x0), // 0x80344778
            new(0xCBE2C, 0xCBE38, 0x3C0E8038, 0x25C28CF8, false, 0x0), // 0x80344908
            new(0xCBE80, 0xCBE8C, 0x3C0E8038, 0x25C28CF8, false, 0x0), // 0x8034495C
        ],
        [MissionObjectKind.Thermal] =
        [
            new(0xA1B14, 0xA1B18, 0x3C028038, 0x2442AB58, false, 0x0), // 0x8031A5E8
            new(0xCDB20, 0xCDB2C, 0x3C038038, 0x2463AB58, false, 0x0), // 0x803465FC
            new(0xCDB24, 0xCDB28, 0x3C048038, 0x2484ACD8, true, 0x0), // 0x803465F8
            new(0xCDC28, 0xCDC5C, 0x3C118038, 0x2631AB58, false, 0x0), // 0x8034672C
            new(0xCDC48, 0xCDC58, 0x3C148038, 0x2694AB5C, false, 0x4), // 0x80346728
            new(0xCDF44, 0xCDF48, 0x3C108038, 0x2610AB58, false, 0x0), // 0x80346A18
            new(0xCE0DC, 0xCE0E0, 0x3C108038, 0x2610AB58, false, 0x0), // 0x80346BB0
            new(0xCE1A0, 0xCE1AC, 0x3C128038, 0x2652AB58, false, 0x0), // 0x80346C7C
        ],
        [MissionObjectKind.LocalWind] =
        [
            new(0xD4348, 0xD4364, 0x3C118038, 0x2631F518, false, 0x0), // 0x8034CE34
            new(0xD434C, 0xD4360, 0x3C138038, 0x2673F51C, false, 0x4), // 0x8034CE30
            new(0xD4A10, 0xD4A14, 0x3C108038, 0x2610F518, false, 0x0), // 0x8034D4E4
            new(0xD4AE8, 0xD4AEC, 0x3C138038, 0x2673F518, false, 0x0), // 0x8034D5BC
        ],
    };

    /// <summary>
    /// The counts of the lists in the code: (ROM offset, instruction, what is added to the limit). ringsLoad and the other
    /// loaders refuse "count > limit" (slti count, limit + 1); ballsInit clears the list, balls_802CB9B4 lets a balloon split
    /// only while the list has room.
    /// </summary>
    private static readonly Dictionary<MissionObjectKind, (int Offset, uint Original, int Add)[]> ListCounts = new()
    {
        [MissionObjectKind.Ring] = [(0xAAF40, 0x28A1001F, 1)],
        [MissionObjectKind.Balloon] = [(0x52248, 0x24060014, 0), (0x52618, 0x28A10015, 1), (0x52F8C, 0x29C10014, 0)],
        [MissionObjectKind.HoverPad] = [(0x90FE4, 0x28A10015, 1)],
        [MissionObjectKind.Target] = [(0xCB888, 0x28A10029, 1)],
        [MissionObjectKind.Thermal] = [(0xCDBE8, 0x28A10011, 1)],
        [MissionObjectKind.LocalWind] = [(0xD4308, 0x28A10007, 1)],
    };

    /// <summary>The tables a reference points to (dobj.c, graphics.c).</summary>
    private enum Table
    {
        Objects,
        ObjectsEnd,
        PartMatrices,
        PartMatricesEnd,
        PartsUsed,
        PartsUsedEnd,
        DrawList,
        DrawListEnd,
        Drawn,
        MatrixStack,
    }

    /// <summary>A lui/addiu pair (kernel ROM offsets) that forms the address of a table or of its end.</summary>
    /// <remarks>The second instruction is an addiu, or a load or store with the address's lower half (indexed accesses
    /// "lui at, hi; addu at, at, index; lhu v0, lo(at)"), then <paramref name="Offset"/> is the field it reads.</remarks>
    private sealed record Reference(int Lui, int Lo, uint OriginalLui, uint OriginalLo, Table Table, uint Offset = 0);

    /// <summary>
    /// All references to D_80263780 (objects), D_80265080 (part matrices), D_80269B80 (in use), D_80269CB0 (draw list),
    /// D_80269D78 (drawn) and gGfxMstack in the clean ROM, with their RAM address. Loops that run over a table compare
    /// with its end, which is the next table's start; the end references are told apart by reading the code.
    /// </summary>
    private static readonly Reference[] References =
    [
        // indexed accesses
        new(0x01610, 0x01620, 0x3C158026, 0x96B53780, Table.Objects), // 0x80200670 lhu s5, modelId
        new(0x1815C, 0x18164, 0x3C018027, 0xA4309CB0, Table.DrawList), // 0x802171B4 sh s0 (uvDobjState)
        new(0x181F8, 0x18200, 0x3C018026, 0xA03937B4, Table.Objects, 0x34), // 0x80217250 sb t9, unk34 (uvDobjState)
        new(0x18B18, 0x18B28, 0x3C018027, 0xAC389D78, Table.Drawn), // 0x80217B78 sw t8 (uvDobj_80217B4C)
        new(0x18E18, 0x18E2C, 0x3C018027, 0xAC399D78, Table.Drawn), // 0x80217E7C sw t9 (uvDobj_80217E24)
        new(0x12FDC, 0x13008, 0x3C148026, 0x26943780, Table.Objects), // 0x80212058
        new(0x12FE0, 0x13004, 0x3C158026, 0x26B55080, Table.PartMatrices), // 0x80212054
        new(0x13258, 0x1325C, 0x3C0C8026, 0x258C3780, Table.Objects), // 0x802122AC
        new(0x13278, 0x1327C, 0x3C198026, 0x27395080, Table.PartMatrices), // 0x802122CC
        new(0x147CC, 0x147D0, 0x3C0C8026, 0x258C5080, Table.PartMatrices), // 0x80213820
        new(0x14A24, 0x14A28, 0x3C0D8026, 0x25AD5080, Table.PartMatrices), // 0x80213A78
        new(0x177A0, 0x177A4, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x802167F4
        new(0x17844, 0x17848, 0x3C058027, 0x24A59B80, Table.PartsUsed), // 0x80216898
        new(0x1786C, 0x17878, 0x3C058027, 0x24A59B80, Table.PartsUsed), // 0x802168C8
        new(0x178C8, 0x178CC, 0x3C088027, 0x25089CB0, Table.DrawList), // 0x8021691C
        new(0x178F4, 0x178F8, 0x3C0F8027, 0x25EF9CB0, Table.DrawList), // 0x80216948
        new(0x1799C, 0x179A4, 0x3C058027, 0x24A59B80, Table.PartsUsed), // 0x802169F4
        new(0x179BC, 0x179C0, 0x3C058027, 0x24A59B80, Table.PartsUsed), // 0x80216A10
        new(0x179CC, 0x179D0, 0x3C058027, 0x24A59B80, Table.PartsUsed), // 0x80216A20
        new(0x17B60, 0x17B68, 0x3C198026, 0x27395080, Table.PartMatrices), // 0x80216BB8
        new(0x17C2C, 0x17C30, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x80216C80
        new(0x17DEC, 0x17DF0, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x80216E40
        new(0x17E04, 0x17E34, 0x3C0A8026, 0x254A5080, Table.PartMatrices), // 0x80216E84
        new(0x17E6C, 0x17E70, 0x3C0A8026, 0x254A5080, Table.PartMatrices), // 0x80216EC0
        new(0x17FC0, 0x17FC4, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x80217014
        new(0x18008, 0x1800C, 0x3C0B8026, 0x256B5080, Table.PartMatrices), // 0x8021705C
        new(0x18128, 0x18134, 0x3C058027, 0x24A59CB0, Table.DrawList), // 0x80217184
        new(0x1817C, 0x18188, 0x3C058027, 0x24A59CB0, Table.DrawList), // 0x802171D8
        new(0x181A8, 0x181AC, 0x3C0D8027, 0x25AD9CB0, Table.DrawList), // 0x802171FC
        new(0x18244, 0x18248, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x80217298
        new(0x1829C, 0x182A0, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x802172F0
        new(0x182CC, 0x182D0, 0x3C028026, 0x24423780, Table.Objects), // 0x80217320
        new(0x1834C, 0x18364, 0x3C028026, 0x24423780, Table.Objects), // 0x802173B4
        new(0x18350, 0x18360, 0x3C048026, 0x24845080, Table.ObjectsEnd), // 0x802173B0
        new(0x1837C, 0x18388, 0x3C028027, 0x24429CB0, Table.DrawList), // 0x802173D8
        new(0x18380, 0x18384, 0x3C048027, 0x24849D78, Table.DrawListEnd), // 0x802173D4
        new(0x1839C, 0x183A8, 0x3C028027, 0x24429B80, Table.PartsUsed), // 0x802173F8
        new(0x183A0, 0x183A4, 0x3C038027, 0x24639CAC, Table.PartsUsedEnd), // 0x802173F4
        new(0x183BC, 0x183C8, 0x3C108026, 0x26105080, Table.PartMatrices), // 0x80217418
        new(0x183C0, 0x183C4, 0x3C118027, 0x26319B80, Table.PartMatricesEnd), // 0x80217414
        new(0x1843C, 0x18450, 0x3C128026, 0x26523780, Table.Objects), // 0x802174A0
        new(0x184D0, 0x184E0, 0x3C0E8026, 0x25CE5080, Table.PartMatrices), // 0x80217530
        new(0x18680, 0x18684, 0x3C0E8026, 0x25CE5080, Table.ObjectsEnd), // 0x802176D4
        new(0x18738, 0x1873C, 0x3C148027, 0x26949CB0, Table.DrawList), // 0x8021778C
        new(0x18748, 0x18754, 0x3C178026, 0x26F73780, Table.Objects), // 0x802177A4
        new(0x1876C, 0x18770, 0x3C088027, 0x25089CB0, Table.DrawList), // 0x802177C0
        new(0x1879C, 0x187AC, 0x3C0C8027, 0x258C9CB0, Table.DrawList), // 0x802177FC
        new(0x187A8, 0x187BC, 0x3C088026, 0x25085080, Table.PartMatrices), // 0x8021780C
        new(0x18984, 0x18988, 0x3C0A8027, 0x254A9CB0, Table.DrawList), // 0x802179D8
        new(0x189FC, 0x18A00, 0x3C098027, 0x25299CB0, Table.DrawList), // 0x80217A50
        new(0x18AE4, 0x18AE8, 0x3C0E8027, 0x25CE9D78, Table.Drawn), // 0x80217B38
        new(0x18B0C, 0x18B10, 0x3C0E8026, 0x25CE3780, Table.Objects), // 0x80217B60
        new(0x18B8C, 0x18B94, 0x3C1E8026, 0x27DE5080, Table.PartMatrices), // 0x80217BE4
        new(0x18D18, 0x18D24, 0x3C1E8026, 0x27DE5080, Table.PartMatrices), // 0x80217D74
        new(0x18DE4, 0x18DE8, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x80217E38
        new(0x18E3C, 0x18E40, 0x3C168026, 0x26D65080, Table.PartMatrices), // 0x80217E90
        new(0x19090, 0x19094, 0x3C0F8026, 0x25EF3780, Table.Objects), // 0x802180E4
        new(0x19104, 0x19108, 0x3C158026, 0x26B55080, Table.PartMatrices), // 0x80218158
        new(0x2345C, 0x23478, 0x3C028027, 0x24429B80, Table.PartsUsed), // 0x802224C8
        new(0x23460, 0x23474, 0x3C038027, 0x24639CAC, Table.PartsUsedEnd), // 0x802224C4
        new(0x2349C, 0x234A8, 0x3C028026, 0x24423780, Table.Objects), // 0x802224F8
        new(0x234A0, 0x234A4, 0x3C048026, 0x24845080, Table.ObjectsEnd), // 0x802224F4
        new(0x2424C, 0x24270, 0x3C0C802B, 0x258C9988, Table.MatrixStack), // 0x802232C0
        new(0x242F4, 0x24318, 0x3C0C802B, 0x258C9988, Table.MatrixStack), // 0x80223368
        new(0x243A0, 0x243AC, 0x3C09802B, 0x25299988, Table.MatrixStack), // 0x802233FC
    ];
}
