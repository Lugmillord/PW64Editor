using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.App.Views;
using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.Compression;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App;

/// <summary>
/// The editor window for an open project. For now it offers all project functions
/// through the File menu; content editors will fill the empty area later.
/// </summary>
public partial class MainWindow : Window
{
    private EditorSession _session;

    /// <summary>Remembers the last choice of the build dialog's restore point checkbox.</summary>
    private bool _lastRestorePointChoice;

    /// <summary>Set once the user has dealt with unsaved changes, so closing goes through.</summary>
    private bool _closeConfirmed;

    public MainWindow(EditorSession session)
    {
        InitializeComponent();
        _session = session;
        ReloadProject();

        // Offer missing code fixes once the window is visible.
        Loaded += async (_, _) => await OfferMissingCodeFixesAsync();
    }

    // ----------------------------------------------------------------- project

    private async void OnNewProject(object sender, RoutedEventArgs e)
    {
        if (await ConfirmUnsavedChangesAsync("creating a new project"))
        {
            SwitchTo(ProjectOpener.CreateNew(this));
        }
    }

    private async void OnOpenProject(object sender, RoutedEventArgs e)
    {
        if (await ConfirmUnsavedChangesAsync("opening another project"))
        {
            SwitchTo(ProjectOpener.OpenWithDialog(this));
        }
    }

    private void OnRecentMenuOpened(object sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        IReadOnlyList<string> recent = RecentProjects.GetExisting(EditorContext.Settings);

        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }

        foreach (string folder in recent)
        {
            // Underscores would be read as access keys, so they are doubled.
            var item = new MenuItem { Header = folder.Replace("_", "__"), Tag = folder };
            item.Click += async (_, _) =>
            {
                if (await ConfirmUnsavedChangesAsync("opening another project"))
                {
                    SwitchTo(ProjectOpener.Open(this, (string)item.Tag));
                }
            };
            RecentMenu.Items.Add(item);
        }
    }

    private void OnOpenProjectFolder(object sender, RoutedEventArgs e) => Ui.OpenFolder(_session.Project.Folder);

    private async void OnCloseProject(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmUnsavedChangesAsync("closing the project"))
        {
            return;
        }

        _closeConfirmed = true;
        var start = new StartWindow();
        Application.Current.MainWindow = start;
        start.Show();
        Close();
    }

    /// <summary>Closing the window ends the program; OnClosing asks about unsaved changes.</summary>
    private void OnExit(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Asks about unsaved changes when the window is closed (menu, red X, Alt+F4).
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !TextEditor.HasUnsavedChanges)
        {
            return;
        }

        // Cancel for now; close again once the user has decided (and saving is done).
        e.Cancel = true;
        if (await ConfirmUnsavedChangesAsync("closing"))
        {
            _closeConfirmed = true;

            // Close() must not be called while the Closing event is still running.
            _ = Dispatcher.InvokeAsync(Close);
        }
    }

    private void OnUnsavedChangesChanged(object? sender, EventArgs e) => UpdateTitle();

    private void OnProjectSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectSettingsDialog(_session) { Owner = this };
        bool saved = dialog.ShowDialog() == true;

        if (dialog.CodeFixesApplied)
        {
            TextEditor.RefreshPreview(); // the preview takes the fixes into account
        }

        if (saved)
        {
            UpdateProjectInfo();
            SetStatus(dialog.CodeFixesApplied ? "Project settings saved, code fix applied." : "Project settings saved.");
        }
        else if (dialog.CodeFixesApplied)
        {
            SetStatus("Code fix applied and hack ROM rebuilt.");
        }
    }

    private async void OnGameFiles(object sender, RoutedEventArgs e)
    {
        // The window can add or remove the text file, which reloads the texts.
        if (await ConfirmUnsavedChangesAsync("opening the game files"))
        {
            new GameFilesWindow(_session) { Owner = this }.ShowDialog();
            ReloadProject();
        }
    }

    private async void OnRestorePoints(object sender, RoutedEventArgs e)
    {
        // Restore points save and restore the project files, so edits must be saved first.
        if (await ConfirmUnsavedChangesAsync("opening the restore points"))
        {
            new RestorePointsWindow(_session) { Owner = this }.ShowDialog();
            ReloadProject();
        }
    }

    // ----------------------------------------------------------------- build and patch

    private async void OnBuild(object sender, RoutedEventArgs e)
    {
        var dialog = new BuildDialog(_session.Project.OutputRomPath, _lastRestorePointChoice) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        bool restorePoint = dialog.CreateRestorePoint;
        _lastRestorePointChoice = restorePoint;
        await SaveAndBuildAsync(restorePoint);
    }

    /// <summary>
    /// Saves all edits into the project and builds the hack ROM ("saving" in the editor).
    /// </summary>
    /// <returns>True if everything was saved and built.</returns>
    private async Task<bool> SaveAndBuildAsync(bool restorePoint)
    {
        string? textWithErrors = TextEditor.FindTextWithErrors();
        if (textWithErrors is not null)
        {
            Ui.ShowError(this, $"The text {textWithErrors} has errors and cannot be saved. It is selected now; " +
                               "the problems are listed below the text.");
            return false;
        }

        int savedTexts;
        try
        {
            savedTexts = TextEditor.SaveChanges(_session);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, $"The texts could not be saved.\n\n{ex.Message}");
            return false;
        }

        string texts = savedTexts > 0 ? $"{savedTexts} text(s) saved. " : string.Empty;
        return await RunAsync("Building hack ROM…", () => _session.Build(restorePoint), outcome =>
        {
            RomBuildResult rom = outcome.Result.RomBuild;
            string backup = outcome.RestorePoint is { } point ? $" Restore point {point.Name} created." : string.Empty;
            SetStatus($"{texts}Hack ROM built ({outcome.Result.AppliedOverrides} changed file(s), " +
                      $"room for {rom.FreeSpace:N0} more bytes).{backup}");
        });
    }

    /// <summary>
    /// If there are unsaved changes, asks whether to save them first.
    /// </summary>
    /// <param name="action">What the user is about to do, e.g. "closing".</param>
    /// <returns>True if the action may continue (saved or discarded), false if cancelled or saving failed.</returns>
    private async Task<bool> ConfirmUnsavedChangesAsync(string action)
    {
        if (!TextEditor.HasUnsavedChanges)
        {
            return true;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"You have unsaved text changes. Save them before {action}?\n\n" +
            "Yes: save the changes and build the hack ROM.\nNo: discard the changes.\nCancel: go back to editing.",
            Ui.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                return await SaveAndBuildAsync(restorePoint: false);
            case MessageBoxResult.No:
                TextEditor.DiscardChanges();
                return true;
            default:
                return false;
        }
    }

    private async void OnExportPatch(object sender, RoutedEventArgs e)
    {
        // The patch is built from the project files, so unsaved edits would be missing.
        if (!await ConfirmUnsavedChangesAsync("exporting the patch"))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export BPS patch",
            Filter = Ui.PatchFilter,
            FileName = $"{_session.Project.GetOutputBaseName()}_v{_session.Project.Settings.Version}.bps",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;
        await RunAsync("Creating patch…", () => _session.ExportPatch(path),
            size => SetStatus($"Patch exported to {path} ({size:N0} bytes, verified)."));
    }

    private async void OnApplyPatch(object sender, RoutedEventArgs e)
    {
        var open = new OpenFileDialog { Title = "Select the BPS patch to apply to the clean ROM", Filter = Ui.PatchFilter };
        if (open.ShowDialog(this) != true)
        {
            return;
        }

        var save = new SaveFileDialog
        {
            Title = "Save the patched ROM",
            Filter = Ui.RomFilter,
            FileName = Path.GetFileNameWithoutExtension(open.FileName) + ".z64",
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        string patchPath = open.FileName, outputPath = save.FileName;
        await RunAsync("Applying patch…", () =>
        {
            byte[] result = BpsReader.Apply(_session.CleanRom.Data, File.ReadAllBytes(patchPath));
            File.WriteAllBytes(outputPath, result);
            return result.Length;
        }, size => SetStatus($"Patched ROM saved to {outputPath} ({size:N0} bytes)."));
    }

    // ----------------------------------------------------------------- ROM

    private async void OnRomInfo(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select a ROM to inspect", Filter = Ui.RomFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;
        await RunAsync("Reading ROM…", () => RomReport.Describe(N64Rom.Load(path)), text =>
        {
            SetStatus("Ready");
            TextReportWindow.ShowReport(this, $"ROM info: {Path.GetFileName(path)}", text);
        });
    }

    private void OnChangeCleanRom(object sender, RoutedEventArgs e)
    {
        if (CleanRomSetup.SelectAndImport(this))
        {
            SetStatus("Clean ROM replaced.");
        }
    }

    // ----------------------------------------------------------------- developer tools

    private async void OnScanMio0(object sender, RoutedEventArgs e)
    {
        await RunAsync("Scanning for MIO0 blocks…", () => DeveloperTools.DescribeMio0Blocks(_session.CleanRom), text =>
        {
            SetStatus("Ready");
            TextReportWindow.ShowReport(this, "MIO0 blocks in the clean ROM", text);
        });
    }

    private async void OnExtractMio0(object sender, RoutedEventArgs e)
    {
        string? input = InputDialog.Ask(this, "Extract MIO0 block", "ROM offset of the block (hex like 0xDE754, or decimal):", "0x");
        if (input is null)
        {
            return;
        }

        if (!Ui.TryParseNumber(input, out int offset) || offset < 0 || offset >= _session.CleanRom.Size)
        {
            Ui.ShowError(this, $"\"{input}\" is not a valid ROM offset.");
            return;
        }

        var save = new SaveFileDialog { Title = "Save the decompressed data", FileName = $"mio0_{offset:X6}.bin" };
        if (save.ShowDialog(this) != true)
        {
            return;
        }

        string path = save.FileName;
        await RunAsync("Decompressing…", () =>
        {
            byte[] data = Mio0.Decompress(_session.CleanRom.Data.AsSpan(offset));
            File.WriteAllBytes(path, data);
            return data.Length;
        }, size => SetStatus($"Decompressed {size:N0} bytes to {path}."));
    }

    private void OnShowChunks(object sender, RoutedEventArgs e)
    {
        string? input = InputDialog.Ask(this, "Show file chunks", "Table index of the game file (0 - 1271):", string.Empty);
        if (input is null)
        {
            return;
        }

        if (!Ui.TryParseNumber(input, out int index))
        {
            Ui.ShowError(this, $"\"{input}\" is not a number.");
            return;
        }

        try
        {
            TextReportWindow.ShowReport(this, $"Chunks of file {index}", DeveloperTools.DescribeChunks(_session.CleanFileSystem, index));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(this, ex.Message);
        }
    }

    private async void OnCreatePatchFromRoms(object sender, RoutedEventArgs e)
    {
        var source = new OpenFileDialog { Title = "Select the original (source) ROM", Filter = Ui.RomFilter };
        if (source.ShowDialog(this) != true)
        {
            return;
        }

        var target = new OpenFileDialog { Title = "Select the modified (target) ROM", Filter = Ui.RomFilter };
        if (target.ShowDialog(this) != true)
        {
            return;
        }

        var save = new SaveFileDialog { Title = "Save the patch", Filter = Ui.PatchFilter, FileName = "patch.bps" };
        if (save.ShowDialog(this) != true)
        {
            return;
        }

        string sourcePath = source.FileName, targetPath = target.FileName, patchPath = save.FileName;
        await RunAsync("Creating patch…", () =>
        {
            N64Rom original = N64Rom.Load(sourcePath);
            N64Rom modified = N64Rom.Load(targetPath);
            byte[] patch = BpsWriter.Create(original.Data, modified.Data);
            if (!BpsReader.Apply(original.Data, patch).AsSpan().SequenceEqual(modified.Data))
            {
                throw new InvalidOperationException("The created patch does not reproduce the target ROM.");
            }

            File.WriteAllBytes(patchPath, patch);
            return patch.Length;
        }, size => SetStatus($"Patch saved to {patchPath} ({size:N0} bytes, verified)."));
    }

    private async void OnStressTest(object sender, RoutedEventArgs e)
    {
        var dialog = new StressTestDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var save = new SaveFileDialog { Title = "Save the test ROM", Filter = Ui.RomFilter, FileName = "stress_test.z64" };
        if (save.ShowDialog(this) != true)
        {
            return;
        }

        if (_session.Project.IsCleanRomPath(save.FileName))
        {
            Ui.ShowError(this, "The test ROM cannot be written to the clean ROM. Choose a different file.");
            return;
        }

        string path = save.FileName;
        var options = new RomBuildOptions(dialog.RelocateAudio, dialog.AllowExpansion);
        bool recompress = dialog.Recompress;
        int? dummySize = dialog.DummyFileSize;

        await RunAsync("Building test ROM…", () =>
        {
            RomBuildResult result = DeveloperTools.BuildStressTest(_session.CleanRom, recompress, options, dummySize);
            result.Rom.Save(path);
            return result;
        }, result => SetStatus(
            $"Test ROM saved to {path}: audio at 0x{result.AudioOffset:X}{(result.AudioRelocated ? " (relocated)" : string.Empty)}, " +
            $"{result.Rom.Size / (1024 * 1024)} MiB."));
    }

    // ----------------------------------------------------------------- helpers

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.S:
                OnBuild(this, e);
                e.Handled = true;
                break;
            case Key.N:
                OnNewProject(this, e);
                e.Handled = true;
                break;
            case Key.O:
                OnOpenProject(this, e);
                e.Handled = true;
                break;
            case Key.G:
                OnGameFiles(this, e);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Replaces the open project with another one (if the user did not cancel).</summary>
    private void SwitchTo(EditorSession? session)
    {
        if (session is null)
        {
            return;
        }

        _session = session;
        ReloadProject();
        SetStatus($"Opened project {session.Project.Settings.Name}.");
        _ = Dispatcher.InvokeAsync(OfferMissingCodeFixesAsync);
    }

    /// <summary>
    /// If the project lacks some of the editor's code fixes (projects made before a fix existed),
    /// explains them and offers to apply them. "Not now" asks again the next time.
    /// </summary>
    private async Task OfferMissingCodeFixesAsync()
    {
        IReadOnlyList<CodeFix> missing = _session.Project.MissingCodeFixes;
        if (missing.Count == 0)
        {
            return;
        }

        var dialog = new CodeFixOfferDialog(missing) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            SetStatus("Code fixes not applied. They are offered again next time, and in File › Project settings.");
            return;
        }

        bool restorePoint = dialog.CreateRestorePoint;
        EditorSession session = _session;
        await RunAsync("Applying code fixes…", () =>
        {
            // The restore point keeps the state before the fixes.
            BackupInfo? point = restorePoint ? session.CreateRestorePoint() : null;
            session.ApplyCodeFixes(missing);
            return point;
        }, point =>
        {
            TextEditor.RefreshPreview();
            string backup = point is not null ? $" Restore point {point.Name} created before." : string.Empty;
            SetStatus($"{missing.Count} code fix(es) applied, hack ROM rebuilt.{backup}");
        });
    }

    /// <summary>
    /// Reloads the tabs, because the project's files may have changed (other project, files added
    /// or removed, restore point). Unsaved edits are dropped, so callers ask the user first.
    /// </summary>
    private void ReloadProject()
    {
        TextEditor.Load(_session);
        UpdateProjectInfo();
    }

    /// <summary>Updates title and status bar.</summary>
    private void UpdateProjectInfo()
    {
        UpdateTitle();
        ProjectText.Text = $"Project: {_session.Project.Folder}";
        HackRomText.Text = $"Hack ROM: {_session.Project.OutputRomPath}";
    }

    /// <summary>Window title with an asterisk while there are unsaved changes, as in most editors.</summary>
    private void UpdateTitle()
    {
        ProjectSettings settings = _session.Project.Settings;
        string unsaved = TextEditor.HasUnsavedChanges ? "*" : string.Empty;
        Title = $"{unsaved}{settings.Name} {settings.Version} - {Ui.AppName}";
    }

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Runs work in the background with a busy indicator, then shows the result or a
    /// friendly error message.
    /// </summary>
    /// <returns>True if the work succeeded.</returns>
    private async Task<bool> RunAsync<T>(string busyText, Func<T> work, Action<T> onSuccess)
    {
        SetStatus(busyText);
        try
        {
            T result = await Ui.RunBusyAsync(this, work);
            onSuccess(result);
            return true;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            SetStatus("Failed.");
            string hint = ex is FileSystemFullException
                ? "\n\nTip: allow enlarging the ROM in File › Project settings."
                : ex is BpsException { Error: BpsError.WrongSource }
                    ? "\n\nThe patch was made for a different ROM than the clean Pilotwings 64 (USA) ROM."
                    : string.Empty;
            Ui.ShowError(this, ex.Message + hint);
            return false;
        }
    }
}
