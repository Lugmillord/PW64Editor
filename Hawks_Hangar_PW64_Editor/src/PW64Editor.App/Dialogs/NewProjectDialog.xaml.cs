using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PW64Editor.App.Services;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Asks for the name and location of a new project. The project gets its own folder,
/// named after the hack, inside the chosen location. Also used for copies and for projects
/// made from a ROM or a patch (<see cref="Configure"/>).
/// </summary>
public partial class NewProjectDialog : Window
{
    public NewProjectDialog()
    {
        InitializeComponent();
        LocationBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hawk's Hangar Projects");
        NameBox.Focus();
        UpdatePreview();
    }

    /// <summary>
    /// Adapts the dialog to another way of creating a project.
    /// </summary>
    /// <param name="title">Window title (translated).</param>
    /// <param name="button">Text of the create button (translated).</param>
    /// <param name="name">The suggested name.</param>
    /// <param name="prepareForGit">Whether the Git box starts checked.</param>
    /// <param name="location">The suggested location, or null for the default.</param>
    public void Configure(string title, string button, string name, bool prepareForGit, string? location = null)
    {
        Title = title;
        CreateButton.Content = button;
        NameBox.Text = name;
        NameBox.SelectAll();
        GitBox.IsChecked = prepareForGit;
        if (location is not null)
        {
            LocationBox.Text = location;
        }

        UpdatePreview();
    }

    /// <summary>The hack name as entered.</summary>
    public string ProjectName => NameBox.Text.Trim();

    /// <summary>The folder that will be created: location + folder name derived from the hack name.</summary>
    public string ProjectFolder => Path.Combine(LocationBox.Text.Trim(), ToFolderName(ProjectName));

    /// <summary>Whether to add a .gitignore.</summary>
    public bool PrepareForGit => GitBox.IsChecked == true;

    private void OnInputChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Where should the project folder be created?") };
        if (Directory.Exists(LocationBox.Text))
        {
            dialog.InitialDirectory = LocationBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            LocationBox.Text = dialog.FolderName;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        string? problem = Validate();
        if (problem is not null)
        {
            Ui.ShowError(this, problem);
            return;
        }

        DialogResult = true;
    }

    private void UpdatePreview()
    {
        // The dialog's controls may not all exist yet while InitializeComponent runs.
        if (FolderPreview is null || CreateButton is null)
        {
            return;
        }

        bool ready = ProjectName.Length > 0 && LocationBox.Text.Trim().Length > 0;
        CreateButton.IsEnabled = ready;
        FolderPreview.Text = ready ? L.F("The project will be created in {0}", ProjectFolder) : string.Empty;
    }

    private string? Validate()
    {
        if (!Path.IsPathFullyQualified(LocationBox.Text.Trim()))
        {
            return L.T("Enter a complete location, for example C:\\Hacks.");
        }

        if (Directory.Exists(ProjectFolder) && Directory.EnumerateFileSystemEntries(ProjectFolder).Any())
        {
            return L.F("The folder {0} already exists and is not empty. Choose a different name or location.", ProjectFolder);
        }

        return null;
    }

    /// <summary>Turns a hack name into a valid folder name by replacing forbidden characters.</summary>
    private static string ToFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim().TrimEnd('.');
    }
}
