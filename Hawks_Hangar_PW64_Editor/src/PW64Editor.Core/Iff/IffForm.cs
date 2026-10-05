using System.Buffers.Binary;

namespace PW64Editor.Core.Iff;

/// <summary>
/// Reads the IFF-style "FORM" containers that every Pilotwings 64 game file is stored in.
/// </summary>
/// <remarks>
/// <para>Layout of a FORM (all values big-endian):</para>
/// <code>
/// 0x00  "FORM"
/// 0x04  u32   size of everything that follows (= total size - 8)
/// 0x08  4     form type, e.g. "UVTX" (texture) or "UPWT" (mission)
/// 0x0C  ...   chunks, back to back: tag (4) + payload size (4) + payload
/// </code>
/// <para>
/// Unlike classic IFF (Amiga/EA), Pilotwings does NOT pad odd-sized chunks to an even length:
/// the game's reader (uvFileReadBlock in the decompilation) simply advances by size + 8.
/// We mirror the game's behavior exactly.
/// </para>
/// </remarks>
public sealed class IffForm
{
    /// <summary>The "FORM" header size: magic + size + form type.</summary>
    public const int HeaderSize = 12;

    /// <summary>Magic "FORM".</summary>
    public const string FormMagic = "FORM";

    private IffForm(string formType, int totalSize, IReadOnlyList<IffChunk> chunks)
    {
        FormType = formType;
        TotalSize = totalSize;
        Chunks = chunks;
    }

    /// <summary>The form type, e.g. "UVTX".</summary>
    public string FormType { get; }

    /// <summary>Total size of the FORM including its 8-byte "FORM" + size header.</summary>
    public int TotalSize { get; }

    /// <summary>All chunks in file order.</summary>
    public IReadOnlyList<IffChunk> Chunks { get; }

    /// <summary>
    /// Reads only the FORM header and returns its total size and type, without parsing chunks.
    /// </summary>
    /// <exception cref="InvalidDataException">No FORM header at the start of the data.</exception>
    public static (string FormType, int TotalSize) ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || FourCC.Read(data) != FormMagic)
        {
            throw new InvalidDataException("Data does not start with a FORM header.");
        }

        uint size = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (size < 4 || size > int.MaxValue - 8)
        {
            throw new InvalidDataException($"Invalid FORM size 0x{size:X}.");
        }

        return (FourCC.Read(data[8..]), (int)size + 8);
    }

    /// <summary>
    /// Parses a FORM and lists its chunks.
    /// </summary>
    /// <param name="data">Data starting at "FORM". May extend beyond the FORM.</param>
    /// <exception cref="InvalidDataException">The structure is inconsistent.</exception>
    public static IffForm Parse(ReadOnlySpan<byte> data)
    {
        (string formType, int totalSize) = ReadHeader(data);
        if (totalSize > data.Length)
        {
            throw new InvalidDataException(
                $"FORM claims 0x{totalSize:X} bytes, but only 0x{data.Length:X} are available.");
        }

        var chunks = new List<IffChunk>();
        int offset = HeaderSize;

        while (offset < totalSize)
        {
            if (offset + IffChunk.HeaderSize > totalSize)
            {
                throw new InvalidDataException($"Truncated chunk header at FORM offset 0x{offset:X}.");
            }

            string tag = FourCC.Read(data[offset..]);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);

            if (size > (uint)(totalSize - offset - IffChunk.HeaderSize))
            {
                throw new InvalidDataException(
                    $"Chunk '{tag}' at FORM offset 0x{offset:X} (size 0x{size:X}) extends beyond the FORM.");
            }

            var chunk = new IffChunk(tag, offset, (int)size);
            chunks.Add(chunk);
            offset += chunk.TotalSize;
        }

        return new IffForm(formType, totalSize, chunks);
    }

    /// <summary>Returns the first chunk with the given tag, or <c>null</c>.</summary>
    public IffChunk? FindChunk(string tag) => Chunks.FirstOrDefault(c => c.Tag == tag);
}
