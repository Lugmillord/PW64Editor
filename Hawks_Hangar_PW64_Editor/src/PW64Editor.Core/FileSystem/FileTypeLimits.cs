namespace PW64Editor.Core.FileSystem;

/// <summary>
/// How many files of each type the game can address.
/// </summary>
/// <remarks>
/// <para>
/// At startup the game walks the file table and stores the ROM address of every file in a
/// fixed-size array per type (struct UVBlockOffsets in include/kernel/uv_memory.h of the
/// decompilation). Files are then requested by type and index, e.g. "texture number 10".
/// The game does not check these limits, so exceeding one overwrites unrelated memory.
/// </para>
/// <para>
/// File types not listed here are "user files" (e.g. UPWT missions, PDAT, SPTH). They all
/// share a single array and a single index counter, in table order.
/// </para>
/// </remarks>
public static class FileTypeLimits
{
    /// <summary>Name used for the shared group of all user file types.</summary>
    public const string UserFileGroup = "user";

    /// <summary>Maximum number of user files (all types together).</summary>
    public const int UserFileLimit = 0x80;

    private static readonly Dictionary<string, int> KernelTypeLimits = new()
    {
        ["UVSY"] = 1,     // system settings
        ["UVMD"] = 0x190, // models
        ["UVCT"] = 0x80,  // contours / collision
        ["UVTX"] = 0x1F4, // textures
        ["UVEN"] = 0x20,  // environments
        ["UVLT"] = 4,     // lights
        ["UVTR"] = 0xA,   // terrains
        ["UVSQ"] = 0xA,   // sequences
        ["UVLV"] = 0x96,  // levels
        ["UVAN"] = 0xAA,  // animations
        ["UVFT"] = 0x14,  // fonts
        ["UVBT"] = 0x7D,  // blits (2D images)
        ["UVSX"] = 1,     // sound effects
        ["UVTP"] = 1,     // texture palette data
    };

    /// <summary>All file types with their own array in the game ("kernel" types).</summary>
    public static IReadOnlyCollection<string> KernelTypes => KernelTypeLimits.Keys;

    /// <summary>True if the type has its own array; false if it is a user file.</summary>
    public static bool IsKernelType(string fileType) => KernelTypeLimits.ContainsKey(fileType);

    /// <summary>
    /// Returns the lookup group a file type belongs to: the type itself for kernel types,
    /// or <see cref="UserFileGroup"/> for everything else.
    /// </summary>
    public static string GetGroup(string fileType) => IsKernelType(fileType) ? fileType : UserFileGroup;

    /// <summary>Returns the maximum number of files in a lookup group.</summary>
    public static int GetLimit(string group) =>
        group == UserFileGroup ? UserFileLimit : KernelTypeLimits[group];
}
