using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Map;

/// <summary>
/// One vertex of a contour or model (0x10 bytes: s16 x, y, z, 2 bytes, s16 s, t texture coordinates in 1/32 texel,
/// u8 r, g, b, a; for lit materials r, g, b are a normal).
/// </summary>
public readonly record struct MeshVertex(short X, short Y, short Z, short S, short T, byte R, byte G, byte B, byte A)
{
    /// <summary>Size of a vertex in the file.</summary>
    public const int Size = 0x10;

    /// <summary>The position.</summary>
    public Vector3 Position => new(X, Y, Z);

    /// <summary>The normal of a lit vertex (r, g, b as signed bytes).</summary>
    public Vector3 Normal => new((sbyte)R / 127f, (sbyte)G / 127f, (sbyte)B / 127f);

    /// <summary>Reads a vertex.</summary>
    public static MeshVertex Read(ReadOnlySpan<byte> data) => new(
        BinaryPrimitives.ReadInt16BigEndian(data),
        BinaryPrimitives.ReadInt16BigEndian(data[2..]),
        BinaryPrimitives.ReadInt16BigEndian(data[4..]),
        BinaryPrimitives.ReadInt16BigEndian(data[8..]),
        BinaryPrimitives.ReadInt16BigEndian(data[10..]),
        data[12], data[13], data[14], data[15]);
}

/// <summary>A triangle: three vertex numbers.</summary>
public readonly record struct MeshTriangle(int A, int B, int C);

/// <summary>
/// One material of a contour or model part: how it is drawn, its texture and its triangles.
/// </summary>
/// <param name="RenderMode">The game's mode word (bit 1 smooth shading, 3/4 culling, 5–8 the RDP mode, 11 lighting,
/// 15 fog; see the noclip viewer's decodeMaterial).</param>
/// <param name="Texture">The texture number (0xFFF or more: no texture).</param>
/// <param name="Triangles">The triangles (vertex numbers of the contour or model).</param>
public sealed record MeshMaterial(int RenderMode, int Texture, IReadOnlyList<MeshTriangle> Triangles)
{
    /// <summary>Texture numbers from this one on mean "no texture".</summary>
    public const int NoTexture = 0xFFF;

    /// <summary>True if the material has a texture.</summary>
    public bool HasTexture => Texture < NoTexture;

    /// <summary>True if the vertices carry normals and the game lights the material.</summary>
    public bool IsLit => (RenderMode & (1 << 11)) != 0;

    /// <summary>True if back faces are not drawn.</summary>
    public bool CullsBack => (RenderMode & (1 << 4)) != 0;

    /// <summary>True if front faces are not drawn.</summary>
    public bool CullsFront => (RenderMode & (1 << 3)) != 0;

    /// <summary>True if the material is drawn translucent (RDP modes 4–7 and 12–15).</summary>
    public bool IsTranslucent => (((RenderMode >> 5) & 0xF) & 4) != 0;

    /// <summary>A decal (bit 8, RDP modes 8–15): drawn on a surface at the same depth (markings, windows, signs).</summary>
    public bool IsDecal => (RenderMode & (1 << 8)) != 0;
}

/// <summary>
/// Reads the drawing commands of a material: a vertex load fills the 16-place vertex buffer (u16 first vertex, then a
/// byte: high nibble = count − 1, low nibble = first place), a word with bit 14 draws one triangle from three places.
/// </summary>
internal static class MeshCommands
{
    /// <summary>Size of a material header (u16 render mode, texture, vertex count, triangle count, command count).</summary>
    public const int HeaderSize = 10;

    /// <summary>Reads the commands at <paramref name="offset"/> and returns the triangles; moves the offset behind them.</summary>
    public static List<MeshTriangle> Read(ReadOnlySpan<byte> data, ref int offset, int commandCount)
    {
        Span<int> buffer = stackalloc int[16];
        var triangles = new List<MeshTriangle>();
        for (int c = 0; c < commandCount; c++)
        {
            int word = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            if ((word & 0x4000) != 0)
            {
                triangles.Add(new MeshTriangle(buffer[(word >> 8) & 0xF], buffer[(word >> 4) & 0xF], buffer[word & 0xF]));
            }
            else
            {
                int load = data[offset++];
                int place = load & 0xF;
                for (int i = 0; i <= load >> 4 && place < 16; i++, place++)
                {
                    buffer[place] = (word & 0x3FFF) + i;
                }
            }
        }

        return triangles;
    }

    /// <summary>Skips the commands at <paramref name="offset"/>.</summary>
    public static void Skip(ReadOnlySpan<byte> data, ref int offset, int commandCount)
    {
        for (int c = 0; c < commandCount; c++)
        {
            int word = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += (word & 0x4000) != 0 ? 2 : 3;
        }
    }
}
