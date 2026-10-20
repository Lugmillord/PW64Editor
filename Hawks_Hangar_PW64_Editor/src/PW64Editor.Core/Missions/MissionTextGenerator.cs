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
        PointSource.Distance => "Distance Pts.",
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

    /// <summary>Longest footnote line the generator writes (the retail footnotes have up to 35 characters).</summary>
    public const int FootnoteWidth = 34;

    /// <summary>
    /// Lines the footnote has room for: a mission screen text has 10 lines, and the rows, the total
    /// and the empty lines above them always take 7.
    /// </summary>
    public const int FootnoteLines = 3;

    /// <summary>
    /// A footnote written from the mission's settings: the time limit, the hard end time, the
    /// conditions for the time points, the altitude for full altitude points, "a crash gives 0
    /// points" and the photo rule. The sentences follow each other, wrapped to <see cref="FootnoteWidth"/> characters; if
    /// they need more than <see cref="FootnoteLines"/> lines, the short forms are used.
    /// </summary>
    /// <param name="comm">The mission's COMM block.</param>
    /// <param name="rows">The point rows.</param>
    /// <param name="conditions">The conditions of the time points.</param>
    /// <param name="hardEndZero">The hard end time (if the mission has one) gives 0 points.</param>
    public static IReadOnlyList<string> SuggestFootnote(MissionComm comm, IReadOnlyList<PointLine> rows, MissionConditions conditions,
        bool hardEndZero = false)
    {
        var parts = new List<(string Long, string Short)>();
        bool time = rows.Any(r => r.Source == PointSource.Time && r.Points > 0);
        if (time)
        {
            TimeCurveSettings curve = MissionCurves.Describe(comm.GetCurve(ScoreCurveKind.Time));
            switch (curve.Shape)
            {
                case TimeCurveShape.Plateau:
                    parts.Add(($"Time points will be deducted when {Clock(curve.FullUntil)} have elapsed.",
                        $"Time points drop after {Clock(curve.FullUntil)}."));
                    break;
                case TimeCurveShape.Target:
                    parts.Add(($"Time points will depend on how close to {Clock(curve.TargetTime)} you are when landing.",
                        $"Land close to {Clock(curve.TargetTime)}."));
                    break;
            }

            var needs = new List<string>();
            foreach ((ConditionObject type, int count) in conditions.Required.OrderBy(r => r.Key))
            {
                needs.Add(count == MissionConditions.All
                    ? $"all {EnglishName(type, 2)}"
                    : $"{(count == 1 ? "1" : $"at least {count}")} {EnglishName(type, count)}");
            }

            if (conditions.MinimumAltitude is { } altitude)
            {
                needs.Add($"{Meters(altitude)} altitude");
            }

            if (conditions.MinimumDistance is { } distance)
            {
                needs.Add($"{Meters(distance)} flown");
            }

            if (needs.Count > 0)
            {
                parts.Add(($"Time points only with {JoinEnglish(needs)}.", $"Time only with {JoinEnglish(needs)}."));
            }
        }

        if (comm.HardEndTime > 0)
        {
            parts.Add(hardEndZero
                ? ($"The flight ends after {Clock(comm.HardEndTime)} with 0 points.", $"Ends at {Clock(comm.HardEndTime)}: 0 pts.")
                : ($"The flight ends after {Clock(comm.HardEndTime)}.", $"Ends at {Clock(comm.HardEndTime)}."));
        }

        if (rows.Any(r => r.Source == PointSource.Altitude && r.Points > 0))
        {
            ScoreCurve curve = comm.GetCurve(ScoreCurveKind.Altitude);
            float full = curve.Points.FirstOrDefault(p => p.Y >= curve.Maximum).X;
            parts.Add(($"Full altitude points from {Meters(full)} on.", $"Full altitude pts. at {Meters(full)}."));
        }

        if (rows.Any(r => r.Source == PointSource.Distance && r.Points > 0))
        {
            ScoreCurve curve = comm.GetCurve(ScoreCurveKind.Distance);
            float full = curve.Points.FirstOrDefault(p => p.Y >= curve.Maximum).X;
            parts.Add(($"Full distance points after {Meters(full)} flown.", $"Full distance pts. at {Meters(full)}."));
        }

        if (conditions.CrashGivesZero)
        {
            parts.Add(("A crash gives 0 points.", "Crash: 0 pts."));
        }
        else if (conditions.CrashTimePoints && time)
        {
            parts.Add(("Time points also count after a crash.", "Crash: time pts. count."));
        }

        if (rows.Any(r => r.Source == PointSource.Photo))
        {
            parts.Add(("The photo with the best score will be counted as the best shot.", "The best photo counts."));
        }

        List<string> lines = Wrap(string.Join(" ", parts.Select(p => p.Long))).ToList();
        if (lines.Count > FootnoteLines)
        {
            lines = Wrap(string.Join(" ", parts.Select(p => p.Short))).ToList();
        }

        return lines;
    }

    /// <summary>Breaks text into lines of at most <see cref="FootnoteWidth"/> characters.</summary>
    private static IEnumerable<string> Wrap(string sentence)
    {
        var line = new System.Text.StringBuilder();
        foreach (string word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > FootnoteWidth)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <summary>A height in game units, written in the meters the game shows.</summary>
    private static string Meters(float units) => $"{MathF.Round(GameUnits.ToMeters(units)).ToString("0", CultureInfo.InvariantCulture)} m";

    private static string JoinEnglish(IReadOnlyList<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    /// <summary>The game's (English) name of an object type, singular for 1.</summary>
    private static string EnglishName(ConditionObject type, int count)
    {
        bool one = count == 1;
        return type switch
        {
            ConditionObject.Rings => one ? "ring" : "rings",
            ConditionObject.Balloons => one ? "balloon" : "balloons",
            ConditionObject.Targets => one ? "target" : "targets",
            ConditionObject.HoverPads => one ? "floating pad" : "floating pads",
            ConditionObject.MecaHawks => one ? "Meca Hawk" : "Meca Hawks",
            ConditionObject.BallTargets => one ? "ball target" : "ball targets",
            _ => one ? "goal" : "goals",
        };
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
