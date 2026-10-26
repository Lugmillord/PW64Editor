using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.Code;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Missions": one test of the game at a time. Title, task and hint are the same texts as
/// in the Text tab. Points, rows of the result sheet, time limit, conditions and footnote are
/// edited here; as soon as the points of a mission change, the editor writes its point texts
/// ("…_S1" and "…_S2"). Changes are kept until the user saves (Ctrl+S), together with the texts.
/// </summary>
public partial class MissionsTab : UserControl
{
    private TextTab? _texts;
    private EditorSession? _session;
    private IReadOnlyList<MissionInfo> _missions = [];
    private IReadOnlyList<string> _problems = [];
    private readonly Dictionary<TestId, MissionDraft> _drafts = [];
    private bool _filling;

    /// <summary>Set while the controls are filled by code, so that does not count as an edit.</summary>
    private bool _showing;

    /// <summary>Tag of the box of the points table that should get the focus after the table was rebuilt.</summary>
    private string? _focusAfterRefresh;

    /// <summary>Missions whose time curve is edited point by point (also when it has a simple shape).</summary>
    private readonly HashSet<TestId> _customTime = [];

    /// <summary>The background of each line of the points table (marks where a dragged row lands).</summary>
    private readonly List<Border> _rowBackgrounds = [];

    /// <summary>Data format of a dragged row of the points table.</summary>
    private const string RowDragFormat = "HawksHangar.MissionPointRow";

    /// <summary>How the time curve is edited.</summary>
    private enum TimeMode
    {
        Plateau,
        Target,
        Custom,
    }

    public MissionsTab()
    {
        InitializeComponent();
        TitleEditor.Caption = L.T("Title");
        TaskEditor.Caption = L.T("Task");
        HintEditor.Caption = L.T("Hint");
        ScreenTextView.Caption = L.T("Text");
        ResultTextView.Caption = L.T("Text");

        foreach (TextBox box in ValueBoxes)
        {
            box.LostKeyboardFocus += (_, _) => OnValueCommitted(box);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OnValueCommitted(box);
                }
            };
        }

        TimeShapeBox.ItemsSource = new[]
        {
            new ChoiceItem<TimeMode>(TimeMode.Plateau, L.T("Full points, then fewer and fewer")),
            new ChoiceItem<TimeMode>(TimeMode.Target, L.T("As close as possible to a target time")),
            new ChoiceItem<TimeMode>(TimeMode.Custom, L.T("Custom points")),
        };
        TimeShapeBox.DisplayMemberPath = nameof(ChoiceItem<int>.Text);
    }

    /// <summary>Raised when changes become unsaved or saved.</summary>
    public event EventHandler? UnsavedChangesChanged;

    /// <summary>True if a mission was changed and not saved yet.</summary>
    public bool HasUnsavedChanges => _drafts.Values.Any(d => d.IsChanged);

    /// <summary>A vehicle of the first list.</summary>
    public sealed record VehicleItem(Vehicle Vehicle, string Text);

    /// <summary>A test of the second list.</summary>
    /// <param name="Mission">The mission shown (for Cannonball the level's first target).</param>
    /// <param name="Text">The text of the list.</param>
    /// <param name="Group">The missions edited together (Cannonball: the four targets of a level; otherwise the mission alone).</param>
    public sealed record TestItem(MissionInfo Mission, string Text, IReadOnlyList<MissionInfo> Group);

    /// <summary>An entry of a choice list.</summary>
    public sealed record ChoiceItem<T>(T Value, string Text);

    /// <summary>A source of the row lists.</summary>
    public sealed record SourceItem(PointSource Source, string Text);

    private MissionInfo? SelectedMission => (TestBox.SelectedItem as TestItem)?.Mission;

    private MissionDraft? SelectedDraft => SelectedMission is { } mission ? Draft(mission) : null;

    private IEnumerable<TextBox> ValueBoxes =>
        [FullUntilBox, ZeroAtBox, TargetTimeBox, WindowBox, ZeroDistanceBox, LandNowBox, HardEndBox, AltitudeConditionValue, DistanceConditionValue, HitCostBox, OutOfAreaBox, ReactionBox, StartFuelBox];

    private IEnumerable<TextEditorPanel> Panels => [TitleEditor, TaskEditor, HintEditor, ScreenTextView, ResultTextView];

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

        // Point texts are written by this tab from the points; they cannot be edited in the Text tab.
        texts.LockReason = name => PointTextNames().Contains(name)
            ? L.T("The Missions tab writes this text from the points of the mission. Change the points there.")
            : ResultTables.IsGeneratedText(name)
                ? L.T("The editor writes this text from the Birdman missions with points of the stage (game patch \"Bonus games and Birdman\").")
                : PW64Editor.Core.Classes.ClassTexts.IsClassText(name)
                    ? L.T("The editor writes this text from the badge limits of the class (Classes tab) and the shared text of the class (e.g. A_S3_GOLD).")
                    : null;

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

    /// <summary>Reads the missions of a project. Unsaved changes are dropped (the main window asks first).</summary>
    public void Load(EditorSession session)
    {
        _session = session;
        TestId? selected = _selectAfterLoad ?? SelectedMission?.Id;
        _selectAfterLoad = null;
        _copyClassOf = null;
        _drafts.Clear();
        _customTime.Clear();
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

        // The point texts of changed missions follow their points, also if the texts were
        // discarded or changed elsewhere in the meantime.
        foreach (MissionInfo mission in _missions.Where(m => m.Settings?.GenerateTexts == true))
        {
            WriteTexts(Draft(mission));
        }

        _filling = true;
        VehicleBox.ItemsSource = MissionCatalog.VehicleOrder
            .Where(v => _missions.Any(m => m.Id.Vehicle == v))
            .Select(v => new VehicleItem(v, Vehicles.Name(v)))
            .ToList();
        _filling = false;

        SourceText.Text = L.F("{0} missions. Changes are saved together with the texts (Ctrl+S).", _missions.Count);

        // The lists are always filled anew: the vehicle items are records, so selecting the same vehicle again raises no
        // SelectionChanged, and the test list would keep the missions as loaded before (a draft made from them would bring
        // back the state before the last save).
        Vehicle vehicle = selected?.Vehicle ?? Vehicle.HangGlider;
        _filling = true;
        VehicleBox.SelectedItem = ((IEnumerable<VehicleItem>)VehicleBox.ItemsSource).FirstOrDefault(v => v.Vehicle == vehicle)
            ?? ((IEnumerable<VehicleItem>)VehicleBox.ItemsSource).FirstOrDefault();
        _filling = false;
        if (VehicleBox.SelectedItem is VehicleItem shownVehicle)
        {
            FillTests(shownVehicle.Vehicle);
            List<TestItem> tests = [.. (IEnumerable<TestItem>)TestBox.ItemsSource!];
            _filling = true;
            TestBox.SelectedItem = (selected is { } id ? tests.FirstOrDefault(t => t.Mission.Id == id) : null) ?? tests.FirstOrDefault();
            _filling = false;
        }

        ShowMission(SelectedMission);

        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves all changed missions into the project.</summary>
    /// <returns>Number of saved missions.</returns>
    /// <exception cref="PW64Editor.Core.Project.ProjectException">A mission cannot be saved.</exception>
    public int SaveChanges(EditorSession session)
    {
        List<MissionDraft> changed = _drafts.Values.Where(d => d.IsChanged).ToList();
        foreach (MissionDraft draft in changed)
        {
            session.SaveMission(draft.Mission, draft.Comm, draft.SettingsToSave());
        }

        if (changed.Count > 0)
        {
            Load(session);
        }

        return changed.Count;
    }

    /// <summary>Drops all unsaved changes of missions.</summary>
    public void DiscardChanges()
    {
        if (_session is not null)
        {
            Load(_session);
        }
    }

    // ----------------------------------------------------------------- selection

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
        List<MissionInfo> missions = _missions.Where(m => m.Id.Vehicle == vehicle).ToList();
        TestBox.ItemsSource = vehicle == Vehicle.Cannonball
            // A Cannonball level is edited as a whole: its four targets share texts and the sheet.
            ? missions.GroupBy(m => m.Id.Class)
                .Select(g => g.OrderBy(m => m.Id.Test).ToList())
                .Select(g => new TestItem(g[0], LevelText(g), g))
                .ToList()
            : missions.Select(m => new TestItem(m, TestText(m), [m])).ToList();
        _filling = false;
    }

    /// <summary>"Level 1 – title (4 targets)" for a Cannonball level.</summary>
    private string LevelText(IReadOnlyList<MissionInfo> targets)
    {
        MissionInfo first = targets[0];
        string title = first.TitleTextName is { } name && _texts?.FindRow(name) is { } row ? PlainText(row.Markup) : first.File.Name;
        string changed = targets.Any(t => _drafts.TryGetValue(t.Id, out MissionDraft? d) && d.IsChanged) ? " ●" : string.Empty;
        return $"{MissionCatalog.ClassName(first.Id.Vehicle, first.Id.Class)} – {title} ({L.F("{0} targets", targets.Count)}){changed}";
    }

    /// <summary>The missions edited together with the selected one.</summary>
    private IReadOnlyList<MissionInfo> SelectedGroup => (TestBox.SelectedItem as TestItem)?.Group ?? [];

    /// <summary>For a Cannonball level: the draft of the target behind a row of the sheet, or null.</summary>
    private MissionDraft? CannonballTarget(MissionDraft draft, int rowIndex)
    {
        if (draft.Vehicle != Vehicle.Cannonball || rowIndex >= draft.Rows.Count
            || draft.Rows[rowIndex] is < PointSource.CannonballTarget1 or > PointSource.CannonballTarget4)
        {
            return null;
        }

        int test = draft.Rows[rowIndex] - PointSource.CannonballTarget1;
        return _missions.FirstOrDefault(m => m.Id == new TestId(draft.Mission.Id.Class, Vehicle.Cannonball, test)) is { } target
            ? Draft(target)
            : null;
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
        string changed = _drafts.TryGetValue(mission.Id, out MissionDraft? draft) && draft.IsChanged ? " ●" : string.Empty;
        return $"{MissionCatalog.ClassName(mission.Id.Vehicle, mission.Id.Class)} · {MissionCatalog.TestName(mission.Id.Vehicle, mission.Id.Test)} – {title}{changed}";
    }

    /// <summary>A text without tags, on one line.</summary>
    private static string PlainText(string markup) =>
        Regex.Replace(markup.Replace("[b]", string.Empty).Replace("[/b]", string.Empty), @"\[[^\]]*\]", " ").Replace('\n', ' ').Trim();

    // ----------------------------------------------------------------- drafts and texts

    private MissionDraft Draft(MissionInfo mission)
    {
        if (!_drafts.TryGetValue(mission.Id, out MissionDraft? draft))
        {
            // Always from the missions as loaded last (a list item may still hold an older state).
            mission = _missions.FirstOrDefault(m => m.Id == mission.Id) ?? mission;
            draft = new MissionDraft(mission, Row(ScreenName(mission))?.Markup);
            _drafts[mission.Id] = draft;
        }

        return draft;
    }

    /// <summary>The current COMM block of another mission (its draft, or as loaded).</summary>
    private MissionComm? OtherComm(TestId id) =>
        _drafts.TryGetValue(id, out MissionDraft? draft) ? draft.Comm : _missions.FirstOrDefault(m => m.Id == id)?.File.Comm;

    /// <summary>True if the project has the game patch "Bonus games and Birdman" (Birdman missions with points).</summary>
    private bool BonusPatch => _session?.Project.HasPatch(GamePatches.BonusGames) == true;

    /// <summary>True if a Birdman mission has points (its draft, or as saved); always false without the patch.</summary>
    private bool BirdmanScored(MissionInfo mission) =>
        mission.IsBirdman && BonusPatch
        && (_drafts.TryGetValue(mission.Id, out MissionDraft? draft) ? draft.Settings.BirdmanPoints : mission.Settings?.BirdmanPoints == true);

    /// <summary>The mission screen text of a mission; for the Birdman as its draft says (free flight or with points).</summary>
    private string? ScreenName(MissionInfo mission) =>
        mission.IsBirdman && BonusPatch
            ? BirdmanScored(mission) ? ResultTables.BirdmanTextName(mission.Id, false) : "BD_ALL_S1"
            : mission.MissionScreenTextName;

    /// <summary>The result sheet text of a mission; for the Birdman as its draft says.</summary>
    private string? SheetName(MissionInfo mission) =>
        mission.IsBirdman && BonusPatch
            ? BirdmanScored(mission) ? ResultTables.BirdmanTextName(mission.Id, true) : "BD_ALL_S2"
            : mission.ResultSheetTextName;

    /// <summary>Names of the point texts of all missions that the editor can write (not the Birdman's free flights).</summary>
    private HashSet<string> PointTextNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (MissionInfo mission in _missions.Where(m => !m.IsBirdman || BirdmanScored(m)))
        {
            foreach (string? name in new[] { ScreenName(mission), SheetName(mission) })
            {
                if (name is not null)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>Names of the point texts the editor writes (missions whose points were changed).</summary>
    private HashSet<string> GeneratedTextNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (MissionInfo mission in _missions)
        {
            bool generated = _drafts.TryGetValue(mission.Id, out MissionDraft? draft)
                ? draft.Settings.GenerateTexts
                : mission.Settings?.GenerateTexts == true;
            if (generated)
            {
                if (ScreenName(mission) is { } screen)
                {
                    names.Add(screen);
                }

                if (SheetName(mission) is { } sheet)
                {
                    names.Add(sheet);
                }
            }
        }

        return names;
    }

    /// <summary>Writes the point texts of a mission from its points into the Text tab's texts (not saved yet).</summary>
    private void WriteTexts(MissionDraft draft)
    {
        if (!draft.Settings.GenerateTexts || draft.Rows.Count == 0 || !draft.HasScoreSheet)
        {
            return;
        }

        SetText(ScreenName(draft.Mission), draft.MissionScreenText(OtherComm));
        SetText(SheetName(draft.Mission), draft.ResultSheetText(OtherComm));
    }

    private void SetText(string? name, string markup)
    {
        if (Row(name) is { } row && row.Markup != markup)
        {
            bool wasUnsaved = row.IsUnsaved;
            row.Markup = markup;
            _texts!.OnTextEdited(row, wasUnsaved);
        }
    }

    /// <summary>After a change: texts, display and the unsaved state.</summary>
    private void Changed(MissionDraft draft)
    {
        WriteTexts(draft);
        if (TestBox.SelectedItem is TestItem item && VehicleBox.SelectedItem is VehicleItem vehicle)
        {
            // The list shows ● for changed missions.
            FillTests(vehicle.Vehicle);
            _filling = true;
            TestBox.SelectedItem = ((IEnumerable<TestItem>)TestBox.ItemsSource).First(t => t.Mission.Id == item.Mission.Id);
            _filling = false;
        }

        ShowMission(SelectedMission ?? draft.Mission);
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the unsaved changes of the mission shown (a Cannonball level: all four targets).</summary>
    private void OnUndoChanges(object sender, RoutedEventArgs e)
    {
        if (SelectedMission is not { } shown)
        {
            return;
        }

        IReadOnlyList<MissionInfo> group = SelectedGroup.Count > 0 ? SelectedGroup : [shown];
        foreach (MissionInfo mission in group)
        {
            _drafts.Remove(mission.Id);
            _customTime.Remove(mission.Id);
        }

        // The point texts back to their saved state, unless another changed mission writes them.
        HashSet<string> stillWritten = _drafts.Values.Where(d => d.IsChanged && d.Settings.GenerateTexts)
            .SelectMany(d => new[] { ScreenName(d.Mission), SheetName(d.Mission) })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (string? name in new[] { ScreenName(shown), SheetName(shown) })
        {
            if (Row(name) is { } row && !stillWritten.Contains(row.Name))
            {
                SetText(name, row.SavedMarkup);
            }
        }

        // Missions whose saved settings generate the texts write them again from the saved state.
        foreach (MissionInfo mission in group.Where(m => m.Settings?.GenerateTexts == true))
        {
            WriteTexts(Draft(mission));
        }

        Changed(Draft(shown));
    }

    private void OnRestoreOriginal(object sender, RoutedEventArgs e)
    {
        if (SelectedDraft is not { } draft || _session is null)
        {
            return;
        }

        IReadOnlyList<MissionInfo> group = SelectedGroup.Count > 0 ? SelectedGroup : [draft.Mission];
        string label = group.Count > 1
            ? $"{Vehicles.Name(draft.Vehicle)} {MissionCatalog.ClassName(draft.Vehicle, draft.Mission.Id.Class)}"
            : MissionCatalog.Label(draft.Mission.Id);
        if (!Ui.Confirm(Window.GetWindow(this)!, L.F("Set points, conditions, time limit and point texts of {0} back to the original game?", label)))
        {
            return;
        }

        try
        {
            foreach (MissionInfo mission in group)
            {
                Draft(mission).RestoreOriginal(_session.OriginalMissionComm(mission));
                _customTime.Remove(mission.Id);
            }
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Window.GetWindow(this)!, ex.Message);
            return;
        }

        // The point texts go back to the original too (unless another changed mission shares them).
        foreach (string? name in new[] { ScreenName(draft.Mission), SheetName(draft.Mission) })
        {
            if (Row(name) is { } row && !GeneratedTextNames().Contains(row.Name))
            {
                SetText(name, row.OriginalMarkup);
            }
        }

        Changed(draft);
    }

    // ----------------------------------------------------------------- one mission

    private void ShowMission(MissionInfo? mission)
    {
        _showing = true;
        try
        {
            ShowWarnings(mission);
            ShowMissionList(mission);
            if (mission is null)
            {
                InfoText.Text = _missions.Count == 0 ? L.T("No missions.") : string.Empty;
                foreach (TextEditorPanel panel in Panels)
                {
                    panel.Show(null);
                }

                PointsGrid.Children.Clear();
                PointsGrid.RowDefinitions.Clear();
                RestoreButton.IsEnabled = false;
                UndoButton.IsEnabled = false;
                return;
            }

            MissionDraft draft = Draft(mission);
            RestoreButton.IsEnabled = draft.HasScoreSheet;
            UndoButton.IsEnabled = SelectedGroup.Append(mission).Any(m => _drafts.TryGetValue(m.Id, out MissionDraft? d) && d.IsChanged);
            InfoText.Text = L.F("Mission file {0} · island: {1} · texts: {2}", mission.FileNumber, MissionCatalog.IslandName(mission.Island),
                mission.File.TextBase.Length > 0 ? mission.File.TextBase : L.T("none"))
                + (mission.FromProject ? " · " + L.T("changed in the project") : string.Empty)
                + (draft.IsChanged ? " · " + L.T("changes not saved yet") : string.Empty);

            ShowStatement(mission);
            ShowPoints(draft);
            ShowTime(draft);
            ShowAltitude(draft);
            ShowDistance(draft);
            ShowLanding(draft);
            ShowImpact(draft);
            ShowGoodLanding(draft);
            ShowConditions(draft);
            DemoPanel.Visibility = draft.CanHideDemo ? Visibility.Visible : Visibility.Collapsed;
            FuelPanel.Visibility = draft.CanSetFuel ? Visibility.Visible : Visibility.Collapsed;
            StartFuelBox.Text = FormatMeters(draft.StartFuel * 100);
            StartFuelBox.IsEnabled = !draft.Settings.UnlimitedFuel;
            UnlimitedFuelBox.IsChecked = draft.Settings.UnlimitedFuel;
            HideDemoBox.IsChecked = draft.Settings.HideDemo;
            ShowWeather(draft);
            ShowResults(draft);
        }
        finally
        {
            _showing = false;
        }

        RestoreFocus();
    }

    /// <summary>The points table is rebuilt after a change: give the focus back to the box the user went to.</summary>
    private void RestoreFocus()
    {
        if (_focusAfterRefresh is not { } tag)
        {
            return;
        }

        _focusAfterRefresh = null;
        IEnumerable<TextBox> boxes = PointsGrid.Children.OfType<TextBox>()
            .Concat(RingTypes.Children.OfType<TextBox>())
            .Concat(CurveBoxes(TimeCurvePanel))
            .Concat(CurveBoxes(AltitudeCurvePanel))
            .Concat(CurveBoxes(LandingCurvePanel))
            .Concat(GoodLandingGrid.Children.OfType<TextBox>())
            .Concat(CurveBoxes(DistanceCurvePanel))
            .Concat(ObjectConditionsGrid.Children.OfType<TextBox>());
        if (boxes.FirstOrDefault(b => Equals(b.Tag, tag)) is { } target)
        {
            Dispatcher.BeginInvoke(new Action(() => target.Focus()));
        }
    }

    /// <summary>The boxes of a curve table (see <see cref="CurveEditor"/>).</summary>
    private static IEnumerable<TextBox> CurveBoxes(Panel host) =>
        host.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Grid>()).SelectMany(g => g.Children.OfType<TextBox>());

    private void ShowWarnings(MissionInfo? mission)
    {
        var warnings = new List<string>(_problems);
        if (mission is not null)
        {
            warnings.AddRange(MissionBudget.Warnings(mission.File.Comm));
        }

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

    // ----------------------------------------------------------------- points

    private void ShowPoints(MissionDraft draft)
    {
        PointsGrid.Children.Clear();
        PointsGrid.RowDefinitions.Clear();
        _rowBackgrounds.Clear();
        MissionInfo mission = draft.Mission;
        RingPanel.Visibility = Visibility.Collapsed;

        // Birdman: free flight (as the original) or, with the game patch "Bonus games and Birdman", a mission with points.
        BirdmanPointsBox.Visibility = mission.IsBirdman && BonusPatch ? Visibility.Visible : Visibility.Collapsed;
        BirdmanPointsBox.IsChecked = draft.Settings.BirdmanPoints;
        BirdmanPointsNote.Visibility = BirdmanPointsBox.Visibility;
        BirdmanPointsNote.Text = draft.HasScoreSheet
            ? L.T("A landing (feet first and slow enough, see \"Good landing\") ends the mission; a landing on a landing pad counts as a good landing, as for the Hang Glider, and the impact curve decides how hard it was. The impact is the Birdman's speed at the touchdown as the HUD shows it. The mission gets its own texts on the mission screen and the result sheet.")
            : L.T("Free flight, as in the original game: after a soft touchdown the Birdman flies on, and the mission shows the shared texts BD_ALL_S1 and BD_ALL_S2 without points.");
        if (!draft.HasScoreSheet)
        {
            PointsNote.Text = BonusPatch
                ? string.Empty
                : L.T("Birdman has no score sheet in the original game: its missions are free flights and show the text BD_ALL_S1 (\"There's no score\"). The points it computes are saved, but never shown. The game patch \"Bonus games and Birdman\" gives them points.");
            PointsNote.Visibility = PointsNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SumText.Text = string.Empty;
            SumWarning.Visibility = Visibility.Collapsed;
            return;
        }

        PointsNote.Visibility = Visibility.Visible;

        PointsNote.Text = draft.CanChangeSources
            ? L.T("The rows of the result sheet, from top to bottom (at most 4). Drag a row by its handle (⠿) to move it. For points from a curve, the number changes the curve; for points from objects, it is the number the texts show (the game counts the objects).")
            : L.T("The four targets of the level. Each target is a test of its own; its row gives the points of its landing curve (the best of the three shots counts).");

        IReadOnlyList<MissionPointRow> rows = draft.EvaluateRows(OtherComm);
        AddHeader();
        for (int i = 0; i < rows.Count; i++)
        {
            AddRow(draft, i, rows[i]);
        }

        if (draft.CanChangeSources && rows.Count < MissionTextGenerator.MaxRows)
        {
            AddRow(draft, rows.Count, null);
        }

        int sum = rows.Select(draft.TextPoints).Sum();
        SumText.Text = L.F("Total: {0} points.", sum);
        var warnings = new List<string>();
        if (sum != 100)
        {
            warnings.Add(sum > 100
                ? L.F("The total is {0}; the game counts at most 100 points.", sum)
                : L.F("The total is {0}, not 100: a perfect run gives fewer than 100 points.", sum));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == PointValueKind.AllObjects && rows[i].Value is { } objects && objects != draft.TextPoints(rows[i]))
            {
                warnings.Add(L.F("{0}: the texts show {1}, all objects together give {2}. This is right if not all of them can be reached (linked rings, a required count).",
                    PointSources.Name(rows[i].Source), draft.TextPoints(rows[i]), objects));
            }
        }

        SumWarning.Text = string.Join("\n", warnings);
        SumWarning.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowRingTypes(draft);
        ShowPointText(ScreenTextView, ScreenTextNote, ScreenName(mission), BirdmanScored(mission) ? [] : mission.SharesMissionScreenTextWith,
            L.T("The game has no mission screen text for this mission."), draft);
    }

    private void AddHeader()
    {
        PointsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headers = [L.T("Row"), L.T("Source"), L.T("Points"), L.T("Where the points come from")];
        for (int c = 0; c < headers.Length; c++)
        {
            var block = new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 14, 4) };
            Grid.SetColumn(block, c);
            PointsGrid.Children.Add(block);
        }
    }

    /// <summary>A line of the points table; <paramref name="row"/> null is the line that adds a row.</summary>
    private void AddRow(MissionDraft draft, int index, MissionPointRow? row)
    {
        int gridRow = PointsGrid.RowDefinitions.Count;
        PointsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Background of the whole line; it marks the place where a dragged row would land.
        var background = new Border { Background = System.Windows.Media.Brushes.Transparent };
        Grid.SetColumnSpan(background, 4);
        Place(background, gridRow, 0);
        _rowBackgrounds.Add(background);

        var numberPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 14, 2) };
        if (row is not null && draft.CanChangeSources && draft.Rows.Count > 1)
        {
            // The handle to drag the row to another place.
            var grip = new TextBlock
            {
                Text = "⠿",
                FontSize = 16,
                Cursor = Cursors.SizeNS,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = L.T("Drag to move this row up or down"),
            };
            int from = index;
            grip.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                DragDrop.DoDragDrop(grip, new DataObject(RowDragFormat, from), DragDropEffects.Move);
                MarkDropTarget(null);
            };
            numberPanel.Children.Add(grip);
        }

        numberPanel.Children.Add(new TextBlock
        {
            Text = row is null ? "+" : (index + 1).ToString(CultureInfo.CurrentCulture),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Place(numberPanel, gridRow, 0);

        if (draft.CanChangeSources)
        {
            var sources = new List<SourceItem> { new(PointSource.None, row is null ? L.T("(add a row)") : PointSources.Name(PointSource.None)) };
            sources.AddRange(MissionCatalog.AvailableSources(draft.Vehicle)
                .Where(s => s == row?.Source || !draft.Rows.Contains(s))
                .Select(s => new SourceItem(s, PointSources.Name(s))));
            if (row is { } current && current.Source != PointSource.None && sources.All(s => s.Source != current.Source))
            {
                // A row of a source that is no longer offered (e.g. landing accuracy of the Jumble Hopper in an older project): keep showing it.
                sources.Add(new SourceItem(current.Source, PointSources.Name(current.Source)));
            }
            var box = new ComboBox
            {
                ItemsSource = sources,
                DisplayMemberPath = nameof(SourceItem.Text),
                Width = 180,
                Margin = new Thickness(0, 2, 14, 2),
            };
            box.SelectedItem = sources.FirstOrDefault(s => s.Source == (row?.Source ?? PointSource.None));
            box.SelectionChanged += (_, _) =>
            {
                if (!_showing && box.SelectedItem is SourceItem item)
                {
                    // The table is rebuilt afterwards, which removes this list; do that after its event.
                    Dispatcher.BeginInvoke(new Action(() => OnSourceChosen(draft, index, item.Source)));
                }
            };
            Place(box, gridRow, 1);
        }
        else
        {
            Place(new TextBlock { Text = PointSources.Name(row!.Source), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 14, 2) }, gridRow, 1);
        }

        if (row is null)
        {
            return;
        }

        int points = draft.TextPoints(row);
        var pointsBox = new TextBox
        {
            Text = points.ToString(CultureInfo.CurrentCulture),
            Width = 60,
            Margin = new Thickness(0, 2, 14, 2),
            IsEnabled = draft.CanSetRowPoints(index) || CannonballTarget(draft, index) is not null,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = $"points{index}",
        };
        void Commit()
        {
            if (_showing || !pointsBox.IsEnabled)
            {
                return;
            }

            if (!int.TryParse(pointsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value is < 0 or > 126)
            {
                pointsBox.Text = points.ToString(CultureInfo.CurrentCulture);
                Ui.ShowError(Window.GetWindow(this)!, L.T("Points are whole numbers from 0 to 126 (the game stores at most 126 per test)."));
                return;
            }

            if (value != points)
            {
                // Cannonball: the row of a target changes that target's own test.
                MissionDraft target = CannonballTarget(draft, index) ?? draft;
                target.SetRowPoints(index, value, OtherComm);
                Changed(target);
            }
        }

        pointsBox.LostKeyboardFocus += (_, e) =>
        {
            _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
            Commit();
        };
        pointsBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        Place(pointsBox, gridRow, 2);

        var origin = new TextBlock
        {
            Text = Origin(draft, row),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 2, 0, 2),
        };
        Place(origin, gridRow, 3);
    }

    private void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        PointsGrid.Children.Add(element);
    }

    // ----------------------------------------------------------------- moving rows

    /// <summary>The row a dragged row would land on, from the mouse position; null if no row is dragged.</summary>
    private int? DropIndex(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(RowDragFormat) || SelectedDraft is not { } draft || draft.Rows.Count == 0)
        {
            return null;
        }

        // Line 0 of the grid is the header; line i + 1 is row i.
        double y = e.GetPosition(PointsGrid).Y;
        double top = 0;
        int line = 0;
        for (; line < PointsGrid.RowDefinitions.Count; line++)
        {
            top += PointsGrid.RowDefinitions[line].ActualHeight;
            if (y < top)
            {
                break;
            }
        }

        return Math.Clamp(line - 1, 0, draft.Rows.Count - 1);
    }

    /// <summary>Marks the line where a dragged row lands (null: no mark).</summary>
    private void MarkDropTarget(int? index)
    {
        for (int i = 0; i < _rowBackgrounds.Count; i++)
        {
            _rowBackgrounds[i].Background = i == index
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : System.Windows.Media.Brushes.Transparent;
            _rowBackgrounds[i].Opacity = i == index ? 0.18 : 1;
        }
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        int? index = DropIndex(e);
        e.Effects = index is null ? DragDropEffects.None : DragDropEffects.Move;
        MarkDropTarget(index);
        e.Handled = true;
    }

    private void OnRowDragLeave(object sender, DragEventArgs e) => MarkDropTarget(null);

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        MarkDropTarget(null);
        if (DropIndex(e) is not { } to || e.Data.GetData(RowDragFormat) is not int from || SelectedDraft is not { } draft)
        {
            return;
        }

        e.Handled = true;
        if (from == to)
        {
            return;
        }

        // The table is rebuilt afterwards, which removes the dragged handle; do that after the drag.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (draft.MoveRow(from, to) is { } problem)
            {
                Ui.ShowError(Window.GetWindow(this)!, problem);
                return;
            }

            Changed(draft);
        }));
    }

    private void OnSourceChosen(MissionDraft draft, int index, PointSource source)
    {
        // A new curve source starts with 10 points (or what is left up to 100).
        int rest = 100 - draft.EvaluateRows(OtherComm).Select((r, i) => i == index ? 0 : draft.TextPoints(r)).Sum();
        string? problem = draft.SetRowSource(index, source, Math.Clamp(rest, 1, 100));
        if (problem is not null)
        {
            Ui.ShowError(Window.GetWindow(this)!, problem);
        }

        Changed(draft);
    }

    /// <summary>Explanation of where the points of a row come from.</summary>
    private static string Origin(MissionDraft draft, MissionPointRow row) => row.Kind switch
    {
        PointValueKind.Curve when row.Source == PointSource.LandingAccuracy && draft.Vehicle == Vehicle.Gyrocopter =>
            L.T("Score curve; the Gyrocopter multiplies two landing measurements (touchdown point and resting position)."),
        PointValueKind.Curve => L.T("Highest value of the score curve."),
        PointValueKind.AllObjects => L.F("Objects: all {0} together give {1}.", ObjectCount(draft.Mission, row.Source), row.Value ?? 0),
        PointValueKind.OtherMission => L.T("The best shot at this target (the target's own test)."),
        PointValueKind.ComputedByGame => L.T("Computed by the game while playing; the number is only shown in the texts."),
        _ => string.Empty,
    };

    private static int ObjectCount(MissionInfo mission, PointSource source) => source switch
    {
        PointSource.Rings => MissionCatalog.ConditionTotal(mission.File, ConditionObject.Rings),
        PointSource.Balloons => mission.File.Comm.GetObjectCount(MissionObjectKind.Balloon),
        PointSource.Targets => mission.File.Comm.GetObjectCount(MissionObjectKind.Target),
        PointSource.HoverPads => mission.File.Comm.GetObjectCount(MissionObjectKind.HoverPad),
        _ => 0,
    };

    /// <summary>The points of each ring type that the mission's rings use.</summary>
    private void ShowRingTypes(MissionDraft draft)
    {
        RingTypes.Children.Clear();
        int[] counts = MissionCatalog.RingTypeCounts(draft.Mission.File);
        if (counts.Sum() == 0 && !draft.Rows.Contains(PointSource.Rings))
        {
            return;
        }

        // Types 1 and 2 always (rings of any type can be added later), the goal ring only where
        // the mission has one. Type 3 rings are invisible check rings that only unlock other
        // rings: they give no points, so they only get a line of explanation.
        RingPanel.Visibility = Visibility.Visible;
        for (int type = 0; type < counts.Length; type++)
        {
            bool goal = type == MissionComm.RingTypeCount - 1;
            if (goal && counts[type] == 0)
            {
                continue;
            }

            if (type == MissionCatalog.CheckRingType)
            {
                if (counts[type] > 0)
                {
                    RingTypes.Children.Add(new TextBlock
                    {
                        Text = L.F("Type 3 ({0}×): invisible check rings, no points", counts[type]),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 18, 0),
                        Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                    });
                }

                continue;
            }

            int ringType = type;
            string label = goal
                ? L.F("Goal ring ({0}×):", counts[type])
                : counts[type] > 0 ? L.F("Type {0} ({1}×):", type + 1, counts[type]) : L.F("Type {0} (not used):", type + 1);
            RingTypes.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            int current = draft.Comm.GetRingPoints(type);
            var box = new TextBox { Text = current.ToString(CultureInfo.CurrentCulture), Width = 50, Margin = new Thickness(0, 0, 18, 0), Tag = $"ring{type}" };
            void Commit()
            {
                if (_showing)
                {
                    return;
                }

                if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value is < 0 or > 126)
                {
                    box.Text = current.ToString(CultureInfo.CurrentCulture);
                    Ui.ShowError(Window.GetWindow(this)!, L.T("Points are whole numbers from 0 to 126 (the game stores at most 126 per test)."));
                    return;
                }

                if (value != current)
                {
                    draft.SetRingPoints(ringType, value);
                    Changed(draft);
                }
            }

            box.LostKeyboardFocus += (_, e) =>
            {
                _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
                Commit();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
            RingTypes.Children.Add(box);
        }
    }

    // ----------------------------------------------------------------- time, conditions, footnote

    private void ShowTime(MissionDraft draft)
    {
        // Cannonball: no time points, no "Land now" (a shot is over in seconds).
        TimeSection.Visibility = draft.Vehicle == Vehicle.Cannonball ? Visibility.Collapsed : Visibility.Visible;
        MissionComm comm = draft.Comm;
        bool hasTime = draft.Rows.Contains(PointSource.Time);
        ScoreCurve curve = comm.GetCurve(ScoreCurveKind.Time);
        TimeCurveSettings time = MissionCurves.Describe(curve);
        TimeMode mode = TimeModeOf(draft);
        TimePanel.Visibility = hasTime && (time.Shape != TimeCurveShape.None || mode == TimeMode.Custom) ? Visibility.Visible : Visibility.Collapsed;
        TimeNote.Text = !hasTime
            ? L.T("This mission has no time points. Choose \"Time\" as a row to add them.")
            : mode == TimeMode.Custom
                ? L.T("Each point gives the points for a time (in seconds since the start). Between two points the points fall or rise evenly; before the first point the first one counts, after the last point the last one. Two points at the same time make a step.")
                : string.Empty;

        TimeShapeBox.SelectedItem = ((IEnumerable<ChoiceItem<TimeMode>>)TimeShapeBox.ItemsSource).FirstOrDefault(i => i.Value == mode);
        PlateauPanel.Visibility = mode == TimeMode.Plateau ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = mode == TimeMode.Target ? Visibility.Visible : Visibility.Collapsed;
        FullUntilBox.Text = FormatNumber(time.FullUntil);
        ZeroAtBox.Text = FormatNumber(time.ZeroAt);
        TargetTimeBox.Text = FormatNumber(time.TargetTime);
        WindowBox.Text = FormatNumber(time.Window);
        ZeroDistanceBox.Text = FormatNumber(time.ZeroDistance);

        TimeCurvePanel.Children.Clear();
        if (mode == TimeMode.Custom && hasTime)
        {
            TimeCurvePanel.Children.Add(CurveEditor(draft, ScoreCurveKind.Time, L.T("Time (seconds)"), 30, "time"));
            TimeCurveText.Text = string.Empty;
        }
        else
        {
            TimeCurveText.Text = time.Shape switch
            {
                TimeCurveShape.Plateau => L.F("Full points up to {0}, no points from {1} on.", FormatTime(time.FullUntil), FormatTime(time.ZeroAt)),
                TimeCurveShape.Target => L.F("Full points from {0} to {1}.", FormatTime(time.TargetTime - time.Window), FormatTime(time.TargetTime + time.Window)),
                _ => string.Empty,
            } + " " + L.F("Curve: {0}.", string.Join(" · ", curve.Points.Select(p => L.F("{0} → {1}", FormatTime(p.X), FormatNumber(p.Y)))));
        }

        LandNowBox.Text = FormatNumber(comm.LandNowTime);
        HardEndBox.Text = FormatNumber(comm.HardEndTime);
        HardEndBox.IsEnabled = draft.CanSetHardEnd;
        HardEndModePanel.Visibility = comm.HardEndTime > 0 ? Visibility.Visible : Visibility.Collapsed;
        HardEndScoredBox.IsChecked = !draft.Settings.HardEndZero;
        HardEndZeroBox.IsChecked = draft.Settings.HardEndZero;
        HardEndJingleOkBox.IsChecked = draft.HardEndJingle == HardEndJingle.LandingOk;
        HardEndJingleMissBox.IsChecked = draft.HardEndJingle == HardEndJingle.LandingMissed;
        HardEndJingleNoneBox.IsChecked = draft.HardEndJingle == HardEndJingle.None;
        HardEndJingleNote.Text = draft.Vehicle switch
        {
            Vehicle.JumbleHopper => L.T("The Jumble Hopper plays its goal jingle for a successful landing and the Rocket Belt's for a wrong one (it has none of its own)."),
            Vehicle.Birdman => L.T("Birdman plays its crash jingle for a wrong landing (it has no other)."),
            _ => string.Empty,
        };
        HardEndJingleNote.Visibility = HardEndJingleNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>How the time curve is shown: point by point if chosen so or if it has no simple shape.</summary>
    private TimeMode TimeModeOf(MissionDraft draft)
    {
        TimeCurveSettings time = MissionCurves.Describe(draft.Comm.GetCurve(ScoreCurveKind.Time));
        if (_customTime.Contains(draft.Mission.Id) || !time.IsExact)
        {
            return TimeMode.Custom;
        }

        return time.Shape == TimeCurveShape.Target ? TimeMode.Target : TimeMode.Plateau;
    }

    private void ShowAltitude(MissionDraft draft)
    {
        AltitudeCurvePanel.Children.Clear();
        bool hasAltitude = draft.Rows.Contains(PointSource.Altitude);
        AltitudePanel.Visibility = hasAltitude ? Visibility.Visible : Visibility.Collapsed;
        if (hasAltitude)
        {
            AltitudeCurvePanel.Children.Add(CurveEditor(draft, ScoreCurveKind.Altitude, L.T("Altitude (m)"), 100, "altitude", GameUnits.MetersPerUnit));
        }
    }

    /// <summary>The distance curve: distance flown in meters as the game counts them.</summary>
    private void ShowDistance(MissionDraft draft)
    {
        DistanceCurvePanel.Children.Clear();
        bool shown = draft.Rows.Contains(PointSource.Distance);
        DistancePanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown)
        {
            DistanceCurvePanel.Children.Add(CurveEditor(draft, ScoreCurveKind.Distance, L.T("Distance (m)"), 500, "distance", GameUnits.MetersPerUnit));
        }
    }

    /// <summary>The landing accuracy curve: distance in meters as the game counts them.</summary>
    private void ShowLanding(MissionDraft draft)
    {
        LandingCurvePanel.Children.Clear();
        bool cannonball = draft.Vehicle == Vehicle.Cannonball;
        bool shown = cannonball || draft.Rows.Contains(PointSource.LandingAccuracy);
        LandingPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            return;
        }

        bool gyrocopter = draft.Vehicle == Vehicle.Gyrocopter;
        LandingTitle.Text = cannonball ? L.T("Target accuracy") : L.T("Landing accuracy");
        bool separate = gyrocopter && draft.TouchdownCurve is not null;
        LandingNote.Text = (cannonball
                ? L.T("The distance of the cannonball's landing point from the centre of the target gives the points of this curve.")
                : gyrocopter
                    ? L.T("The Gyrocopter's landing is measured at two points: the touchdown point T (first wheel contact) and the resting point R (where it stops). Both are used for the points. Both distances (dT, dR) are measured sideways from the centre line of the runway. For a good landing, T and R must both be on the runway, as close to the centre line as possible.")
                        + " " + (separate ? L.T("Full points need 100 % at both distances.") : L.T("Both distances use the same curve."))
                    : L.T("The distance of the resting position from the centre of the nearest landing pad (or from the centre line of a runway) gives the points of this curve. A good landing needs the resting position on the pad (within 21 m of its centre)."))
            + (gyrocopter ? string.Empty
                : " " + L.T("Distances are in meters as the game counts them. Between two points the points fall or rise evenly; beyond the last point the last one counts. Two points at the same distance make a step."));
        SeparateTouchdownBox.Visibility = gyrocopter ? Visibility.Visible : Visibility.Collapsed;
        SeparateTouchdownBox.IsChecked = draft.TouchdownCurve is not null;
        // The Gyrocopter: the curve editors on the left, a drawing of T, R, dT and dR on the right.
        Panel editors = LandingCurvePanel;
        if (gyrocopter)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            UIElement drawing = GyrocopterLandingDrawing();
            Grid.SetColumn(drawing, 1);
            grid.Children.Add(left);
            grid.Children.Add(drawing);
            LandingCurvePanel.Children.Add(grid);
            editors = left;
        }

        if (separate && draft.TouchdownCurve is { } touchdown)
        {
            // Two curves in percent of their highest value: the points of the row are reached with 100 % at both.
            ScoreCurve rest = draft.Comm.GetCurve(ScoreCurveKind.LandingAccuracy);
            editors.Children.Add(new TextBlock { Text = L.T("Touchdown point T (first wheel contact)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            editors.Children.Add(CurveEditor(draft, ScoreCurveKind.LandingAccuracy, L.T("Distance dT (m)"), 5, "touchdown",
                GameUnits.MetersPerUnit, custom: (Percent(touchdown), "%", points => draft.SetLandingPercentCurve(true, points))));
            editors.Children.Add(new TextBlock { Text = L.T("Resting point R"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
            editors.Children.Add(CurveEditor(draft, ScoreCurveKind.LandingAccuracy, L.T("Distance dR (m)"), 5, "landing",
                GameUnits.MetersPerUnit, custom: (Percent(rest), "%", points => draft.SetLandingPercentCurve(false, points))));
            return;
        }

        editors.Children.Add(CurveEditor(draft, ScoreCurveKind.LandingAccuracy, L.T("Distance (m)"), 5, "landing",
            GameUnits.MetersPerUnit, squaredPoints: gyrocopter));
    }

    /// <summary>The landing impact curve: the measured speed in km/h like the HUD's speedometer.</summary>
    private void ShowImpact(MissionDraft draft)
    {
        ImpactCurvePanel.Children.Clear();
        bool shown = draft.Rows.Contains(PointSource.LandingImpact);
        ImpactPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            return;
        }

        string measured = draft.Vehicle switch
        {
            Vehicle.HangGlider => L.T("The impact is the Hang Glider's vertical speed at the touchdown."),
            Vehicle.RocketBelt => L.T("The impact is the Rocket Belt's speed into the ground at the touchdown."),
            Vehicle.Gyrocopter => L.T("The impact is the highest speed of a wheel into the ground at the first touchdown."),
            Vehicle.SkyDiving => L.T("The impact is the vertical speed at the touchdown."),
            Vehicle.Birdman => L.T("The impact is the Birdman's speed at the touchdown as the HUD shows it (vertical, forward and sideways together)."),
            _ => string.Empty,
        };
        ImpactNote.Text = (measured + " " + L.T("Speeds are in km/h like the speedometer of the HUD. Between two points the points fall or rise evenly; beyond the last point the last one counts. Two points at the same speed make a step.")).Trim();
        ImpactCurvePanel.Children.Add(CurveEditor(draft, ScoreCurveKind.Impact, L.T("Speed (km/h)"), 5, "impact",
            LandingValues.ImpactShownPerStored(draft.Vehicle)));
    }

    /// <summary>
    /// The runway from above with the touchdown point T, the resting point R and their distances dT and dR
    /// from the centre line (the letters of the note of the Gyrocopter's landing accuracy).
    /// </summary>
    private static UIElement GyrocopterLandingDrawing()
    {
        static System.Windows.Media.Brush Rgb(byte r, byte g, byte b) => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        System.Windows.Media.Brush blue = Rgb(0x2E, 0x6D, 0xA4), red = Rgb(0xC0, 0x39, 0x2B), dark = Rgb(0x1D, 0x2A, 0x36);
        var canvas = new Canvas { Width = 280, Height = 140, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };

        void Place(UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            canvas.Children.Add(element);
        }

        void Line(double x1, double y1, double x2, double y2, System.Windows.Media.Brush brush, double thickness, double[]? dashes = null)
        {
            var line = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness };
            if (dashes is not null)
            {
                line.StrokeDashArray = new System.Windows.Media.DoubleCollection(dashes);
            }

            canvas.Children.Add(line);
        }

        void Arrow(params double[] xy)
        {
            var head = new System.Windows.Shapes.Polygon { Fill = blue };
            for (int i = 0; i < xy.Length; i += 2)
            {
                head.Points.Add(new Point(xy[i], xy[i + 1]));
            }

            canvas.Children.Add(head);
        }

        void Label(string text, double x, double y, System.Windows.Media.Brush brush, double size) =>
            Place(new TextBlock { Text = text, Foreground = brush, FontSize = size, FontWeight = FontWeights.Bold }, x, y);

        void Dimension(double x, double y1, double y2, string text, double labelY)
        {
            Line(x, y1, x, y2, red, 1.5);
            Line(x - 4, y1, x + 4, y1, red, 1.5);
            Line(x - 4, y2, x + 4, y2, red, 1.5);
            Label(text, x + 5, labelY, red, 11);
        }

        // The runway and its centre line.
        Place(new System.Windows.Shapes.Rectangle { Width = 240, Height = 70, RadiusX = 3, RadiusY = 3, Fill = Rgb(0xB9, 0xC0, 0xC7), Stroke = Rgb(0x7D, 0x87, 0x90), StrokeThickness = 1 }, 20, 35);
        Line(28, 70, 252, 70, System.Windows.Media.Brushes.White, 2.5, [6, 4]);
        Place(new TextBlock { Text = "centre line", FontSize = 10, Foreground = Rgb(0x4A, 0x53, 0x5C) }, 196, 51);

        // The approach, then rolling from T to R.
        Line(2, 52, 62, 52, blue, 1.5, [2, 2]);
        Arrow(62, 48, 70, 52, 62, 56);
        var roll = new System.Windows.Shapes.Polyline { Stroke = blue, StrokeThickness = 1.5, StrokeDashArray = new System.Windows.Media.DoubleCollection([3, 2]) };
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0; // quadratic curve from T (86, 52) over (140, 52) to (186, 84)
            roll.Points.Add(new Point(((1 - t) * (1 - t) * 86) + (2 * (1 - t) * t * 140) + (t * t * 186), ((1 - t) * (1 - t) * 52) + (2 * (1 - t) * t * 52) + (t * t * 84)));
        }

        canvas.Children.Add(roll);
        Arrow(183, 78, 191, 88, 180, 86);

        // T and R with their distances from the centre line.
        Place(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = blue }, 81, 47);
        Label("T", 81, 28, dark, 13);
        Dimension(98, 52, 70, "dT", 53);
        Place(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = blue }, 191, 85);
        Label("R", 191, 106, dark, 13);
        Dimension(208, 70, 90, "dR", 73);
        return canvas;
    }

    /// <summary>A curve's points in percent of its highest value.</summary>
    private static List<CurvePoint> Percent(ScoreCurve curve)
    {
        float max = curve.Maximum;
        return curve.Points.Select(p => new CurvePoint(p.X, max > 0 ? MathF.Round(p.Y / max * 1000) / 10 : 0)).ToList();
    }

    /// <summary>The Gyrocopter's own curve for the touchdown point was switched on or off.</summary>
    private void OnSeparateTouchdownChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        draft.SetSeparateTouchdown(SeparateTouchdownBox.IsChecked == true);
        Changed(draft);
    }

    // ----------------------------------------------------------------- good landing

    /// <summary>The pad radius, the widths of the runways and the speed limits of the vehicle.</summary>
    private void ShowGoodLanding(MissionDraft draft)
    {
        GoodLandingGrid.Children.Clear();
        GoodLandingGrid.RowDefinitions.Clear();
        IReadOnlyList<LandingValue> values = draft.LandingValuesShown;
        IReadOnlyList<float> runways = LandingValues.UsesPads(draft.Vehicle) ? draft.RunwayWidths : [];
        GoodLandingPanel.Visibility = values.Count > 0 || runways.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        void AddRow(string name, string tag, float shown, string unit, float original, Action<float> commit, float maximum)
        {
            int row = GoodLandingGrid.RowDefinitions.Count;
            GoodLandingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 10, 2), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
            string text = FormatMeters(shown);
            var box = new TextBox { Text = text, Width = 60, Margin = new Thickness(0, 2, 6, 2), Tag = tag };
            var unitText = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 10, 2) };
            var originalText = new TextBlock
            {
                Text = L.F("(original: {0} {1})", FormatMeters(original), unit),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            };

            void Commit()
            {
                if (_showing || box.Text.Trim() == text)
                {
                    return;
                }

                if (!TryParseNumber(box.Text, out double value) || value < 0 || value > maximum)
                {
                    Ui.ShowError(Window.GetWindow(this)!, L.F("\"{0}\" is not a number from 0 to {1}.", box.Text.Trim(), FormatMeters(maximum)));
                    ShowMission(draft.Mission);
                    return;
                }

                commit((float)value);
                Changed(draft);
            }

            box.LostKeyboardFocus += (_, e) =>
            {
                _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
                Commit();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };

            UIElement[] cells = [label, box, unitText, originalText];
            for (int c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], row);
                Grid.SetColumn(cells[c], c);
                GoodLandingGrid.Children.Add(cells[c]);
            }
        }

        foreach (LandingValue value in values)
        {
            AddRow(LandingValues.Name(value), $"landing{(int)value}", LandingValues.Shown(value, draft.LandingValueOf(value)), LandingValues.Unit(value),
                LandingValues.Shown(value, LandingValues.Original(value)),
                shown => draft.SetLandingValue(value, LandingValues.Stored(value, shown)), LandingValues.MaximumShown(value));
        }

        IReadOnlyList<float> fileWidths = draft.Mission.File.RunwayWidths;
        for (int i = 0; i < runways.Count; i++)
        {
            int index = i;
            AddRow(runways.Count == 1 ? L.T("Width of the runway for a good landing") : L.F("Width of runway {0} for a good landing", i + 1),
                $"runway{i}", GameUnits.ToMeters(runways[i]), "m", GameUnits.ToMeters(fileWidths[i]),
                shown => draft.SetRunwayWidth(index, GameUnits.FromMeters(shown)), 10000);
        }
    }

    // ----------------------------------------------------------------- curve points

    /// <summary>
    /// A table with every point of a score curve (measured value and points), buttons to add and
    /// remove points, and a small drawing of the curve.
    /// </summary>
    /// <param name="draft">The mission.</param>
    /// <param name="kind">The curve.</param>
    /// <param name="valueHeader">Header of the measured value (time, altitude).</param>
    /// <param name="step">Distance of a new point behind the last one.</param>
    /// <param name="tagPrefix">Prefix of the boxes' tags (to keep the focus after rebuilding).</param>
    /// <param name="xScale">Shown measured value per stored one (heights and distances: meters per game unit).</param>
    /// <param name="squaredPoints">The points shown are the square of the stored ones (Gyrocopter landing:
    /// the game multiplies two values of the same curve).</param>
    /// <param name="custom">Other points than the curve's (for example in percent), the header of their column and how they are stored.</param>
    private UIElement CurveEditor(MissionDraft draft, ScoreCurveKind kind, string valueHeader, float step, string tagPrefix, float xScale = 1,
        bool squaredPoints = false, (List<CurvePoint> Points, string Header, Func<List<CurvePoint>, string?> Apply)? custom = null)
    {
        List<CurvePoint> points = custom is { } own ? own.Points : [.. draft.Comm.GetCurve(kind).Points];
        string ShowX(float x) => xScale == 1 ? FormatNumber(x) : FormatMeters(x * xScale);
        string ShowY(float y) => squaredPoints ? FormatMeters(y * y) : FormatNumber(y);
        var panel = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void Add(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headers = [L.T("Point"), valueHeader, custom?.Header ?? L.T("Points"), string.Empty];
        for (int c = 0; c < headers.Length; c++)
        {
            Add(new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 14, 4) }, 0, c);
        }

        var valueBoxes = new List<TextBox>();
        var pointBoxes = new List<TextBox>();

        // Takes all boxes; on a mistake the table shows the curve as it was.
        void Commit()
        {
            if (_showing)
            {
                return;
            }

            var typed = new List<CurvePoint>();
            for (int i = 0; i < valueBoxes.Count; i++)
            {
                if (!TryParseNumber(valueBoxes[i].Text, out double x) || !TryParseNumber(pointBoxes[i].Text, out double y))
                {
                    Ui.ShowError(Window.GetWindow(this)!, L.F("Point {0}: please enter numbers.", i + 1));
                    ShowMission(draft.Mission);
                    return;
                }

                if (squaredPoints && y > MissionCurves.MaxCurvePoints)
                {
                    Ui.ShowError(Window.GetWindow(this)!, L.T("Points are whole numbers from 0 to 126 (the game stores at most 126 per test)."));
                    ShowMission(draft.Mission);
                    return;
                }

                // An unchanged box keeps the stored value exactly (shown values are rounded).
                float storedX = valueBoxes[i].Text.Trim() == ShowX(points[i].X) ? points[i].X : (float)(x / xScale);
                float storedY = pointBoxes[i].Text.Trim() == ShowY(points[i].Y) ? points[i].Y
                    : squaredPoints ? MathF.Sqrt((float)Math.Max(0, y)) : (float)y;
                typed.Add(new CurvePoint(storedX, storedY));
            }

            if (!typed.SequenceEqual(points))
            {
                Apply(typed);
            }
        }

        void Apply(List<CurvePoint> changed)
        {
            if ((custom is { } c ? c.Apply(changed) : draft.SetCurvePoints(kind, changed)) is { } problem)
            {
                Ui.ShowError(Window.GetWindow(this)!, problem);
                ShowMission(draft.Mission);
                return;
            }

            Changed(draft);
        }

        void Hook(TextBox box)
        {
            box.LostKeyboardFocus += (_, e) =>
            {
                _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
                Commit();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
        }

        for (int i = 0; i < points.Count; i++)
        {
            int row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 14, 2) }, row, 0);

            var valueBox = new TextBox { Text = ShowX(points[i].X), Width = 80, Margin = new Thickness(0, 2, 14, 2), Tag = $"{tagPrefix}x{i}" };
            var pointBox = new TextBox { Text = ShowY(points[i].Y), Width = 60, Margin = new Thickness(0, 2, 14, 2), Tag = $"{tagPrefix}y{i}" };
            Hook(valueBox);
            Hook(pointBox);
            valueBoxes.Add(valueBox);
            pointBoxes.Add(pointBox);
            Add(valueBox, row, 1);
            Add(pointBox, row, 2);

            int at = i;
            var remove = new Button
            {
                Content = "✕",
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 2, 0, 2),
                IsEnabled = points.Count > 1,
                ToolTip = L.T("Remove this point"),
            };
            remove.Click += (_, _) => Apply(points.Where((_, j) => j != at).ToList());
            Add(remove, row, 3);
        }

        panel.Children.Add(grid);

        var addButton = new Button
        {
            Content = L.T("Add point"),
            Style = (Style)FindResource("DialogButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            IsEnabled = points.Count < ScoreCurve.MaxPoints,
            ToolTip = L.F("A curve has at most {0} points.", ScoreCurve.MaxPoints),
        };
        addButton.Click += (_, _) =>
        {
            CurvePoint last = points.Count > 0 ? points[^1] : new CurvePoint(0, 0);
            Apply([.. points, new CurvePoint(last.X + (step / xScale), last.Y)]);
        };
        panel.Children.Add(addButton);
        panel.Children.Add(CurveDrawing(points.Select(p => new CurvePoint(p.X * xScale, squaredPoints ? p.Y * p.Y : p.Y)).ToList()));
        return panel;
    }

    /// <summary>A small drawing of a curve: measured value to the right, points upwards.</summary>
    private UIElement CurveDrawing(IReadOnlyList<CurvePoint> points)
    {
        const double width = 360;
        const double height = 90;
        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        canvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Stroke = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            StrokeThickness = 0.5,
        });
        if (points.Count == 0)
        {
            return canvas;
        }

        // A bit of room behind the last point shows that its points stay.
        float maxX = Math.Max(1, points[^1].X * 1.15f);
        float maxY = Math.Max(1, points.Max(p => p.Y));
        var line = new System.Windows.Shapes.Polyline
        {
            Stroke = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            StrokeThickness = 2,
        };
        double X(float x) => x / maxX * width;
        double Y(float y) => height - 4 - (y / maxY * (height - 8));
        line.Points.Add(new Point(0, Y(points[0].Y)));
        foreach (CurvePoint p in points)
        {
            line.Points.Add(new Point(X(p.X), Y(p.Y)));
        }

        line.Points.Add(new Point(width, Y(points[^1].Y)));
        canvas.Children.Add(line);
        canvas.ToolTip = L.F("Highest points: {0}", FormatNumber(maxY));
        return canvas;
    }

    /// <summary>The controls of one object type in the conditions (see <see cref="ShowConditions"/>).</summary>
    private sealed record ObjectConditionRow(ConditionObject Type, int Total, CheckBox Check, ComboBox Kind, TextBox Count);

    private readonly List<ObjectConditionRow> _objectRows = [];

    private void ShowConditions(MissionDraft draft)
    {
        MissionComm comm = draft.Comm;
        MissionConditions conditions = draft.Conditions;
        bool cannonball = draft.Vehicle == Vehicle.Cannonball;

        // Cannonball: a shot always ends in the water or on a target, and there are no time points.
        ConditionsSection.Visibility = cannonball || !draft.HasScoreSheet ? Visibility.Collapsed : Visibility.Visible;
        CrashOriginalBox.IsChecked = draft.CrashScoring == CrashScoring.Original;
        CrashTimeBox.IsChecked = draft.CrashScoring == CrashScoring.TimePoints;
        CrashZeroBox.IsChecked = draft.CrashScoring == CrashScoring.Zero;
        CrashPanel.Visibility = draft.CanSetCrashCondition ? Visibility.Visible : Visibility.Collapsed;
        CrashTimeNote.Text = draft.CrashScoring != CrashScoring.TimePoints
            ? string.Empty
            : !draft.Rows.Contains(PointSource.Time)
                ? L.T("This mission has no time points, so this changes nothing yet.")
                : draft.EarlyCrashTimePoints > 0
                    ? L.F("Note: a crash right after the start gives {0} time points (the time curve at 0 seconds). Combine this with conditions, or use a time curve that rises with the time.", draft.EarlyCrashTimePoints)
                    : L.T("The conditions of the time points below apply after a crash too.");
        TimeConditionsNote.Text = draft.Rows.Contains(PointSource.Time)
            ? L.T("All checked conditions must be met; otherwise the time points are 0.")
            : L.T("This mission has no time points: these conditions only matter once \"Time\" is one of its rows.");

        ObjectConditionsGrid.Children.Clear();
        ObjectConditionsGrid.RowDefinitions.Clear();
        _objectRows.Clear();
        foreach (ConditionObject type in Enum.GetValues<ConditionObject>())
        {
            int total = MissionCatalog.ConditionTotal(draft.Mission.File, type);
            if (total > 0 || conditions.Required.ContainsKey(type))
            {
                AddObjectCondition(type, total, conditions);
            }
        }

        if (_objectRows.Count == 0)
        {
            ObjectConditionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ObjectConditionsGrid.Children.Add(new TextBlock
            {
                Text = L.T("The mission has no objects that can be counted (rings, balloons, targets, floating pads, Meca Hawks, ball targets, goals)."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            });
        }

        LandWhenReachedBox.Visibility = conditions.CanLandWhenReached(comm) ? Visibility.Visible : Visibility.Collapsed;
        LandWhenReachedBox.IsChecked = conditions.LandWhenReached;
        AltitudeConditionBox.IsChecked = conditions.MinimumAltitude is not null;
        // Shown in the game's meters, stored in game units.
        AltitudeConditionValue.Text = conditions.MinimumAltitude is { } minimum
            ? FormatMeters(GameUnits.ToMeters(minimum))
            : FormatMeters(comm.MinimumAltitude > 0 ? GameUnits.ToMeters(comm.MinimumAltitude) : 400);
        AltitudeConditionValue.IsEnabled = conditions.MinimumAltitude is not null;
        DistanceConditionBox.IsChecked = conditions.MinimumDistance is not null;
        DistanceConditionValue.Text = FormatMeters(conditions.MinimumDistance is { } distance ? GameUnits.ToMeters(distance) : 1000);
        DistanceConditionValue.IsEnabled = conditions.MinimumDistance is not null;

        // The highest altitude counts from the start: a value below the takeoff height is always reached.
        float? start = MissionCatalog.StartAltitude(draft.Mission.File);
        StartAltitudeNote.Text = start is { } height
            ? L.F("The flight starts {0} m above sea level (the HUD's \"SEA LEVEL\"), so the highest altitude reached is always at least that much.",
                FormatMeters(MathF.Round(GameUnits.ToMeters(height))))
                + (conditions.MinimumAltitude is { } needed && needed <= height ? " " + L.T("This condition is therefore always met.") : string.Empty)
            : string.Empty;
        ConditionsHowNote.Text = conditions.IsEmpty
            ? string.Empty
            : conditions.NeedsModule(comm)
                ? L.T("The original game can only check one of these conditions at a time. This combination is checked by the editor's game code, which is built into the ROM for it.")
                : L.T("The original game checks these conditions itself.");

        bool birdman = !draft.HasScoreSheet; // a Birdman free flight
        HitPanel.Visibility = draft.Vehicle is Vehicle.Gyrocopter or Vehicle.RocketBelt || comm.HitDeduction != 0 ? Visibility.Visible : Visibility.Collapsed;
        HitCostBox.Text = (-comm.HitDeduction).ToString(CultureInfo.CurrentCulture);
        OutOfAreaPanel.Visibility = draft.Vehicle == Vehicle.JumbleHopper ? Visibility.Visible : Visibility.Collapsed;
        OutOfAreaBox.Text = FormatNumber(comm.OutOfAreaDeduction);
        ReactionPanel.Visibility = birdman || cannonball ? Visibility.Collapsed : Visibility.Visible;
        ReactionBox.Text = comm.ReactionThreshold.ToString(CultureInfo.CurrentCulture);
        ConditionsExpander.Visibility = new UIElement[] { ConditionsSection, HitPanel, OutOfAreaPanel, ReactionPanel }
            .Any(e => e.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;

        // Footnote: written from the settings, or typed in.
        bool automatic = draft.Settings.AutoFootnote ?? !draft.Settings.GenerateTexts;
        AutoFootnoteBox.IsChecked = automatic;
        FootnoteBox.Text = string.Join("\n", draft.Settings.GenerateTexts
            ? draft.Footnote(OtherComm)
            : automatic
                ? MissionTextGenerator.SuggestFootnote(comm, draft.TextLines(OtherComm), conditions, draft.Settings.HardEndZero)
                : draft.CurrentScreenText is { } screen ? MissionTextGenerator.ExtractFootnote(screen) : []);
        FootnoteBox.IsReadOnly = automatic;
        FootnoteBox.IsEnabled = !birdman && ScreenName(draft.Mission) is not null;
        AutoFootnoteBox.IsEnabled = FootnoteBox.IsEnabled;
    }

    /// <summary>One line of the object conditions: "[x] Rings   [at least ▾] [5] of 15 rings".</summary>
    private void AddObjectCondition(ConditionObject type, int total, MissionConditions conditions)
    {
        int gridRow = ObjectConditionsGrid.RowDefinitions.Count;
        ObjectConditionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool required = conditions.Required.TryGetValue(type, out int count);
        string name = MissionConditions.Name(type);

        var check = new CheckBox
        {
            Content = char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..],
            IsChecked = required,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 10, 2),
        };
        var kind = new ComboBox
        {
            ItemsSource = new[] { L.F("all {0}", total), L.T("at least") },
            SelectedIndex = required && count != MissionConditions.All ? 1 : 0,
            Width = 110,
            IsEnabled = required,
            Margin = new Thickness(0, 2, 6, 2),
        };
        var number = new TextBox
        {
            Text = (required && count != MissionConditions.All ? count : Math.Max(1, total)).ToString(CultureInfo.CurrentCulture),
            Width = 50,
            IsEnabled = required && count != MissionConditions.All,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 6, 2),
            Tag = $"condition{(int)type}",
        };
        var label = new TextBlock
        {
            Text = L.F("of {0} {1}", total, name),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
        };

        check.Click += (_, _) => OnConditionsChanged(check, new RoutedEventArgs());
        kind.SelectionChanged += (_, _) =>
        {
            if (!_showing)
            {
                // The rows are rebuilt afterwards; do that after this list's event.
                Dispatcher.BeginInvoke(new Action(() => OnConditionsChanged(kind, new RoutedEventArgs())));
            }
        };
        number.LostKeyboardFocus += (_, e) =>
        {
            _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
            OnConditionsChanged(number, new RoutedEventArgs());
        };
        number.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnConditionsChanged(number, new RoutedEventArgs());
            }
        };

        UIElement[] cells = [check, kind, number, label];
        for (int c = 0; c < cells.Length; c++)
        {
            Grid.SetRow(cells[c], gridRow);
            Grid.SetColumn(cells[c], c);
            ObjectConditionsGrid.Children.Add(cells[c]);
        }

        _objectRows.Add(new ObjectConditionRow(type, total, check, kind, number));
    }

    /// <summary>A condition was switched or changed: read all of them and store them.</summary>
    private void OnUnlimitedFuelChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        draft.SetUnlimitedFuel(UnlimitedFuelBox.IsChecked == true);
        Changed(draft);
    }

    /// <summary>What the hard end time gives: the flight's points or 0.</summary>
    private void OnHardEndModeChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        bool zero = HardEndZeroBox.IsChecked == true;
        if (zero != draft.Settings.HardEndZero)
        {
            draft.SetHardEndZero(zero);
            Changed(draft);
        }
    }

    /// <summary>The jingle when the hard end time is reached.</summary>
    private void OnHardEndJingleChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        HardEndJingle jingle = HardEndJingleMissBox.IsChecked == true ? HardEndJingle.LandingMissed
            : HardEndJingleNoneBox.IsChecked == true ? HardEndJingle.None
            : HardEndJingle.LandingOk;
        if (jingle != draft.HardEndJingle || draft.Settings.HardEndJingle is null)
        {
            draft.SetHardEndJingle(jingle);
            Changed(draft);
        }
    }

    private void OnBirdmanPointsChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        bool points = BirdmanPointsBox.IsChecked == true;
        draft.SetBirdmanPoints(points);
        if (points && _texts is not null)
        {
            // The mission's own texts (added to the Text tab, saved with the other changes), at first copies of the free flight's.
            foreach ((string? name, string source) in new[] { (ScreenName(draft.Mission), "BD_ALL_S1"), (SheetName(draft.Mission), "BD_ALL_S2") })
            {
                if (name is not null && Row(name) is null)
                {
                    _texts.AddPatchText(name, Row(source)?.Markup ?? string.Empty);
                }
            }
        }

        Changed(draft);
    }

    private void OnHideDemoChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        draft.SetHideDemo(HideDemoBox.IsChecked == true);
        Changed(draft);
    }

    private void OnConditionsChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        var conditions = new MissionConditions
        {
            CrashGivesZero = CrashZeroBox.IsChecked == true,
            CrashTimePoints = CrashTimeBox.IsChecked == true,
            LandWhenReached = LandWhenReachedBox.Visibility == Visibility.Visible && LandWhenReachedBox.IsChecked == true,
        };
        foreach (ObjectConditionRow row in _objectRows.Where(r => r.Check.IsChecked == true))
        {
            if (row.Kind.SelectedIndex == 0)
            {
                conditions.Required[row.Type] = MissionConditions.All;
                continue;
            }

            if (!int.TryParse(row.Count.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int count) || count < 1
                || (row.Total > 0 && count > row.Total))
            {
                Ui.ShowError(Window.GetWindow(this)!, L.F("The number of {0} must be from 1 to {1}.", MissionConditions.Name(row.Type), Math.Max(1, row.Total)));
                ShowMission(draft.Mission);
                return;
            }

            conditions.Required[row.Type] = count;
        }

        if (AltitudeConditionBox.IsChecked == true)
        {
            if (!TryParseNumber(AltitudeConditionValue.Text, out double altitude) || altitude < 0 || altitude > 100000)
            {
                Ui.ShowError(Window.GetWindow(this)!, L.F("\"{0}\" is not a number from 0 on.", AltitudeConditionValue.Text.Trim()));
                ShowMission(draft.Mission);
                return;
            }

            // An unchanged box keeps the stored value exactly (the shown meters are rounded).
            conditions.MinimumAltitude = draft.Conditions.MinimumAltitude is { } current && AltitudeConditionValue.Text.Trim() == FormatMeters(GameUnits.ToMeters(current))
                ? current
                : GameUnits.FromMeters((float)altitude);
        }

        if (DistanceConditionBox.IsChecked == true)
        {
            if (!TryParseNumber(DistanceConditionValue.Text, out double distance) || distance < 0 || distance > 1000000)
            {
                Ui.ShowError(Window.GetWindow(this)!, L.F("\"{0}\" is not a number from 0 on.", DistanceConditionValue.Text.Trim()));
                ShowMission(draft.Mission);
                return;
            }

            // An unchanged box keeps the stored value exactly (the shown meters are rounded).
            conditions.MinimumDistance = draft.Conditions.MinimumDistance is { } current && DistanceConditionValue.Text.Trim() == FormatMeters(GameUnits.ToMeters(current))
                ? current
                : GameUnits.FromMeters((float)distance);
        }

        if (!conditions.SameAs(draft.Conditions))
        {
            draft.SetConditions(conditions);
            Changed(draft);
        }
        else
        {
            ShowMission(SelectedMission);
        }
    }

    private void OnTimeShapeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft || TimeShapeBox.SelectedItem is not ChoiceItem<TimeMode> item)
        {
            return;
        }

        TimeMode old = TimeModeOf(draft);
        if (item.Value == old)
        {
            return;
        }

        if (item.Value == TimeMode.Custom)
        {
            // The curve stays as it is; only the way to edit it changes.
            _customTime.Add(draft.Mission.Id);
            ShowMission(draft.Mission);
            return;
        }

        _customTime.Remove(draft.Mission.Id);
        TimeCurveSettings time = MissionCurves.Describe(draft.Comm.GetCurve(ScoreCurveKind.Time));
        if (time.Points <= 0)
        {
            ShowMission(draft.Mission);
            return;
        }

        // Start the new kind around the old times.
        draft.SetTimeCurve(item.Value == TimeMode.Target
            ? time.Shape == TimeCurveShape.Target
                ? time with { IsExact = true }
                : new TimeCurveSettings(TimeCurveShape.Target, time.Points, TargetTime: Math.Max(30, time.FullUntil), Window: 3, ZeroDistance: 60)
            : time.Shape == TimeCurveShape.Plateau
                ? time with { IsExact = true }
                : new TimeCurveSettings(TimeCurveShape.Plateau, time.Points, FullUntil: Math.Max(0, time.TargetTime - time.Window),
                    ZeroAt: time.TargetTime + time.ZeroDistance));
        Changed(draft);
    }

    /// <summary>A value box lost the focus or got Enter: take its value.</summary>
    private void OnValueCommitted(TextBox box)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        if (!TryParseNumber(box.Text, out double value) || value < 0 || value > 100000)
        {
            Ui.ShowError(Window.GetWindow(this)!, L.F("\"{0}\" is not a number from 0 on.", box.Text.Trim()));
            ShowMission(draft.Mission);
            return;
        }

        float number = (float)value;
        MissionComm comm = draft.Comm;
        TimeCurveSettings time = MissionCurves.Describe(comm.GetCurve(ScoreCurveKind.Time));
        bool changed = true;
        if (box == FullUntilBox || box == ZeroAtBox || box == TargetTimeBox || box == WindowBox || box == ZeroDistanceBox)
        {
            TimeCurveSettings next = box == FullUntilBox ? time with { FullUntil = number }
                : box == ZeroAtBox ? time with { ZeroAt = number }
                : box == TargetTimeBox ? time with { TargetTime = number }
                : box == WindowBox ? time with { Window = number }
                : time with { ZeroDistance = number };
            changed = next != time;
            if (changed)
            {
                draft.SetTimeCurve(next with { IsExact = true });
            }
        }
        else if (box == LandNowBox)
        {
            changed = number != comm.LandNowTime;
            if (changed)
            {
                draft.SetLandNowTime(number);
            }
        }
        else if (box == HardEndBox)
        {
            changed = number != comm.HardEndTime;
            if (changed)
            {
                draft.SetHardEndTime(number);
            }
        }
        else if (box == StartFuelBox)
        {
            if (number is < 0 or > 100)
            {
                Ui.ShowError(Window.GetWindow(this)!, L.T("The fuel is a number from 0 to 100 (%)."));
                ShowMission(draft.Mission);
                return;
            }

            // An unchanged box keeps the stored value exactly (the percent shown is rounded).
            changed = box.Text.Trim() != FormatMeters(draft.StartFuel * 100);
            if (changed)
            {
                draft.SetStartFuel((float)(number / 100));
            }
        }
        else if (box == AltitudeConditionValue || box == DistanceConditionValue)
        {
            OnConditionsChanged(box, new RoutedEventArgs());
            return;
        }
        else if (box == HitCostBox)
        {
            changed = (int)number != -comm.HitDeduction;
            if (changed)
            {
                draft.SetHitCost((int)number);
            }
        }
        else if (box == OutOfAreaBox)
        {
            changed = number != comm.OutOfAreaDeduction;
            if (changed)
            {
                draft.SetOutOfAreaCost(number);
            }
        }
        else if (box == ReactionBox)
        {
            changed = (int)number != comm.ReactionThreshold;
            if (changed)
            {
                draft.SetReactionThreshold((int)number);
            }
        }

        if (changed)
        {
            Changed(draft);
        }
    }

    private void OnFootnoteCommitted(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft || !FootnoteBox.IsEnabled || FootnoteBox.IsReadOnly)
        {
            return;
        }

        string[] lines = FootnoteBox.Text.Replace("\r\n", "\n").Split('\n');

        // Only characters of the game's font (the text could not be saved otherwise).
        if (_texts?.Library is { } library
            && lines.SelectMany(l => l).FirstOrDefault(c => !library.Font.TryGetGlyph(c, false, out _) && !library.Font.TryGetGlyph(c, true, out _)
                && "[]=/".IndexOf(c) < 0) is var bad and not '\0')
        {
            Ui.ShowError(Window.GetWindow(this)!, L.F("'{0}' is not in the game's font.", bad));
            ShowMission(draft.Mission);
            return;
        }

        IReadOnlyList<string> current = draft.Settings.GenerateTexts
            ? draft.Footnote(OtherComm)
            : draft.CurrentScreenText is { } screen ? MissionTextGenerator.ExtractFootnote(screen) : [];
        if (!lines.Select(l => l.Trim()).Where(l => l.Length > 0).SequenceEqual(current.Select(l => l.Trim()).Where(l => l.Length > 0)))
        {
            draft.SetFootnote(lines);
            Changed(draft);
        }
    }

    private void OnAutoFootnoteChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft)
        {
            return;
        }

        draft.SetAutoFootnote(AutoFootnoteBox.IsChecked == true, OtherComm);
        Changed(draft);
    }

    // ----------------------------------------------------------------- results

    private void ShowResults(MissionDraft draft)
    {
        MissionInfo mission = draft.Mission;
        if (!draft.HasScoreSheet)
        {
            ResultRowsText.Text = L.T("Birdman has no result sheet in the original game.");
        }
        else
        {
            ResultRowsText.Text = L.F("The result sheet shows these rows, from top to bottom: {0}.", string.Join(", ", draft.Rows.Select(PointSources.Name)))
                + " " + (MissionTextGenerator.HasDeductionRow(mission.Id.Vehicle)
                    ? L.T("Below them come \"Points Deducted\" and the total.")
                    : L.T("Below them comes the total (this vehicle has no \"Points Deducted\" row)."));
        }

        ShowPointText(ResultTextView, ResultTextNote, SheetName(mission), BirdmanScored(mission) ? [] : mission.SharesResultSheetTextWith,
            L.T("The game has no result sheet text for this mission."), draft);
    }

    private void ShowPointText(TextEditorPanel panel, TextBlock note, string? name, IReadOnlyList<TestId> sharedWith, string missing, MissionDraft draft)
    {
        TextRow? row = Row(name);
        panel.Caption = name is null ? L.T("Text") : L.F("Text ({0})", name);
        panel.Show(row);
        panel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;

        var lines = new List<string>
        {
            draft.Settings.GenerateTexts
                ? L.T("The editor writes this text from the points of the mission.")
                : L.T("The original text. As soon as you change the points, the editor writes this text from them."),
        };
        if (row is null)
        {
            lines.Add(name is null ? missing : L.F("The game has no text {0}.", name));
        }

        if (sharedWith.Count > 0)
        {
            lines.Add(L.F("{0} other mission(s) use the same text: {1}. The text written for this mission is shown for them too.",
                sharedWith.Count, string.Join(", ", sharedWith.Select(MissionCatalog.Label))));
        }

        note.Text = string.Join("\n", lines);
    }

    // ----------------------------------------------------------------- numbers

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>A time like the game's texts: 1'30" (with the seconds as a number when they have a fraction).</summary>
    private static string FormatTime(float seconds)
    {
        int whole = (int)seconds;
        string clock = $"{whole / 60}'{whole % 60:D2}\"";
        return Math.Abs(seconds - whole) < 0.001f ? clock : $"{clock} ({FormatNumber(seconds)} s)";
    }

    private static string FormatNumber(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Meters, to a tenth (converted values are not whole numbers exactly).</summary>
    private static string FormatMeters(float value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
