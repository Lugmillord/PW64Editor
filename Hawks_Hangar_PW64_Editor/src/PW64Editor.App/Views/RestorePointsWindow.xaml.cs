using System.Windows;
using System.Windows.Controls;
using PW64Editor.App.Services;
using PW64Editor.Core.Project;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Lists the project's restore points and lets the user create, restore and delete them.
/// </summary>
public partial class RestorePointsWindow : Window
{
    private readonly EditorSession _session;

    public RestorePointsWindow(EditorSession session)
    {
        InitializeComponent();
        _session = session;
        LoadPoints();
    }

    /// <summary>One line of the table.</summary>
    public sealed record PointRow(string Name, string Created, int FileCount, string HasRom);

    private void LoadPoints()
    {
        List<PointRow> rows = _session.GetRestorePoints()
            .Select(b => new PointRow(b.Name, b.CreatedAt.ToString("g"), b.FileCount, b.ContainsRom ? "Yes" : "No"))
            .ToList();

        PointGrid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool selected = PointGrid.SelectedItem is PointRow;
        RestoreButton.IsEnabled = selected;
        DeleteButton.IsEnabled = selected;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        Run(() =>
        {
            BackupInfo info = _session.CreateRestorePoint();
            LoadPoints();
            Ui.ShowInfo(this, $"Restore point created ({info.FileCount} file(s){(info.ContainsRom ? " and the hack ROM" : string.Empty)}).");
        });
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (PointGrid.SelectedItem is not PointRow row)
        {
            return;
        }

        // Restoring replaces the current state, so offer to keep it first.
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Return the project to the restore point from {row.Created}?\n\n" +
            "Your current project files and hack ROM will be replaced.\n" +
            "Save the current state as a new restore point first?",
            Ui.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel)
        {
            return;
        }

        Run(() =>
        {
            if (answer == MessageBoxResult.Yes)
            {
                _session.CreateRestorePoint();
            }

            _session.RestoreTo(row.Name);
            LoadPoints();
            Ui.ShowInfo(this, $"The project was restored to {row.Created}.");
        });
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (PointGrid.SelectedItem is not PointRow row
            || !Ui.Confirm(this, $"Delete the restore point from {row.Created}? This cannot be undone."))
        {
            return;
        }

        Run(() =>
        {
            _session.DeleteRestorePoint(row.Name);
            LoadPoints();
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, ex.Message);
        }
    }
}
