using System.Windows;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Confirms a build and offers to create a restore point. Enter starts the build right away,
/// so Ctrl+S, Enter is the quick way to save.
/// </summary>
public partial class BuildDialog : Window
{
    public BuildDialog(string outputPath, bool createRestorePoint)
    {
        InitializeComponent();
        PathText.Text = outputPath;
        RestorePointBox.IsChecked = createRestorePoint;
    }

    /// <summary>Whether the user wants a restore point after building.</summary>
    public bool CreateRestorePoint => RestorePointBox.IsChecked == true;

    private void OnBuild(object sender, RoutedEventArgs e) => DialogResult = true;
}
