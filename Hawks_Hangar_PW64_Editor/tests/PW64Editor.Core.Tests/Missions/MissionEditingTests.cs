using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

public class MissionEditingTests
{
    [Fact]
    public void Generator_WritesTheLayoutOfTheOriginalTexts()
    {
        PointLine[] rows = [new(PointSource.Rings, 20), new(PointSource.Time, 30), new(PointSource.LandingAccuracy, 30), new(PointSource.LandingImpact, 20)];
        string screen = MissionTextGenerator.MissionScreenText(rows, ["Time points will be deducted", "when 1'30\" have elapsed."]);
        Assert.Equal(
            "\n  Ring [x=212][b]20[/b] pts.\n  Time Pts. [x=212][b]30[/b] pts.\n  Landing Accuracy [x=212][b]30[/b] pts.\n" +
            "  Landing Impact [x=212][b]20[/b] pts.\n  [b]Total[/b] [x=202][b]100 pts[/b].\n\n *Time points will be deducted\n    when 1'30\" have elapsed.",
            screen);

        string sheet = MissionTextGenerator.ResultSheetText(rows, Vehicle.Gyrocopter);
        Assert.EndsWith("  Points Deducted [x=212] pts.\n  [b]Total[/b]", sheet);
        Assert.DoesNotContain("Deducted", MissionTextGenerator.ResultSheetText(rows, Vehicle.HangGlider));

        // One row: four empty lines first, like the Jumble Hopper texts.
        Assert.StartsWith("\n\n\n\n  Time Pts.", MissionTextGenerator.ResultSheetText([new(PointSource.Time, 100)], Vehicle.JumbleHopper));
        Assert.Throws<ArgumentException>(() => MissionTextGenerator.ResultSheetText([], Vehicle.HangGlider));
    }

    [Fact]
    public void TimeCurves_AreDescribedAndBuilt()
    {
        // GC Beginner: full points up to 1'30" (90.5 s), nothing from 210 s on (with a step in between).
        ScoreCurve gc = ScoreCurve.Create([new(0, 30), new(90.5f, 30), new(120, 15), new(210, 0)]);
        TimeCurveSettings plateau = MissionCurves.Describe(gc);
        Assert.Equal(TimeCurveShape.Plateau, plateau.Shape);
        Assert.Equal(30, plateau.Points);
        Assert.Equal(90f, plateau.FullUntil);
        Assert.Equal(210f, plateau.ZeroAt);
        Assert.False(plateau.IsExact);

        ScoreCurve simple = MissionCurves.FromSettings(plateau);
        Assert.Equal([new(0, 30), new(90.5f, 30), new(210, 0)], simple.Points);
        Assert.True(MissionCurves.Describe(simple).IsExact);
        Assert.Equal(plateau with { IsExact = true }, MissionCurves.Describe(simple));

        // HG Class B 3: as close as possible to 3'00".
        ScoreCurve hg = ScoreCurve.Create([new(0, 0), new(150, 0), new(150, 40), new(177, 70), new(183, 70), new(210, 40), new(270, 0)]);
        TimeCurveSettings target = MissionCurves.Describe(hg);
        Assert.Equal(TimeCurveShape.Target, target.Shape);
        Assert.Equal(180f, target.TargetTime);
        Assert.Equal(3f, target.Window);
        Assert.Equal(90f, target.ZeroDistance);
        ScoreCurve built = MissionCurves.FromSettings(target with { ZeroDistance = 30 });
        Assert.Equal(70f, built.Evaluate(180));
        Assert.Equal(0f, built.Evaluate(140));
        Assert.Equal(35f, built.Evaluate(196.5f), 3);
        Assert.Equal(TimeCurveShape.None, MissionCurves.Describe(MissionCurves.Empty).Shape);
    }

    [Fact]
    public void ShownPoints_ChangeWithTheShapeKept()
    {
        ScoreCurve accuracy = ScoreCurve.Create([new(0, 40), new(6, 40), new(11, 30), new(35, 4), new(35, 0)]);
        ScoreCurve changed = MissionCurves.WithShownPoints(ScoreCurveKind.LandingAccuracy, Vehicle.HangGlider, accuracy, 20);
        Assert.Equal([20f, 20f, 15f, 2f, 0f], changed.Points.Select(p => p.Y));
        Assert.Equal(accuracy.Points.Select(p => p.X), changed.Points.Select(p => p.X));

        // The Gyrocopter shows the square of its curve.
        ScoreCurve gc = MissionCurves.WithShownPoints(ScoreCurveKind.LandingAccuracy, Vehicle.Gyrocopter, MissionCurves.Empty, 30);
        Assert.Equal(30, MissionCurves.ShownPoints(ScoreCurveKind.LandingAccuracy, Vehicle.Gyrocopter, gc));
        Assert.Equal(5.477f, gc.Maximum, 3);
        foreach (int points in new[] { 1, 7, 20, 33, 45, 100 })
        {
            foreach (ScoreCurveKind kind in Enum.GetValues<ScoreCurveKind>())
            {
                ScoreCurve curve = MissionCurves.WithShownPoints(kind, Vehicle.Gyrocopter, MissionCurves.Empty, points);
                Assert.Equal(points, MissionCurves.ShownPoints(kind, Vehicle.Gyrocopter, curve));
                Assert.Equal(points, MissionCurves.ShownPoints(kind, Vehicle.RocketBelt,
                    MissionCurves.WithShownPoints(kind, Vehicle.RocketBelt, curve, points)));
            }
        }

        Assert.False(MissionCurves.WithShownPoints(ScoreCurveKind.Time, Vehicle.HangGlider, accuracy, 0).GivesPoints);
    }

    [RealRomFact]
    public void GeneratedTexts_LineUpLikeTheOriginalsAndAreValid()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem files = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        GameTextLibrary texts = GameTextLibrary.Create(TextFont.Load(files), GameTextFile.FindFile(files).Data, false);
        Dictionary<string, string> markup = texts.Texts.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Markup);
        MissionCatalogResult catalog = MissionCatalog.Build(
            files.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => new MissionSource(f.TableIndex, f.GroupIndex, f.Data, false)),
            new MissionTables(rom.Data, MissionLayout.PilotwingsUsa));

        int checkedTexts = 0;
        foreach (MissionInfo mission in catalog.Missions.Where(m => m.Rows.Count > 0 && m.MissionScreenTextName is not null))
        {
            // Unknown values (photo, formation, Meca Hawks) take the number of the original text.
            string original = markup[mission.MissionScreenTextName!];
            List<int> numbers = System.Text.RegularExpressions.Regex.Matches(original, @"\[b\](\d+)\[/b\] pts")
                .Select(m => int.Parse(m.Groups[1].Value)).ToList();
            var rows = mission.Rows.Select((r, i) => new PointLine(r.Source, r.Value ?? numbers[i])).ToList();

            string screen = MissionTextGenerator.MissionScreenText(rows, MissionTextGenerator.ExtractFootnote(original));
            AssertSameLines(original, screen, mission);
            Assert.Equal(MissionTextGenerator.ExtractFootnote(original), MissionTextGenerator.ExtractFootnote(screen));

            string sheetName = mission.ResultSheetTextName!;
            string sheet = MissionTextGenerator.ResultSheetText(rows, mission.Id.Vehicle);
            AssertSameLines(markup[sheetName], sheet, mission);

            foreach ((string name, string text) in new[] { (mission.MissionScreenTextName!, screen), (sheetName, sheet) })
            {
                TextValidation result = TextValidator.Validate(texts.Codec, text, markup[name],
                    TextLimits.GetMaxLines(name, markup[name]), name);
                Assert.False(result.HasErrors, $"{name}: {string.Join("; ", result.Issues.Select(i => i.Message))}");
            }

            checkedTexts++;
        }

        Assert.True(checkedTexts >= 40, $"only {checkedTexts} texts checked");
    }

    /// <summary>Rows, total and footnote stand on the same lines as in the original text.</summary>
    private static void AssertSameLines(string original, string generated, MissionInfo mission)
    {
        string[] a = original.Split('\n');
        string[] b = generated.Split('\n');
        Assert.True(a.Length == b.Length, $"{MissionCatalog.Label(mission.Id)}: {a.Length} lines in the original, {b.Length} generated");
        for (int i = 0; i < a.Length; i++)
        {
            Assert.True((a[i].Trim().Length == 0) == (b[i].Trim().Length == 0), $"{MissionCatalog.Label(mission.Id)}: line {i}");
            Assert.True(a[i].Contains("Total") == b[i].Contains("Total"), $"{MissionCatalog.Label(mission.Id)}: total on line {i}");
        }
    }

    [RealRomFact]
    public void SavedMission_IsBuiltWithItsResultRows()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Missions", false, rom, TestRomLocator.RomPath!);
        MissionInfo gc = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.Gyrocopter, 0));

        // Time points 50 instead of 30, rings left out of the sheet.
        MissionComm comm = gc.File.Comm.Clone();
        comm.SetCurve(ScoreCurveKind.Time, MissionCurves.WithShownPoints(ScoreCurveKind.Time, Vehicle.Gyrocopter, comm.GetCurve(ScoreCurveKind.Time), 50));
        MissionSettings settings = MissionSettings.For(gc.Id);
        settings.ResultRows = [(int)PointSource.Time, (int)PointSource.LandingAccuracy, (int)PointSource.LandingImpact];
        settings.GenerateTexts = true;
        session.SaveMission(gc, comm, settings);

        MissionInfo reloaded = session.LoadMissions().Missions.Single(m => m.Id == gc.Id);
        Assert.True(reloaded.FromProject);
        Assert.Equal([(PointSource.Time, 50), (PointSource.LandingAccuracy, 30), (PointSource.LandingImpact, 20)],
            reloaded.Rows.Select(r => (r.Source, r.Value ?? -1)));
        Assert.True(reloaded.Settings!.GenerateTexts);

        (PW64Editor.Core.Project.ProjectBuildResult result, _, _) = session.Build(createRestorePoint: false);
        int offset = new MissionTables(rom.Data, MissionLayout.PilotwingsUsa).ResultRowsOffset(gc.Id)!.Value;
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, result.RomBuild.Rom.Data.AsSpan(offset, 4).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, rom.Data.AsSpan(offset, 4).ToArray());

        // Back to the original: no project file and no settings left.
        session.SaveMission(reloaded, gc.File.Comm, null);
        MissionInfo restored = session.LoadMissions().Missions.Single(m => m.Id == gc.Id);
        Assert.False(restored.FromProject);
        Assert.Null(restored.Settings);
        Assert.Empty(session.Project.Settings.Missions);
    }

    [RealRomFact]
    public void Draft_ChangesPointsRowsAndTexts()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem files = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        GameTextLibrary texts = GameTextLibrary.Create(TextFont.Load(files), GameTextFile.FindFile(files).Data, false);
        MissionCatalogResult catalog = MissionCatalog.Build(
            files.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => new MissionSource(f.TableIndex, f.GroupIndex, f.Data, false)),
            new MissionTables(rom.Data, MissionLayout.PilotwingsUsa));
        MissionInfo gc = catalog.Missions.Single(m => m.Id == new TestId(0, Vehicle.Gyrocopter, 0));
        string screen = texts.Texts.First(t => t.Name == gc.MissionScreenTextName).Markup;
        MissionComm? None(TestId _) => null;

        var draft = new MissionDraft(gc, screen);
        Assert.False(draft.IsChanged);

        // Time 30 → 20: the curve keeps its shape, the texts are generated from now on.
        draft.SetRowPoints(1, 20, None);
        Assert.True(draft.IsChanged);
        Assert.True(draft.Settings.GenerateTexts);
        Assert.Equal(["Time points will be deducted", "when 1'30\" have elapsed."], draft.Settings.Footnote);
        Assert.Equal(20f, draft.Comm.GetCurve(ScoreCurveKind.Time).Maximum);
        Assert.Equal(4, draft.Comm.GetCurve(ScoreCurveKind.Time).Points.Count);
        Assert.Contains("Time Pts. [x=212][b]20[/b] pts.", draft.MissionScreenText(None));
        Assert.Contains("[b]90 pts[/b]", draft.MissionScreenText(None));

        // Rings keep their number of the text (20) until another one is typed in.
        Assert.Equal(20, draft.TextLines(None)[0].Points);
        draft.SetRowPoints(0, 30, None);
        Assert.Equal(30, draft.TextLines(None)[0].Points);
        Assert.Equal(20, MissionCatalog.RingSum(gc.File, draft.Comm)); // the game still counts the rings

        // Impact leaves the sheet: its curve is emptied, so it does not count invisibly.
        Assert.Null(draft.SetRowSource(3, PointSource.None, 0));
        Assert.False(draft.Comm.GetCurve(ScoreCurveKind.Impact).GivesPoints);
        Assert.Equal([PointSource.Rings, PointSource.Time, PointSource.LandingAccuracy], draft.Rows);
        Assert.NotNull(draft.SetRowSource(0, PointSource.Time, 10)); // already a row
        Assert.NotNull(draft.SetRowSource(3, PointSource.Photo, 10)); // the Gyrocopter has no camera
        Assert.Null(draft.SetRowSource(3, PointSource.Altitude, 15));
        Assert.Equal(15, MissionCurves.ShownPoints(ScoreCurveKind.Altitude, Vehicle.Gyrocopter, draft.Comm.GetCurve(ScoreCurveKind.Altitude)));
        Assert.Equal([4, 3, 2, 6], draft.SettingsToSave()!.ResultRows);
        Assert.StartsWith("\n  Ring", draft.ResultSheetText(None));

        // Time curve by its values, and back to the original.
        draft.SetTimeCurve(new TimeCurveSettings(TimeCurveShape.Plateau, 25, FullUntil: 60, ZeroAt: 150));
        Assert.Contains("when 1'00\"", MissionTextGenerator.SuggestFootnote(draft.Comm, draft.TextLines(None))[1]);
        draft.RestoreOriginal(gc.File.Comm);
        Assert.False(draft.IsChanged);
        Assert.Null(draft.SettingsToSave());

        // Cannonball: only the own target can be changed; the sources are fixed.
        MissionInfo cannon = catalog.Missions.Single(m => m.Id == new TestId(0, Vehicle.Cannonball, 1));
        var cannonDraft = new MissionDraft(cannon, null);
        Assert.False(cannonDraft.CanChangeSources);
        Assert.False(cannonDraft.CanSetRowPoints(0));
        Assert.True(cannonDraft.CanSetRowPoints(1));
        cannonDraft.SetRowPoints(1, 30, None);
        Assert.Equal(30, cannonDraft.EvaluateRows(id => catalog.Missions.Single(m => m.Id == id).File.Comm)[1].Value);
        Assert.NotNull(cannonDraft.MoveRow(0, 1));
    }

    [RealRomFact]
    public void Draft_MovesRowsAndKeepsTheirTextNumbers()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem files = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        GameTextLibrary texts = GameTextLibrary.Create(TextFont.Load(files), GameTextFile.FindFile(files).Data, false);
        MissionCatalogResult catalog = MissionCatalog.Build(
            files.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => new MissionSource(f.TableIndex, f.GroupIndex, f.Data, false)),
            new MissionTables(rom.Data, MissionLayout.PilotwingsUsa));
        MissionInfo gc = catalog.Missions.Single(m => m.Id == new TestId(0, Vehicle.Gyrocopter, 0));
        MissionComm? None(TestId _) => null;
        var draft = new MissionDraft(gc, texts.Texts.First(t => t.Name == gc.MissionScreenTextName).Markup);

        // Rings (20 in the text) from the top to the bottom.
        Assert.Null(draft.MoveRow(0, 3));
        Assert.Equal([PointSource.Time, PointSource.LandingAccuracy, PointSource.LandingImpact, PointSource.Rings], draft.Rows);
        Assert.Equal(20, draft.TextLines(None)[3].Points);
        Assert.Equal([(int)PointSource.Time, (int)PointSource.LandingAccuracy, (int)PointSource.LandingImpact, (int)PointSource.Rings],
            draft.SettingsToSave()!.ResultRows);
        Assert.NotNull(draft.MoveRow(0, 4));

        // Back to the original order: no rows to store.
        Assert.Null(draft.MoveRow(3, 0));
        Assert.Null(draft.Settings.ResultRows);

        // Hang Glider, Pilot class, test 1: the altitude curve point by point.
        MissionInfo hg = catalog.Missions.Single(m => m.Id == new TestId(3, Vehicle.HangGlider, 0));
        Assert.Contains(PointSource.Altitude, hg.Rows.Select(r => r.Source));
        var hgDraft = new MissionDraft(hg, null);
        Assert.NotNull(hgDraft.SetCurvePoints(ScoreCurveKind.Altitude, [new(500, 10), new(100, 70)]));
        Assert.False(hgDraft.IsChanged);
        Assert.Null(hgDraft.SetCurvePoints(ScoreCurveKind.Altitude, [new(0, 0), new(300, 35), new(900, 70)]));
        Assert.Equal(70, hgDraft.EvaluateRows(None).Single(r => r.Source == PointSource.Altitude).Value);
        Assert.Equal(3, hgDraft.Comm.GetCurve(ScoreCurveKind.Altitude).Points.Count);
        Assert.True(hgDraft.Settings.GenerateTexts);
    }

    [Fact]
    public void CurvePoints_AreCheckedAndStored()
    {
        Assert.Null(MissionCurves.CheckPoints([new(0, 20), new(60, 20), new(60, 10), new(120, 0)]));
        Assert.NotNull(MissionCurves.CheckPoints([]));
        Assert.NotNull(MissionCurves.CheckPoints([new(10, 5), new(5, 0)]));      // falling values
        Assert.NotNull(MissionCurves.CheckPoints([new(0, 127)]));                // too many points
        Assert.NotNull(MissionCurves.CheckPoints([new(-1, 5)]));
        Assert.NotNull(MissionCurves.CheckPoints(Enumerable.Range(0, 11).Select(i => new CurvePoint(i, 1)).ToList()));

        // A step: the game never divides by the zero distance of the two points.
        ScoreCurve step = ScoreCurve.Create([new(0, 20), new(60, 20), new(60, 10), new(120, 0)]);
        Assert.Equal(20f, step.Evaluate(59.9f));
        Assert.Equal(10f, step.Evaluate(60f));
        Assert.Equal(5f, step.Evaluate(90f));
    }
}
