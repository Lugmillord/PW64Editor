using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Project;
using PW64Editor.Core.Textures;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Textures": all textures of the game in a grid. The selected one is shown enlarged
/// and can be exported as PNG or replaced by a PNG of the same size; all textures can be
/// exported to and imported from a ZIP file.
/// </summary>
/// <remarks>
/// Replaced textures are written into the project right away (as replaced game files), like
/// files added in the game files window. The hack ROM contains them after the next build.
/// </remarks>
public partial class TexturesTab : UserControl
{
    private static string PngFilter => L.T("PNG images") + " (*.png)|*.png|" + L.T("All files") + " (*.*)|*.*";

    private static string ZipFilter => L.T("ZIP files") + " (*.zip)|*.zip|" + L.T("All files") + " (*.*)|*.*";

    /// <summary>The first part of exported file names.</summary>
    private static string FileWord => L.T("Texture");

    private readonly ObservableCollection<TextureRow> _rows = [];
    private readonly ListCollectionView _view;
    private EditorSession? _session;
    private int _loadGeneration;
    private bool _fillingFilters;

    public TexturesTab()
    {
        InitializeComponent();
        _view = new ListCollectionView(_rows);
        _view.Filter = item => item is TextureRow row && Matches(row);
        TextureList.ItemsSource = _view;
        ShowDetails(null);
    }

    private TextureRow? SelectedRow => TextureList.SelectedItem as TextureRow;

    private Window Owner => Window.GetWindow(this)!;

    /// <summary>Shows the textures of a project (reading them takes a moment, so it runs in the background).</summary>
    public async void Load(EditorSession session)
    {
        _session = session;
        int generation = ++_loadGeneration;
        int? selected = SelectedRow?.Number;
        _rows.Clear();
        ShowDetails(null);
        ActionText.Text = string.Empty;
        CountText.Text = L.T("Reading textures…");

        List<TextureRow> rows;
        try
        {
            rows = await Task.Run(() => session.LoadTextures().Select(TextureRow.Create).ToList());
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            if (generation == _loadGeneration)
            {
                CountText.Text = L.F("The textures could not be read: {0}", ex.Message);
            }

            return;
        }

        if (generation != _loadGeneration)
        {
            return; // another project was opened in the meantime
        }

        foreach (TextureRow row in rows)
        {
            _rows.Add(row);
        }

        FillFilters();
        UpdateCount();
        TextureList.SelectedItem = _rows.FirstOrDefault(r => r.Number == selected);
    }

    // ----------------------------------------------------------------- grid and filters

    private void FillFilters()
    {
        _fillingFilters = true;
        FormatFilter.ItemsSource = new[] { L.T("All formats") }
            .Concat(_rows.Select(r => TextureFormats.Name(r.Entry.Original.Format)).Distinct().Order()).ToList();
        FormatFilter.SelectedIndex = 0;
        SizeFilter.ItemsSource = new[] { L.T("All sizes") }
            .Concat(_rows.Select(r => r.Entry.Original).DistinctBy(t => (t.Width, t.Height))
                .OrderBy(t => t.Width * t.Height).ThenBy(t => t.Width).Select(t => $"{t.Width} × {t.Height}")).ToList();
        SizeFilter.SelectedIndex = 0;
        _fillingFilters = false;
        _view.Refresh();
    }

    private bool Matches(TextureRow row) =>
        (FormatFilter.SelectedIndex <= 0 || TextureFormats.Name(row.Entry.Original.Format) == (string)FormatFilter.SelectedItem)
        && (SizeFilter.SelectedIndex <= 0 || row.SizeText == (string)SizeFilter.SelectedItem)
        && (ChangedOnlyBox.IsChecked != true || row.IsChanged);

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_fillingFilters)
        {
            return;
        }

        _view.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int shown = _rows.Count(Matches);
        int changed = _rows.Count(r => r.IsChanged);
        CountText.Text = shown == _rows.Count
            ? L.F("{0} textures, {1} changed", _rows.Count, changed)
            : L.F("{0} of {1} textures shown, {2} changed", shown, _rows.Count, changed);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails(SelectedRow);

    // ----------------------------------------------------------------- the selected texture

    private void ShowDetails(TextureRow? row)
    {
        bool hasSession = _session is not null && _rows.Count > 0;
        ExportAllButton.IsEnabled = hasSession;
        ImportAllButton.IsEnabled = hasSession;

        if (row is null)
        {
            TitleText.Text = L.T("No texture selected");
            LargeImage.Source = null;
            ResolutionText.Text = string.Empty;
            DetailsText.Text = hasSession ? L.T("Click a texture to see it enlarged.") : string.Empty;
            ProblemText.Visibility = Visibility.Collapsed;
            ExportButton.IsEnabled = ReplaceButton.IsEnabled = RestoreButton.IsEnabled = false;
            return;
        }

        GameTexture original = row.Entry.Original;
        TitleText.Text = L.F("Texture {0}", row.Label) + (row.IsChanged ? " " + L.T("(changed)") : string.Empty);
        LargeImage.Source = row.Thumbnail;
        ResolutionText.Text = L.F("{0} × {1} pixels", original.Width, original.Height);

        var details = new List<string>
        {
            L.F("Format: {0} – {1}", TextureFormats.Name(original.Format), DescribeFormat(original.Format)),
            original.MipmapCount > 0
                ? L.F("Smaller copies for distant surfaces: {0} (computed automatically when replacing)", original.MipmapCount)
                : L.T("Smaller copies for distant surfaces: none"),
            L.F("Game file: {0}", row.Entry.TableIndex),
            row.IsChanged ? L.T("Replaced in this project.") : L.T("Original of the game."),
        };
        DetailsText.Text = string.Join("\n", details);

        ProblemText.Text = row.Entry.Problem ?? string.Empty;
        ProblemText.Visibility = row.Entry.Problem is null ? Visibility.Collapsed : Visibility.Visible;
        ExportButton.IsEnabled = row.Image is not null;
        ReplaceButton.IsEnabled = row.Entry.CanEdit;
        RestoreButton.IsEnabled = row.IsChanged;
    }

    /// <summary>A short description of a texel format.</summary>
    public static string DescribeFormat(TextureFormat format) => format switch
    {
        TextureFormat.Rgba16 => L.T("color, each pixel visible or transparent"),
        TextureFormat.Ia16 => L.T("greyscale with transparency (256 levels each)"),
        TextureFormat.Ia8 => L.T("greyscale with transparency (16 levels each)"),
        TextureFormat.Ia4 => L.T("greyscale (8 levels), each pixel visible or transparent"),
        TextureFormat.I8 => L.T("greyscale (256 levels), no transparency"),
        TextureFormat.I4 => L.T("greyscale (16 levels), no transparency"),
        _ => L.T("not supported"),
    };

    /// <summary>Reads the newest version of a texture from the project and shows it.</summary>
    private void Refresh(TextureRow row)
    {
        if (_session?.LoadTexture(row.Number) is { } entry)
        {
            row.Update(entry);
        }

        _view.Refresh();
        UpdateCount();
        if (ReferenceEquals(SelectedRow, row))
        {
            ShowDetails(row);
        }
    }

    // ----------------------------------------------------------------- export, replace, restore

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { Image: { } image } row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Export texture as PNG"),
            Filter = PngFilter,
            FileName = TextureNames.FileName(FileWord, row.Number, row.Entry.Original),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, TextureImages.ToPng(image));
            ActionText.Text = L.F("Texture {0} exported to {1}.", row.Label, dialog.FileName);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The file could not be written.\n\n{0}", ex.Message));
        }
    }

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelectedRow is not { Entry.CanEdit: true } row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.F("Replace texture {0}", row.Label), Filter = PngFilter };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        RgbaImage? image;
        try
        {
            image = TextureImages.FromFile(File.ReadAllBytes(dialog.FileName));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The file could not be read.\n\n{0}", ex.Message));
            return;
        }

        GameTexture original = row.Entry.Original;
        if (image is null)
        {
            Ui.ShowError(Owner, L.T("The file is not a readable image."));
            return;
        }

        if (image.Width != original.Width || image.Height != original.Height)
        {
            Ui.ShowError(Owner, L.F("The image has {0} × {1} pixels, but texture {2} needs exactly {3} × {4} pixels.",
                image.Width, image.Height, row.Label, original.Width, original.Height));
            return;
        }

        if (new TextureImportDialog(row, image) { Owner = Owner }.ShowDialog() != true)
        {
            return;
        }

        try
        {
            TextureSaveResult result = _session.SaveTexture(row.Number, image);
            ActionText.Text = result switch
            {
                TextureSaveResult.Changed => L.F("Texture {0} replaced. Build the hack ROM (Ctrl+S) to see it in the game.", row.Label),
                TextureSaveResult.RestoredOriginal => L.F("The image is the original one: texture {0} is the original again.", row.Label),
                _ => L.T("The image looks exactly like the current texture; nothing was changed."),
            };
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The texture could not be replaced.\n\n{0}", ex.Message));
        }

        Refresh(row);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelectedRow is not { IsChanged: true } row
            || !Ui.Confirm(Owner, L.F("Restore the original of texture {0}? The replaced image is removed from the project.", row.Label)))
        {
            return;
        }

        try
        {
            _session.RestoreTexture(row.Number);
            ActionText.Text = L.F("Texture {0} is the original again. Build the hack ROM (Ctrl+S) to see it in the game.", row.Label);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The texture could not be restored.\n\n{0}", ex.Message));
        }

        Refresh(row);
    }

    // ----------------------------------------------------------------- all textures as ZIP

    private async void OnExportAll(object sender, RoutedEventArgs e)
    {
        if (_session is null || _rows.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Export all textures"),
            Filter = ZipFilter,
            FileName = L.F("{0} textures.zip", _session.Project.Settings.Name),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        string path = dialog.FileName;
        List<(string Name, RgbaImage Image)> images = _rows.Where(r => r.Image is not null)
            .Select(r => (TextureNames.FileName(FileWord, r.Number, r.Entry.Original), r.Image!)).ToList();
        try
        {
            await Ui.RunBusyAsync(Owner, () =>
            {
                using FileStream stream = File.Create(path);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach ((string name, RgbaImage image) in images)
                {
                    using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                    entry.Write(TextureImages.ToPng(image));
                }

                return images.Count;
            });
            ActionText.Text = L.F("{0} textures exported to {1}.", images.Count, path);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The file could not be written.\n\n{0}", ex.Message));
        }
    }

    private async void OnImportAll(object sender, RoutedEventArgs e)
    {
        if (_session is null || _rows.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.T("Import textures from ZIP"), Filter = ZipFilter };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        EditorSession session = _session;
        string path = dialog.FileName;
        Dictionary<int, TextureEntry> textures = _rows.ToDictionary(r => r.Number, r => r.Entry);
        (int Changed, int Unchanged, int Restored, List<TextureImportRejection> Rejected) outcome;
        try
        {
            outcome = await Ui.RunBusyAsync(Owner, () => ImportZip(session, path, textures));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex) || ex is InvalidDataException)
        {
            Ui.ShowError(Owner, L.F("The ZIP file could not be read.\n\n{0}", ex.Message));
            return;
        }

        Load(session);

        int total = outcome.Changed + outcome.Unchanged + outcome.Restored + outcome.Rejected.Count;
        if (total == 0)
        {
            Ui.ShowInfo(Owner, L.T("The ZIP file contains no PNG images."));
            return;
        }

        string summary = L.F("{0} texture(s) replaced, {1} unchanged, {2} back to the original.",
            outcome.Changed, outcome.Unchanged, outcome.Restored);
        if (outcome.Changed + outcome.Restored > 0)
        {
            summary += " " + L.T("Build the hack ROM (Ctrl+S) to see the changes in the game.");
        }

        ActionText.Text = summary;
        if (outcome.Rejected.Count == 0)
        {
            Ui.ShowInfo(Owner, summary);
        }
        else
        {
            new TextureImportReportDialog($"{summary} {L.F("{0} file(s) were not imported.", outcome.Rejected.Count)}", outcome.Rejected)
            {
                Owner = Owner,
            }.ShowDialog();
        }
    }

    /// <summary>Reads the ZIP file, checks its images and saves the good ones (runs in the background).</summary>
    private static (int, int, int, List<TextureImportRejection>) ImportZip(EditorSession session, string path,
        IReadOnlyDictionary<int, TextureEntry> textures)
    {
        var files = new List<TextureImportFile>();
        using (ZipArchive zip = ZipFile.OpenRead(path))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                // Directories have no name; very large files cannot be textures (at most 4 KB of image data).
                if (entry.Name.Length == 0 || entry.Length > 16 * 1024 * 1024)
                {
                    continue;
                }

                using Stream stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                files.Add(new TextureImportFile(entry.FullName, buffer.ToArray()));
            }
        }

        TextureImportCheck check = TextureImport.Check(files, textures, TextureImages.FromFile);
        int changed = 0, unchanged = 0, restored = 0;
        var rejected = check.Rejected.ToList();
        foreach ((int number, RgbaImage image) in check.Accepted)
        {
            try
            {
                switch (session.SaveTexture(number, image))
                {
                    case TextureSaveResult.Changed:
                        changed++;
                        break;
                    case TextureSaveResult.RestoredOriginal:
                        restored++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }
            catch (Exception ex) when (ex is ProjectException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                rejected.Add(new TextureImportRejection(TextureNames.FileName(FileWord, number, textures[number].Original), ex.Message));
            }
        }

        return (changed, unchanged, restored, rejected);
    }
}
