using System.Buffers.Binary;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.FileSystem;

/// <summary>
/// Reads the file table of Pilotwings 64.
/// </summary>
/// <remarks>
/// <para>The table is stored in the ROM as a FORM of type "UVRM":</para>
/// <code>
/// FORM "UVRM"
///   PAD   (4 bytes, ignored)
///   PAD   (4 bytes, ignored)
///   GZIP  wrapping "TABL": the actual table, MIO0 compressed
/// </code>
/// <para>
/// The TABL payload is a list of 8-byte entries: FourCC type + big-endian u32 size.
/// We accept an uncompressed TABL chunk as well, because the game's reader would too.
/// </para>
/// </remarks>
public static class FileTable
{
    /// <summary>Form type of the file table container.</summary>
    public const string FormType = "UVRM";

    /// <summary>Chunk tag of the table itself.</summary>
    public const string TableTag = "TABL";

    /// <summary>
    /// Reads the table FORM at the start of <paramref name="data"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The table is missing or corrupt.</exception>
    public static IReadOnlyList<FileTableEntry> Read(ReadOnlySpan<byte> data)
    {
        IffForm form = IffForm.Parse(data);
        if (form.FormType != FormType)
        {
            throw new InvalidDataException($"Expected file table FORM type '{FormType}', found '{form.FormType}'.");
        }

        foreach (IffChunk chunk in form.Chunks)
        {
            (string tag, byte[] payload) = GzipChunk.ReadChunkData(data, chunk);
            if (tag == TableTag)
            {
                return ParseEntries(payload);
            }
        }

        throw new InvalidDataException($"File table FORM contains no '{TableTag}' chunk.");
    }

    /// <summary>Parses the decompressed TABL payload.</summary>
    public static IReadOnlyList<FileTableEntry> ParseEntries(ReadOnlySpan<byte> table)
    {
        if (table.Length % FileTableEntry.EntrySize != 0)
        {
            throw new InvalidDataException($"File table size 0x{table.Length:X} is not a multiple of 8.");
        }

        var entries = new List<FileTableEntry>(table.Length / FileTableEntry.EntrySize);
        for (int pos = 0; pos < table.Length; pos += FileTableEntry.EntrySize)
        {
            ReadOnlySpan<byte> raw = table.Slice(pos, FileTableEntry.EntrySize);
            string? type = FourCC.IsZero(raw) ? null : FourCC.Read(raw);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(raw[4..]);

            if (size > int.MaxValue)
            {
                throw new InvalidDataException($"File table entry {pos / 8} has an invalid size 0x{size:X}.");
            }

            entries.Add(new FileTableEntry(type, (int)size));
        }

        return entries;
    }
}
