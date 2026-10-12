using System.Globalization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>A row of a generated point text: source and points.</summary>
public readonly record struct PointLine(PointSource Source, int Points);

/// <summary>
/// Writes the point texts of a mission: "…_S1" for the mission screen (points without slash, total
/// and footnote) and "…_S2" for the result sheet (points with slash; the game writes the reached
/// points in front of the slash).
/// </summary>
/// <remarks>
/// <para>The layout follows the original texts. The game draws the numbers of the result sheet at
/// fixed heights, bottom-aligned, and the line above the total itself; the text lines up with them
/// by starting with empty lines: 4 rows → 1, 3 → 2, 2 → 3, 1 → 4 (5 − rows).</para>
/// <para>The labels are the English words of the original game (the game's texts are English).</para>
/// </remarks>
public static class MissionTextGenerator
{
    /// <summary>Most point rows the result sheet has.</summary>
    public const int MaxRows = 4;

    /// <summary>Horizontal position of the numbers (as in the original texts).</summary>
    public const int NumberColumn = 212;

    /// <summary>Position for numbers with three digits on the mission screen, so they end where two-digit numbers end.</summary>
    public const int WideNumberColumn = 202;

    /// <summary>The label of a source on the score sheet, as in the original game.</summary>
    public static string SheetLabel(PointSource source) => source switch
    {
        PointSource.LandingImpact => "Landing Impact",
        PointSource.LandingAccuracy => "Landing Accuracy",
        PointSource.Time => "Time Pts.",
        PointSource.Rings => "Ring",
        PointSource.Balloons => "Balloon",
        PointSource.Altitude => "Altitude Pts.",
        PointSource.Targets => "Target",
        PointSource.MecaHawks => "Destroyed",
        PointSource.HoverPads => "Floating Pads",
        PointSource.Photo => "Photo Pts.",
        PointSource.CannonballTarget1 => "Target 1",
        PointSource.CannonballTarget2 => "Target 2",
        PointSource.CannonballTarget3 => "Target 3",
        PointSource.CannonballTarget4 => "Target 4",
        PointSource.Formation => "Formation",
        _ => string.Empty,
    };

    /// <summary>
    /// True if the result sheet of the vehicle has a "Points Deducted" row. Fixed in the game code
    /// (resultDrawTally in results.c): Hang Glider, Cannonball and Sky Diving have none.
    /// </summary>
    public static bool HasDeductionRow(Vehicle vehicle) =>
        vehicle is not (Vehicle.HangGlider or Vehicle.Cannonball or Vehicle.SkyDiving);

    /// <summary>The text of the mission screen ("…_S1").</summary>
    /// <param name="rows">The rows, top row first (at most 4).</param>
    /// <param name="footnote">Lines of the footnote, without the leading "*" (empty: no footnote).</param>
    public static string MissionScreenText(IReadOnlyList<PointLine> rows, IReadOnlyList<string> footnote)
    {
        CheckRows(rows);
        var lines = new List<string>();
        AddLeadingLines(lines, rows.Count);
        foreach (PointLine row in rows)
        {
            lines.Add($"  {SheetLabel(row.Source)} [x={Column(row.Points)}][b]{Number(row.Points)}[/b] pts.");
        }

        int total = rows.Sum(r => r.Points);
        lines.Add($"  [b]Total[/b] [x={Column(total)}][b]{Number(total)} pts[/b].");

        List<string> note = footnote.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (note.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(" *" + note[0].TrimStart('*').TrimStart());
            lines.AddRange(note.Skip(1).Select(l => "    " + l));
        }

        return string.Join("\n", lines);
    }

    /// <summary>The text of the result sheet ("…_S2").</summary>
    /// <param name="rows">The rows, top row first (at most 4).</param>
    /// <param name="vehicle">The vehicle (decides whether there is a "Points Deducted" row).</param>
    public static string ResultSheetText(IReadOnlyList<PointLine> rows, Vehicle vehicle)
    {
        CheckRows(rows);
        var lines = new List<string>();
        AddLeadingLines(lines, rows.Count);
        foreach (PointLine row in rows)
        {
            lines.Add($"  {SheetLabel(row.Source)} [x={NumberColumn}]/[b]{Number(row.Points)}[/b] pts.");
        }

        if (HasDeductionRow(vehicle))
        {
            lines.Add($"  Points Deducted [x={NumberColumn}] pts.");
        }

        lines.Add("  [b]Total[/b]");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The footnote of a mission screen text: the lines after the empty line behind the total,
    /// without the leading "*" and indentation.
    /// </summary>
    public static IReadOnlyList<string> ExtractFootnote(string missionScreenMarkup)
    {
        string[] lines = missionScreenMarkup.Split('\n');
        int total = Array.FindLastIndex(lines, l => l.Contains("Total", StringComparison.Ordinal));
        if (total < 0)
        {
            return [];
        }

        return lines.Skip(total + 1)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select((l, i) => i == 0 ? l.TrimStart('*').TrimStart() : l)
            .ToList();
    }

    /// <summary>A footnote that fits the mission: the time limit, the altitude, or the photo rule.</summary>
    public static IReadOnlyList<string> SuggestFootnote(MissionComm comm, IReadOnlyList<PointLine> rows)
    {
        if (rows.Any(r => r.Source == PointSource.Time))
        {
            TimeCurveSettings time = MissionCurves.Describe(comm.GetCurve(ScoreCurveKind.Time));
            return time.Shape switch
            {
                TimeCurveShape.Plateau => ["Time points will be deducted", $"when {Clock(time.FullUntil)} have elapsed."],
                TimeCurveShape.Target => ["Time points will depend on how", $"close to {Clock(time.TargetTime)} you are when landing."],
                _ => [],
            };
        }

        if (rows.Any(r => r.Source == PointSource.Altitude))
        {
            return ["The higher the altitude, the more", "altitude points you will receive."];
        }

        if (rows.Any(r => r.Source == PointSource.Photo))
        {
            return ["The photo with the best score", "will be counted as the best shot."];
        }

        return [];
    }

    /// <summary>A time like the game's texts: 1'30".</summary>
    public static string Clock(float seconds)
    {
        int whole = (int)MathF.Round(seconds);
        return $"{whole / 60}'{whole % 60:D2}\"";
    }

    private static void AddLeadingLines(List<string> lines, int rows)
    {
        lines.Add(string.Empty);
        for (int i = rows; i < MaxRows; i++)
        {
            lines.Add(string.Empty);
        }
    }

    private static int Column(int points) => points >= 100 ? WideNumberColumn : NumberColumn;

    private static string Number(int points) => points.ToString(CultureInfo.InvariantCulture);

    private static void CheckRows(IReadOnlyList<PointLine> rows)
    {
        if (rows.Count is 0 or > MaxRows)
        {
            throw new ArgumentException($"A score sheet has 1 to {MaxRows} rows.", nameof(rows));
        }

        if (rows.Any(r => SheetLabel(r.Source).Length == 0))
        {
            throw new ArgumentException("A row has no source.", nameof(rows));
        }
    }
}
