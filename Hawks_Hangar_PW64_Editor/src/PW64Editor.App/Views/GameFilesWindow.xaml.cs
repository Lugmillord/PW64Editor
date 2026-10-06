using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Lists all game files of the clean ROM and shows which ones the project replaces.
/// Files can be added to the project (copied for editing) or removed again.
/// </summary>
public partial class GameFilesWindow : Window
{
    private readonly EditorSession _session;
    private List<GameFileRow> _allRows = [];

    public GameFilesWindow(EditorSession session)
    {
        InitializeComponent();
        _session = session;
        LoadRows();
    }

    /// <summary>One line of the table. Public properties, because WPF data binding reads them.</summary>
    public sealed class GameFileRow
    {
        public required int TableIndex { get; init; }
        public required string FileType { get; init; }
        public required string Group { get; init; }
        public required int GroupIndex { get; init; }
        public required string Offset { get; init; }
        public required int Size { get; init; }
        public string SizeText => Size.ToString("N0");

        /// <summary>Empty if the project does not replace the file, otherwise its state.</summary>
        public string State { get; set; } = string.Empty;

        public bool InProject => State.Length > 0;
    }

    private void LoadRows()
    {
        Dictionary<int, string> states = GetProjectStates();

        _allRows = _session.CleanFileSystem.Files.Select(f => new GameFileRow
        {
            TableIndex = f.TableIndex,
            FileType = f.FileType,
            Group = f.Group,
            GroupIndex = f.GroupIndex,
            Offset = $"0x{f.RomOffset:X6}",
            Size = f.Size,
            State = states.GetValueOrDefault(f.TableIndex, string.Empty),
        }).ToList();

        ApplyFilter();
    }

    /// <summary>Reads the state of every file in the project, keyed by table index.</summary>
    private Dictionary<int, string> GetProjectStates()
    {
        try
        {
            return _session.GetStatus().ToDictionary(
                s => s.Override.TableIndex,
                s => s.State switch
                {
                    OverrideState.Unchanged => "Yes, unchanged",
                    OverrideState.Modified => $"Yes, modified ({s.Message})",
                    OverrideState.Resized => $"Yes, size changed ({s.Message})",
                    _ => $"Problem: {s.Message}",
                });
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            // E.g. a file in the "files" folder with a name that cannot be interpreted.
            Ui.ShowError(this, $"The project's files folder has a problem:\n\n{ex.Message}");
            return [];
        }
    }

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();
        bool projectOnly = ProjectOnlyBox.IsChecked == true;

        List<GameFileRow> visible = _allRows
            .Where(r => !projectOnly || r.InProject)
            .Where(r => filter.Length == 0
                || r.FileType.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.TableIndex.ToString() == filter)
            .ToList();

        FileGrid.ItemsSource = visible;
        SummaryText.Text = $"{visible.Count} of {_allRows.Count} files, {_allRows.Count(r => r.InProject)} in the project";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var row = FileGrid.SelectedItem as GameFileRow;
        AddButton.IsEnabled = row is { InProject: false };
        RemoveButton.IsEnabled = row is { InProject: true };
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Events can fire while InitializeComponent is still creating the controls.
        if (FileGrid is not null && _session is not null)
        {
            ApplyFilter();
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileGrid.SelectedItem is GameFileRow { InProject: false })
        {
            OnAdd(sender, e);
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is not GameFileRow row)
        {
            return;
        }

        try
        {
            string path = _session.AddFileToProject(row.TableIndex);
            ReloadKeepingSelection(row.TableIndex);
            Ui.ShowInfo(this, $"File {row.TableIndex} was copied into the project:\n\n{path}\n\n" +
                              "Edit it there (for example with a hex editor). The next build uses your version.");
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, ex.Message);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is not GameFileRow row)
        {
            return;
        }

        if (!Ui.Confirm(this, $"Remove file {row.TableIndex} from the project?\n\n" +
                              "Your edited version is deleted and the next build uses the original again."))
        {
            return;
        }

        try
        {
            _session.RemoveFileFromProject(row.TableIndex);
            ReloadKeepingSelection(row.TableIndex);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, ex.Message);
        }
    }

    private void ReloadKeepingSelection(int tableIndex)
    {
        LoadRows();
        GameFileRow? row = (FileGrid.ItemsSource as List<GameFileRow>)?.FirstOrDefault(r => r.TableIndex == tableIndex);
        if (row is not null)
        {
            FileGrid.SelectedItem = row;
            FileGrid.ScrollIntoView(row);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => Ui.OpenFolder(_session.Project.FilesFolder);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
