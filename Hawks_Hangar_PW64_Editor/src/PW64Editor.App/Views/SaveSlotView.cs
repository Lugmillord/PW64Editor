using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PW64Editor.App.Services;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.App.Views;

/// <summary>
/// One saved game: the points of the license tests (a table like the game's class selection),
/// the bonus games, and the photo album. Built in code, because the tables follow the layout.
/// </summary>
/// <remarks>
/// Fields of classes and levels that the game has not opened yet are grey and cannot be changed;
/// the "Open" buttons raise the points of the class or level before, as far as the game needs.
/// "–" is a test without a result (stored as 127), which is different from 0 points.
/// </remarks>
public sealed class SaveSlotView : UserControl
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));
    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF4));

    private readonly SaveSlot _slot;
    private readonly SaveProgress _progress;
    private readonly string _name;
    private readonly Action _changed;
    private readonly List<Action> _refreshers = [];
    private readonly StackPanel _root = new();
    private readonly StackPanel _photoList = new();

    public SaveSlotView(SaveSlot slot, string name, Action changed)
    {
        _slot = slot;
        _progress = new SaveProgress(slot);
        _name = name;
        _changed = changed;
        Content = _root;
        Build();
    }

    /// <summary>Builds all controls anew (after the state of the file changed).</summary>
    private void Build()
    {
        _root.Children.Clear();
        _refreshers.Clear();
        _root.Children.Add(BuildStateBar());
        if (_slot.State != SaveSlotState.InUse)
        {
            return;
        }

        _root.Children.Add(Heading(L.T("License tests")));
        _root.Children.Add(BuildLicenseTable());
        _root.Children.Add(Heading(L.T("Bonus games")));
        _root.Children.Add(BuildBonusTable());
        _root.Children.Add(Heading(L.T("Photo album")));
        _root.Children.Add(_photoList);
        _root.Children.Add(Note(L.T(
            "Points: 0-100 per test (Cannonball: per level; the game stores the points of the four targets, " +
            "the editor spreads them, 25 each). \"–\" means no result yet, which is not the same as 0 points. " +
            "Grey fields belong to classes and levels the game has not opened yet.")));
        Refresh();
    }

    /// <summary>Shows the current values in all controls.</summary>
    private void Refresh()
    {
        foreach (Action refresh in _refreshers)
        {
            refresh();
        }

        BuildPhotoList();
    }

    /// <summary>Called after every change.</summary>
    private void OnChanged()
    {
        Refresh();
        _changed();
    }

    // ----- State of the file -----

    private UIElement BuildStateBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        string text = _slot.State switch
        {
            SaveSlotState.InUse => L.F("{0} holds a saved game.", _name),
            SaveSlotState.Empty => L.F("{0} is empty: the game shows it as a new file.", _name),
            _ => L.F("{0} is not prepared; the game prepares it as an empty file when it starts.", _name),
        };
        bar.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        if (_slot.State == SaveSlotState.InUse)
        {
            bar.Children.Add(MakeButton(L.T("Set perfect score"),
                L.T("Gives every test 100 points (Cannonball: 100 per level, 25 per target). This opens all classes, " +
                "bonus games and Birdman stages."), () =>
                {
                    _progress.SetPerfectScore();
                    OnChanged();
                }));
            bar.Children.Add(MakeButton(L.T("Erase file"), L.T("Erases the saved game, like \"Erase\" in the game's file menu."), () =>
            {
                if (Ui.Confirm(Window.GetWindow(this)!, L.F("Erase {0}? All points and photos of this file are lost (when you save).", _name)))
                {
                    _slot.Erase();
                    Build();
                    _changed();
                }
            }));
        }
        else
        {
            bar.Children.Add(MakeButton(L.T("New saved game"), L.T("Makes the file a saved game without any results, as after the first save in the game."), () =>
            {
                _slot.StartNew();
                Build();
                _changed();
            }));
        }

        return bar;
    }

    // ----- License tests -----

    private UIElement BuildLicenseTable()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        foreach (Vehicle _ in Vehicles.Main)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        }

        int row = AddRow(grid);
        for (int v = 0; v < Vehicles.Main.Count; v++)
        {
            Place(grid, Bold(Vehicles.Name(Vehicles.Main[v])), row, v + 1);
        }

        for (int classIndex = 0; classIndex < 4; classIndex++)
        {
            int c = classIndex;
            row = AddRow(grid);
            var header = new Border { Background = HeaderBackground };
            Grid.SetRow(header, row);
            Grid.SetColumnSpan(header, 4);
            grid.Children.Add(header);

            var label = new StackPanel { Margin = new Thickness(4, 4, 0, 4) };
            label.Children.Add(Bold(Vehicles.ClassName(c)));
            if (c == 1)
            {
                Button open = MakeButton(L.T("Open Class A"),
                    L.T("Raises the Beginner test of every vehicle to at least 70 points (a bronze medal each), " +
                    "and gives the Class A tests 0 points."), () => { _progress.OpenClassA(); OnChanged(); });
                open.Margin = new Thickness(0, 4, 0, 0);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                label.Children.Add(open);
                _refreshers.Add(() => open.Visibility = Show(_progress.CanOpenClassA));
            }

            Place(grid, label, row, 0);

            for (int v = 0; v < Vehicles.Main.Count; v++)
            {
                Vehicle vehicle = Vehicles.Main[v];
                var cell = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var summary = new TextBlock { Foreground = Muted };
                cell.Children.Add(summary);
                _refreshers.Add(() => summary.Text = ClassSummary(vehicle, c));

                if (c >= 2)
                {
                    int limit = SaveProgress.Limit(c - 1, Medal.Bronze);
                    Button open = MakeButton(L.F("Open {0}", Vehicles.ClassName(c)),
                        L.F("Raises the {0} tests of the {1} one after the other (up to 100 each) to {2} points together " +
                            "(a bronze medal), and gives the {3} tests 0 points.", Vehicles.ClassName(c - 1), Vehicles.Name(vehicle), limit,
                            Vehicles.ClassName(c)), () => { _progress.OpenClass(vehicle, c); OnChanged(); });
                    open.Margin = new Thickness(0, 4, 0, 0);
                    open.HorizontalAlignment = HorizontalAlignment.Left;
                    cell.Children.Add(open);
                    _refreshers.Add(() => open.Visibility = Show(_progress.CanOpenClass(vehicle, c)));
                }

                Place(grid, cell, row, v + 1);
            }

            int tests = Vehicles.Main.Max(v => _slot.Layout.TestCount(c, v));
            for (int t = 0; t < tests; t++)
            {
                row = AddRow(grid);
                Place(grid, new TextBlock { Text = L.F("Test {0}", t + 1), Margin = new Thickness(4, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center }, row, 0);
                for (int v = 0; v < Vehicles.Main.Count; v++)
                {
                    Vehicle vehicle = Vehicles.Main[v];
                    var id = new TestId(c, vehicle, t);
                    if (!_slot.Layout.IsSaved(id))
                    {
                        continue;
                    }

                    var box = new ScoreBox(() => _slot.GetResult(id), value => _slot.SetResult(id, value), OnChanged);
                    _refreshers.Add(() => box.Update(_progress.IsClassOpen(vehicle, c)));
                    Place(grid, box, row, v + 1);
                }
            }
        }

        return grid;
    }

    private string ClassSummary(Vehicle vehicle, int classIndex)
    {
        if (!_progress.IsClassOpen(vehicle, classIndex))
        {
            return L.T("Not opened yet");
        }

        Medal medal = _progress.ClassMedal(vehicle, classIndex);
        string medalText = medal == Medal.None ? L.T("no medal") : L.T(medal.ToString());
        return L.F("{0} points, {1}", _progress.ClassTotal(vehicle, classIndex), medalText);
    }

    // ----- Bonus games -----

    private UIElement BuildBonusTable()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (int l = 0; l < SaveProgress.BirdmanStages; l++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        }

        int row = AddRow(grid);
        for (int l = 0; l < SaveProgress.BirdmanStages; l++)
        {
            Place(grid, Bold(L.F("Level {0}", l + 1)), row, l + 1);
        }

        // Birdman: no points; a check mark shows whether the game opens the stage.
        row = AddRow(grid);
        Place(grid, Bold("Birdman"), row, 0);
        for (int l = 0; l < SaveProgress.BirdmanStages; l++)
        {
            int stage = l;
            var cell = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            var check = new CheckBox { Content = L.T("Opened"), IsEnabled = false,
                ToolTip = L.T("Birdman has no points. The stage is open when the points below reach the game's limits.") };
            string needs = stage == 0
                ? L.T("Raises the Beginner test of all three license vehicles to 80 points (a silver medal).")
                : L.F("Raises the {0} tests of all three license vehicles to a silver medal ({1} points each), and every level of {2} to 80 points.",
                    Vehicles.ClassName(stage), SaveProgress.Limit(stage, Medal.Silver), Vehicles.Name(Vehicles.BonusGames[stage - 1]));
            Button open = MakeButton(L.T("Open"), needs, () => { _progress.OpenBirdmanStage(stage); OnChanged(); });
            open.Margin = new Thickness(0, 4, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            cell.Children.Add(check);
            cell.Children.Add(open);
            _refreshers.Add(() =>
            {
                check.IsChecked = _progress.IsBirdmanStageOpen(stage);
                open.Visibility = Show(_progress.CanOpenBirdmanStage(stage));
            });
            Place(grid, cell, row, l + 1);
        }

        foreach (Vehicle game in Vehicles.BonusGames)
        {
            row = AddRow(grid);
            Place(grid, Bold(Vehicles.Name(game)), row, 0);
            for (int l = 0; l < 3; l++)
            {
                int level = l;
                var cell = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var box = new ScoreBox(() => _progress.LevelValue(game, level), value => _progress.SetLevelValue(game, level, value), OnChanged);
                var state = new TextBlock { Foreground = Muted };
                string needs = level == 0
                    ? L.F("Raises the {0} tests of all three license vehicles to a silver medal ({1} points each), and gives level 1 0 points.",
                        Vehicles.ClassName(SaveProgress.RequiredClass(game)), SaveProgress.Limit(SaveProgress.RequiredClass(game), Medal.Silver))
                    : L.F("Raises level {0} to 70 points, and gives level {1} 0 points.", level, level + 1);
                Button open = MakeButton(L.T("Open"), needs, () => { _progress.OpenLevel(game, level); OnChanged(); });
                open.Margin = new Thickness(0, 4, 0, 0);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                cell.Children.Add(box);
                cell.Children.Add(state);
                cell.Children.Add(open);
                _refreshers.Add(() =>
                {
                    bool isOpen = _progress.IsLevelOpen(game, level);
                    box.Update(isOpen);
                    Medal medal = _progress.LevelMedal(game, level);
                    state.Text = !isOpen ? L.T("Not opened yet") : medal == Medal.None ? L.T("No medal") : L.T(medal.ToString());
                    open.Visibility = Show(_progress.CanOpenLevel(game, level));
                });
                Place(grid, cell, row, l + 1);
            }
        }

        return grid;
    }

    // ----- Photo album -----

    private void BuildPhotoList()
    {
        _photoList.Children.Clear();
        int shown = 0;
        for (int i = 0; i < _slot.Photos.Count; i++)
        {
            SavePhoto photo = _slot.Photos[i];
            if (photo.IsEmpty)
            {
                continue;
            }

            int index = i;
            shown++;
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            Button delete = MakeButton(L.T("Delete"), L.T("Removes the photo; the photos behind it move up."), () =>
            {
                _slot.DeletePhoto(index);
                OnChanged();
            });
            DockPanel.SetDock(delete, Dock.Right);
            line.Children.Add(delete);
            line.Children.Add(new TextBlock { Text = DescribePhoto(i + 1, photo), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            _photoList.Children.Add(line);
        }

        if (shown == 0)
        {
            _photoList.Children.Add(new TextBlock { Text = L.T("No photos."), Foreground = Muted });
        }
    }

    private static string DescribePhoto(int number, SavePhoto photo)
    {
        string where = Vehicles.IsMain(photo.Vehicle)
            ? L.F("{0}, {1}, test {2}", Vehicles.Name(photo.Vehicle), Vehicles.ClassName(photo.Class), photo.Test + 1)
            : L.F("{0}, level {1}", Vehicles.Name(photo.Vehicle), photo.Class + 1);
        string objects = photo.ObjectCount == 0
            ? L.T("no objects")
            : string.Join(", ", photo.ObjectTypes.Select(SavePhoto.ObjectName));
        (short x, short y, short z) = photo.Position;
        return L.F("Photo {0}: {1}. On the photo: {2}. Camera at ({3}, {4}, {5}).", number, where, objects, x, y, z);
    }

    // ----- Helpers -----

    private static int AddRow(Grid grid)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid.RowDefinitions.Count - 1;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock Bold(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 16, 0, 2) };

    private static TextBlock Note(string text) =>
        new() { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0), MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static Button MakeButton(string text, string toolTip, Action click)
    {
        var button = new Button { Content = text, ToolTip = toolTip, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// A field for points: 0-100, or "–" for no result. Other input is set to the nearest allowed
    /// value when the field is left or Enter is pressed.
    /// </summary>
    private sealed class ScoreBox : TextBox
    {
        private readonly Func<int> _get;
        private readonly Action<int> _set;
        private readonly Action _changed;

        public ScoreBox(Func<int> get, Action<int> set, Action changed)
        {
            _get = get;
            _set = set;
            _changed = changed;
            Width = 52;
            HorizontalAlignment = HorizontalAlignment.Left;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTip = L.T("0 to 100 points, or \"–\" (empty) for no result.");
            LostFocus += (_, _) => Commit();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
        }

        /// <summary>Shows the stored value; <paramref name="editable"/> greys the field out when false.</summary>
        public void Update(bool editable)
        {
            int value = _get();
            Text = value == SaveSlot.NoResult ? "–" : value.ToString(CultureInfo.InvariantCulture);
            IsEnabled = editable;
        }

        private void Commit()
        {
            if (!IsEnabled)
            {
                return;
            }

            string text = Text.Trim();
            int value;
            if (text is "" or "-" or "–")
            {
                value = SaveSlot.NoResult;
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                value = SaveProgress.NearestPoints(number);
            }
            else
            {
                value = _get(); // not a number: keep the old value
            }

            if (value != _get())
            {
                _set(value);
            }

            // Shows the value as stored (also when the input was changed to the nearest allowed value).
            _changed();
        }
    }
}
