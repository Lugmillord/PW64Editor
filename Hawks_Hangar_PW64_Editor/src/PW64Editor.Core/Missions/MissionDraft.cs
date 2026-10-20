using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// The changes of a mission while it is edited in the Missions tab, before they are saved: its
/// COMM block and its settings (rows of the result sheet, numbers and footnote of the texts).
/// </summary>
/// <remarks>
/// As soon as anything is changed, the mission's point texts are written by the editor
/// (<see cref="MissionSettings.GenerateTexts"/>). The footnote then starts with the footnote of the
/// current mission screen text, so nothing is lost.
/// </remarks>
public sealed class MissionDraft
{
    private readonly byte[] _loadedComm;
    private readonly MissionSettings? _loadedSettings;

    /// <summary>Starts editing a mission.</summary>
    /// <param name="mission">The mission as loaded.</param>
    /// <param name="currentScreenText">The current markup of its mission screen text (for the footnote and the numbers), or null.</param>
    public MissionDraft(MissionInfo mission, string? currentScreenText)
    {
        Mission = mission;
        Comm = mission.File.Comm.Clone();
        _loadedComm = mission.File.Comm.ToArray();
        _loadedSettings = mission.Settings?.Clone();
        Settings = mission.Settings?.Clone() ?? MissionSettings.For(mission.Id);
        Rows = mission.Rows.Select(r => r.Source).ToList();
        CurrentScreenText = currentScreenText;
    }

    /// <summary>The mission as loaded.</summary>
    public MissionInfo Mission { get; }

    /// <summary>The edited COMM block.</summary>
    public MissionComm Comm { get; private set; }

    /// <summary>The edited settings.</summary>
    public MissionSettings Settings { get; private set; }

    /// <summary>The rows of the result sheet, top row first.</summary>
    public List<PointSource> Rows { get; private set; }

    /// <summary>The mission screen text when editing started.</summary>
    public string? CurrentScreenText { get; }

    /// <summary>True if something differs from the loaded state.</summary>
    public bool IsChanged =>
        !Comm.ToArray().AsSpan().SequenceEqual(_loadedComm)
        || !SettingsEqual(Settings, _loadedSettings);

    /// <summary>The vehicle.</summary>
    public Vehicle Vehicle => Mission.Id.Vehicle;

    /// <summary>True if the rows and the sources of this mission can be changed (not Cannonball, not Birdman).</summary>
    public bool CanChangeSources => MissionCatalog.AvailableSources(Vehicle).Count > 0;

    /// <summary>The rows with their values for the edited data.</summary>
    /// <param name="otherComm">The current COMM block of another mission (Cannonball targets), or null.</param>
    public IReadOnlyList<MissionPointRow> EvaluateRows(Func<TestId, MissionComm?> otherComm) =>
        Rows.Select(r => MissionCatalog.Evaluate(r, Mission.Id, Mission.File, Comm, otherComm, TouchdownCurve)).ToList();

    /// <summary>
    /// The number a row shows in the point texts: the curve value, or for points from objects and
    /// points the game computes the number the user entered (by default the number of the current
    /// text, or the sum over all objects).
    /// </summary>
    public int TextPoints(MissionPointRow row)
    {
        if (row.Kind is PointValueKind.Curve or PointValueKind.OtherMission && row.Value is { } exact)
        {
            return exact;
        }

        if (Settings.TextPoints.TryGetValue((int)row.Source, out int entered))
        {
            return entered;
        }

        // The number of the current text, found by the row's place when editing started (rows can
        // be moved since then).
        List<int> numbers = CurrentScreenText is null ? [] : TextNumbers(CurrentScreenText);
        int loaded = Mission.Rows.ToList().FindIndex(r => r.Source == row.Source);
        return loaded >= 0 && loaded < numbers.Count
            ? numbers[loaded]
            : row.Value ?? 0;
    }

    /// <summary>The rows as the generated texts show them.</summary>
    public IReadOnlyList<PointLine> TextLines(Func<TestId, MissionComm?> otherComm)
    {
        IReadOnlyList<MissionPointRow> rows = EvaluateRows(otherComm);
        return rows.Select(r => new PointLine(r.Source, TextPoints(r))).ToList();
    }

    /// <summary>The generated mission screen text.</summary>
    public string MissionScreenText(Func<TestId, MissionComm?> otherComm) =>
        MissionTextGenerator.MissionScreenText(TextLines(otherComm), Footnote(otherComm));

    /// <summary>The footnote the mission screen text gets: written from the settings, or as typed in.</summary>
    public IReadOnlyList<string> Footnote(Func<TestId, MissionComm?> otherComm) =>
        Settings.AutoFootnote == true
            ? MissionTextGenerator.SuggestFootnote(Comm, TextLines(otherComm), Conditions, Settings.HardEndZero)
            : Settings.Footnote;

    /// <summary>The conditions: as set in the editor, or the retail condition of the mission file.</summary>
    public MissionConditions Conditions => Settings.Conditions ?? MissionConditions.FromComm(Comm);

    /// <summary>True if "a crash gives 0 points" can be set (not for Cannonball: a shot always ends in the water or on a target).</summary>
    public bool CanSetCrashCondition => Vehicle != Vehicle.Cannonball;

    /// <summary>What a crash gives: the original rules, time points too, or 0 points.</summary>
    public CrashScoring CrashScoring =>
        Conditions.CrashGivesZero ? CrashScoring.Zero : Conditions.CrashTimePoints ? CrashScoring.TimePoints : CrashScoring.Original;

    /// <summary>
    /// With time points after a crash: the time points a crash right after the start gives (the time curve at
    /// 0 s), or 0. The editor shows a hint, since most time curves give their full points at the start.
    /// </summary>
    public int EarlyCrashTimePoints =>
        CrashScoring == CrashScoring.TimePoints && Rows.Contains(PointSource.Time) && Comm.GetCurve(ScoreCurveKind.Time) is { IsUsed: true } curve
            ? (int)curve.Points[0].Y
            : 0;

    /// <summary>The Gyrocopter's own touchdown curve, or null (both measurements use the landing accuracy curve).</summary>
    public ScoreCurve? TouchdownCurve =>
        Vehicle == Vehicle.Gyrocopter && Settings.TouchdownCurve is { Count: > 0 } points ? ScoreCurve.Create(points) : null;

    /// <summary>The generated result sheet text.</summary>
    public string ResultSheetText(Func<TestId, MissionComm?> otherComm) =>
        MissionTextGenerator.ResultSheetText(TextLines(otherComm), Vehicle);

    // ----------------------------------------------------------------- changes

    /// <summary>
    /// Changes the source of a row. A curve source that leaves the sheet gets an empty curve (it
    /// would still count, invisibly); a newly chosen curve source gets the default shape with the
    /// given points. <see cref="PointSource.None"/> removes the row.
    /// </summary>
    /// <param name="index">Row (top row is 0; one behind the last adds a row).</param>
    /// <param name="source">The new source.</param>
    /// <param name="points">Points for a newly chosen curve source.</param>
    /// <returns>Null, or why the change is not possible.</returns>
    public string? SetRowSource(int index, PointSource source, int points)
    {
        if (!CanChangeSources)
        {
            return CoreText.T("The sources of this mission cannot be changed.");
        }

        if (index < 0 || index > Rows.Count || index >= MissionTextGenerator.MaxRows)
        {
            return CoreText.F("A result sheet has at most {0} rows.", MissionTextGenerator.MaxRows);
        }

        if (source != PointSource.None && !MissionCatalog.AvailableSources(Vehicle).Contains(source))
        {
            return CoreText.F("{0} cannot collect {1}.", Vehicles.Name(Vehicle), PointSources.Name(source));
        }

        if (source != PointSource.None && Rows.Where((r, i) => i != index).Contains(source))
        {
            return CoreText.F("{0} is already a row of this sheet.", PointSources.Name(source));
        }

        if (source == PointSource.None && index < Rows.Count && Rows.Count == 1)
        {
            return CoreText.T("A result sheet needs at least one row.");
        }

        PointSource old = index < Rows.Count ? Rows[index] : PointSource.None;
        if (old == source)
        {
            return null;
        }

        if (PointSources.Curve(old) is { } oldCurve)
        {
            Comm.SetCurve(oldCurve, MissionCurves.Empty);
        }

        Settings.TextPoints.Remove((int)old);
        if (PointSources.Curve(source) is { } newCurve && !Comm.GetCurve(newCurve).GivesPoints)
        {
            Comm.SetCurve(newCurve, MissionCurves.Default(newCurve, Vehicle, Math.Max(1, points)));
        }

        if (source == PointSource.None)
        {
            if (index < Rows.Count)
            {
                Rows.RemoveAt(index);
            }
        }
        else if (index < Rows.Count)
        {
            Rows[index] = source;
        }
        else
        {
            Rows.Add(source);
        }

        Touch();
        return null;
    }

    /// <summary>Moves a row to another place of the sheet (the order of the result sheet and the texts).</summary>
    /// <param name="from">Row to move (top row is 0).</param>
    /// <param name="to">Its new place.</param>
    /// <returns>Null, or why the change is not possible.</returns>
    public string? MoveRow(int from, int to)
    {
        if (!CanChangeSources)
        {
            return CoreText.T("The sources of this mission cannot be changed.");
        }

        if (from < 0 || from >= Rows.Count || to < 0 || to >= Rows.Count)
        {
            return CoreText.T("This row does not exist.");
        }

        if (from == to)
        {
            return null;
        }

        PointSource source = Rows[from];
        Rows.RemoveAt(from);
        Rows.Insert(to, source);
        Touch();
        return null;
    }

    /// <summary>
    /// Replaces the points of a score curve by points the user typed in (the value measured, for
    /// example seconds or altitude, and the points for it).
    /// </summary>
    /// <returns>Null, or why the points cannot be used.</returns>
    public string? SetCurvePoints(ScoreCurveKind kind, IReadOnlyList<CurvePoint> points)
    {
        if (MissionCurves.CheckPoints(points) is { } problem)
        {
            return problem;
        }

        Comm.SetCurve(kind, ScoreCurve.Create(points));
        Touch();
        return null;
    }

    /// <summary>Changes the points of a row: the curve for curve sources, the number of the texts for the others.</summary>
    public void SetRowPoints(int index, int points, Func<TestId, MissionComm?> otherComm)
    {
        PointSource source = Rows[index];
        if (PointSources.Curve(source) == ScoreCurveKind.LandingAccuracy && TouchdownCurve is { } touchdown)
        {
            // The Gyrocopter with two curves: both are scaled, so the product gives the points.
            (ScoreCurve rest, ScoreCurve touch) = MissionCurves.WithShownPoints(Comm.GetCurve(ScoreCurveKind.LandingAccuracy), touchdown, points);
            Comm.SetCurve(ScoreCurveKind.LandingAccuracy, rest);
            Settings.TouchdownCurve = [.. touch.Points];
        }
        else if (PointSources.Curve(source) is { } kind)
        {
            Comm.SetCurve(kind, MissionCurves.WithShownPoints(kind, Vehicle, Comm.GetCurve(kind), points));
        }
        else if (source >= PointSource.CannonballTarget1 && source <= PointSource.CannonballTarget4
                 && source - PointSource.CannonballTarget1 == Mission.Id.Test)
        {
            // Cannonball: the row of this target is this mission's own landing curve.
            Comm.SetCurve(ScoreCurveKind.LandingAccuracy, MissionCurves.WithShownPoints(ScoreCurveKind.LandingAccuracy, Vehicle,
                Comm.GetCurve(ScoreCurveKind.LandingAccuracy), points));
        }
        else
        {
            Settings.TextPoints[(int)source] = Math.Max(0, points);
        }

        Touch();
    }

    /// <summary>True if the row's points can be typed in (Cannonball: only the row of this target).</summary>
    public bool CanSetRowPoints(int index)
    {
        PointSource source = Rows[index];
        return source >= PointSource.CannonballTarget1 && source <= PointSource.CannonballTarget4
            ? source - PointSource.CannonballTarget1 == Mission.Id.Test
            : source != PointSource.None;
    }

    /// <summary>Changes the points of a ring type (0-3; 3 is the goal ring).</summary>
    public void SetRingPoints(int type, int points)
    {
        Comm.SetRingPoints(type, Math.Clamp(points, short.MinValue, short.MaxValue));
        Touch();
    }

    /// <summary>Replaces the time curve.</summary>
    public void SetTimeCurve(TimeCurveSettings time)
    {
        Comm.SetCurve(ScoreCurveKind.Time, MissionCurves.FromSettings(time));
        Touch();
    }

    /// <summary>Replaces the conditions (written into the mission file where the retail game can express them).</summary>
    public void SetConditions(MissionConditions conditions)
    {
        MissionConditions copy = conditions.Clone();
        if (!CanSetCrashCondition)
        {
            copy.CrashGivesZero = false;
            copy.CrashTimePoints = false;
        }

        if (copy.CrashGivesZero)
        {
            copy.CrashTimePoints = false;
        }

        foreach (ConditionObject type in copy.Required.Keys.ToList())
        {
            if (copy.Required[type] is not MissionConditions.All and < 1)
            {
                copy.Required.Remove(type);
            }
        }

        if (!copy.CanLandWhenReached(Comm))
        {
            copy.LandWhenReached = false;
        }

        Settings.Conditions = copy;
        copy.ApplyTo(Comm);
        Touch();
    }

    /// <summary>Chooses what a crash gives (not for Cannonball).</summary>
    public void SetCrashScoring(CrashScoring scoring)
    {
        MissionConditions conditions = Conditions.Clone();
        conditions.CrashGivesZero = scoring == CrashScoring.Zero;
        conditions.CrashTimePoints = scoring == CrashScoring.TimePoints;
        SetConditions(conditions);
    }

    /// <summary>
    /// Gyrocopter: switches the own curve of the touchdown point on (it starts as a copy of the landing accuracy
    /// curve, so nothing changes yet) or off (both measurements use the landing accuracy curve again).
    /// </summary>
    public void SetSeparateTouchdown(bool separate)
    {
        if (Vehicle != Vehicle.Gyrocopter)
        {
            return;
        }

        if (!separate)
        {
            Settings.TouchdownCurve = null;
        }
        else if (Settings.TouchdownCurve is null)
        {
            // A copy changes no points, so the texts stay as they are.
            Settings.TouchdownCurve = [.. Comm.GetCurve(ScoreCurveKind.LandingAccuracy).Points];
            return;
        }

        Touch();
    }

    /// <summary>
    /// Gyrocopter with two curves: replaces the points of one of them, given as percent of the curve's highest
    /// value (one point must have 100 %, so the points of the row stay).
    /// </summary>
    /// <param name="touchdown">True for the touchdown point, false for the resting position (the landing accuracy curve).</param>
    /// <param name="points">Distance (game units) and percent.</param>
    /// <returns>Null, or why the points cannot be used.</returns>
    public string? SetLandingPercentCurve(bool touchdown, IReadOnlyList<CurvePoint> points)
    {
        ScoreCurve? current = touchdown ? TouchdownCurve : Comm.GetCurve(ScoreCurveKind.LandingAccuracy);
        if (current is null || TouchdownCurve is null)
        {
            return CoreText.T("The Gyrocopter has no separate touchdown curve.");
        }

        if (points.Any(p => p.Y is < 0 or > 100))
        {
            return CoreText.T("The percentages must be from 0 to 100.");
        }

        if (!points.Any(p => p.Y >= 100))
        {
            return CoreText.T("One point of each curve must have 100 % (the points of the row set the highest value).");
        }

        float max = current.Maximum;
        var stored = points.Select(p => new CurvePoint(p.X, MathF.Round(p.Y / 100 * max * 1000) / 1000)).ToList();
        if (MissionCurves.CheckPoints(stored) is { } problem)
        {
            return problem;
        }

        if (touchdown)
        {
            Settings.TouchdownCurve = stored;
        }
        else
        {
            Comm.SetCurve(ScoreCurveKind.LandingAccuracy, ScoreCurve.Create(stored));
        }

        Touch();
        return null;
    }

    /// <summary>The landing values the editor offers for this mission (pad radius if it has landing pads, the vehicle's speeds).</summary>
    public IReadOnlyList<LandingValue> LandingValuesShown =>
        [.. (LandingValues.UsesPads(Vehicle) && Comm.GetObjectCount(MissionObjectKind.LandingPad) > 0 ? [LandingValue.PadRadius] : Array.Empty<LandingValue>()),
         .. LandingValues.SpeedsOf(Vehicle)];

    /// <summary>A landing value of this mission (stored form): its setting, or the original.</summary>
    public float LandingValueOf(LandingValue value) => LandingValues.Effective(Settings, value);

    /// <summary>Changes a landing value (stored form); the original value removes the setting.</summary>
    public void SetLandingValue(LandingValue value, float stored)
    {
        if (MathF.Abs(stored - LandingValues.Original(value)) < 0.0005f)
        {
            Settings.Landing.Remove(value);
        }
        else
        {
            Settings.Landing[value] = stored;
        }
    }

    /// <summary>The widths of the runways (game units): the settings, or the mission file's.</summary>
    public IReadOnlyList<float> RunwayWidths
    {
        get
        {
            IReadOnlyList<float> file = Mission.File.RunwayWidths;
            return file.Select((w, i) => Settings.RunwayWidths is { } set && i < set.Count ? set[i] : w).ToList();
        }
    }

    /// <summary>Changes the width of a runway (game units).</summary>
    public void SetRunwayWidth(int index, float width)
    {
        IReadOnlyList<float> file = Mission.File.RunwayWidths;
        if (index < 0 || index >= file.Count)
        {
            return;
        }

        List<float> widths = [.. RunwayWidths];
        widths[index] = Math.Max(0, width);
        Settings.RunwayWidths = widths.SequenceEqual(file) ? null : widths;
    }

    /// <summary>Changes the "Land now" time (0 = off).</summary>
    public void SetLandNowTime(float seconds)
    {
        Comm.LandNowTime = Math.Max(0, seconds);
        Touch();
    }

    /// <summary>True if the hard end time can be set (not for Cannonball, whose flights end with the shot).</summary>
    public bool CanSetHardEnd => Vehicle != Vehicle.Cannonball;

    /// <summary>
    /// Changes the hard end time (0 = off): when it is reached the mission ends at once, scored as
    /// <see cref="MissionSettings.HardEndZero"/> says (game module).
    /// </summary>
    public void SetHardEndTime(float seconds)
    {
        Comm.HardEndTime = CanSetHardEnd ? Math.Max(0, seconds) : 0;
        Touch();
    }

    /// <summary>Chooses what the hard end time gives: 0 points (true) or the points of the flight so far (false).</summary>
    public void SetHardEndZero(bool zero)
    {
        Settings.HardEndZero = zero;
        Touch();
    }

    /// <summary>The jingle at the hard end time: as chosen, or the default for the scoring.</summary>
    public HardEndJingle HardEndJingle => HardEndJingles.Effective(Settings);

    /// <summary>Chooses the jingle at the hard end time.</summary>
    public void SetHardEndJingle(HardEndJingle jingle)
    {
        Settings.HardEndJingle = jingle;
    }

    /// <summary>Changes the points a hit costs (a positive number; stored as negative).</summary>
    public void SetHitCost(int points)
    {
        Comm.HitDeduction = -Math.Abs(points);
        Touch();
    }

    /// <summary>Changes the points leaving the area costs the Jumble Hopper.</summary>
    public void SetOutOfAreaCost(float points)
    {
        Comm.OutOfAreaDeduction = Math.Max(0, points);
        Touch();
    }

    /// <summary>Changes the score above which the pilot cheers.</summary>
    public void SetReactionThreshold(int points)
    {
        Comm.ReactionThreshold = Math.Clamp(points, 0, 100);
        Touch();
    }

    /// <summary>Switches between a footnote written from the settings and one typed in.</summary>
    public void SetAutoFootnote(bool automatic, Func<TestId, MissionComm?> otherComm)
    {
        if (!automatic && Settings.AutoFootnote == true)
        {
            // Start the typed-in footnote with the one written so far.
            Settings.Footnote = [.. Footnote(otherComm)];
        }

        Settings.AutoFootnote = automatic;
        Touch();
    }

    /// <summary>True if the vehicle uses fuel (Rocket Belt, Gyrocopter) and the mission has a takeoff pad.</summary>
    public bool CanSetFuel => Vehicle is Vehicle.RocketBelt or Vehicle.Gyrocopter && Mission.File.StartFuel is not null;

    /// <summary>The fuel at the start (0 to 1): the setting, or the mission file's.</summary>
    public float StartFuel => Settings.StartFuel ?? Mission.File.StartFuel ?? 1;

    /// <summary>Changes the fuel at the start (0 to 1; the mission file's value: back to it).</summary>
    public void SetStartFuel(float fuel)
    {
        fuel = Math.Clamp(fuel, 0, 1);
        Settings.StartFuel = CanSetFuel && fuel != Mission.File.StartFuel ? fuel : null;
    }

    /// <summary>Switches unlimited fuel on or off.</summary>
    public void SetUnlimitedFuel(bool unlimited)
    {
        Settings.UnlimitedFuel = unlimited && CanSetFuel;
    }

    /// <summary>True if this mission has a "View Demo" button that can be hidden (Beginner of HG, RB, GC).</summary>
    public bool CanHideDemo => DemoButton.HasButton(Mission.Id);

    /// <summary>Hides or shows the "View Demo" button of the hint screen (does not touch the texts).</summary>
    public void SetHideDemo(bool hide)
    {
        Settings.HideDemo = hide && CanHideDemo;
    }

    /// <summary>Changes the footnote of the mission screen text (it is no longer written automatically).</summary>
    public void SetFootnote(IEnumerable<string> lines)
    {
        Settings.AutoFootnote = false;
        Settings.Footnote = lines.Select(l => l.TrimEnd()).ToList();
        while (Settings.Footnote.Count > 0 && Settings.Footnote[^1].Length == 0)
        {
            Settings.Footnote.RemoveAt(Settings.Footnote.Count - 1);
        }

        Touch();
    }

    /// <summary>Back to the original game: original COMM block, original rows, texts not generated.</summary>
    public void RestoreOriginal(MissionComm original)
    {
        Comm = original.Clone();
        Settings = MissionSettings.For(Mission.Id);
        Rows = Mission.OriginalRowSources.ToList();
    }

    /// <summary>The settings to save (null if they change nothing).</summary>
    public MissionSettings? SettingsToSave()
    {
        MissionSettings settings = Settings.Clone();
        settings.ResultRows = Rows.SequenceEqual(Mission.OriginalRowSources) ? null : Rows.Select(r => (int)r).ToList();
        return settings.IsEmpty ? null : settings;
    }

    /// <summary>Marks the texts as generated; the first time, the footnote starts with the current one.</summary>
    private void Touch()
    {
        if (!Settings.GenerateTexts)
        {
            Settings.GenerateTexts = true;
            if (Settings.AutoFootnote is null && Settings.Footnote.Count == 0)
            {
                // From now on the footnote follows the settings; the old one is kept for "typed in".
                Settings.AutoFootnote = true;
                if (CurrentScreenText is not null)
                {
                    Settings.Footnote = [.. MissionTextGenerator.ExtractFootnote(CurrentScreenText)];
                }
            }
        }

        Settings.ResultRows = Rows.SequenceEqual(Mission.OriginalRowSources) ? null : Rows.Select(r => (int)r).ToList();
    }

    private static List<int> TextNumbers(string markup) =>
        System.Text.RegularExpressions.Regex.Matches(markup, @"\[b\](\d+)\[/b\] pts")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    private static bool SettingsEqual(MissionSettings a, MissionSettings? b)
    {
        MissionSettings other = b ?? MissionSettings.For(a.Id);
        return a.GenerateTexts == other.GenerateTexts
            && (a.ResultRows ?? []).SequenceEqual(other.ResultRows ?? [])
            && (a.ResultRows is null) == (other.ResultRows is null)
            && a.Footnote.SequenceEqual(other.Footnote)
            && a.AutoFootnote == other.AutoFootnote
            && a.HideDemo == other.HideDemo
            && a.StartFuel == other.StartFuel
            && a.UnlimitedFuel == other.UnlimitedFuel
            && a.HardEndZero == other.HardEndZero
            && a.HardEndJingle == other.HardEndJingle
            && (a.TouchdownCurve ?? []).SequenceEqual(other.TouchdownCurve ?? []) && (a.TouchdownCurve is null) == (other.TouchdownCurve is null)
            && a.Landing.Count == other.Landing.Count && a.Landing.All(p => other.Landing.TryGetValue(p.Key, out float v) && v == p.Value)
            && (a.RunwayWidths ?? []).SequenceEqual(other.RunwayWidths ?? []) && (a.RunwayWidths is null) == (other.RunwayWidths is null)
            && (a.Conditions is null ? other.Conditions is null : a.Conditions.SameAs(other.Conditions))
            && a.TextPoints.Count == other.TextPoints.Count
            && a.TextPoints.All(p => other.TextPoints.TryGetValue(p.Key, out int v) && v == p.Value);
    }
}

/// <summary>What a crash gives in a mission.</summary>
public enum CrashScoring
{
    /// <summary>As the original game: objects, deductions, altitude and distance.</summary>
    Original,

    /// <summary>The time points count too (with the time of the crash).</summary>
    TimePoints,

    /// <summary>0 points.</summary>
    Zero,
}
