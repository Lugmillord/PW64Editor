namespace PW64Editor.Core.Textures;

/// <summary>A texel format of the N64's graphics chip (RDP), as used in a texture tile.</summary>
public enum TextureFormat
{
    /// <summary>A format the editor cannot edit (color index, YUV, 32-bit color) or does not know.</summary>
    Unsupported,

    /// <summary>Color: 5 bits each for red, green and blue, 1 bit for transparency.</summary>
    Rgba16,

    /// <summary>Greyscale (3 bits) with 1 bit transparency.</summary>
    Ia4,

    /// <summary>Greyscale (4 bits) with 4 bits transparency.</summary>
    Ia8,

    /// <summary>Greyscale (8 bits) with 8 bits transparency.</summary>
    Ia16,

    /// <summary>Greyscale, 4 bits.</summary>
    I4,

    /// <summary>Greyscale, 8 bits.</summary>
    I8,
}

/// <summary>Facts about the texel formats.</summary>
public static class TextureFormats
{
    /// <summary>
    /// The format of a tile from the values of the RDP command G_SETTILE.
    /// </summary>
    /// <param name="format">G_IM_FMT: 0 RGBA, 1 YUV, 2 CI, 3 IA, 4 I.</param>
    /// <param name="size">G_IM_SIZ: 0 4-bit, 1 8-bit, 2 16-bit, 3 32-bit.</param>
    public static TextureFormat FromRdp(int format, int size) => (format, size) switch
    {
        (0, 2) => TextureFormat.Rgba16,
        (3, 0) => TextureFormat.Ia4,
        (3, 1) => TextureFormat.Ia8,
        (3, 2) => TextureFormat.Ia16,
        (4, 0) => TextureFormat.I4,
        (4, 1) => TextureFormat.I8,
        _ => TextureFormat.Unsupported,
    };

    /// <summary>Short name as in N64 documentation, e.g. "RGBA16" or "IA8".</summary>
    public static string Name(TextureFormat format) => format switch
    {
        TextureFormat.Rgba16 => "RGBA16",
        TextureFormat.Ia4 => "IA4",
        TextureFormat.Ia8 => "IA8",
        TextureFormat.Ia16 => "IA16",
        TextureFormat.I4 => "I4",
        TextureFormat.I8 => "I8",
        _ => "?",
    };

    /// <summary>Bits per texel.</summary>
    public static int BitsPerTexel(TextureFormat format) => format switch
    {
        TextureFormat.Ia4 or TextureFormat.I4 => 4,
        TextureFormat.Ia8 or TextureFormat.I8 => 8,
        TextureFormat.Rgba16 or TextureFormat.Ia16 => 16,
        _ => 0,
    };

    /// <summary>True for the greyscale formats (the game colors them itself).</summary>
    public static bool IsGreyscale(TextureFormat format) =>
        format is TextureFormat.Ia4 or TextureFormat.Ia8 or TextureFormat.Ia16 or TextureFormat.I4 or TextureFormat.I8;

    /// <summary>True if the format stores transparency.</summary>
    public static bool HasTransparency(TextureFormat format) =>
        format is TextureFormat.Rgba16 or TextureFormat.Ia4 or TextureFormat.Ia8 or TextureFormat.Ia16;

    /// <summary>True if a pixel is either fully visible or fully transparent (1 bit of transparency).</summary>
    public static bool HasOnOffTransparency(TextureFormat format) =>
        format is TextureFormat.Rgba16 or TextureFormat.Ia4;
}
