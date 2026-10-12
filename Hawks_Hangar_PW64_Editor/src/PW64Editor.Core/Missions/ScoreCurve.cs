using System.Buffers.Binary;

namespace PW64Editor.Core.Missions;

/// <summary>A point of a <see cref="ScoreCurve"/>: from the measured value <paramref name="X"/> on, the curve gives <paramref name="Y"/> points.</summary>
public readonly record struct CurvePoint(float X, float Y);

/// <summary>
/// A piecewise linear curve that turns a measured value (distance, time, altitude, impact) into
/// points. Stored in the mission file as <c>s32 count</c> followed by room for 10 points of two
/// floats (Unk803599D0 in src/app/task.h, 0x54 bytes).
/// </summary>
/// <remarks>
/// The game evaluates it in func_80313F08 (code_9A960.c): below the first point the first point's
/// points, above the last point the last point's points, linear in between. Two points with the
/// same x make a step. A curve with count 0 is skipped (no points).
/// </remarks>
public sealed class ScoreCurve
{
    /// <summary>Size of a curve in the mission file.</summary>
    public const int Size = 0x54;

    /// <summary>Points that fit into a curve (0x50 bytes of room). Retail curves use at most 7.</summary>
    public const int MaxPoints = 10;

    private readonly byte[] _unusedSlots;

    private ScoreCurve(IReadOnlyList<CurvePoint> points, byte[] unusedSlots)
    {
        Points = points;
        _unusedSlots = unusedSlots;
    }

    /// <summary>The points of the curve, in order.</summary>
    public IReadOnlyList<CurvePoint> Points { get; }

    /// <summary>True if the game evaluates the curve (count above 0).</summary>
    public bool IsUsed => Points.Count > 0;

    /// <summary>The most points the curve gives, or 0 for an empty curve.</summary>
    public float Maximum => Points.Count == 0 ? 0 : Points.Max(p => p.Y);

    /// <summary>True if the curve can give points at all.</summary>
    public bool GivesPoints => Maximum > 0;

    /// <summary>Reads a curve.</summary>
    /// <exception cref="InvalidDataException">The data is too short or the count is out of range.</exception>
    public static ScoreCurve Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new InvalidDataException("A score curve needs 0x54 bytes.");
        }

        int count = BinaryPrimitives.ReadInt32BigEndian(data);
        if (count is < 0 or > MaxPoints)
        {
            throw new InvalidDataException($"A score curve has {count} points; at most {MaxPoints} fit.");
        }

        var points = new List<CurvePoint>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> p = data.Slice(4 + (i * 8), 8);
            points.Add(new CurvePoint(BinaryPrimitives.ReadSingleBigEndian(p), BinaryPrimitives.ReadSingleBigEndian(p[4..])));
        }

        // The slots behind the used points are kept as they are, so an unchanged curve is
        // written back byte for byte.
        return new ScoreCurve(points, data[(4 + (count * 8))..Size].ToArray());
    }

    /// <summary>A curve with the given points.</summary>
    /// <exception cref="ArgumentException">More than <see cref="MaxPoints"/> points.</exception>
    public static ScoreCurve Create(IEnumerable<CurvePoint> points)
    {
        List<CurvePoint> list = points.ToList();
        if (list.Count > MaxPoints)
        {
            throw new ArgumentException($"A score curve can have at most {MaxPoints} points.", nameof(points));
        }

        return new ScoreCurve(list, new byte[Size - 4 - (list.Count * 8)]);
    }

    /// <summary>Writes the curve (0x54 bytes).</summary>
    public void Write(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination, Points.Count);
        for (int i = 0; i < Points.Count; i++)
        {
            Span<byte> p = destination.Slice(4 + (i * 8), 8);
            BinaryPrimitives.WriteSingleBigEndian(p, Points[i].X);
            BinaryPrimitives.WriteSingleBigEndian(p[4..], Points[i].Y);
        }

        _unusedSlots.CopyTo(destination[(4 + (Points.Count * 8))..Size]);
    }

    /// <summary>The points for a measured value, computed like the game (func_80313F08).</summary>
    public float Evaluate(float value)
    {
        if (Points.Count == 0)
        {
            return 0;
        }

        if (value <= Points[0].X)
        {
            return Points[0].Y;
        }

        if (Points[^1].X <= value)
        {
            return Points[^1].Y;
        }

        int i = 1;
        while (i < Points.Count - 1 && value >= Points[i].X)
        {
            i++;
        }

        CurvePoint prev = Points[i - 1];
        CurvePoint cur = Points[i];
        float t = (value - prev.X) / (cur.X - prev.X);
        return (cur.Y * t) + (prev.Y * (1.0f - t));
    }
}
