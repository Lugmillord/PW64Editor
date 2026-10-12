using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PW64Editor.App.Services;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Missions": one test of the game at a time. Title, task and hint can be edited (they are
/// the same texts as in the Text tab); points, conditions, time limit and the point texts are shown
/// as stored in the mission. Editing them follows in later steps.
/// </summary>
public partial class MissionsTab : UserControl
{
    private TextTab? _texts;
    private IReadOnlyList<MissionInfo> _missions = [];
    private IReadOnlyList<string> _problems = [];
    private bool _filling;

    public MissionsTab()
    {
        InitializeComponent();
        TitleEditor.Caption = L.T("Title");
        TaskEditor.Caption = L.T("Task");
        HintEditor.Caption = L.T("Hint");
        ScreenTextView.Caption = L.T("Text");
        ResultTextView.Caption = L.T("Text");
    }

    /// <summary>A vehicle of the first list.</summary>
    /// <param name="Vehicle">The vehicle.</param>
    /// <param name="Text">Its name.</param>
    public sealed record VehicleItem(Vehicle Vehicle, string Text);

    /// <summary>A test of the second list.</summary>
    /// <param name="Mission">The mission.</param>
    /// <param name="Text">Class, test and title, e.g. "Beginner · Test 1 – Novice Rings".</param>
    public sealed record TestItem(MissionInfo Mission, string Text);

    private MissionInfo? SelectedMission => (TestBox.SelectedItem as TestItem)?.Mission;

    /// <summary>
    /// Connects the tab to the Text tab, which owns the texts. The editors of this tab edit the
    /// Text tab's rows, so edits are shared and saved together.
    /// </summary>
    public void Connect(TextTab texts)
    {
        _texts = texts;
        foreach (TextEditorPanel panel in Panels)
        {
            panel.Attach(texts);
        }

        // After saving or reloading, the Text tab has new row objects (and maybe new titles).
        texts.TextsReloaded += (_, _) => RefreshTexts();
    }

    /// <summary>Draws the previews again (code fixes change how texts are drawn).</summary>
    public void RefreshPreview()
    {
        foreach (TextEditorPanel panel in Panels)
        {
            panel.RefreshPreview();
        }
    }

    private IEnumerable<TextEditorPanel> Panels => [TitleEditor, TaskEditor, HintEditor, ScreenTextView, ResultTextView];

    /// <summary>Reads the missions of a project. Errors are shown inside the tab.</summary>
    public void Load(EditorSession session)
    {
        TestId? selected = SelectedMission?.Id;
        try
        {
            MissionCatalogResult result = session.LoadMissions();
            _missions = result.Missions;
            _problems = result.Problems;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            _missions = [];
            _problems = [L.F("The missions could not be read: {0}", ex.Message)];
        }

        _filling = true;
        VehicleBox.ItemsSource = MissionCatalog.VehicleOrder
            .Where(v => _missions.Any(m => m.Id.Vehicle == v))
            .Select(v => new VehicleItem(v, Vehicles.Name(v)))
            .ToList();
        _filling = false;

        SourceText.Text = L.F("{0} missions. Title, task and hint can be edited now; editing the points, conditions and point texts follows in a later version.", _missions.Count);

        Vehicle vehicle = selected?.Vehicle ?? Vehicle.HangGlider;
        VehicleBox.SelectedItem = ((IEnumerable<VehicleItem>)VehicleBox.ItemsSource).FirstOrDefault(v => v.Vehicle == vehicle)
            ?? ((IEnumerable<VehicleItem>)VehicleBox.ItemsSource).FirstOrDefault();
        if (selected is { } id && ((IEnumerable<TestItem>?)TestBox.ItemsSource)?.FirstOrDefault(t => t.Mission.Id == id) is { } item)
        {
            TestBox.SelectedItem = item;
        }

        if (_missions.Count == 0)
        {
            ShowMission(null);
        }
    }

    /// <summary>Shows the current texts again: new titles in the list, new rows in the editors.</summary>
    private void RefreshTexts()
    {
        if (VehicleBox.SelectedItem is not VehicleItem vehicle)
        {
            return;
        }

        TestId? selected = SelectedMission?.Id;
        FillTests(vehicle.Vehicle);
        _filling = true;
        TestBox.SelectedItem = ((IEnumerable<TestItem>?)TestBox.ItemsSource)?.FirstOrDefault(t => t.Mission.Id == selected);
        _filling = false;
        ShowMission(SelectedMission);
    }

    private void OnVehicleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || VehicleBox.SelectedItem is not VehicleItem vehicle)
        {
            return;
        }

        FillTests(vehicle.Vehicle);
        TestBox.SelectedIndex = TestBox.Items.Count > 0 ? 0 : -1;
    }

    private void FillTests(Vehicle vehicle)
    {
        _filling = true;
        TestBox.ItemsSource = _missions.Where(m => m.Id.Vehicle == vehicle).Select(m => new TestItem(m, TestText(m))).ToList();
        _filling = false;
    }

    private void OnTestChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling)
        {
            ShowMission(SelectedMission);
        }
    }

    /// <summary>"Beginner · Test 1 – Novice Rings": class, test and the current title.</summary>
    private string TestText(MissionInfo mission)
    {
        string title = mission.TitleTextName is { } name && _texts?.FindRow(name) is { } row ? PlainText(row.Markup) : mission.File.Name;
        return $"{MissionCatalog.ClassName(mission.Id.Vehicle, mission.Id.Class)} · {MissionCatalog.TestName(mission.Id.Vehicle, mission.Id.Test)} – {title}";
    }

    /// <summary>A text without tags, on one line.</summary>
    private static string PlainText(string markup) =>
        Regex.Replace(markup.Replace("[b]", string.Empty).Replace("[/b]", string.Empty), @"\[[^\]]*\]", " ").Replace('\n', ' ').Trim();

    // ----------------------------------------------------------------- one mission

    private void ShowMission(MissionInfo? mission)
    {
        ShowWarnings(mission);
        if (mission is null)
        {
            InfoText.Text = _missions.Count == 0 ? L.T("No missions.") : string.Empty;
            foreach (TextEditorPanel panel in Panels)
            {
                panel.Show(null);
            }

            PointsGrid.Children.Clear();
            PointsGrid.RowDefinitions.Clear();
            return;
        }

        InfoText.Text = L.F("Mission file {0} · island: {1} · texts: {2}", mission.FileNumber, MissionCatalog.IslandName(mission.Island),
            mission.File.TextBase.Length > 0 ? mission.File.TextBase : L.T("none"))
            + (mission.FromProject ? " · " + L.T("changed in the project") : string.Empty);

        ShowStatement(mission);
        ShowPoints(mission);
        ShowResults(mission);
    }

    private void ShowWarnings(MissionInfo? mission)
    {
        var warnings = new List<string>(_problems);
        if (mission?.ClassIsland is { } classIsland && classIsland != mission.Island)
        {
            warnings.Add(L.F("The mission is played on {0}, but the class menu shows {1} for this class. The game uses both; they should be the same.",
                MissionCatalog.IslandName(mission.Island), MissionCatalog.IslandName(classIsland)));
        }

        WarningText.Text = string.Join("\n", warnings);
        WarningBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TextRow? Row(string? name) => name is not null ? _texts?.FindRow(name) : null;

    private void ShowStatement(MissionInfo mission)
    {
        ShowText(TitleEditor, L.T("Title"), mission.TitleTextName);
        ShowText(TaskEditor, L.T("Task"), mission.TaskTextName);
        ShowText(HintEditor, L.T("Hint"), mission.HintTextName);

        if (mission.SharesStatementWith.Count > 0)
        {
            StatementNote.Text = L.F("These texts belong to the whole {0}: all {1} targets use them.",
                MissionCatalog.ClassName(mission.Id.Vehicle, mission.Id.Class), mission.SharesStatementWith.Count + 1);
            StatementNote.Visibility = Visibility.Visible;
        }
        else
        {
            StatementNote.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Shows a text in a panel, with its name in the caption, or says that it does not exist.</summary>
    private void ShowText(TextEditorPanel panel, string caption, string? name)
    {
        TextRow? row = Row(name);
        panel.Caption = name is null
            ? L.F("{0}: the mission has no text name", caption)
            : row is null
                ? L.F("{0} ({1}): the game has no text with this name", caption, name)
                : $"{caption} ({name})";
        panel.Show(row);
    }

    private void ShowPoints(MissionInfo mission)
    {
        PointsGrid.Children.Clear();
        PointsGrid.RowDefinitions.Clear();
        MissionComm comm = mission.File.Comm;

        if (mission.IsBirdman)
        {
            PointsNote.Text = L.T("Birdman has no score sheet in the original game: its missions are free flights and show the text BD_ALL_S1 (\"There's no score\"). The points it computes are saved, but never shown.");
            SumText.Text = string.Empty;
        }
        else
        {
            PointsNote.Text = L.T("The rows of the result sheet, from top to bottom, with the most points the mission data gives for each. \"Text\" is the number the mission screen text shows.");
            List<int> textNumbers = TextNumbers(mission.MissionScreenTextName);
            AddPointRow(0, L.T("Row"), L.T("Source"), L.T("Points"), L.T("Text"), L.T("Where the points come from"), header: true);
            for (int i = 0; i < mission.Rows.Count; i++)
            {
                MissionPointRow row = mission.Rows[i];
                string text = i < textNumbers.Count ? textNumbers[i].ToString(CultureInfo.CurrentCulture) : "–";
                AddPointRow(i + 1, (i + 1).ToString(CultureInfo.CurrentCulture), PointSources.Name(row.Source),
                    row.Value?.ToString(CultureInfo.CurrentCulture) ?? "?", text, Origin(mission, row), header: false,
                    differs: row.Value is { } v && i < textNumbers.Count && v != textNumbers[i]);
            }

            SumText.Text = mission.AllRowsKnown
                ? L.F("Sum: {0} points (the game counts at most 100).", mission.KnownRowSum)
                : L.F("Sum of the known rows: {0} points; the game computes the others while playing.", mission.KnownRowSum);
        }

        ConditionText.Text = comm.Condition switch
        {
            MissionCondition.None => L.T("Condition: none. Objects also count after a crash."),
            MissionCondition.CrashGivesZero => L.T("Condition: a crash gives 0 points in total."),
            MissionCondition.AllObjects => L.T("Condition: time points only if all objects are cleared."),
            MissionCondition.SomeObjects => L.F("Condition: time points only if at least {0} objects are cleared.", comm.RequiredObjects),
            MissionCondition.MinimumAltitude => L.F("Condition: time points only if an altitude of {0} was reached.", comm.MinimumAltitude),
            _ => L.F("Condition: unknown value {0}.", (int)comm.Condition),
        };

        ScoreCurve time = comm.GetCurve(ScoreCurveKind.Time);
        TimeCurveText.Text = time.GivesPoints
            ? L.F("Time points: {0}.", string.Join(" · ", time.Points.Select(p => L.F("{0} → {1}", FormatTime(p.X), FormatNumber(p.Y)))))
            : L.T("Time points: none.");

        var events = new List<string>();
        if (comm.LandNowTime > 0)
        {
            events.Add(L.F("After {0}, \"Land now\" is shown and the mission objects are removed.", FormatTime(comm.LandNowTime)));
        }

        if (comm.HardEndTime > 0)
        {
            events.Add(L.F("After {0}, the mission ends at once.", FormatTime(comm.HardEndTime)));
        }

        TimeEventsText.Text = events.Count > 0 ? string.Join("\n", events) : L.T("No \"Land now\" time and no hard end time.");

        var deductions = new List<string>();
        if (comm.HitDeduction != 0)
        {
            deductions.Add(L.F("Each hit costs {0} point(s).", -comm.HitDeduction));
        }

        if (mission.Id.Vehicle == Vehicle.JumbleHopper && comm.OutOfAreaDeduction != 0)
        {
            deductions.Add(L.F("Each time the hopper leaves the area costs {0} point(s).", FormatNumber(comm.OutOfAreaDeduction)));
        }

        if (!mission.IsBirdman)
        {
            deductions.Add(L.F("Above {0} points the pilot cheers at the end.", comm.ReactionThreshold));
        }

        DeductionText.Text = string.Join("\n", deductions);

        ShowPointText(ScreenTextView, ScreenTextNote, mission.MissionScreenTextName, mission.SharesMissionScreenTextWith,
            L.T("The game has no mission screen text for this mission."));
    }

    private void ShowResults(MissionInfo mission)
    {
        if (mission.IsBirdman)
        {
            ResultRowsText.Text = L.T("Birdman has no result sheet in the original game.");
        }
        else
        {
            bool deductionRow = mission.Id.Vehicle is not (Vehicle.HangGlider or Vehicle.Cannonball or Vehicle.SkyDiving);
            ResultRowsText.Text = L.F("The result sheet shows these rows, from top to bottom: {0}.", string.Join(", ", mission.Rows.Select(r => PointSources.Name(r.Source))))
                + " " + (deductionRow
                    ? L.T("Below them come \"Points Deducted\" and the total.")
                    : L.T("Below them comes the total (this vehicle has no \"Points Deducted\" row)."));
        }

        ShowPointText(ResultTextView, ResultTextNote, mission.ResultSheetTextName, mission.SharesResultSheetTextWith,
            L.T("The game has no result sheet text for this mission."));
    }

    private void ShowPointText(TextEditorPanel panel, TextBlock note, string? name, IReadOnlyList<TestId> sharedWith, string missing)
    {
        TextRow? row = Row(name);
        panel.Caption = name is null ? L.T("Text") : L.F("Text ({0})", name);
        panel.Show(row);
        panel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;

        var lines = new List<string>
        {
            L.T("Shown here as it is; in a later version the editor writes this text from the points."),
        };
        if (row is null)
        {
            lines.Add(name is null ? missing : L.F("The game has no text {0}.", name));
        }

        if (sharedWith.Count > 0)
        {
            lines.Add(L.F("{0} other mission(s) use the same text: {1}.", sharedWith.Count, string.Join(", ", sharedWith.Select(TestLabel))));
        }

        note.Text = string.Join("\n", lines);
    }

    private static string TestLabel(TestId id) =>
        $"{Vehicles.Name(id.Vehicle)} {MissionCatalog.ClassName(id.Vehicle, id.Class)} {MissionCatalog.TestName(id.Vehicle, id.Test)}";

    /// <summary>Explanation of where the points of a row come from.</summary>
    private static string Origin(MissionInfo mission, MissionPointRow row) => row.Kind switch
    {
        PointValueKind.Curve when row.Source == PointSource.LandingAccuracy && mission.Id.Vehicle == Vehicle.Gyrocopter =>
            L.T("Score curve; the Gyrocopter multiplies two landing measurements (touchdown point and resting position)."),
        PointValueKind.Curve => L.T("Highest value of the score curve."),
        PointValueKind.AllObjects => L.F("Sum over all {0} objects. Linked rings (only one of a group counts) or a required count can make it less.",
            ObjectCount(mission, row.Source)),
        PointValueKind.OtherMission => L.T("The best shot at this target (the target's own test)."),
        PointValueKind.ComputedByGame => L.T("Computed by the game while playing."),
        _ => string.Empty,
    };

    private static int ObjectCount(MissionInfo mission, PointSource source) => source switch
    {
        PointSource.Rings => mission.File.Comm.GetObjectCount(MissionObjectKind.Ring),
        PointSource.Balloons => mission.File.Comm.GetObjectCount(MissionObjectKind.Balloon),
        PointSource.Targets => mission.File.Comm.GetObjectCount(MissionObjectKind.Target),
        PointSource.HoverPads => mission.File.Comm.GetObjectCount(MissionObjectKind.HoverPad),
        _ => 0,
    };

    /// <summary>Adds a line to the points table: row number, source, "points / text", origin.</summary>
    private void AddPointRow(int index, string number, string source, string points, string text, string origin, bool header, bool differs = false)
    {
        PointsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] cells = [number, source, $"{points} / {text}", origin];
        for (int column = 0; column < cells.Length; column++)
        {
            var block = new TextBlock
            {
                Text = cells[column],
                Margin = new Thickness(0, 2, 14, 2),
                TextWrapping = column == 3 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (column == 2 && differs)
            {
                block.Foreground = (System.Windows.Media.Brush)FindResource("ChangedBrush");
                block.ToolTip = L.T("The mission data and the text do not agree.");
            }
            else if (column == 3 && !header)
            {
                block.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
            }

            Grid.SetRow(block, index);
            Grid.SetColumn(block, column);
            PointsGrid.Children.Add(block);
        }
    }

    /// <summary>The numbers of a mission screen text ("[b]30[/b] pts."), in order.</summary>
    private List<int> TextNumbers(string? name) =>
        Row(name) is { } row
            ? Regex.Matches(row.Markup, @"\[b\](\d+)\[/b\] pts").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList()
            : [];

    /// <summary>A time like the game's texts: 1'30" (with the seconds as a number when they have a fraction).</summary>
    private static string FormatTime(float seconds)
    {
        int whole = (int)seconds;
        string clock = $"{whole / 60}'{whole % 60:D2}\"";
        return Math.Abs(seconds - whole) < 0.001f ? clock : $"{clock} ({FormatNumber(seconds)} s)";
    }

    private static string FormatNumber(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}
