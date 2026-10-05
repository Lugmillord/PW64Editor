using System.Buffers.Binary;
using PW64Editor.Core.Compression;

namespace PW64Editor.Core.Iff;

/// <summary>
/// Creates IFF chunks and FORMs in the exact format the game reads (see <see cref="IffForm"/>).
/// </summary>
public static class IffWriter
{
    /// <summary>Builds a chunk: tag + big-endian payload size + payload. No padding.</summary>
    public static byte[] BuildChunk(string tag, ReadOnlySpan<byte> payload)
    {
        byte[] chunk = new byte[IffChunk.HeaderSize + payload.Length];
        FourCC.Write(chunk, tag);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk.AsSpan(IffChunk.HeaderSize));
        return chunk;
    }

    /// <summary>
    /// Builds a "GZIP" chunk that wraps a chunk of type <paramref name="innerTag"/>
    /// with MIO0-compressed <paramref name="data"/>.
    /// </summary>
    public static byte[] BuildGzipChunk(string innerTag, ReadOnlySpan<byte> data)
    {
        byte[] mio0 = Mio0.Compress(data);
        byte[] payload = new byte[GzipChunk.InnerHeaderSize + mio0.Length];
        FourCC.Write(payload, innerTag);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)data.Length);
        mio0.CopyTo(payload, GzipChunk.InnerHeaderSize);
        return BuildChunk(GzipChunk.Tag, payload);
    }

    /// <summary>Builds a FORM of type <paramref name="formType"/> from already built chunks.</summary>
    public static byte[] BuildForm(string formType, IEnumerable<byte[]> chunks)
    {
        List<byte[]> chunkList = chunks.ToList();
        int contentSize = chunkList.Sum(c => c.Length);

        byte[] form = new byte[IffForm.HeaderSize + contentSize];
        FourCC.Write(form, IffForm.FormMagic);
        BinaryPrimitives.WriteUInt32BigEndian(form.AsSpan(4), (uint)(4 + contentSize)); // type + chunks
        FourCC.Write(form.AsSpan(8), formType);

        int offset = IffForm.HeaderSize;
        foreach (byte[] chunk in chunkList)
        {
            chunk.CopyTo(form, offset);
            offset += chunk.Length;
        }

        return form;
    }

    /// <summary>
    /// Rebuilds a FORM, decompressing and recompressing every GZIP chunk with our own MIO0
    /// compressor. All other chunks are copied unchanged.
    /// </summary>
    /// <remarks>
    /// The result contains exactly the same game data as the input, only the compressed bytes
    /// (and therefore usually the size) differ. Useful for testing the build pipeline with a
    /// real size change, and later as the basis for editing compressed chunks.
    /// </remarks>
    public static byte[] RecompressForm(ReadOnlySpan<byte> formData)
    {
        IffForm form = IffForm.Parse(formData);
        var chunks = new List<byte[]>(form.Chunks.Count);

        foreach (IffChunk chunk in form.Chunks)
        {
            if (chunk.Tag == GzipChunk.Tag)
            {
                (string innerTag, byte[] data) = GzipChunk.ReadChunkData(formData, chunk);
                chunks.Add(BuildGzipChunk(innerTag, data));
            }
            else
            {
                chunks.Add(formData.Slice(chunk.Offset, chunk.TotalSize).ToArray());
            }
        }

        return BuildForm(form.FormType, chunks);
    }
}
