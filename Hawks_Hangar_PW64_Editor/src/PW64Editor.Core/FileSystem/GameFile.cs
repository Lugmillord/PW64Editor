namespace PW64Editor.Core.FileSystem;

/// <summary>
/// One file from the Pilotwings 64 file system.
/// </summary>
/// <param name="TableIndex">Position in the file table (0 = first entry).</param>
/// <param name="FileType">FourCC of the file, e.g. "UVTX".</param>
/// <param name="Group">Lookup group: the file type for kernel types, or "user" for user files.
/// See <see cref="FileTypeLimits"/>.</param>
/// <param name="GroupIndex">The index the game uses to request this file within its group,
/// e.g. 10 for "texture number 10".</param>
/// <param name="RomOffset">Where the file starts in the ROM.</param>
/// <param name="Data">The complete file (FORM header and all chunks) as stored in the ROM.</param>
public sealed record GameFile(
    int TableIndex,
    string FileType,
    string Group,
    int GroupIndex,
    int RomOffset,
    byte[] Data)
{
    /// <summary>Size of the file in bytes.</summary>
    public int Size => Data.Length;

    /// <summary>
    /// A file name for exporting, unique and sortable in table order, e.g. "0042_UVTX_017.bin".
    /// </summary>
    public string ExportName => $"{TableIndex:D4}_{FileType}_{GroupIndex:D3}.bin";
}
