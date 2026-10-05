using System.Buffers.Binary;
using PW64Editor.Core.Compression;

namespace PW64Editor.Core.Iff;

/// <summary>
/// The compressed chunk wrapper used by Pilotwings 64. Despite its name, "GZIP" chunks
/// contain MIO0 data, not gzip (probably a leftover from the developers' original plans).
/// </summary>
/// <remarks>
/// <para>Payload layout of a GZIP chunk:</para>
/// <code>
/// 0x00  4    tag of the wrapped chunk, e.g. "COMM", "BITM", "TABL"
/// 0x04  u32  decompressed size
/// 0x08  ...  MIO0 block
/// </code>
/// <para>
/// The game replaces the GZIP chunk by the decompressed inner chunk when reading
/// (see uvFileReadBlock in the decompilation), so for all purposes a GZIP chunk behaves like
/// the chunk it wraps.
/// </para>
/// </remarks>
public static class GzipChunk
{
    /// <summary>Chunk tag of compressed chunks.</summary>
    public const string Tag = "GZIP";

    /// <summary>Size of the inner header before the MIO0 block.</summary>
    public const int InnerHeaderSize = 8;

    /// <summary>
    /// Decompresses the payload of a GZIP chunk.
    /// </summary>
    /// <param name="payload">The chunk payload (after the 8-byte "GZIP" + size header).</param>
    /// <returns>The tag of the wrapped chunk and its decompressed data.</returns>
    /// <exception cref="InvalidDataException">The payload is corrupt.</exception>
    public static (string InnerTag, byte[] Data) Decompress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < InnerHeaderSize + Mio0Header.Size)
        {
            throw new InvalidDataException("GZIP chunk is too small.");
        }

        string innerTag = FourCC.Read(payload);
        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        byte[] data = Mio0.Decompress(payload[InnerHeaderSize..]);

        if (data.Length != declaredSize)
        {
            throw new InvalidDataException(
                $"GZIP chunk declares 0x{declaredSize:X} bytes, but MIO0 block holds 0x{data.Length:X}.");
        }

        return (innerTag, data);
    }

    /// <summary>
    /// Returns the payload of a chunk, decompressing it first if it is a GZIP chunk.
    /// </summary>
    /// <param name="form">Data starting at the "FORM" the chunk belongs to.</param>
    /// <param name="chunk">The chunk to read.</param>
    /// <returns>The effective tag (inner tag for GZIP chunks) and the payload.</returns>
    public static (string Tag, byte[] Data) ReadChunkData(ReadOnlySpan<byte> form, IffChunk chunk)
    {
        ReadOnlySpan<byte> payload = form.Slice(chunk.DataOffset, chunk.DataSize);
        return chunk.Tag == Tag ? Decompress(payload) : (chunk.Tag, payload.ToArray());
    }
}
