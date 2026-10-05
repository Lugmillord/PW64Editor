using System.Buffers.Binary;
using PW64Editor.Core.Compression;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Tests.TestSupport;

/// <summary>
/// Builds IFF FORMs and chunks in memory for tests.
/// </summary>
internal static class IffBuilder
{
    /// <summary>Builds a chunk: tag + big-endian size + payload.</summary>
    public static byte[] Chunk(string tag, byte[] payload)
    {
        byte[] chunk = new byte[IffChunk.HeaderSize + payload.Length];
        FourCC.Write(chunk, tag);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, IffChunk.HeaderSize);
        return chunk;
    }

    /// <summary>Builds a GZIP chunk that wraps <paramref name="innerTag"/> with MIO0-compressed data.</summary>
    public static byte[] GzipChunk(string innerTag, byte[] data)
    {
        byte[] mio0 = Mio0.Compress(data);
        byte[] payload = new byte[8 + mio0.Length];
        FourCC.Write(payload, innerTag);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)data.Length);
        mio0.CopyTo(payload, 8);
        return Chunk("GZIP", payload);
    }

    /// <summary>Builds a FORM of the given type from already built chunks.</summary>
    public static byte[] Form(string formType, params byte[][] chunks)
    {
        int contentSize = chunks.Sum(c => c.Length);
        byte[] form = new byte[IffForm.HeaderSize + contentSize];
        FourCC.Write(form, "FORM");
        BinaryPrimitives.WriteUInt32BigEndian(form.AsSpan(4), (uint)(contentSize + 4));
        FourCC.Write(form.AsSpan(8), formType);

        int offset = IffForm.HeaderSize;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(form, offset);
            offset += chunk.Length;
        }

        return form;
    }

    /// <summary>Builds a decompressed TABL payload from (type, size) pairs. Type null = tag 0.</summary>
    public static byte[] TableEntries(params (string? Type, int Size)[] entries)
    {
        byte[] table = new byte[entries.Length * 8];
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Type is not null)
            {
                FourCC.Write(table.AsSpan(i * 8), entries[i].Type!);
            }

            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan((i * 8) + 4), (uint)entries[i].Size);
        }

        return table;
    }
}
