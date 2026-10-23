using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.Classes;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Classes": what applies to all missions of a class of a vehicle (a level of a bonus game, a stage of the
/// Birdman): the medal limits and the island. Changes are saved in the project right away and take effect with the next
/// build (<see cref="ClassLimits"/>, <see cref="ClassTexts"/>).
/// </summary>
public partial class ClassesTab : UserControl
{
    private EditorSession? _session;

    /// <summary>Set while the controls are filled by code.</summary>
    private bool _showing;

    private string? _focusAfterRefresh;

    public ClassesTab()
    {
        InitializeComponent();
    }

    /// <summary>Raised after a class was saved (the generated texts and the save file tab depend on the limits).</summary>
    public event EventHandler? ClassesChanged;

    /// <summary>Shows the classes of a project.</summary>
    public void Load(EditorSession session)
    {
        _session = session;
        _showing = true;
        int selected = Math.Max(0, VehicleBox.SelectedIndex);
        VehicleBox.Items.Clear();
        foreach (Vehicle vehicle in MissionCatalog.VehicleOrder)
        {
            VehicleBox.Items.Add(new ComboBoxItem { Content = Vehicles.Name(vehicle), Tag = vehicle });
        }

        VehicleBox.SelectedIndex = Math.Min(selected, VehicleBox.Items.Count - 1);
        _showing = false;
        StatusText.Text = string.Empty;
        Show();
    }

    private Vehicle SelectedVehicle => VehicleBox.SelectedItem is ComboBoxItem { Tag: Vehicle vehicle } ? vehicle : Vehicle.HangGlider;

    private void OnVehicleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing)
        {
            StatusText.Text = string.Empty;
            Show();
        }
    }

    /// <summary>Fills the table of the selected vehicle.</summary>
    private void Show()
    {
        if (_session is null)
        {
            return;
        }

        Vehicle vehicle = SelectedVehicle;
        List<ClassInfo> classes;
        try
        {
            classes = [.. _session.LoadClasses().Where(c => c.Vehicle == vehicle)];
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            StatusText.Text = ex.Message;
            return;
        }

        _showing = true;
        ClassGrid.Children.Clear();
        ClassGrid.RowDefinitions.Clear();
        ClassGrid.ColumnDefinitions.Clear();
        for (int c = 0; c < 10; c++)
        {
            ClassGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        void Add(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            ClassGrid.Children.Add(element);
        }

        ClassGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headers = [L.T("Class"), L.T("Missions"), L.T("Island"), L.T("Bronze"), L.T("Silver"), L.T("Gold"), L.T("Perfect"), string.Empty, string.Empty, string.Empty];
        for (int c = 0; c < headers.Length; c++)
        {
            Add(new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 6) }, 0, c);
        }

        int row = 1;
        var scaleNotes = new List<string>();
        foreach (ClassInfo info in classes)
        {
            ClassGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(new TextBlock { Text = MissionCatalog.ClassName(info.Vehicle, info.Class), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 14, 3) }, row, 0);
            Add(new TextBlock { Text = info.TestCount.ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 14, 3) }, row, 1);
            Add(IslandBox(info), row, 2);

            var boxes = new TextBox[3];
            for (int m = 0; m < 3; m++)
            {
                var box = new TextBox
                {
                    Text = info.Medals[m].ToString(CultureInfo.CurrentCulture),
                    Width = 60,
                    Margin = new Thickness(0, 3, 14, 3),
                    Tag = $"medal{info.Class}x{m}",
                    FontWeight = info.HasOwnMedals ? FontWeights.SemiBold : FontWeights.Normal,
                    ToolTip = L.F("Original: {0} points", info.OriginalMedals[m]),
                };
                boxes[m] = box;
                Add(box, row, 3 + m);
            }

            foreach (TextBox box in boxes)
            {
                box.LostKeyboardFocus += (_, e) =>
                {
                    _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
                    CommitMedals(info, boxes);
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        CommitMedals(info, boxes);
                    }
                };
            }

            Add(new TextBlock { Text = info.Perfect.ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 14, 3) }, row, 6);

            var reset = new Button
            {
                Content = L.T("Original"),
                Style = (Style)FindResource("DialogButton"),
                Margin = new Thickness(0, 3, 14, 3),
                IsEnabled = info.HasOwnMedals,
                ToolTip = info.Vehicle == Vehicle.Birdman && info.BirdmanPointMissions > 0
                    ? L.T("Back to the automatic limits of the stage (70/80/90 points per mission with points).")
                    : L.T("Back to the original limits."),
            };
            reset.Click += (_, _) => SaveMedals(info, null);
            Add(reset, row, 7);

            string state = info.HasOwnMedals
                ? L.T("own limits")
                : info.IsAutomatic
                    ? L.F("automatic: {0} mission(s) with points", info.BirdmanPointMissions)
                    : string.Empty;
            Add(new TextBlock { Text = state, Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 14, 3) }, row, 8);

            // The class has another number of missions than its limits were made for (missions added or removed).
            if (info.LimitsNeedScaling)
            {
                IReadOnlyList<int> scaled = info.ScaledMedals();
                var scale = new Button
                {
                    Content = L.T("Scale limits"),
                    Style = (Style)FindResource("DialogButton"),
                    Margin = new Thickness(0, 3, 0, 3),
                    ToolTip = L.F("Converts the current limits from {0} to {1} missions: {2}.", info.LimitsForTests, info.TestCount, string.Join(" / ", scaled)),
                };
                scale.Click += (_, _) => ScaleMedals(info);
                Add(scale, row, 9);
                scaleNotes.Add(L.F("{0}: the limits are made for {1} missions, the class has {2}.",
                    MissionCatalog.ClassName(info.Vehicle, info.Class), info.LimitsForTests, info.TestCount));
            }

            row++;
        }

        ScaleNote.Text = scaleNotes.Count == 0
            ? string.Empty
            : string.Join("\n", scaleNotes) + "\n" + L.T("\"Scale limits\" converts the current limits to the number of missions of the class (e.g. 140/160/180 for 2 missions become 210/240/270 for 3).");
        ScaleNote.Visibility = scaleNotes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        MedalNote.Text = L.T("Points of the whole class (the sum of its missions) for the bronze, silver and gold badge; a perfect score is 100 points per mission. The limits also decide what the menus show and which classes, bonus games and Birdman stages open (with the rules of the original game).")
            + (vehicle == Vehicle.Birdman
                ? " " + L.T("A Birdman stage with missions with points has 70/80/90 points per such mission, unless you enter own limits; stages without points keep 280/320/360 (game patch \"Bonus games and Birdman\").")
                : string.Empty);
        bool anyOwn = classes.Any(c => c.HasOwnMedals);
        TextsNote.Text = vehicle == Vehicle.Birdman
            ? L.T("The badge texts of a Birdman stage with points are written from its limits.")
            : classes.FirstOrDefault()?.OwnTexts == true
                ? L.T("A class with own limits gets its own badge texts for bronze, silver and gold (e.g. HG_A_S3_GOLD), written from the class's text in the Text tab with the new number.")
                : L.T("The code fix for more texts is not applied: the badge texts keep naming the original limits.");
        TextsNote.Visibility = anyOwn || vehicle == Vehicle.Birdman ? Visibility.Visible : Visibility.Collapsed;
        IslandNote.Text = L.T("The island is where every mission of the class is flown and what the test screen shows. The objects of the missions stay where they are.");
        _showing = false;

        if (_focusAfterRefresh is { } tag)
        {
            _focusAfterRefresh = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (ClassGrid.Children.OfType<TextBox>().FirstOrDefault(b => b.Tag as string == tag) is { } next)
                {
                    next.Focus();
                    next.SelectAll();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private ComboBox IslandBox(ClassInfo info)
    {
        var box = new ComboBox { Width = 170, Margin = new Thickness(0, 3, 14, 3), IsEnabled = info.OriginalIsland is not null && info.TestCount > 0 };
        for (int island = 0; island < MissionCatalog.IslandNames.Count; island++)
        {
            string name = MissionCatalog.IslandName(island);
            box.Items.Add(new ComboBoxItem
            {
                Content = island == info.OriginalIsland ? L.F("{0} (original)", name) : name,
                Tag = island,
            });
        }

        box.SelectedIndex = info.Island ?? -1;
        box.SelectionChanged += (_, _) =>
        {
            if (_showing || _session is null || box.SelectedItem is not ComboBoxItem { Tag: int island })
            {
                return;
            }

            ClassSettings settings = info.Settings?.Clone() ?? ClassSettings.For(info.Vehicle, info.Class);
            settings.Island = island == info.OriginalIsland ? null : island;
            Save(settings);
        };
        return box;
    }

    private void CommitMedals(ClassInfo info, TextBox[] boxes)
    {
        if (_showing || _session is null)
        {
            return;
        }

        var values = new int[3];
        for (int m = 0; m < 3; m++)
        {
            if (!int.TryParse(boxes[m].Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out values[m]))
            {
                StatusText.Text = L.T("Please enter whole numbers.");
                Show();
                return;
            }
        }

        if (values.SequenceEqual(info.Medals))
        {
            return;
        }

        if (ClassLimits.Problem(values, info.Perfect) is { } problem)
        {
            StatusText.Text = problem;
            Show();
            return;
        }

        SaveMedals(info, values.SequenceEqual(info.DefaultMedals) ? null : values);
    }

    private void SaveMedals(ClassInfo info, IReadOnlyList<int>? values)
    {
        ClassSettings settings = info.Settings?.Clone() ?? ClassSettings.For(info.Vehicle, info.Class);
        settings.Medals = values is null ? null : [.. values];

        // Own limits are made for the class's current number of missions (stored only when it is not the game's).
        settings.MedalsForTests = values is null || info.TestCount == MissionPositions.GameTestCount(info.Vehicle, info.Class) ? null : info.TestCount;
        Save(settings);
    }

    private void ScaleMedals(ClassInfo info)
    {
        IReadOnlyList<int> scaled = info.ScaledMedals();
        if (ClassLimits.Problem([.. scaled], info.Perfect) is { } problem)
        {
            StatusText.Text = problem;
            return;
        }

        SaveMedals(info, scaled.SequenceEqual(info.DefaultMedals) && info.TestCount == MissionPositions.GameTestCount(info.Vehicle, info.Class) ? null : scaled);
    }

    private void Save(ClassSettings settings)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.SaveClass(settings);
            StatusText.Text = L.T("Saved. The change takes effect with the next build.");
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            StatusText.Text = ex.Message;
        }

        Show();
        ClassesChanged?.Invoke(this, EventArgs.Empty);
    }
}
