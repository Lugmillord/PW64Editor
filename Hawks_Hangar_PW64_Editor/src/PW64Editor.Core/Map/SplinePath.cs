using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Map;

/// <summary>
/// A spline path of the game (SPTH user file): keys of x, y, z, h, p, r over a time of 0–100. The planes of Little States
/// and the ski lift of Ever-Frost follow such paths.
/// </summary>
/// <remarks>
/// <para>Layout: chunks SCPX/SCPY/SCPZ/SCPH/SCPP/SCPR, each a u32 count, then count × (f32 time, f32 value), then 4 zero
/// bytes; SCP# is ignored. The first and the last key are the same point, which closes the loop.</para>
/// <para>The curves are cubic Hermite segments (spath.c of the decompilation, https://github.com/gcsmith/Pilotwings64Decomp,
/// MIT license). Planes (spathInterpolate): SCPH/SCPP/SCPR hold the derivatives dx/dt, dy/dt, dz/dt, the tangents are
/// derivative × segment time, the heading follows the velocity. Ski lift (spathScaleUpdate): the tangents are the
/// directions to the neighbouring keys × |segment time| × 0.01 × 512, the heading comes from the SCPH keys in degrees
/// (a straight line with a bump of u(1−u) per segment).</para>
/// </remarks>
public sealed class SplinePath
{
    private readonly Dictionary<char, (float Time, float Value)[]> _axes;

    private SplinePath(Dictionary<char, (float Time, float Value)[]> axes) => _axes = axes;

    /// <summary>The keys of an axis (x, y, z, h, p, r).</summary>
    public IReadOnlyList<(float Time, float Value)> Keys(char axis) => _axes.GetValueOrDefault(char.ToUpperInvariant(axis)) ?? [];

    /// <summary>Reads a path file.</summary>
    public static SplinePath ParseFile(byte[] file)
    {
        var axes = new Dictionary<char, (float, float)[]>();
        foreach ((string tag, byte[] data) in MapChunks.Read(file, "SPTH"))
        {
            if (!tag.StartsWith("SCP", StringComparison.Ordinal) || tag == "SCP#" || data.Length < 4)
            {
                continue;
            }

            int count = Math.Min(BinaryPrimitives.ReadInt32BigEndian(data), (data.Length - 4) / 8);
            var keys = new (float, float)[Math.Max(0, count)];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = (BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(4 + i * 8)), BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(8 + i * 8)));
            }

            axes[tag[3]] = keys;
        }

        if (!axes.ContainsKey('X') || !axes.ContainsKey('Y') || !axes.ContainsKey('Z') || axes['X'].Length < 2)
        {
            throw new InvalidDataException("The path has no x, y and z keys.");
        }

        return new SplinePath(axes);
    }

    /// <summary>A plane's place and heading (degrees) at a time (0–100), as spathInterpolate computes them.</summary>
    public (Vector3 Position, float Heading) PlaneAt(float t)
    {
        var x = _axes['X'];
        int idx = Segment(x, t);
        float dt = x[idx].Time - x[idx - 1].Time;
        float u = dt == 0 ? 0 : (t - x[idx - 1].Time) / dt;
        var position = new float[3];
        var velocity = new float[3];
        char[] values = ['X', 'Y', 'Z'], tangents = ['H', 'P', 'R'];
        for (int k = 0; k < 3; k++)
        {
            var a = _axes[values[k]];
            var b = _axes.GetValueOrDefault(tangents[k]) ?? [];
            int i = Math.Min(idx, a.Length - 1);
            float m0 = i - 1 < b.Length ? b[i - 1].Value * dt : 0, m1 = i < b.Length ? b[i].Value * dt : 0;
            (position[k], float derivative) = Hermite(a[i - 1].Value, a[i].Value, m0, m1, u);
            velocity[k] = dt == 0 ? 0 : derivative / dt;
        }

        float heading = MathF.Atan2(-velocity[0], velocity[1]) * 180 / MathF.PI;
        return (new Vector3(position[0], position[1], position[2]), heading);
    }

    /// <summary>A ski lift chair's place and heading (degrees) at a time (0–100), as spathScaleUpdate computes them.</summary>
    public (Vector3 Position, float Heading) LiftAt(float t)
    {
        var x = _axes['X'];
        var y = _axes['Y'];
        var z = _axes['Z'];
        int n = x.Length;
        Vector3 Key(int i) => new(x[i].Value, y[i].Value, z[i].Value);
        Vector3 position;
        if (t <= x[0].Time)
        {
            position = Key(0);
        }
        else if (t >= x[n - 1].Time)
        {
            position = Key(n - 1);
        }
        else
        {
            int idx = Segment(x, t);
            int next = idx + 1 < n ? idx + 1 : 0;
            int previous = idx - 2 >= 0 ? idx - 2 : n - 1;
            Vector3 d0 = Key(idx - 1) - Key(previous), d1 = Key(next) - Key(idx);
            Vector3 p0 = d0 == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(d0);
            Vector3 p1 = d1 == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(d1);
            float s = MathF.Abs(x[idx].Time - x[idx - 1].Time) * 0.01f * 512;
            float u = (t - x[idx - 1].Time) / (x[idx].Time - x[idx - 1].Time);
            position = new Vector3(
                Hermite(Key(idx - 1).X, Key(idx).X, p0.X * s, p1.X * s, u).Value,
                Hermite(Key(idx - 1).Y, Key(idx).Y, p0.Y * s, p1.Y * s, u).Value,
                Hermite(Key(idx - 1).Z, Key(idx).Z, p0.Z * s, p1.Z * s, u).Value);
        }

        return (position, Bumped(_axes.GetValueOrDefault('H') ?? [], t));
    }

    /// <summary>spath_80340CB8: clamped, tangents +1 and −1 (a line with a small bump).</summary>
    private static float Bumped((float Time, float Value)[] keys, float t)
    {
        if (keys.Length == 0)
        {
            return 0;
        }

        if (t <= keys[0].Time)
        {
            return keys[0].Value;
        }

        if (t >= keys[^1].Time)
        {
            return keys[^1].Value;
        }

        int i = Segment(keys, t);
        float u = (t - keys[i - 1].Time) / (keys[i].Time - keys[i - 1].Time);
        return Hermite(keys[i - 1].Value, keys[i].Value, 1, -1, u).Value;
    }

    /// <summary>The first key index ≥ 1 whose time is at least t.</summary>
    private static int Segment((float Time, float Value)[] keys, float t)
    {
        int i = 1;
        while (i < keys.Length - 1 && keys[i].Time < t)
        {
            i++;
        }

        return i;
    }

    private static (float Value, float Derivative) Hermite(float p0, float p1, float m0, float m1, float u)
    {
        float c0 = 2 * p0 - 2 * p1 + m0 + m1;
        float c1 = -3 * p0 + 3 * p1 - 2 * m0 - m1;
        float c2 = m0;
        float c3 = p0;
        return (((c0 * u + c1) * u + c2) * u + c3, (3 * c0 * u + 2 * c1) * u + c2);
    }
}
