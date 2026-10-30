using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Map;

/// <summary>
/// Matrices and angles as the game uses them.
/// </summary>
/// <remarks>
/// <para>The game's world is Z-up: x points east, y north, z up (game units; 1 unit = 0.7 game meters). Matrices are
/// row-vector matrices with the translation in the last row (the N64 layout), which is also the layout of
/// <see cref="Matrix4x4"/>: a point is transformed as <c>Vector3.Transform(p, m)</c>.</para>
/// <para>Object angles in the mission files are degrees, stored as (heading, pitch, roll): heading turns about the z
/// axis, pitch about the x axis, roll about the y axis; a vertex is rolled first, then pitched, then turned
/// (func_80313640, code_9A960.c of the decompilation, https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class MapMath
{
    /// <summary>Degrees to radians.</summary>
    public const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>
    /// The rotation of an object with the game's angles in degrees (heading about z, pitch about x, roll about y).
    /// </summary>
    public static Matrix4x4 Rotation(float heading, float pitch, float roll) =>
        Matrix4x4.CreateRotationY(roll * DegreesToRadians)
        * Matrix4x4.CreateRotationX(pitch * DegreesToRadians)
        * Matrix4x4.CreateRotationZ(heading * DegreesToRadians);

    /// <summary>Scale, then the game's rotation, then the position.</summary>
    public static Matrix4x4 Placement(Vector3 position, Vector3 angles, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Rotation(angles.X, angles.Y, angles.Z) * Matrix4x4.CreateTranslation(position);

    /// <summary>
    /// Reads an N64 fixed-point matrix (Mtx, 64 bytes: sixteen s16 integer parts, then sixteen u16 fractions, row by row).
    /// </summary>
    public static Matrix4x4 ReadFixedMatrix(ReadOnlySpan<byte> data)
    {
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 16; i++)
        {
            int whole = BinaryPrimitives.ReadInt16BigEndian(data[(i * 2)..]);
            int fraction = BinaryPrimitives.ReadUInt16BigEndian(data[(32 + i * 2)..]);
            m[i] = ((whole << 16) | fraction) / 65536f;
        }

        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    /// <summary>Reads a matrix of sixteen big-endian f32, row by row.</summary>
    public static Matrix4x4 ReadFloatMatrix(ReadOnlySpan<byte> data)
    {
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 16; i++)
        {
            m[i] = BinaryPrimitives.ReadSingleBigEndian(data[(i * 4)..]);
        }

        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    /// <summary>Reads three big-endian f32.</summary>
    public static Vector3 ReadVector(ReadOnlySpan<byte> data) => new(
        BinaryPrimitives.ReadSingleBigEndian(data),
        BinaryPrimitives.ReadSingleBigEndian(data[4..]),
        BinaryPrimitives.ReadSingleBigEndian(data[8..]));

    /// <summary>Writes three big-endian f32.</summary>
    public static void WriteVector(Span<byte> data, Vector3 value)
    {
        BinaryPrimitives.WriteSingleBigEndian(data, value.X);
        BinaryPrimitives.WriteSingleBigEndian(data[4..], value.Y);
        BinaryPrimitives.WriteSingleBigEndian(data[8..], value.Z);
    }
}

/// <summary>An axis-aligned box.</summary>
public readonly record struct Box3(Vector3 Min, Vector3 Max)
{
    /// <summary>A box that contains nothing (grows with <see cref="Include(Vector3)"/>).</summary>
    public static Box3 Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    /// <summary>True if the box contains no point.</summary>
    public bool IsEmpty => Min.X > Max.X;

    /// <summary>The middle of the box.</summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>The edge lengths.</summary>
    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

    /// <summary>The box grown to contain the point.</summary>
    public Box3 Include(Vector3 point) => new(Vector3.Min(Min, point), Vector3.Max(Max, point));

    /// <summary>The box grown to contain the other box.</summary>
    public Box3 Include(Box3 other) => other.IsEmpty ? this : IsEmpty ? other : new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>The eight corners.</summary>
    public Vector3[] Corners() =>
    [
        new(Min.X, Min.Y, Min.Z), new(Max.X, Min.Y, Min.Z), new(Max.X, Max.Y, Min.Z), new(Min.X, Max.Y, Min.Z),
        new(Min.X, Min.Y, Max.Z), new(Max.X, Min.Y, Max.Z), new(Max.X, Max.Y, Max.Z), new(Min.X, Max.Y, Max.Z),
    ];
}
