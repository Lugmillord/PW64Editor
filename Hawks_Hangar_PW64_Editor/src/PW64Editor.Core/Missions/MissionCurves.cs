using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>The shape of a time curve as the Missions tab edits it.</summary>
public enum TimeCurveShape
{
    /// <summary>No time points.</summary>
    None,

    /// <summary>Full points up to a time, then falling to 0 (most missions).</summary>
    Plateau,

    /// <summary>Full points around a target time, fewer before and after ("as close as possible to 3'00"").</summary>
    Target,
}

/// <summary>
/// A time curve described by a few values.
/// </summary>
/// <param name="Shape">The shape.</param>
/// <param name="Points">The most time points.</param>
/// <param name="FullUntil">Plateau: full points up to this many seconds (the curve adds half a second of grace, like the original).</param>
/// <param name="ZeroAt">Plateau: no points from this many seconds on.</param>
/// <param name="TargetTime">Target: the time to land at.</param>
/// <param name="Window">Target: full points up to this many seconds before and after the target time.</param>
/// <param name="ZeroDistance">Target: no points from this many seconds before or after the target time on.</param>
/// <param name="IsExact">True if the curve has exactly this simple shape; false if it has more
/// steps (the values then describe it roughly, and writing them simplifies the curve).</param>
public sealed record TimeCurveSettings(
    TimeCurveShape Shape,
    int Points,
    float FullUntil = 0,
    float ZeroAt = 0,
    float TargetTime = 0,
    float Window = 0,
    float ZeroDistance = 0,
    bool IsExact = true);

/// <summary>
/// Creating, scaling and describing the score curves of a mission.
/// </summary>
public static class MissionCurves
{
    /// <summary>Half a second of grace the original curves add to the time in the text (90.5 s for "1'30"").</summary>
    public const float Grace = 0.5f;

    /// <summary>An empty curve as in the original missions: one point (0, 0), no points.</summary>
    public static ScoreCurve Empty { get; } = ScoreCurve.Create([new CurvePoint(0, 0)]);

    /// <summary>
    /// The value of a curve as the score sheet shows it. The Gyrocopter multiplies its two landing
    /// measurements, which share the accuracy curve, so its shown value is the square of the curve's maximum.
    /// </summary>
    /// <param name="kind">The curve.</param>
    /// <param name="vehicle">The vehicle.</param>
    /// <param name="curve">The curve of the mission file.</param>
    /// <param name="touchdown">The Gyrocopter's own touchdown curve, or null (it uses <paramref name="curve"/> twice).</param>
    public static int ShownPoints(ScoreCurveKind kind, Vehicle vehicle, ScoreCurve curve, ScoreCurve? touchdown = null)
    {
        float max = curve.Maximum;
        return kind switch
        {
            ScoreCurveKind.LandingAccuracy when vehicle == Vehicle.Gyrocopter => (int)((max * (touchdown?.Maximum ?? max)) + 0.5f),
            ScoreCurveKind.Time => (int)max,
            _ => (int)(max + 0.001f),
        };
    }

    /// <summary>
    /// The curve with another number of shown points, keeping its shape. A curve without points gets
    /// the default shape (<see cref="Default"/>); 0 points give an empty curve.
    /// </summary>
    public static ScoreCurve WithShownPoints(ScoreCurveKind kind, Vehicle vehicle, ScoreCurve curve, int points)
    {
        if (points <= 0)
        {
            return Empty;
        }

        float target = kind == ScoreCurveKind.LandingAccuracy && vehicle == Vehicle.Gyrocopter ? MathF.Sqrt(points) : points;
        float max = curve.Maximum;
        if (max <= 0)
        {
            return Default(kind, vehicle, points);
        }

        float factor = target / max;
        return ScoreCurve.Create(curve.Points.Select(p => new CurvePoint(p.X, Round(p.Y * factor))));
    }

    /// <summary>
    /// The Gyrocopter's two landing curves with another number of shown points: both are scaled by the same
    /// factor, so their shapes (and the share of each) stay.
    /// </summary>
    /// <returns>The new resting position curve and touchdown curve.</returns>
    public static (ScoreCurve Resting, ScoreCurve Touchdown) WithShownPoints(ScoreCurve rest, ScoreCurve touchdown, int points)
    {
        float shown = rest.Maximum * touchdown.Maximum;
        if (points <= 0 || shown <= 0)
        {
            ScoreCurve both = WithShownPoints(ScoreCurveKind.LandingAccuracy, Vehicle.Gyrocopter, rest, points);
            return (both, both);
        }

        float factor = MathF.Sqrt(points / shown);
        ScoreCurve Scale(ScoreCurve c) => ScoreCurve.Create(c.Points.Select(p => new CurvePoint(p.X, Round(p.Y * factor))));
        return (Scale(rest), Scale(touchdown));
    }

    /// <summary>
    /// The shape a newly added source gets, taken from typical original missions.
    /// </summary>
    public static ScoreCurve Default(ScoreCurveKind kind, Vehicle vehicle, int points)
    {
        float p = points;
        return kind switch
        {
            ScoreCurveKind.LandingAccuracy when vehicle == Vehicle.Gyrocopter => GyrocopterAccuracy(MathF.Sqrt(p)),
            ScoreCurveKind.LandingAccuracy => ScoreCurve.Create(
                [new(0, p), new(6, p), new(11, Round(p * 0.75f)), new(18, Round(p * 0.5f)), new(35, Round(p * 0.1f)), new(35, 0)]),
            ScoreCurveKind.Impact => ScoreCurve.Create([new(0, p), new(5, p), new(9, Round(p * 0.5f)), new(14, 0)]),
            // In meters as the game shows them (full points at 500 m), stored in game units.
            ScoreCurveKind.Altitude => ScoreCurve.Create(
                [new(0, 0), new(M(140), 0), new(M(215), Round(p * 3 / 7)), new(M(320), Round(p * 5 / 7)), new(M(400), Round(p * 6 / 7)), new(M(500), p)]),
            // Full points after 2000 m flown (meters as the game shows them).
            ScoreCurveKind.Distance => ScoreCurve.Create([new(0, 0), new(M(2000), p)]),
            ScoreCurveKind.Time => Plateau(points, 120, 300),
            _ => ScoreCurve.Create([new(0, p)]),
        };
    }

    /// <summary>Meters as shown in the game to game units (rounded to a tenth).</summary>
    private static float M(float meters) => MathF.Round(GameUnits.FromMeters(meters), 1);

    /// <summary>Full points up to <paramref name="fullUntil"/> seconds (plus the grace), then a straight fall to 0 at <paramref name="zeroAt"/>.</summary>
    public static ScoreCurve Plateau(int points, float fullUntil, float zeroAt)
    {
        if (points <= 0)
        {
            return Empty;
        }

        float end = fullUntil + Grace;
        float zero = Math.Max(zeroAt, end + 1);
        return ScoreCurve.Create([new(0, points), new(end, points), new(zero, 0)]);
    }

    /// <summary>
    /// Full points within <paramref name="window"/> seconds of the target time, a straight rise and
    /// fall over <paramref name="zeroDistance"/> seconds around it, nothing outside.
    /// </summary>
    public static ScoreCurve Target(int points, float targetTime, float window, float zeroDistance)
    {
        if (points <= 0)
        {
            return Empty;
        }

        float w = Math.Max(0, window);
        float z = Math.Max(zeroDistance, w + 1);
        float start = Math.Max(0, targetTime - z);
        return ScoreCurve.Create(
        [
            new(0, 0), new(start, 0), new(Math.Max(start, targetTime - w), points),
            new(targetTime + w, points), new(targetTime + z, 0),
        ]);
    }

    /// <summary>Describes a time curve by the values of <see cref="TimeCurveSettings"/>.</summary>
    public static TimeCurveSettings Describe(ScoreCurve curve)
    {
        IReadOnlyList<CurvePoint> p = curve.Points;
        int points = (int)curve.Maximum;
        if (points <= 0 || p.Count == 0)
        {
            return new TimeCurveSettings(TimeCurveShape.None, 0);
        }

        float max = curve.Maximum;
        if (p[0].Y >= max)
        {
            // Plateau: full points from the start; the plateau ends at the last point with full points.
            int last = 0;
            while (last + 1 < p.Count && p[last + 1].Y >= max)
            {
                last++;
            }

            float fullUntil = MathF.Max(0, p[last].X - Grace);
            float zeroAt = p.FirstOrDefault(q => q.X > p[last].X && q.Y <= 0) is { X: > 0 } zero ? zero.X : p[^1].X;
            bool exact = p.Count == 3 && last == 1 && p[2].Y == 0;
            return new TimeCurveSettings(TimeCurveShape.Plateau, points, FullUntil: fullUntil, ZeroAt: zeroAt, IsExact: exact);
        }

        // Target: the middle of the points with full points.
        List<CurvePoint> full = p.Where(q => q.Y >= max).ToList();
        float target = (full[0].X + full[^1].X) / 2;
        float window = (full[^1].X - full[0].X) / 2;
        float endZero = p.LastOrDefault(q => q.Y <= 0 && q.X > full[^1].X) is { } after && after.X > 0 ? after.X : p[^1].X;
        bool isExact = p.Count == 5 && p[0].Y == 0 && p[1].Y == 0 && p[4].Y == 0 && full.Count == 2;
        return new TimeCurveSettings(TimeCurveShape.Target, points, TargetTime: target, Window: window, ZeroDistance: endZero - target, IsExact: isExact);
    }

    /// <summary>A time curve from its settings.</summary>
    public static ScoreCurve FromSettings(TimeCurveSettings settings) => settings.Shape switch
    {
        TimeCurveShape.Plateau => Plateau(settings.Points, settings.FullUntil, settings.ZeroAt),
        TimeCurveShape.Target => Target(settings.Points, settings.TargetTime, settings.Window, settings.ZeroDistance),
        _ => Empty,
    };

    /// <summary>The Gyrocopter's accuracy curve (two measurements multiplied), shape of the original.</summary>
    /// <summary>Highest number of points a curve point may give (the game stores at most 126 per test).</summary>
    public const float MaxCurvePoints = 126;

    /// <summary>
    /// Checks points typed in for a curve: 1 to 10 points, measured values from 0 on and never
    /// falling (two points at the same value make a step), points from 0 to 126. The game never
    /// divides by the distance of two points at the same value (func_80313F08), so steps are safe.
    /// </summary>
    /// <returns>Null, or what is wrong.</returns>
    public static string? CheckPoints(IReadOnlyList<CurvePoint> points)
    {
        if (points.Count == 0)
        {
            return CoreText.T("A curve needs at least one point.");
        }

        if (points.Count > ScoreCurve.MaxPoints)
        {
            return CoreText.F("A curve has at most {0} points.", ScoreCurve.MaxPoints);
        }

        for (int i = 0; i < points.Count; i++)
        {
            CurvePoint p = points[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0 || p.X > 100000)
            {
                return CoreText.F("Point {0}: the value must be a number from 0 to 100000.", i + 1);
            }

            if (p.Y is < 0 or > MaxCurvePoints)
            {
                return CoreText.F("Point {0}: the points must be from 0 to {1}.", i + 1, MaxCurvePoints);
            }

            if (i > 0 && p.X < points[i - 1].X)
            {
                return CoreText.F("Point {0}: the values must not get smaller from one point to the next.", i + 1);
            }
        }

        return null;
    }

    private static ScoreCurve GyrocopterAccuracy(float s) =>
        ScoreCurve.Create([new(0, s), new(2, s), new(6, Round(s * 0.56f)), new(12, Round(s * 0.33f)), new(12, 0)]);

    /// <summary>Rounds to 3 decimals, so curves stay readable.</summary>
    private static float Round(float value) => MathF.Round(value * 1000) / 1000;
}
