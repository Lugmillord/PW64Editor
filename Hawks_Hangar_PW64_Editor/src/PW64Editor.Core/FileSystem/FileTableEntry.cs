namespace PW64Editor.Core.FileSystem;

/// <summary>
/// One entry of the game's file table: just the file type and size, no offset.
/// The game computes offsets by adding up the sizes of all preceding entries.
/// </summary>
/// <param name="FileType">FourCC of the file (equal to the FORM type), or <c>null</c> for an
/// entry with tag 0, which the game skips (its size is still added to the running offset).</param>
/// <param name="Size">Size of the file in bytes, including its FORM header.</param>
public sealed record FileTableEntry(string? FileType, int Size)
{
    /// <summary>Size of one table entry in bytes (4 bytes tag + 4 bytes size).</summary>
    public const int EntrySize = 8;
}
