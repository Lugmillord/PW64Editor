using System.IO;
using System.Windows;
using System.Windows.Input;
using PW64Editor.App.Services;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App;

/// <summary>
/// The first window: create a project, open one (also from the recent list), or exit.
/// </summary>
public partial class StartWindow : Window
{
    public StartWindow()
    {
        InitializeComponent();
    }

    /// <summary>One entry in the recent projects list.</summary>
    public sealed record RecentEntry(string Name, string Folder);

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshRecentList();

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
    private void ShowEditor(EditorSession? session)
    {
        if (session is null)
        {
            return;
        }

        var editor = new MainWindow(session);
        Application.Current.MainWindow = editor;
        editor.Show();
        Close();
    }
}
