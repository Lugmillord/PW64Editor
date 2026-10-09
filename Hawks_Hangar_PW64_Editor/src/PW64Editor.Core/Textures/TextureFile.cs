using System.Buffers.Binary;
using PW64Editor.Core.Compression;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Textures;

/// <summary>
/// A texture file (FORM "UVTX"): two PAD chunks and the texture in a COMM chunk, which is
/// usually compressed (GZIP chunk with MIO0 data).
/// </summary>
public sealed class TextureFile
{
    /// <summary>The FORM type and file group of textures.</summary>
    public const string FormType = "UVTX";

    private const string DataTag = "COMM";

    private readonly byte[] _file;
    private readonly IffChunk _chunk;

    private TextureFile(byte[] file, IffChunk chunk, GameTexture texture)
    {
        _file = file;
        _chunk = chunk;
        Texture = texture;
    }

    /// <summary>The texture.</summary>
    public GameTexture Texture { get; }

    /// <summary>True if the texture is stored compressed.</summary>
    public bool IsCompressed => _chunk.Tag == GzipChunk.Tag;

    /// <summary>Reads a texture file.</summary>
    /// <exception cref="InvalidDataException">Not a texture file, or damaged.</exception>
    public static TextureFile Parse(byte[] file)
    {
        IffForm form = IffForm.Parse(file);
        if (form.FormType != FormType)
        {
            throw new InvalidDataException(CoreText.F("This is not a texture file (type {0}).", form.FormType));
        }

        foreach (IffChunk chunk in form.Chunks)
        {
            if (chunk.Tag is not (DataTag or GzipChunk.Tag))
            {
                continue;
            }

            (string tag, byte[] data) = GzipChunk.ReadChunkData(file, chunk);
            if (tag == DataTag)
            {
                return new TextureFile(file, chunk, GameTexture.Parse(data));
            }
        }

        throw new InvalidDataException(CoreText.T("The texture file has no texture data (COMM chunk)."));
    }

    /// <summary>
    /// Builds the file with new texture data. All other chunks stay as they are; the texture is
    /// compressed again if it was compressed before.
    /// </summary>
    /// <param name="textureData">New content of the COMM chunk (see <see cref="GameTexture.WithImage"/>).</param>
    public byte[] Build(byte[] textureData)
    {
        IffForm form = IffForm.Parse(_file);
        var chunks = new List<byte[]>(form.Chunks.Count);
        foreach (IffChunk chunk in form.Chunks)
        {
            chunks.Add(chunk.Offset == _chunk.Offset
                ? IsCompressed ? BuildCompressedChunk(textureData) : IffWriter.BuildChunk(DataTag, textureData)
                : _file.AsSpan(chunk.Offset, chunk.TotalSize).ToArray());
        }

        return IffWriter.BuildForm(FormType, chunks);
    }

    /// <summary>
    /// A GZIP chunk. The MIO0 data is padded with zeros to a multiple of 4 bytes, so the file size
    /// stays a multiple of 4 as the game's files need (the decompressor ignores bytes behind the data).
    /// </summary>
    private static byte[] BuildCompressedChunk(byte[] data)
    {
        byte[] mio0 = Mio0.Compress(data);
        byte[] payload = new byte[GzipChunk.InnerHeaderSize + (mio0.Length + 3) / 4 * 4];
        FourCC.Write(payload, DataTag);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)data.Length);
        mio0.CopyTo(payload, GzipChunk.InnerHeaderSize);
        return IffWriter.BuildChunk(GzipChunk.Tag, payload);
    }
}
