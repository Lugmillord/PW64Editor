using System.Buffers.Binary;
using System.Numerics;
using PW64Editor.Core.Textures;

namespace PW64Editor.Core.Map;

/// <summary>
/// The N64 colour combiner of a texture: two cycles of (a − b) × c + d for colour and alpha, as the texture's
/// G_SETCOMBINE sets them. The second cycle's result is what is drawn.
/// </summary>
/// <remarks>Inputs as the RDP numbers them. Colour a/b: 0 combined, 1 texel 0, 2 texel 1, 3 primitive, 4 shade,
/// 5 environment, 6 one (b: centre), 7 noise (b: K4), ≥ 8 zero. Colour c: 0–5 as a, 6 scale, 7 combined alpha,
/// 8 texel 0 alpha, 9 texel 1 alpha, 10 primitive alpha, 11 shade alpha, 12 environment alpha, 13 LOD fraction,
/// 14 primitive LOD fraction, 15 K5, ≥ 16 zero. Colour d: 0–5 as a, 6 one, 7 zero. Alpha a/b/d: 0 combined, 1 texel 0,
/// 2 texel 1, 3 primitive, 4 shade, 5 environment, 6 one, 7 zero; alpha c: 0 LOD fraction, 1–5 as a, 6 primitive LOD
/// fraction, 7 zero. The editor takes the LOD fraction as 0 (the most detailed level).</remarks>
public readonly record struct Combiner(
    byte A0, byte B0, byte C0, byte D0, byte Aa0, byte Ab0, byte Ac0, byte Ad0,
    byte A1, byte B1, byte C1, byte D1, byte Aa1, byte Ab1, byte Ac1, byte Ad1)
{
    /// <summary>Texture × shade in both cycles (what textures without a combine command get).</summary>
    public static Combiner TextureTimesShade { get; } = new(1, 15, 4, 7, 1, 7, 4, 7, 15, 15, 31, 0, 7, 7, 7, 0);

    /// <summary>Decodes a G_SETCOMBINE command (its two words).</summary>
    public static Combiner Decode(uint w0, uint w1) => new(
        (byte)((w0 >> 20) & 15), (byte)((w1 >> 28) & 15), (byte)((w0 >> 15) & 31), (byte)((w1 >> 15) & 7),
        (byte)((w0 >> 12) & 7), (byte)((w1 >> 12) & 7), (byte)((w0 >> 9) & 7), (byte)((w1 >> 9) & 7),
        (byte)((w0 >> 5) & 15), (byte)((w1 >> 24) & 15), (byte)(w0 & 31), (byte)((w1 >> 6) & 7),
        (byte)((w1 >> 21) & 7), (byte)((w1 >> 3) & 7), (byte)((w1 >> 18) & 7), (byte)(w1 & 7));

    /// <summary>True if the drawn alpha depends on the texture (otherwise texels with low alpha must not be cut out).</summary>
    public bool AlphaUsesTexture
    {
        get
        {
            static bool Texel(byte v) => v is 1 or 2;
            bool firstUsesTexture = Texel(Aa0) || Texel(Ab0) || Texel(Ac0) || Texel(Ad0);
            bool UsesCombined(byte v) => v == 0;
            return Texel(Aa1) || Texel(Ab1) || Texel(Ac1) || Texel(Ad1)
                || (firstUsesTexture && (UsesCombined(Aa1) || UsesCombined(Ab1) || UsesCombined(Ad1)));
        }
    }

    /// <summary>The colour of one pixel (channels 0–1, alpha in W).</summary>
    public Vector4 Evaluate(Vector4 texel0, Vector4 texel1, Vector4 shade, Vector4 primitive, Vector4 environment, float primitiveLod)
    {
        var inputs = new Inputs(texel0, texel1, shade, primitive, environment, primitiveLod);
        Vector4 first = Cycle(Vector4.Zero, A0, B0, C0, D0, Aa0, Ab0, Ac0, Ad0, inputs);
        Vector4 second = Cycle(first, A1, B1, C1, D1, Aa1, Ab1, Ac1, Ad1, inputs);
        return Vector4.Clamp(second, Vector4.Zero, Vector4.One);
    }

    private readonly record struct Inputs(Vector4 T0, Vector4 T1, Vector4 Shade, Vector4 Prim, Vector4 Env, float PrimLod);

    private static Vector4 Cycle(Vector4 combined, byte a, byte b, byte c, byte d, byte aa, byte ab, byte ac, byte ad, in Inputs i)
    {
        Vector3 color = (ColorAB(a, combined, i, false) - ColorAB(b, combined, i, true)) * ColorC(c, combined, i) + ColorD(d, combined, i);
        float alpha = (AlphaABD(aa, combined, i) - AlphaABD(ab, combined, i)) * AlphaC(ac, combined, i) + AlphaABD(ad, combined, i);
        return new Vector4(color, alpha);
    }

    private static Vector3 Rgb(Vector4 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ColorAB(byte v, Vector4 combined, in Inputs i, bool isB) => v switch
    {
        0 => Rgb(combined),
        1 => Rgb(i.T0),
        2 => Rgb(i.T1),
        3 => Rgb(i.Prim),
        4 => Rgb(i.Shade),
        5 => Rgb(i.Env),
        6 => isB ? Vector3.Zero : Vector3.One, // b: key centre (no keying)
        7 => isB ? Vector3.Zero : new Vector3(0.5f), // a: noise
        _ => Vector3.Zero,
    };

    private static Vector3 ColorC(byte v, Vector4 combined, in Inputs i) => v switch
    {
        0 => Rgb(combined),
        1 => Rgb(i.T0),
        2 => Rgb(i.T1),
        3 => Rgb(i.Prim),
        4 => Rgb(i.Shade),
        5 => Rgb(i.Env),
        6 => Vector3.Zero, // key scale
        7 => new Vector3(combined.W),
        8 => new Vector3(i.T0.W),
        9 => new Vector3(i.T1.W),
        10 => new Vector3(i.Prim.W),
        11 => new Vector3(i.Shade.W),
        12 => new Vector3(i.Env.W),
        13 => Vector3.Zero, // LOD fraction: the most detailed level
        14 => new Vector3(i.PrimLod),
        _ => Vector3.Zero,
    };

    private static Vector3 ColorD(byte v, Vector4 combined, in Inputs i) => v switch
    {
        0 => Rgb(combined),
        1 => Rgb(i.T0),
        2 => Rgb(i.T1),
        3 => Rgb(i.Prim),
        4 => Rgb(i.Shade),
        5 => Rgb(i.Env),
        6 => Vector3.One,
        _ => Vector3.Zero,
    };

    private static float AlphaABD(byte v, Vector4 combined, in Inputs i) => v switch
    {
        0 => combined.W,
        1 => i.T0.W,
        2 => i.T1.W,
        3 => i.Prim.W,
        4 => i.Shade.W,
        5 => i.Env.W,
        6 => 1,
        _ => 0,
    };

    private static float AlphaC(byte v, Vector4 combined, in Inputs i) => v switch
    {
        1 => i.T0.W,
        2 => i.T1.W,
        3 => i.Prim.W,
        4 => i.Shade.W,
        5 => i.Env.W,
        6 => i.PrimLod,
        _ => 0, // 0: LOD fraction (most detailed level), 7: zero
    };
}

/// <summary>
/// A texture ready for drawing: its main image as packed ARGB pixels, how the game samples it, and how the colour
/// combiner mixes it with the vertex colour, the primitive and environment colours and a second tile.
/// </summary>
/// <remarks>The sampling values come from the texture's G_SETTILE command of its main tile: shift (texture
/// coordinates are scaled by 2^−shift, 11–15 scale up), mask (the image repeats every 2^mask texels) and the clamp and
/// mirror bits per direction. Many terrain textures multiply two tiles: the image with itself at another scale, or a
/// colour image of a paired texture with the texture's own grey detail image; <see cref="Detail"/> is the second
/// sampler (texel 1). Mipmaps are drawn as the main image (the editor draws the most detailed level).</remarks>
public sealed class MapTexture
{
    internal MapTexture(int width, int height, uint[] pixels, float scaleS, float scaleT, int wrapS, int wrapT,
        bool mirrorS, bool mirrorT, bool clampS, bool clampT, bool hasAlpha, bool hasPartialAlpha)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        ScaleS = scaleS;
        ScaleT = scaleT;
        WrapS = wrapS;
        WrapT = wrapT;
        MirrorS = mirrorS;
        MirrorT = mirrorT;
        ClampS = clampS;
        ClampT = clampT;
        HasAlpha = hasAlpha;
        HasPartialAlpha = hasPartialAlpha;
    }

    /// <summary>Width of the image in texels.</summary>
    public int Width { get; }

    /// <summary>Height of the image in texels.</summary>
    public int Height { get; }

    /// <summary>The pixels as 0xAARRGGBB, row by row.</summary>
    public uint[] Pixels { get; }

    /// <summary>Texel per vertex texture unit (1/32 texel, scaled by the tile's shift) in s.</summary>
    public float ScaleS { get; }

    /// <summary>Texel per vertex texture unit in t.</summary>
    public float ScaleT { get; }

    /// <summary>The image repeats every this many texels in s.</summary>
    public int WrapS { get; }

    /// <summary>The image repeats every this many texels in t.</summary>
    public int WrapT { get; }

    /// <summary>Every second repetition is mirrored in s.</summary>
    public bool MirrorS { get; }

    /// <summary>Every second repetition is mirrored in t.</summary>
    public bool MirrorT { get; }

    /// <summary>The image is not repeated in s (the edge texel continues).</summary>
    public bool ClampS { get; }

    /// <summary>The image is not repeated in t.</summary>
    public bool ClampT { get; }

    /// <summary>True if some texels are not fully opaque.</summary>
    public bool HasAlpha { get; }

    /// <summary>True if some texels are neither fully clear nor fully opaque.</summary>
    public bool HasPartialAlpha { get; }

    /// <summary>The colour combiner (texture × shade if the texture sets none).</summary>
    public Combiner Combine { get; private init; } = Combiner.TextureTimesShade;

    /// <summary>The primitive colour (channels 0–1).</summary>
    public Vector4 Primitive { get; private init; } = Vector4.One;

    /// <summary>The environment colour (channels 0–1).</summary>
    public Vector4 Environment { get; private init; } = Vector4.One;

    /// <summary>The primitive LOD fraction (0–1).</summary>
    public float PrimitiveLod { get; private init; }

    /// <summary>The second tile when it samples the same image differently (texel 1), or null to use the main tile.</summary>
    public MapTexture? Detail { get; private init; }

    /// <summary>True if texels can be cut out or blended: the drawn alpha depends on the texture and some texels are not opaque.</summary>
    public bool CutsOut => HasAlpha && Combine.AlphaUsesTexture;

    /// <summary>True if the drawn alpha depends on the texture and some texels are partly clear.</summary>
    public bool BlendsAlpha => HasPartialAlpha && Combine.AlphaUsesTexture;

    /// <summary>The average colour of the opaque texels (for the overview colours).</summary>
    public uint AverageColor()
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach (uint p in Pixels)
        {
            long a = p >> 24;
            r += ((p >> 16) & 0xFF) * a;
            g += ((p >> 8) & 0xFF) * a;
            b += (p & 0xFF) * a;
            n += a;
        }

        return n == 0 ? 0xFF808080 : 0xFF000000 | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
    }

    /// <summary>A decoded image: packed ARGB pixels and its size.</summary>
    public readonly record struct Image(uint[] Pixels, int Width, int Height);

    /// <summary>Decodes the main image of a texture (null if the editor cannot read its format).</summary>
    public static Image? DecodeMain(GameTexture texture)
    {
        if (!texture.IsSupported)
        {
            return null;
        }

        RgbaImage image = texture.DecodeImage();
        var pixels = new uint[image.Width * image.Height];
        byte[] rgba = image.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (uint)rgba[i * 4 + 3] << 24 | (uint)rgba[i * 4] << 16 | (uint)rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2];
        }

        return new Image(pixels, image.Width, image.Height);
    }

    /// <summary>The texture a texture is paired with (the trailer's u16 after the display list), or null for none.</summary>
    public static int? PairedIndex(GameTexture texture)
    {
        byte[] data = texture.Data;
        int imageSize = BinaryPrimitives.ReadUInt16BigEndian(data);
        int commandCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        int trailer = 0x14 + imageSize + commandCount * 8;
        if (trailer + 11 > data.Length)
        {
            return null;
        }

        int index = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(trailer + 9));
        return index < 0xFFF ? index : null;
    }

    /// <summary>Reads a texture for drawing (null if the editor cannot read its format).</summary>
    /// <param name="texture">The texture.</param>
    /// <param name="pairedImage">The main image of another texture by number: a texture can load a second texture's image
    /// into one of its tiles (e.g. a colour image multiplied with its own grey detail image).</param>
    /// <remarks>Texel 0 is the tile G_TEXTURE names, texel 1 the next tile. Tiles set after the second G_SETTIMG hold
    /// the paired texture's image; the others the texture's own (mipmaps are drawn as the main image).</remarks>
    public static MapTexture? From(GameTexture texture, Func<int, Image?>? pairedImage = null)
    {
        if (DecodeMain(texture) is not { } own)
        {
            return null;
        }

        // The display list: G_TEXTURE (tile in bits 8–10), G_SETTIMG, G_SETTILE per tile (lower word: cmt 18–19, maskt
        // 14–17, shiftt 10–13, cms 8–9, masks 4–7, shifts 0–3), G_SETCOMBINE, G_SETPRIMCOLOR, G_SETENVCOLOR.
        byte[] data = texture.Data;
        int imageSize = BinaryPrimitives.ReadUInt16BigEndian(data);
        int commandCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        const int headerSize = 0x14;
        int textureTile = texture.Levels[0].TileIndex;
        var tiles = new Dictionary<int, (uint Lower, bool Paired)>();
        Combiner? combine = null;
        Vector4 primitive = Vector4.One, environment = Vector4.One;
        float primitiveLod = 0;
        int images = 0;
        bool tileCommand = false;
        for (int i = 0; i < commandCount; i++)
        {
            ulong command = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(headerSize + imageSize + i * 8));
            uint upper = (uint)(command >> 32), lower = (uint)command;
            switch (upper >> 24)
            {
                case 0xBB:
                    textureTile = (int)((upper >> 8) & 7);
                    tileCommand = true;
                    break;
                case 0xFD:
                    images++;
                    break;
                case 0xF5 when ((lower >> 24) & 7) != 7:
                    tiles.TryAdd((int)((lower >> 24) & 7), (lower, images >= 2));
                    break;
                case 0xFC:
                    combine = Combiner.Decode(upper, lower);
                    break;
                case 0xFA:
                    primitive = Color(lower);
                    primitiveLod = (upper & 0xFF) / 255f;
                    break;
                case 0xFB:
                    environment = Color(lower);
                    break;
            }
        }

        if (!tileCommand || !tiles.ContainsKey(textureTile))
        {
            textureTile = texture.Levels[0].TileIndex;
        }

        Image? paired = images >= 2 && PairedIndex(texture) is { } pairedNumber ? pairedImage?.Invoke(pairedNumber) : null;
        Combiner combiner = combine ?? Combiner.TextureTimesShade;

        MapTexture Sampler((uint Lower, bool Paired) tile)
        {
            Image image = tile.Paired && paired is { } p ? p : own;
            bool alpha = false, partial = false;
            foreach (uint pixel in image.Pixels)
            {
                uint a = pixel >> 24;
                alpha |= a < 255;
                partial |= a is > 8 and < 247;
            }

            uint w = tile.Lower;
            return new MapTexture(image.Width, image.Height, image.Pixels, ShiftScale((int)w & 0xF) / 32f, ShiftScale((int)(w >> 10) & 0xF) / 32f,
                Wrap((int)(w >> 4) & 0xF, image.Width), Wrap((int)(w >> 14) & 0xF, image.Height),
                ((w >> 8) & 1) != 0, ((w >> 18) & 1) != 0,
                ((w >> 8) & 2) != 0 && ((w >> 4) & 0xF) == 0, ((w >> 18) & 2) != 0 && ((w >> 14) & 0xF) == 0, alpha, partial)
            {
                Combine = combiner,
                Primitive = primitive,
                Environment = environment,
                PrimitiveLod = primitiveLod,
            };
        }

        var mainTile = tiles.GetValueOrDefault(textureTile);
        MapTexture main = Sampler(mainTile);
        MapTexture? detail = tiles.TryGetValue(textureTile + 1, out var second) && second != mainTile ? Sampler(second) : null;
        return detail is null ? main : main.WithDetail(detail);
    }

    private MapTexture WithDetail(MapTexture detail) =>
        new(Width, Height, Pixels, ScaleS, ScaleT, WrapS, WrapT, MirrorS, MirrorT, ClampS, ClampT, HasAlpha, HasPartialAlpha)
        {
            Combine = Combine,
            Primitive = Primitive,
            Environment = Environment,
            PrimitiveLod = PrimitiveLod,
            Detail = detail,
        };

    private static float ShiftScale(int shift) => shift == 0 ? 1f : shift <= 10 ? 1f / (1 << shift) : 1 << (16 - shift);

    private static int Wrap(int mask, int size) => mask > 0 ? 1 << mask : size;

    private static Vector4 Color(uint rgba) =>
        new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
}
