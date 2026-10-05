using System.Buffers.Binary;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.FileSystem;

/// <summary>
/// Creates the "UVRM" FORM that holds the compressed file table.
/// </summary>
/// <remarks>
/// The original table FORM contains two 4-byte "PAD " chunks before the GZIP chunk. The game
/// ignores them (it only looks for the TABL chunk), so if space gets tight we leave them out.
/// </remarks>
public static class FileTableWriter
{
    private const string PadTag = "PAD ";

    /// <summary>Serializes table entries into the raw (decompressed) TABL format.</summary>
    public static byte[] SerializeEntries(IReadOnlyList<FileTableEntry> entries)
    {
        byte[] table = new byte[entries.Count * FileTableEntry.EntrySize];
        for (int i = 0; i < entries.Count; i++)
        {
            Span<byte> slot = table.AsSpan(i * FileTableEntry.EntrySize, FileTableEntry.EntrySize);
            if (entries[i].FileType is { } type)
            {
                FourCC.Write(slot, type);
            }

            BinaryPrimitives.WriteUInt32BigEndian(slot[4..], (uint)entries[i].Size);
        }

        return table;
    }

    /// <summary>
    /// Builds the complete table FORM, choosing the variant with PAD chunks if it fits
    /// into <paramref name="maxSize"/> bytes, otherwise the one without.
    /// </summary>
    /// <exception cref="FileSystemFullException">The table does not fit even without padding.</exception>
    public static byte[] BuildForm(IReadOnlyList<FileTableEntry> entries, int maxSize)
    {
        byte[] rawTable = SerializeEntries(entries);
        byte[] gzipChunk = IffWriter.BuildGzipChunk(FileTable.TableTag, rawTable);
        byte[] padChunk = IffWriter.BuildChunk(PadTag, new byte[4]);

        byte[] withPadding = IffWriter.BuildForm(FileTable.FormType, [padChunk, padChunk, gzipChunk]);
        if (withPadding.Length <= maxSize)
        {
            return withPadding;
        }

        byte[] withoutPadding = IffWriter.BuildForm(FileTable.FormType, [gzipChunk]);
        if (withoutPadding.Length <= maxSize)
        {
            return withoutPadding;
        }

        throw new FileSystemFullException(
            $"The compressed file table needs {withoutPadding.Length:N0} bytes, " +
            $"but only {maxSize:N0} bytes are reserved for it.");
    }
}
