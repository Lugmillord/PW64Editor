using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.Classes;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Menu": the unlock conditions of the 25 fields of the vehicle and class select (<see cref="MenuUnlocks"/>).
/// Changes are saved in the project right away and take effect with the next build.
/// </summary>
public partial class MenuTab : UserControl
{
    private EditorSession? _session;

    /// <summary>The rules being edited (always all 25 fields).</summary>
    private List<FieldUnlock> _rules = [];

    /// <summary>Set while the controls are filled by code.</summary>
    private bool _showing;

    public MenuTab()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the rules were saved (the save file tab follows them).</summary>
    public event EventHandler? UnlocksChanged;

    /// <summary>Shows the rules of a project.</summary>
    public void Load(EditorSession session)
    {
        _session = session;
        _rules = session.LoadUnlocks();
        StatusText.Text = string.Empty;
        ShowList(Math.Max(0, FieldList.SelectedIndex));
    }

    private void ShowList(int select)
    {
        _showing = true;
        FieldList.Items.Clear();
        List<FieldUnlock> original = MenuUnlocks.Original();
        for (int i = 0; i < _rules.Count; i++)
        {
            FieldUnlock field = _rules[i];
            bool changed = !MenuUnlocks.IsOriginal([.. Enumerable.Range(0, _rules.Count).Select(j => j == i ? field : original[j])]);
            string summary = field.Conditions.Count == 0
                ? L.T("open from the start")
                : L.F("{0} condition(s)", field.Conditions.Count);
            FieldList.Items.Add(new ListBoxItem
            {
                Content = $"{MenuUnlocks.FieldName((Vehicle)field.Vehicle, field.Class)} – {summary}",
                FontWeight = changed ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = i,
            });
        }

        FieldList.SelectedIndex = Math.Clamp(select, 0, FieldList.Items.Count - 1);
        bool own = _session?.Project.Settings.Unlocks is not null;
        RulesText.Text = own ? L.T("The menu uses own rules (fields in bold are changed).") : L.T("The menu uses the game's rules.");
        OriginalButton.IsEnabled = own;
        _showing = false;
        ShowField();
    }

    private int SelectedField => FieldList.SelectedItem is ListBoxItem { Tag: int index } ? index : -1;

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing)
        {
            ShowField();
        }
    }

    /// <summary>Shows the conditions of the selected field.</summary>
    private void ShowField()
    {
        ConditionGrid.Children.Clear();
        ConditionGrid.RowDefinitions.Clear();
        ConditionGrid.ColumnDefinitions.Clear();
        if (SelectedField is not (>= 0 and < MenuUnlocks.FieldCount) || SelectedField >= _rules.Count)
        {
            return;
        }

        int index = SelectedField;
        FieldUnlock field = _rules[index];
        FieldTitle.Text = MenuUnlocks.FieldName((Vehicle)field.Vehicle, field.Class);
        FieldNote.Text = field.Conditions.Count == 0
            ? L.T("No conditions: the field is open from the start.")
            : L.T("The field opens when all of these conditions are met:");
        AddButton.IsEnabled = field.Conditions.Count < MenuUnlocks.MaxConditions;
        AddButton.ToolTip = L.F("A field has at most {0} conditions.", MenuUnlocks.MaxConditions);

        for (int c = 0; c < 4; c++)
        {
            ConditionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        void Add(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            ConditionGrid.Children.Add(element);
        }

        for (int i = 0; i < field.Conditions.Count; i++)
        {
            int at = i;
            UnlockCondition condition = field.Conditions[i];
            ConditionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fieldBox = new ComboBox { Width = 230, Margin = new Thickness(0, 3, 10, 3) };
            foreach ((Vehicle vehicle, int classIndex) in MenuUnlocks.Fields)
            {
                if (MenuUnlocks.FieldIndex(vehicle, classIndex) == index)
                {
                    continue; // a field cannot need itself
                }

                var item = new ComboBoxItem { Content = MenuUnlocks.FieldName(vehicle, classIndex), Tag = (vehicle, classIndex) };
                fieldBox.Items.Add(item);
                if ((int)vehicle == condition.Vehicle && classIndex == condition.Class)
                {
                    fieldBox.SelectedItem = item;
                }
            }

            fieldBox.SelectionChanged += (_, _) =>
            {
                if (fieldBox.SelectedItem is ComboBoxItem { Tag: (Vehicle vehicle, int classIndex) })
                {
                    Change(index, at, c => { c.Vehicle = (int)vehicle; c.Class = classIndex; });
                }
            };
            Add(fieldBox, i, 0);

            var kindBox = new ComboBox { Width = 150, Margin = new Thickness(0, 3, 10, 3) };
            kindBox.Items.Add(new ComboBoxItem { Content = L.T("badge at least"), Tag = UnlockKind.Medal });
            kindBox.Items.Add(new ComboBoxItem { Content = L.T("points at least"), Tag = UnlockKind.Points });
            kindBox.SelectedIndex = condition.Kind == UnlockKind.Medal ? 0 : 1;
            kindBox.SelectionChanged += (_, _) =>
            {
                if (kindBox.SelectedItem is ComboBoxItem { Tag: UnlockKind kind } && kind != condition.Kind)
                {
                    Change(index, at, c => { c.Kind = kind; c.Value = kind == UnlockKind.Medal ? (int)UnlockMedal.Bronze : 70; });
                }
            };
            Add(kindBox, i, 1);

            if (condition.Kind == UnlockKind.Medal)
            {
                var medalBox = new ComboBox { Width = 110, Margin = new Thickness(0, 3, 10, 3) };
                for (int m = 1; m <= 4; m++)
                {
                    medalBox.Items.Add(new ComboBoxItem { Content = MenuUnlocks.MedalName(m), Tag = m });
                }

                medalBox.SelectedIndex = Math.Clamp(condition.Value, 1, 4) - 1;
                medalBox.SelectionChanged += (_, _) =>
                {
                    if (medalBox.SelectedItem is ComboBoxItem { Tag: int medal })
                    {
                        Change(index, at, c => c.Value = medal);
                    }
                };
                Add(medalBox, i, 2);
            }
            else
            {
                var pointsBox = new TextBox { Width = 110, Margin = new Thickness(0, 3, 10, 3), Text = condition.Value.ToString(CultureInfo.CurrentCulture) };
                void Commit()
                {
                    if (!int.TryParse(pointsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int points)
                        || points is < 0 or > MenuUnlocks.MaxPoints)
                    {
                        StatusText.Text = L.F("Points are whole numbers from 0 to {0}.", MenuUnlocks.MaxPoints);
                        ShowField();
                        return;
                    }

                    if (points != condition.Value)
                    {
                        Change(index, at, c => c.Value = points);
                    }
                }

                pointsBox.LostKeyboardFocus += (_, _) => Commit();
                pointsBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Commit();
                    }
                };
                Add(pointsBox, i, 2);
            }

            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 3, 0, 3), ToolTip = L.T("Remove this condition") };
            remove.Click += (_, _) => Edit(index, f => f.Conditions.RemoveAt(at));
            Add(remove, i, 3);
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        int index = SelectedField;
        if (index < 0)
        {
            return;
        }

        // A new condition names the first other field with a bronze badge.
        (Vehicle vehicle, int classIndex) = MenuUnlocks.Fields.First(f => MenuUnlocks.FieldIndex(f.Vehicle, f.Class) != index);
        Edit(index, f => f.Conditions.Add(UnlockCondition.Of(vehicle, classIndex, UnlockKind.Medal, (int)UnlockMedal.Bronze)));
    }

    private void OnFieldOriginal(object sender, RoutedEventArgs e)
    {
        int index = SelectedField;
        if (index >= 0)
        {
            Edit(index, f => f.Conditions = [.. MenuUnlocks.Original()[index].Conditions]);
        }
    }

    private void OnOriginal(object sender, RoutedEventArgs e)
    {
        if (_session is null || !Ui.Confirm(Window.GetWindow(this)!, L.T("Use the game's rules for every field again? Your conditions are lost.")))
        {
            return;
        }

        Save(null, SelectedField);
    }

    private void Change(int field, int condition, Action<UnlockCondition> change) =>
        Edit(field, f => change(f.Conditions[condition]));

    /// <summary>Changes a field of a copy of the rules and saves them (or shows why not).</summary>
    private void Edit(int field, Action<FieldUnlock> change)
    {
        if (_showing || _session is null)
        {
            return;
        }

        List<FieldUnlock> rules = [.. _rules.Select(r => r.Clone())];
        change(rules[field]);
        Save(rules, field);
    }

    private void Save(List<FieldUnlock>? rules, int select)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.SaveUnlocks(rules);
            _rules = _session.LoadUnlocks();
            StatusText.Text = L.T("Saved. The change takes effect with the next build.");
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            StatusText.Text = ex.Message;
        }

        Dispatcher.BeginInvoke(() => ShowList(select), System.Windows.Threading.DispatcherPriority.Background);
        UnlocksChanged?.Invoke(this, EventArgs.Empty);
    }
}
