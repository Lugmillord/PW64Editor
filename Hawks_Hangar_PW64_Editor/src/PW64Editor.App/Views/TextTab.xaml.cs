using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Code;
using PW64Editor.Core.Text;
using PW64Editor.Core.Text.Preview;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Shows all game texts, grouped by where they appear in the game, and lets the user edit them.
/// </summary>
/// <remarks>
/// Edits are kept in memory until the main window saves them (see <see cref="SaveChanges"/>).
/// The tab reports unsaved changes through <see cref="UnsavedChangesChanged"/>.
/// </remarks>
public partial class TextTab : UserControl, ITextEditorHost
{
    private ListCollectionView? _view;
    private GameTextLibrary? _library;
    private List<TextRow> _rows = [];
    private Dictionary<string, TextRow> _rowsByName = [];
    private string _search = string.Empty;

    /// <summary>Draws texts like the game; null if the game's fonts could not be read.</summary>
    private TextPreviewRenderer? _previewRenderer;

    /// <summary>Why there is no preview, if <see cref="_previewRenderer"/> is null.</summary>
    private string? _previewProblem;

    /// <summary>The open project, to know which code fixes the preview has to take into account.</summary>
    private EditorSession? _session;

    public TextTab()
    {
        InitializeComponent();
        Editor.Attach(this);
    }

    /// <summary>Raised after the texts were (re)loaded: the rows are new objects then.</summary>
    public event EventHandler? TextsReloaded;

    // ----------------------------------------------------------------- for the text editor panels

    /// <inheritdoc />
    public GameTextLibrary? Library => _library;

    /// <inheritdoc />
    public TextPreviewRenderer? PreviewRenderer => _previewRenderer;

    /// <inheritdoc />
    public string? PreviewProblem => _previewProblem;

    /// <inheritdoc />
    public EditorSession? Session => _session;

    /// <inheritdoc />
    public void OnTextEdited(TextRow row, bool wasUnsaved)
    {
        if (wasUnsaved != row.IsUnsaved)
        {
            UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Tells for a text name why it must not be edited here (for example because the Missions tab
    /// writes it), or null. Such texts are shown read-only with the reason.
    /// </summary>
    public Func<string, string?>? LockReason { get; set; }

    /// <summary>The text with this name, or null. Other tabs edit texts through these rows.</summary>
    public TextRow? FindRow(string name) => _rowsByName.GetValueOrDefault(name);

    /// <summary>Raised when texts become unsaved or saved.</summary>
    public event EventHandler? UnsavedChangesChanged;

    /// <summary>True if at least one text was edited and not saved yet.</summary>
    public bool HasUnsavedChanges => _rows.Any(r => r.IsUnsaved) || _removedRows.Count > 0;

    /// <summary>Saved custom texts the user removed; they disappear from the file when saving.</summary>
    private readonly List<TextRow> _removedRows = [];

    /// <summary>
    /// Loads the texts of a project (its own copy of the text file, or the original).
    /// Unsaved edits are dropped; the main window asks the user before calling this.
    /// Errors are shown inside the tab instead of interrupting the user.
    /// </summary>
    public void Load(EditorSession session)
    {
        _session = session;
        GameTextLibrary library;
        GameTextLibrary original;
        try
        {
            library = session.LoadTexts();
            original = session.LoadOriginalTexts();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            TextGrid.ItemsSource = null;
            _view = null;
            _library = null;
            _rows = [];
            _rowsByName = [];
            ShowDetails(null);
            SourceText.Text = L.F("The texts could not be read: {0}", ex.Message);
            UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
            TextsReloaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        _library = library;
        // Free slots (numbers of removed custom texts) are not shown.
        _rows = library.Texts.Where(t => !t.IsFree).Select(t => new TextRow(t, OriginalMarkupOf(original, t))).ToList();
        _removedRows.Clear();
        UpdateAddTextButton();

        // For the preview: other texts on the same screen are looked up by name. Names are unique
        // in the retail game; should a file contain one twice, the first one counts (like in the game).
        _rowsByName = _rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First());
        LoadPreviewRenderer(session);

        var view = new ListCollectionView(_rows);

        // Sort first, so groups appear in a meaningful order (not alphabetically).
        view.SortDescriptions.Add(new SortDescription(nameof(TextRow.AreaOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TextRow.SectionOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TextRow.Section), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TextRow.SortKey), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TextRow.Index), ListSortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TextRow.Area)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TextRow.Section)));
        view.Filter = MatchesSearch;

        _view = view;
        TextGrid.ItemsSource = view;
        ShowDetails(null);

        int changed = _rows.Count(r => r.DiffersFromOriginal);
        int custom = _rows.Count(r => r.IsCustom);
        string added = custom > 0 ? L.F(", {0} added (marked +)", custom) : string.Empty;
        SourceText.Text = library.FromProject
            ? L.F("{0} texts from the project, {1} changed compared to the original game (marked ✎){2}. Unsaved edits are marked ●.", _rows.Count, changed, added)
            : custom > 0
                ? L.F("{0} texts from the original game and {1} added by a game patch (marked +; they are written into the project when you save a text). Edited texts are marked ● until saved.",
                    _rows.Count - custom, custom)
                : L.F("{0} texts from the original game. Edited texts are marked ● until saved.", _rows.Count);
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
        TextsReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the first text with unsaved edits that cannot be saved, or null if all can be saved.
    /// The text is selected, so the user sees the problem.
    /// </summary>
    public string? FindTextWithErrors()
    {
        if (_library is null)
        {
            return null;
        }

        foreach (TextRow row in _rows.Where(r => r.IsUnsaved))
        {
            if (TextValidator.Validate(_library.Codec, row.Markup, row.OriginalMarkup, row.MaxLines, row.Name).HasErrors)
            {
                SearchBox.Text = string.Empty;
                TextGrid.SelectedItem = row;
                TextGrid.ScrollIntoView(row);
                return row.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes all unsaved edits into the project's text file.
    /// </summary>
    /// <returns>Number of saved texts.</returns>
    /// <exception cref="PW64Editor.Core.Project.ProjectException">A text has errors.</exception>
    public int SaveChanges(EditorSession session)
    {
        if (_library is null)
        {
            return 0;
        }

        Dictionary<int, string> changes = _rows.Where(r => r.IsUnsaved && !r.IsNew).ToDictionary(r => r.Index, r => r.Markup);
        List<NewText> added = _rows.Where(r => r.IsNew).Select(r => new NewText(r.Index, r.Name, r.Markup)).ToList();
        List<int> removed = _removedRows.Select(r => r.Index).ToList();
        if (changes.Count + added.Count + removed.Count == 0)
        {
            return 0;
        }

        session.SaveTexts(_library, changes, added, removed);
        _removedRows.Clear();

        // Reload, so sizes and "saved" states come from the file that was just written.
        int? selected = (TextGrid.SelectedItem as TextRow)?.Index;
        Load(session);
        if (selected is { } index && _rows.FirstOrDefault(r => r.Index == index) is { } row)
        {
            TextGrid.SelectedItem = row;
            TextGrid.ScrollIntoView(row);
        }

        return changes.Count + added.Count + removed.Count;
    }

    /// <summary>Drops all unsaved edits, additions and removals.</summary>
    public void DiscardChanges()
    {
        _rows.RemoveAll(r => r.IsNew);
        _rows.AddRange(_removedRows);
        _removedRows.Clear();
        _rowsByName = _rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (TextRow row in _rows.Where(r => r.IsUnsaved))
        {
            row.Markup = row.SavedMarkup;
        }

        _view?.Refresh();

        ShowDetails(TextGrid.SelectedItem as TextRow);
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string OriginalMarkupOf(GameTextLibrary original, GameText text) =>
        text.Index < original.Texts.Count && original.Texts[text.Index].Name == text.Name
            ? original.Texts[text.Index].Markup
            : text.Markup;

    // ----------------------------------------------------------------- search and groups

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        if (_view is null)
        {
            return;
        }

        _view.Refresh();

        // While searching, show every match; the group containers are rebuilt by Refresh,
        // so expand them once they exist.
        if (_search.Length > 0)
        {
            Dispatcher.InvokeAsync(() => SetAllExpanded(true), DispatcherPriority.Loaded);
        }
    }

    private bool MatchesSearch(object item)
    {
        if (_search.Length == 0)
        {
            return true;
        }

        var row = (TextRow)item;
        return row.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Preview.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Area.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Section.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Text.Category.Description.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || TextEditorPanel.DescriptionOf(row.Text.Category).Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    private void OnShowCodes(object sender, RoutedEventArgs e)
    {
        if (_library is not null)
        {
            new TextCodesWindow(_library.Font) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }

    private void OnExpandAll(object sender, RoutedEventArgs e) => SetAllExpanded(true);

    private void OnCollapseAll(object sender, RoutedEventArgs e) => SetAllExpanded(false);

    /// <summary>
    /// Opens or closes every group. Inner groups only exist once their outer group is open,
    /// so expanding is repeated until no new closed group appears.
    /// </summary>
    private void SetAllExpanded(bool expanded)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            foreach (Expander expander in FindChildren<Expander>(TextGrid).ToList())
            {
                if (expander.IsExpanded != expanded)
                {
                    expander.IsExpanded = expanded;
                    changed = true;
                }
            }

            if (!changed || !expanded)
            {
                break;
            }

            TextGrid.UpdateLayout();
        }
    }

    // ----------------------------------------------------------------- details and editing

    private TextRow? SelectedRow => TextGrid.SelectedItem as TextRow;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails(SelectedRow);

    // True while a row asks to be scrolled into view with our own, narrowed request.
    private bool _bringingRowIntoView;

    /// <summary>
    /// Keeps the list from scrolling sideways when a row is selected (arrow keys, mouse, search).
    /// </summary>
    /// <remarks>
    /// By default WPF scrolls so that as much of the selected row as possible is visible. The rows
    /// are wider than the list, so it scrolls to the right and the status column (unsaved/edited
    /// marks) disappears. Here the request is replaced by one for a 1-pixel strip at the row's
    /// left edge: the list still scrolls up or down to the row, but horizontally it only moves
    /// (to the left) if the start of the row is out of view.
    /// </remarks>
    private void OnRowRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_bringingRowIntoView || sender is not ListViewItem item)
        {
            return;
        }

        e.Handled = true;
        _bringingRowIntoView = true;
        try
        {
            item.BringIntoView(new Rect(0, 0, 1, item.ActualHeight));
        }
        finally
        {
            _bringingRowIntoView = false;
        }
    }

    private void ShowDetails(TextRow? row)
    {
        DetailPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelectionText.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        string? locked = row is null ? null : LockReason?.Invoke(row.Name);
        Editor.IsReadOnly = locked is not null;
        Editor.Note = locked;
        Editor.Show(row);
    }

    // ----------------------------------------------------------------- CSV export and import

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_library is null)
        {
            return;
        }

        Window owner = Window.GetWindow(this)!;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Export texts as CSV"),
            Filter = CsvFilter,
            FileName = L.F("{0} texts.csv", _session?.Project.Settings.Name ?? "Pilotwings 64"),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            // The texts as shown now, including edits that are not saved yet.
            File.WriteAllBytes(dialog.FileName, TextCsv.ToFileBytes(TextCsv.Export(_rows.Select(r => (r.Index, r.Markup)))));
            Ui.ShowInfo(owner, L.F("{0} texts exported to {1}.", _rows.Count, dialog.FileName));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("The file could not be written.\n\n{0}", ex.Message));
        }
    }

    private void OnImportCsv(object sender, RoutedEventArgs e)
    {
        if (_library is null)
        {
            return;
        }

        Window owner = Window.GetWindow(this)!;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.T("Import texts from CSV"), Filter = CsvFilter };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        TextImportResult result;
        try
        {
            string content = TextCsv.Decode(File.ReadAllBytes(dialog.FileName));
            Dictionary<int, TextImportTarget> targets = _rows.ToDictionary(
                r => r.Index, r => new TextImportTarget(r.Index, r.Name, r.OriginalMarkup, r.Markup, r.MaxLines));
            result = TextCsv.Import(content, _library.Codec, targets);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("The file could not be read.\n\n{0}", ex.Message));
            return;
        }

        bool wasUnsaved = HasUnsavedChanges;
        foreach ((int index, string markup) in result.Changes)
        {
            if (_rows.FirstOrDefault(r => r.Index == index) is { } row)
            {
                row.Markup = markup;
            }
        }

        _view?.Refresh();
        ShowDetails(SelectedRow);
        if (wasUnsaved != HasUnsavedChanges)
        {
            UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
        }

        string summary = result.Changes.Count > 0
            ? L.F("{0} text(s) changed, {1} unchanged. The changes are not saved yet: save and build as usual (Ctrl+S).", result.Changes.Count, result.Unchanged)
            : L.F("{0} text(s) changed, {1} unchanged.", result.Changes.Count, result.Unchanged);
        if (result.Rejected.Count == 0)
        {
            Ui.ShowInfo(owner, summary);
        }
        else
        {
            new TextImportReportDialog($"{summary} {L.F("{0} row(s) were not imported.", result.Rejected.Count)}", result.Rejected) { Owner = owner }.ShowDialog();
        }
    }

    private static string CsvFilter => L.T("CSV files") + " (*.csv)|*.csv|" + L.T("All files") + " (*.*)|*.*";

    // ----------------------------------------------------------------- adding and removing texts

    private void UpdateAddTextButton()
    {
        bool canAdd = _session?.CanAddTexts == true && _library is not null;
        AddTextButton.IsEnabled = canAdd;
        AddTextButton.ToolTip = canAdd
            ? L.T("Adds a new text with the lowest free number and a name of your choice")
            : L.F("Needs the code fix \"{0}\" (File › Project settings): the original game has no room for more texts.", L.T(CodeFixes.ExpandedTexts.Name));
    }

    private void OnAddText(object sender, RoutedEventArgs e)
    {
        if (_library is null || _session is null || !_session.CanAddTexts)
        {
            return;
        }

        Window owner = Window.GetWindow(this);
        int? index = CustomTexts.NextFreeIndex(_rows.Select(r => r.Index), _library.OriginalCount);
        if (index is not { } number)
        {
            Ui.ShowError(owner, L.F("The game has room for {0} texts, and all are used.", CustomTexts.Capacity));
            return;
        }

        string? input = InputDialog.Ask(owner, L.T("Add text"),
            L.F("Name of the new text {0}. Capital letters A-Z, digits 0-9 and '_', at most {1} characters. Every name may be used only once.",
                number, CustomTexts.MaxNameLength), "MY_TEXT");
        if (input is null)
        {
            return;
        }

        // Not trimmed: a name with spaces is refused with its own message.
        string name = input.ToUpperInvariant();
        if (CustomTexts.CheckName(name, _rows.Select(r => r.Name)) is { } problem)
        {
            Ui.ShowError(owner, problem);
            return;
        }

        var entry = new GameTextEntry(number, name, TextFileEntry.FreeSlotData);
        var text = new GameText(entry, string.Empty, CustomTexts.Category(name)) { IsCustom = true };
        var row = new TextRow(text, string.Empty) { IsNew = true };
        _rows.Add(row);
        _rowsByName[name] = row;

        SearchBox.Text = string.Empty;
        _view?.Refresh();
        Dispatcher.InvokeAsync(() =>
        {
            SetAllExpanded(true);
            TextGrid.SelectedItem = row;
            TextGrid.ScrollIntoView(row);
            Editor.FocusText();
        }, DispatcherPriority.Loaded);
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void RemoveText(TextRow row)
    {
        if (!row.IsCustom)
        {
            return;
        }

        if (!Ui.Confirm(Window.GetWindow(this),
                L.F("Remove the text {0} (number {1})?\n\nIts number becomes free and is given to the next new text. The removal is saved together with the other changes.",
                    row.Name, row.Index)))
        {
            return;
        }

        _rows.Remove(row);
        _rowsByName.Remove(row.Name);
        if (!row.IsNew)
        {
            _removedRows.Add(row);
        }

        _view?.Refresh();
        ShowDetails(null);
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----------------------------------------------------------------- in-game preview

    /// <summary>Reads the game's fonts for the preview. Problems only disable the preview.</summary>
    private void LoadPreviewRenderer(EditorSession session)
    {
        if (_previewRenderer is not null)
        {
            return; // the fonts are part of the clean ROM and never change
        }

        try
        {
            _previewRenderer = TextPreviewRenderer.Load(session.CleanFileSystem);
            _previewProblem = null;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            _previewProblem = L.F("The preview is not available: the game's font could not be read ({0}).", ex.Message);
        }
    }

    /// <summary>
    /// Updates what depends on the project's code fixes: the preview (which draws like the fixed
    /// game) and whether texts can be added.
    /// </summary>
    public void RefreshPreview()
    {
        UpdateAddTextButton();
        Editor.RefreshPreview();
    }

    /// <summary>The current version of another text, encoded, for screens that show several texts.</summary>
    public IReadOnlyList<ushort>? OtherTextCodes(string name)
    {
        if (_library is null || !_rowsByName.TryGetValue(name, out TextRow? row))
        {
            return null;
        }

        TextEncodeResult encoded = _library.Codec.Encode(row.Markup);
        return encoded.Success ? encoded.Codes : null;
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
