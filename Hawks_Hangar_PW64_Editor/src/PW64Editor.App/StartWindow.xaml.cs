using System.IO;
using System.Windows;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App;

/// <summary>
/// The first window: create a project, open one (also from the recent list), copy one, make one from a
/// hack ROM or a BPS patch, or exit.
/// </summary>
public partial class StartWindow : Window
{
    public StartWindow()
    {
        InitializeComponent();
    }

    /// <summary>One entry in the recent projects list.</summary>
    public sealed record RecentEntry(string Name, string Folder);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshRecentList();

        // After a restart of the editor (new language), the project opens again by itself.
        if (App.Restart is { } restart)
        {
            EditorSession? session = ProjectOpener.Open(this, restart.ProjectFolder);
            if (session is null)
            {
                App.Restart = null;
            }

            ShowEditor(session);
        }
    }

    private void RefreshRecentList()
    {
        var entries = RecentProjects.GetExisting(EditorContext.Settings)
            .Select(folder => new RecentEntry(Path.GetFileName(folder), folder))
            .ToList();

        RecentList.ItemsSource = entries;
        EmptyRecentText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewProject(object sender, RoutedEventArgs e) => ShowEditor(ProjectOpener.CreateNew(this));

    private void OnOpenProject(object sender, RoutedEventArgs e) => ShowEditor(ProjectOpener.OpenWithDialog(this));

    private async void OnCopyProject(object sender, RoutedEventArgs e) => ShowEditor(await ProjectOpener.CopyAsync(this, null));

    private async void OnProjectFromRom(object sender, RoutedEventArgs e) => ShowImported(await ProjectOpener.FromRomAsync(this));

    private async void OnProjectFromPatch(object sender, RoutedEventArgs e) => ShowImported(await ProjectOpener.FromPatchAsync(this));

    /// <summary>Opens the editor for a project made from a ROM and shows the report of the import there.</summary>
    private void ShowImported((EditorSession Session, string Report)? imported)
    {
        if (imported is not { } result)
        {
            return;
        }

        if (ShowEditor(result.Session) is { } editor)
        {
            ProjectOpener.ShowReport(editor, result.Report);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedRecent();

    private void OnRecentOpen(object sender, RoutedEventArgs e) => OpenSelectedRecent();

    private void OnRecentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedRecent();
            e.Handled = true;
        }
    }

    private void OnRecentRemove(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentEntry entry)
        {
            EditorContext.RemoveRecentProject(entry.Folder);
            RefreshRecentList();
        }
    }

    private void OpenSelectedRecent()
    {
        if (RecentList.SelectedItem is RecentEntry entry)
        {
            ShowEditor(ProjectOpener.Open(this, entry.Folder));
            RefreshRecentList();
        }
    }

    /// <summary>Opens the main window for a project and closes the start window.</summary>
    /// <returns>The main window, or null without a project.</returns>
    private MainWindow? ShowEditor(EditorSession? session)
    {
        if (session is null)
        {
            return null;
        }

        var editor = new MainWindow(session);
        Application.Current.MainWindow = editor;
        editor.Show();
        Close();
        return editor;
    }
}
