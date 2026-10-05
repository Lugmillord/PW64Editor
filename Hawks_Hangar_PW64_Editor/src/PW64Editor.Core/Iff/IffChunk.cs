namespace PW64Editor.Core.Iff;

/// <summary>
/// One chunk inside an IFF "FORM" container.
/// </summary>
/// <param name="Tag">Four-character chunk type, e.g. "COMM", "PAD ", "GZIP".</param>
/// <param name="Offset">Offset of the chunk header (the tag), relative to the start of the FORM.</param>
/// <param name="DataSize">Size of the chunk payload, excluding the 8-byte chunk header.</param>
public sealed record IffChunk(string Tag, int Offset, int DataSize)
{
    /// <summary>Size of a chunk header: 4 bytes tag + 4 bytes size.</summary>
    public const int HeaderSize = 8;

    /// <summary>Offset of the payload, relative to the start of the FORM.</summary>
    public int DataOffset => Offset + HeaderSize;

    /// <summary>Total chunk size including its header.</summary>
    public int TotalSize => HeaderSize + DataSize;
}
