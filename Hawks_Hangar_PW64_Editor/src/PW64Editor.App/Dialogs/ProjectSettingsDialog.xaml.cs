using System.IO;
using System.Windows;
using Microsoft.Win32;
using PW64Editor.App.Services;
using PW64Editor.Core.Code;
using PW64Editor.Core.Project;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Edits the project description, the build options and the location of the hack ROM.
/// Changes are only applied when the user clicks "Save". Code fixes are the exception: "Apply"
/// adds a fix to the project and rebuilds the hack ROM at once.
/// </summary>
public partial class ProjectSettingsDialog : Window
{
    private readonly EditorSession _session;
    private readonly HackProject _project;

    /// <summary>The chosen output path; null means the default location.</summary>
    private string? _outputPath;

    public ProjectSettingsDialog(EditorSession session)
    {
        InitializeComponent();
        _session = session;
        _project = session.Project;
        HackProject project = _project;
        FixList.ItemsSource = CodeFixes.All
            .Select(f => new CodeFixRow(f, project.Settings.AppliedCodeFixes.Contains(f.Id)))
            .ToList();
        PatchList.ItemsSource = GamePatches.All
            .Select(p => new GamePatchRow(p, project.HasPatch(p)))
            .ToList();

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

    /// <summary>True if a code fix or a game patch was applied or undone (the hack ROM was rebuilt), even if the dialog is cancelled.</summary>
    public bool CodeFixesApplied { get; private set; }

    private async void OnApplyPatch(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GamePatchRow row || row.IsApplied)
        {
            return;
        }

        await SetPatchAsync(row, true);
    }

    private async void OnUndoPatch(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GamePatchRow row || !row.IsApplied)
        {
            return;
        }

        // Missions that use what the patch adds: say which ones before undoing.
        IReadOnlyList<string> uses;
        try
        {
            uses = GamePatches.MissionsUsing(row.Patch, _session.LoadMissions().Missions);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            uses = [];
        }

        string question = uses.Count == 0
            ? L.F("Undo \"{0}\"? No mission uses it at the moment. You can apply it again at any time.", row.Name)
            : L.F("Undo \"{0}\"? These missions use it and will not work as set without it:\n\n{1}\n\nTheir settings stay, so applying the patch again restores them.",
                row.Name, string.Join("\n", uses.Take(15).Select(u => "• " + u)) + (uses.Count > 15 ? "\n" + L.F("… and {0} more", uses.Count - 15) : string.Empty));
        if (!Ui.Confirm(this, question))
        {
            return;
        }

        await SetPatchAsync(row, false);
    }

    private async Task SetPatchAsync(GamePatchRow row, bool apply)
    {
        try
        {
            (_, string romPath) = await Ui.RunBusyAsync(this, () => _session.SetGamePatch(row.Patch, apply));
            row.IsApplied = apply;
            CodeFixesApplied = true;
            PatchStatus.Text = apply
                ? L.F("\"{0}\" applied. Hack ROM rebuilt: {1}", row.Name, romPath)
                : L.F("\"{0}\" undone. Hack ROM rebuilt: {1}", row.Name, romPath);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, L.F("The patch could not be changed; the project is unchanged.\n\n{0}", ex.Message));
        }
    }

    private async void OnApplyFix(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CodeFixRow row || row.IsApplied)
        {
            return;
        }

        try
        {
            (_, string romPath) = await Ui.RunBusyAsync(this, () => _session.ApplyCodeFixes([row.Fix]));
            row.IsApplied = true;
            CodeFixesApplied = true;
            FixStatus.Text = L.F("\"{0}\" applied. Hack ROM rebuilt: {1}", row.Name, romPath);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, L.F("The fix could not be applied; the project is unchanged.\n\n{0}", ex.Message));
        }
    }

    private void OnChangeOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = L.T("Where should the hack ROM be written?"),
            Filter = "N64 ROM (*.z64)|*.z64",
            FileName = Path.GetFileName(_outputPath ?? _project.OutputRomPath),
            OverwritePrompt = false, // overwriting the hack ROM is the whole point
        };

        if (dialog.ShowDialog(this) == true)
        {
            if (_project.IsCleanRomPath(dialog.FileName))
            {
                Ui.ShowError(this, L.T("The hack ROM cannot be written to the clean ROM. Choose a different file."));
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
        OutputBox.Text = _outputPath ?? $"{_project.Folder}\\{L.T("(hack name)")}.z64  ({L.T("default")})";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (NameBox.Text.Trim().Length == 0)
        {
            Ui.ShowError(this, L.T("The project needs a name."));
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
