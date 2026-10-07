using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.App.Views;
using PW64Editor.Core.Build;
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

    public MainWindow(EditorSession session)
    {
        InitializeComponent();
        _session = session;
        RefreshProjectInfo();
    }

    // ----------------------------------------------------------------- project

    private void OnNewProject(object sender, RoutedEventArgs e) => SwitchTo(ProjectOpener.CreateNew(this));

    private void OnOpenProject(object sender, RoutedEventArgs e) => SwitchTo(ProjectOpener.OpenWithDialog(this));

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
            item.Click += (_, _) => SwitchTo(ProjectOpener.Open(this, (string)item.Tag));
            RecentMenu.Items.Add(item);
        }
    }

    private void OnOpenProjectFolder(object sender, RoutedEventArgs e) => Ui.OpenFolder(_session.Project.Folder);

    private void OnCloseProject(object sender, RoutedEventArgs e)
    {
        var start = new StartWindow();
        Application.Current.MainWindow = start;
        start.Show();
        Close();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnProjectSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectSettingsDialog(_session.Project) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RefreshProjectInfo();
            SetStatus("Project settings saved.");
        }
    }

    private void OnGameFiles(object sender, RoutedEventArgs e)
    {
        new GameFilesWindow(_session) { Owner = this }.ShowDialog();
        RefreshProjectInfo();
    }

    private void OnRestorePoints(object sender, RoutedEventArgs e)
    {
        new RestorePointsWindow(_session) { Owner = this }.ShowDialog();
        RefreshProjectInfo();
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

        await RunAsync("Building hack ROM…", () => _session.Build(restorePoint), outcome =>
        {
            RomBuildResult rom = outcome.Result.RomBuild;
            string backup = outcome.RestorePoint is { } point ? $" Restore point {point.Name} created." : string.Empty;
            SetStatus($"Hack ROM built ({outcome.Result.AppliedOverrides} changed file(s), " +
                      $"room for {rom.FreeSpace:N0} more bytes).{backup}");
        });
    }

    private async void OnExportPatch(object sender, RoutedEventArgs e)
    {
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
        RefreshProjectInfo();
        SetStatus($"Opened project {session.Project.Settings.Name}.");
    }

    /// <summary>
    /// Updates title and status bar and reloads the tabs, because the project's files may have
    /// changed (other project, files added or removed, restore point).
    /// </summary>
    private void RefreshProjectInfo()
    {
        TextEditor.Load(_session);
        ProjectSettings settings = _session.Project.Settings;
        Title = $"{settings.Name} {settings.Version} - {Ui.AppName}";
        ProjectText.Text = $"Project: {_session.Project.Folder}";
        HackRomText.Text = $"Hack ROM: {_session.Project.OutputRomPath}";
    }

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Runs work in the background with a busy indicator, then shows the result or a
    /// friendly error message.
    /// </summary>
    private async Task RunAsync<T>(string busyText, Func<T> work, Action<T> onSuccess)
    {
        SetStatus(busyText);
        try
        {
            T result = await Ui.RunBusyAsync(this, work);
            onSuccess(result);
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
        }
    }
}
