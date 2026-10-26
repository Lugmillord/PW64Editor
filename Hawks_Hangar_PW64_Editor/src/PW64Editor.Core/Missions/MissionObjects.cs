using PW64Editor.Core.Code;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Missions;

/// <summary>
/// How much of the game's object memory a mission uses (stage 8): taskLoadCommObj (task.c) takes one block per object
/// type from mem_get (mem.c), count (COMM block) × size of the type, rounded up to 8 bytes. If the memory runs out, the
/// game copies the objects to address 0 and crashes.
/// </summary>
/// <remarks>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</remarks>
public static class MissionObjects
{
    /// <summary>Bytes per object of each type (TaskTHER … TaskOBSV in task.h), in the order of <see cref="MissionObjectKind"/>.</summary>
    private static readonly int[] Sizes = [0x28, 0x54, 0x30, 0x30, 0x24, 0x84, 0x68, 0x20, 0x40, 0x1C, 0x14, 0xAC, 0x4C, 0x1C, 0x20, 0x10];

    /// <summary>Bytes one object of a type takes.</summary>
    public static int Size(MissionObjectKind kind) => Sizes[(int)kind];

    /// <summary>Bytes of the object memory a mission uses (the blocks of all types, each rounded up to 8).</summary>
    public static int MemoryUsed(MissionComm comm) =>
        Enum.GetValues<MissionObjectKind>().Sum(kind => ((comm.GetObjectCount(kind) * Size(kind)) + 7) & ~7);

    /// <summary>What is wrong with a mission's objects, or null: more object memory than the game has.</summary>
    /// <param name="comm">The mission's COMM block.</param>
    /// <param name="limit">The bytes the game allows (<see cref="HighDataArea.ObjectMemoryLimit"/> in a hack).</param>
    public static string? Problem(MissionComm comm, int limit = HighDataArea.ObjectMemoryLimit)
    {
        int used = MemoryUsed(comm);
        return used < limit
            ? null
            : CoreText.F("The objects of {0} need {1} bytes of object memory, the game has {2}.", MissionCatalog.Label(comm.Id), used, limit);
    }
}
