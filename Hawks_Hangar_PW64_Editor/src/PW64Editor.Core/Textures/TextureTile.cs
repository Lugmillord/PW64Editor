namespace PW64Editor.Core.Textures;

/// <summary>
/// Where and how one image of a texture lies in the texture memory (TMEM) of the N64, as set by
/// the commands G_SETTILE and G_SETTILESIZE of the texture's display list.
/// </summary>
/// <param name="TileIndex">Number of the tile descriptor (0-7; 7 is used for loading).</param>
/// <param name="Format">Texel format.</param>
/// <param name="Line">Length of one row in 64-bit words.</param>
/// <param name="Tmem">Start in TMEM, in 64-bit words.</param>
/// <param name="Width">Width in texels.</param>
/// <param name="Height">Height in texels.</param>
public sealed record TextureTile(int TileIndex, TextureFormat Format, int Line, int Tmem, int Width, int Height)
{
    /// <summary>Byte offset of the image in TMEM (and in the texture's image data).</summary>
    public int ByteOffset => Tmem * 8;

    /// <summary>Bytes of one row (including padding up to the next 64-bit word).</summary>
    public int RowBytes => Line * 8;
}
