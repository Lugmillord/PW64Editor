using System.IO;
using System.Windows;
using Microsoft.Win32;
using PW64Editor.App.Services;
using PW64Editor.Core.Project;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Edits the project description, the build options and the location of the hack ROM.
/// Changes are only applied when the user clicks "Save".
/// </summary>
public partial class ProjectSettingsDialog : Window
{
    private readonly HackProject _project;

    /// <summary>The chosen output path; null means the default location.</summary>
    private string? _outputPath;

    public ProjectSettingsDialog(HackProject project)
    {
        InitializeComponent();
        _project = project;

        ProjectSettings s = project.Settings;
        NameBox.Text = s.Name;
        VersionBox.Text = s.Version;
        AuthorBox.Text = s.Author;
        DescriptionBox.Text = s.Description;
        RelocateBox.IsChecked = s.RelocateAudio;
        ExpandBox.IsChecked = s.AllowExpansion;
        _outputPath = project.Local.OutputRomPath;
        ShowOutputPath();
    }

    private void OnChangeOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Where should the hack ROM be written?",
            Filter = "N64 ROM (*.z64)|*.z64",
            FileName = Path.GetFileName(_outputPath ?? _project.OutputRomPath),
            OverwritePrompt = false, // overwriting the hack ROM is the whole point
        };

        if (dialog.ShowDialog(this) == true)
        {
            if (_project.IsCleanRomPath(dialog.FileName))
            {
                Ui.ShowError(this, "The hack ROM cannot be written to the clean ROM. Choose a different file.");
                return;
            }

            _outputPath = dialog.FileName;
            ShowOutputPath();
        }
    }

    private void OnDefaultOutput(object sender, RoutedEventArgs e)
    {
        _outputPath = null;
        ShowOutputPath();
    }

    private void ShowOutputPath()
    {
        OutputBox.Text = _outputPath ?? $"{_project.Folder}\\(hack name).z64  (default)";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (NameBox.Text.Trim().Length == 0)
        {
            Ui.ShowError(this, "The project needs a name.");
            return;
        }

        try
        {
            _project.SetOutputRomPath(_outputPath);
            ProjectSettings s = _project.Settings;
            s.Name = NameBox.Text.Trim();
            s.Version = VersionBox.Text.Trim();
            s.Author = AuthorBox.Text.Trim();
            s.Description = DescriptionBox.Text;
            s.RelocateAudio = RelocateBox.IsChecked == true;
            s.AllowExpansion = ExpandBox.IsChecked == true;
            _project.Save();
            DialogResult = true;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, ex.Message);
        }
    }
}
