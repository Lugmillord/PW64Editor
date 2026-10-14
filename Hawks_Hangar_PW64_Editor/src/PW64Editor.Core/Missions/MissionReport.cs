using System.Globalization;
using System.Text;
using System.Text.Json;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Missions;

/// <summary>
/// A plain text report of everything the Missions tab sets in a project, for debugging: the
/// project and its build options, then every changed mission in full (settings, COMM values,
/// conditions and how they are built, texts, the bytes that differ from the original game).
/// </summary>
/// <remarks>
/// The report is in English and uses invariant numbers, so it reads the same on every system.
/// It shows the saved state of the project (changes not yet saved in the Missions tab are not in it).
/// </remarks>
public static class MissionReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Writes the report of a project.</summary>
    public static string Create(EditorSession session)
    {
        var text = new StringBuilder();
        HackProject project = session.Project;
        ProjectSettings settings = project.Settings;
        string version = typeof(MissionReport).Assembly.GetName().Version?.ToString() ?? "?";

        Line(text, "Hawk's Hangar - mission settings report");
        Line(text, $"Written:      {DateTime.Now:yyyy-MM-dd HH:mm:ss} (editor core {version})");
        Line(text, $"Project:      \"{settings.Name}\" {settings.Version}, format {settings.FormatVersion}");
        Line(text, $"Base ROM:     SHA-1 {settings.BaseRomSha1}, {session.CleanRom.Size} bytes");
        Line(text, $"Build:        relocate audio {YesNo(settings.RelocateAudio)}, allow expansion {YesNo(settings.AllowExpansion)}");
        Line(text, $"Code fixes:   {(settings.AppliedCodeFixes.Count == 0 ? "none" : string.Join(", ", settings.AppliedCodeFixes))}");
        GameFeatureSettings f = settings.Features;
        Line(text, $"Features:     debug output {YesNo(f.DebugOutput)}, startup info {YesNo(f.StartupInfo)}, "
            + $"developers' messages {YesNo(f.GameMessages)}, on screen {YesNo(f.OnScreen)}, live values {f.LiveMask}");

        IReadOnlyList<FileOverride> overrides;
        try
        {
            overrides = project.GetOverrides();
        }
        catch (ProjectException ex)
        {
            Line(text, $"Files:        cannot be listed: {ex.Message}");
            overrides = [];
        }

        Line(text, $"Files:        {overrides.Count} replaced: {string.Join(", ", overrides.Select(o => Path.GetFileName(o.Path)))}");

        MissionCatalogResult catalog = session.LoadMissions();
        foreach (string problem in catalog.Problems)
        {
            Line(text, $"Problem:      {problem}");
        }

        GameTextLibrary? texts = TryLoad(session.LoadTexts, text, "texts");
        GameTextLibrary? originalTexts = TryLoad(session.LoadOriginalTexts, text, "original texts");

        // The conditions table the build gives the game module, from the current mission files (as the build makes it).
        var moduleConditions = new List<(TestId Id, GameModuleCondition Entry)>();
        try
        {
            IReadOnlyList<GameFile> files = catalog.Missions.Select(m => new GameFile(m.TableIndex, MissionFile.FormType, string.Empty, m.FileNumber, 0, m.File.WithComm(m.File.Comm))).ToList();
            foreach (GameModuleCondition entry in ProjectBuilder.ModuleConditions(project, files))
            {
                moduleConditions.Add((new TestId(entry.Class, (Vehicle)entry.Vehicle, entry.Test), entry));
            }
        }
        catch (InvalidDataException ex)
        {
            Line(text, $"Problem:      the module table cannot be made: {ex.Message}");
        }

        Line(text, $"Module:       {(f.ModuleOptions.IsNeeded || moduleConditions.Count > 0 ? "built in" : "not built in")}, "
            + $"{moduleConditions.Count} condition entries (capacity {GameModuleImage.ConditionsCapacity})");

        List<MissionInfo> changed = catalog.Missions.Where(m => m.FromProject || m.Settings is not null).ToList();
        Line(text, $"Missions:     {catalog.Missions.Count}, changed {changed.Count}");
        Line(text, $"Units:        curves and altitudes in game units; the game shows heights as units x {Num(GameUnits.MetersPerUnit)} m");
        Line(text, string.Empty);
        Line(text, "Overview (* = changed):");
        foreach (MissionInfo mission in catalog.Missions)
        {
            bool isChanged = changed.Contains(mission);
            Line(text, $"  {(isChanged ? '*' : ' ')} {Label(mission)}  file {mission.FileNumber} (table {mission.TableIndex})"
                + (isChanged ? $"  [{string.Join(", ", ChangeKinds(mission))}]" : string.Empty));
        }

        foreach (MissionInfo mission in changed)
        {
            Line(text, string.Empty);
            Line(text, new string('=', 78));
            WriteMission(text, session, mission, texts, originalTexts, moduleConditions);
        }

        // All other texts that differ from the original game (titles, tasks, other screens).
        if (texts is not null && originalTexts is not null)
        {
            var missionTexts = new HashSet<string>(changed
                .SelectMany(m => new[] { m.MissionScreenTextName, m.ResultSheetTextName })
                .OfType<string>());
            var originals = originalTexts.Texts.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Markup);
            List<GameText> other = texts.Texts
                .Where(t => !missionTexts.Contains(t.Name) && (!originals.TryGetValue(t.Name, out string? before) || before != t.Markup))
                .ToList();
            Line(text, string.Empty);
            Line(text, new string('=', 78));
            Line(text, $"Other changed or new texts: {other.Count} (texts: {texts.Texts.Count}, original {originalTexts.Texts.Count})");
            foreach (GameText changedText in other)
            {
                Line(text, $"Text {changedText.Name} (#{changedText.Index}{(originals.ContainsKey(changedText.Name) ? string.Empty : ", new")}):");
                foreach (string line in changedText.Markup.Split('\n'))
                {
                    Line(text, "  | " + line);
                }
            }
        }

        Line(text, string.Empty);
        Line(text, new string('=', 78));
        Line(text, "Mission settings in project.json:");
        Line(text, JsonSerializer.Serialize(settings.Missions, JsonOptions));
        return text.ToString();
    }

    private static void WriteMission(StringBuilder text, EditorSession session, MissionInfo mission, GameTextLibrary? texts,
        GameTextLibrary? originalTexts, List<(TestId Id, GameModuleCondition Entry)> moduleConditions)
    {
        MissionComm comm = mission.File.Comm;
        MissionComm original = session.OriginalMissionComm(mission);
        MissionSettings? settings = mission.Settings;

        Line(text, $"{Label(mission)}  (class {mission.Id.Class}, vehicle {(int)mission.Id.Vehicle}, test {mission.Id.Test})");
        Line(text, $"Mission file {mission.FileNumber}, table index {mission.TableIndex}, name \"{mission.File.Name}\", texts \"{mission.File.TextBase}\", "
            + $"{(mission.FromProject ? "replaced by the project" : "original")}");
        if (texts?.Texts.FirstOrDefault(t => t.Name == mission.TitleTextName) is { } title)
        {
            Line(text, $"Title: {title.Markup.Replace("\n", " | ", StringComparison.Ordinal)}");
        }

        // Points.
        Line(text, string.Empty);
        Line(text, $"Result rows ({(settings?.ResultRows is null ? "original" : "changed")}): "
            + string.Join(", ", mission.Rows.Select(r => $"{r.Source} ({r.Kind}{(r.Value is { } v ? " " + Num(v) : string.Empty)})")));
        if (settings?.ResultRows is { } rows)
        {
            Line(text, $"  stored as: [{string.Join(", ", rows)}], original: {string.Join(", ", mission.OriginalRowSources)}");
        }

        foreach (ScoreCurveKind kind in Enum.GetValues<ScoreCurveKind>())
        {
            Line(text, $"Curve {kind}: {Curve(comm.GetCurve(kind))}{Was(Curve(comm.GetCurve(kind)), Curve(original.GetCurve(kind)))}");
        }

        foreach (PhotoCurveKind kind in Enum.GetValues<PhotoCurveKind>())
        {
            if (comm.GetPhotoCurve(kind).GivesPoints || original.GetPhotoCurve(kind).GivesPoints)
            {
                Line(text, $"Photo curve {kind}: {Curve(comm.GetPhotoCurve(kind))}{Was(Curve(comm.GetPhotoCurve(kind)), Curve(original.GetPhotoCurve(kind)))}");
            }
        }

        int[] ringTypes = MissionCatalog.RingTypeCounts(mission.File);
        string Rings(MissionComm c) => string.Join(", ", Enumerable.Range(0, MissionComm.RingTypeCount)
            .Select(t => $"type {t + 1} {c.GetRingPoints(t)} (+{Num(c.GetRingExtra(t))})"));
        Line(text, $"Ring points: {Rings(comm)}{Was(Rings(comm), Rings(original))}");
        Line(text, $"Rings per type: {string.Join(" / ", ringTypes)} (type 3: invisible check rings, type 4: goal ring)");
        Value(text, "Hit deduction", comm.HitDeduction, original.HitDeduction);
        Value(text, "Pilot cheer threshold", comm.ReactionThreshold, original.ReactionThreshold);
        Value(text, "\"Land now\" time (s)", comm.LandNowTime, original.LandNowTime);
        Value(text, "Hard end time (s)", comm.HardEndTime, original.HardEndTime);
        Value(text, "Out of area deduction", comm.OutOfAreaDeduction, original.OutOfAreaDeduction);

        // Conditions.
        Line(text, string.Empty);
        Line(text, $"Condition byte: {comm.Condition} ({(int)comm.Condition}){Was(comm.Condition.ToString(), original.Condition.ToString())}, "
            + $"required objects {Num(comm.RequiredObjects)}, minimum altitude {Num(comm.MinimumAltitude)} units");
        if (settings?.Conditions is { } conditions)
        {
            var parts = new List<string>();
            if (conditions.CrashGivesZero)
            {
                parts.Add("crash gives 0");
            }

            parts.AddRange(conditions.Required.Select(r =>
                $"{r.Key} {(r.Value == MissionConditions.All ? "all" : "at least " + Num(r.Value))} of {MissionCatalog.ConditionTotal(mission.File, r.Key)}"));
            if (conditions.MinimumAltitude is { } altitude)
            {
                parts.Add($"highest altitude at least {Num(altitude)} units = {Num(GameUnits.ToMeters(altitude))} m");
            }

            if (conditions.MinimumDistance is { } distance)
            {
                parts.Add($"distance at least {Num(distance)} units = {Num(GameUnits.ToMeters(distance))} m");
            }

            if (conditions.LandWhenReached)
            {
                parts.Add("\"please land\" when reached");
            }

            Line(text, $"Conditions: {(parts.Count == 0 ? "none" : string.Join("; ", parts))}");
            MissionCondition? retail = conditions.RetailMode(comm);
            Line(text, $"  checked by: {(retail is { } mode ? $"the game (condition {mode})" : "the game module")}");
            MissionCondition built = retail ?? MissionCondition.None;
            if (built != comm.Condition)
            {
                Line(text, $"  note: the saved condition byte does not match; the build writes {built} ({(int)built})");
            }
            if (moduleConditions.FirstOrDefault(c => c.Id == mission.Id) is { Entry: { } entry })
            {
                Line(text, $"  module entry: vehicle {entry.Vehicle}, class {entry.Class}, test {entry.Test}, crash zero {YesNo(entry.CrashGivesZero)}, "
                    + $"altitude {(entry.MinimumAltitude is { } a ? Num(a) : "-")}, distance {(entry.MinimumDistance is { } dd ? Num(dd) : "-")}, unlimited fuel {YesNo(entry.UnlimitedFuel)}, required [{string.Join(" ", entry.Required.Select(b => b.ToString(CultureInfo.InvariantCulture)))}]");
            }
        }
        else
        {
            Line(text, "Conditions: those of the condition byte (not changed in the editor)");
            if (moduleConditions.FirstOrDefault(c => c.Id == mission.Id) is { Entry: { } fuelEntry })
            {
                Line(text, $"  module entry (fuel only): unlimited fuel {YesNo(fuelEntry.UnlimitedFuel)}");
            }
        }

        if (MissionCatalog.StartAltitude(mission.File) is { } start)
        {
            Line(text, $"Start altitude: {Num(start)} units = {Num(GameUnits.ToMeters(start))} m above sea level");
        }

        string counts = string.Join(", ", Enum.GetValues<MissionObjectKind>().Where(k => comm.GetObjectCount(k) > 0).Select(k => $"{k} {comm.GetObjectCount(k)}"));
        Line(text, $"Objects: {(counts.Length == 0 ? "none" : counts)}");

        // Texts.
        Line(text, string.Empty);
        if (settings is not null)
        {
            if (mission.Id.Vehicle is Vehicle.RocketBelt or Vehicle.Gyrocopter)
            {
                Line(text, $"Fuel: start {Num((settings.StartFuel ?? mission.File.StartFuel ?? 1) * 100)} %"
                    + (settings.StartFuel is null ? " (mission file)" : $" (mission file {Num((mission.File.StartFuel ?? 1) * 100)} %)")
                    + $", unlimited {YesNo(settings.UnlimitedFuel)}");
            }

            if (DemoButton.HasButton(mission.Id))
            {
                Line(text, $"\"View Demo\" button: {(settings.HideDemo ? "hidden" : "shown")}");
            }

            Line(text, $"Texts written by the editor: {YesNo(settings.GenerateTexts)}, footnote automatic: "
                + $"{(settings.AutoFootnote is { } auto ? YesNo(auto) : "not set")}");
            Line(text, $"Footnote: {(settings.Footnote.Count == 0 ? "none" : string.Join(" | ", settings.Footnote))}");
            if (settings.TextPoints.Count > 0)
            {
                Line(text, $"Text points: {string.Join(", ", settings.TextPoints.Select(p => $"{(PointSource)p.Key} {p.Value}"))}");
            }
        }

        foreach (string? name in new[] { mission.MissionScreenTextName, mission.ResultSheetTextName })
        {
            if (name is null || texts?.Texts.FirstOrDefault(t => t.Name == name) is not { } current)
            {
                continue;
            }

            bool same = originalTexts?.Texts.FirstOrDefault(t => t.Name == name)?.Markup == current.Markup;
            Line(text, $"Text {name} (#{current.Index}, {(same ? "original" : "changed")}):");
            foreach (string line in current.Markup.Split('\n'))
            {
                Line(text, "  | " + line);
            }
        }

        // The bytes that differ from the original mission file.
        Line(text, string.Empty);
        GameFile? originalFile = session.CleanFileSystem.Files.FirstOrDefault(x => x.TableIndex == mission.TableIndex);
        byte[] now = mission.File.WithComm(comm);
        Line(text, "COMM bytes changed (offset: original -> now):");
        WriteDiff(text, original.ToArray(), comm.ToArray());
        if (originalFile is not null && !originalFile.Data.AsSpan().SequenceEqual(now))
        {
            byte[] withOriginalComm = mission.File.WithComm(original);
            if (!withOriginalComm.AsSpan().SequenceEqual(originalFile.Data))
            {
                Line(text, $"The mission file also differs outside the COMM block ({now.Length} bytes now, {originalFile.Data.Length} originally):");
                WriteDiff(text, originalFile.Data, withOriginalComm);
            }
        }
    }

    /// <summary>Hex lines of the differing byte ranges (at most 64 lines).</summary>
    private static void WriteDiff(StringBuilder text, byte[] before, byte[] after)
    {
        int length = Math.Max(before.Length, after.Length);
        int lines = 0;
        int i = 0;
        while (i < length)
        {
            if (i < before.Length && i < after.Length && before[i] == after[i])
            {
                i++;
                continue;
            }

            int start = i;
            while (i < length && i - start < 16 && !(i < before.Length && i < after.Length && before[i] == after[i]))
            {
                i++;
            }

            if (++lines > 64)
            {
                Line(text, "  ...");
                return;
            }

            Line(text, $"  0x{start:X3}: {Hex(before, start, i)} -> {Hex(after, start, i)}");
        }

        if (lines == 0)
        {
            Line(text, "  none");
        }
    }

    private static string Hex(byte[] data, int start, int end) =>
        start >= data.Length ? "(none)" : Convert.ToHexString(data, start, Math.Min(end, data.Length) - start);

    private static IEnumerable<string> ChangeKinds(MissionInfo mission)
    {
        if (mission.FromProject)
        {
            yield return "mission file";
        }

        if (mission.Settings is { } s)
        {
            if (s.ResultRows is not null)
            {
                yield return "rows";
            }

            if (s.Conditions is not null)
            {
                yield return "conditions";
            }

            if (s.GenerateTexts)
            {
                yield return "texts";
            }

            if (s.Footnote.Count > 0)
            {
                yield return "footnote";
            }

            if (s.HideDemo)
            {
                yield return "no demo";
            }

            if (s.StartFuel is not null || s.UnlimitedFuel)
            {
                yield return "fuel";
            }
        }
    }

    private static GameTextLibrary? TryLoad(Func<GameTextLibrary> load, StringBuilder text, string what)
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (ex is ProjectException or IOException or FormatException or InvalidDataException)
        {
            Line(text, $"Problem:      the {what} cannot be read: {ex.Message}");
            return null;
        }
    }

    private static string Label(MissionInfo mission) => MissionCatalog.Label(mission.Id);

    private static string Curve(ScoreCurve curve) =>
        curve.IsUsed ? string.Join(" ", curve.Points.Select(p => $"({Num(p.X)},{Num(p.Y)})")) : "-";

    private static void Value(StringBuilder text, string name, float now, float before) =>
        Line(text, $"{name}: {Num(now)}{Was(Num(now), Num(before))}");

    private static string Was(string now, string before) => now == before ? string.Empty : $"   [original: {before}]";

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
