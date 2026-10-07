using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Shows all game texts, grouped by where they appear in the game, and lets the user edit them.
/// </summary>
/// <remarks>
/// Edits are kept in memory until the main window saves them (see <see cref="SaveChanges"/>).
/// The tab reports unsaved changes through <see cref="UnsavedChangesChanged"/>.
/// </remarks>
public partial class TextTab : UserControl
{
    private ListCollectionView? _view;
    private GameTextLibrary? _library;
    private List<TextRow> _rows = [];
    private string _search = string.Empty;

    /// <summary>Set while the text box is filled by code, so that does not count as an edit.</summary>
    private bool _updatingTextBox;

    /// <summary>Removes the message about rejected input (and the red character) after a few seconds.</summary>
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public TextTab()
    {
        InitializeComponent();
        _flashTimer.Tick += (_, _) => ClearFlash();

        // Pasting goes through our own check, like typing.
        DataObject.AddPastingHandler(DetailText, OnDetailPasting);
    }

    /// <summary>Raised when texts become unsaved or saved.</summary>
    public event EventHandler? UnsavedChangesChanged;

    /// <summary>True if at least one text was edited and not saved yet.</summary>
    public bool HasUnsavedChanges => _rows.Any(r => r.IsUnsaved);

    /// <summary>One line of the table. Public properties, because WPF data binding reads them.</summary>
    public sealed partial class TextRow : INotifyPropertyChanged
    {
        private string _markup;
        private string _savedMarkup;

        public TextRow(GameText text, string originalMarkup)
        {
            Text = text;
            _markup = text.Markup;
            _savedMarkup = text.Markup;
            OriginalMarkup = originalMarkup;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameText Text { get; }

        public int Index => Text.Index;

        public string Name => Text.Name;

        public string Area => Text.Category.Area;

        public int AreaOrder => Text.Category.AreaOrder;

        public string Section => Text.Category.Section;

        public int SectionOrder => Text.Category.SectionOrder;

        public string SortKey => Text.Category.SortKey;

        /// <summary>The text of the original game.</summary>
        public string OriginalMarkup { get; }

        /// <summary>The text as currently shown and edited.</summary>
        public string Markup
        {
            get => _markup;
            set
            {
                _markup = value;
                NotifyAll();
            }
        }

        /// <summary>The text as last saved in the project (or loaded from the game).</summary>
        public string SavedMarkup
        {
            get => _savedMarkup;
            set
            {
                _savedMarkup = value;
                NotifyAll();
            }
        }

        public bool IsUnsaved => _markup != _savedMarkup;

        public bool DiffersFromOriginal => _markup != OriginalMarkup;

        /// <summary>The text on one line, without markup, for the table.</summary>
        public string Preview => ToPreview(_markup);

        /// <summary>● for unsaved edits, ✎ for saved changes compared to the original game.</summary>
        public string StatusMark => IsUnsaved ? "●" : DiffersFromOriginal ? "✎" : string.Empty;

        public string? StatusText => IsUnsaved ? "Changed, not saved yet" : DiffersFromOriginal ? "Changed compared to the original game" : null;

        private void NotifyAll()
        {
            foreach (string name in new[] { nameof(Markup), nameof(SavedMarkup), nameof(Preview), nameof(StatusMark), nameof(StatusText), nameof(IsUnsaved) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>Turns markup into a single readable line: no tags, line breaks shown as ⏎.</summary>
        private static string ToPreview(string markup)
        {
            string text = markup.Replace("[b]", string.Empty).Replace("[/b]", string.Empty).Replace("[nonl]", string.Empty);
            text = PositionTag().Replace(text, "  ");
            return text.Trim('\n').Replace("\n", "  ⏎  ");
        }

        [GeneratedRegex(@"\[x=\d+(,y=\d+)?\]")]
        private static partial Regex PositionTag();
    }

    /// <summary>
    /// Loads the texts of a project (its own copy of the text file, or the original).
    /// Unsaved edits are dropped; the main window asks the user before calling this.
    /// Errors are shown inside the tab instead of interrupting the user.
    /// </summary>
    public void Load(EditorSession session)
    {
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
            ShowDetails(null);
            SourceText.Text = $"The texts could not be read: {ex.Message}";
            UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _library = library;
        _rows = library.Texts.Select(t => new TextRow(t, OriginalMarkupOf(original, t))).ToList();

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
        SourceText.Text = library.FromProject
            ? $"{library.Texts.Count} texts from the project, {changed} changed compared to the original game (marked ✎). Unsaved edits are marked ●."
            : $"{library.Texts.Count} texts from the original game. Edited texts are marked ● until saved.";
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
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
            if (TextValidator.Validate(_library.Codec, row.Markup, row.OriginalMarkup, MaxLines(row)).HasErrors)
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

        Dictionary<int, string> changes = _rows.Where(r => r.IsUnsaved).ToDictionary(r => r.Index, r => r.Markup);
        if (changes.Count == 0)
        {
            return 0;
        }

        session.SaveTexts(_library, changes);

        // Reload, so sizes and "saved" states come from the file that was just written.
        int? selected = (TextGrid.SelectedItem as TextRow)?.Index;
        Load(session);
        if (selected is { } index && _rows.FirstOrDefault(r => r.Index == index) is { } row)
        {
            TextGrid.SelectedItem = row;
            TextGrid.ScrollIntoView(row);
        }

        return changes.Count;
    }

    /// <summary>Drops all unsaved edits.</summary>
    public void DiscardChanges()
    {
        foreach (TextRow row in _rows.Where(r => r.IsUnsaved))
        {
            row.Markup = row.SavedMarkup;
        }

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
            || row.Text.Category.Description.Contains(_search, StringComparison.OrdinalIgnoreCase);
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

    private void ShowDetails(TextRow? row)
    {
        DetailPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelectionText.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        if (row is null)
        {
            return;
        }

        TextCategory category = row.Text.Category;
        DetailName.Text = row.Name;
        DetailCategory.Text = category.Description.Length > 0
            ? $"Text {row.Index}: {category.Area} › {category.Section} › {category.Description}"
            : $"Text {row.Index}: {category.Area} › {category.Section}";
        DetailUsage.Text = category.SourceFiles.Count > 0
            ? $"Used in the game code: {string.Join(", ", category.SourceFiles.Select(Path.GetFileName))}"
            : "Looked up by a name the game builds while running.";

        ClearFlash();
        _updatingTextBox = true;
        DetailText.Text = row.Markup;
        _updatingTextBox = false;

        UpdateValidation(row);
    }

    // ----------------------------------------------------------------- input rules
    //
    // The text box only accepts what the game can show:
    // - characters the font does not have are rejected (shown in red for a few seconds),
    // - a line never gets longer than the game can draw: a line break is inserted automatically,
    // - a text never gets more lines than its screen has room for (TextLimits).
    // Everything else (selecting, copying, cutting, undo, keyboard navigation) is the
    // normal behavior of the WPF TextBox. Changes are made through SelectedText, so they
    // stay part of the TextBox's undo history.

    /// <summary>Characters that may be typed besides the font's own: the brackets and '=' of tags.</summary>
    private const string TagCharacters = "[]=";

    /// <summary>
    /// Most lines this text may have: the room the game's screen offers (see TextLimits),
    /// or more if the saved text already has more lines (so it can still be shortened).
    /// </summary>
    private static int MaxLines(TextRow row) =>
        Math.Max(TextLimits.GetMaxLines(row.Name, row.OriginalMarkup), TextWrapper.CountLines(row.SavedMarkup));

    private bool IsAllowed(char c) =>
        _library is not null
        && (c == '\n' || TagCharacters.Contains(c) || _library.Font.TryGetGlyph(c, false, out _) || _library.Font.TryGetGlyph(c, true, out _));

    private void OnDetailPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SelectedRow is null || e.Text.Length == 0)
        {
            return;
        }

        // Let the TextBox handle normal typing itself (it then wraps in OnDetailTextChanged),
        // and only step in when something has to be rejected.
        if (e.Text.All(IsAllowed) && FitsLineLimit(e.Text))
        {
            return;
        }

        e.Handled = true;
        InsertChecked(e.Text);
    }

    private void OnDetailPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter is not text input in WPF; insert our own "\n" so the text never contains "\r\n".
        if (e.Key == Key.Enter && SelectedRow is not null)
        {
            e.Handled = true;
            InsertChecked("\n");
        }
    }

    private void OnDetailPasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string pasted)
        {
            InsertChecked(pasted.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' '));
        }
    }

    /// <summary>
    /// Inserts text at the cursor (replacing the selection), leaving out characters the font does
    /// not have, and refusing it completely if the text would get too many lines.
    /// </summary>
    private void InsertChecked(string input)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        int position = DetailText.SelectionStart;
        string allowed = new(input.Where(IsAllowed).ToArray());
        char? rejected = input.FirstOrDefault(c => !IsAllowed(c)) is char r and not '\0' ? r : null;

        if (!FitsLineLimit(allowed))
        {
            Flash($"This text can have at most {MaxLines(row)} line(s); the game's screen has no room for more.", null, position);
            return;
        }

        if (allowed.Length > 0)
        {
            DetailText.SelectedText = allowed;
            DetailText.SelectionLength = 0;
            DetailText.CaretIndex = position + allowed.Length;
        }

        if (rejected is { } c)
        {
            int count = input.Count(ch => !IsAllowed(ch));
            Flash(count == 1
                    ? $"'{c}' is not in the game's font."
                    : $"{count} characters are not in the game's font and were left out.",
                c,
                position + allowed.Length);
        }

        DetailText.Focus();
    }

    /// <summary>True if inserting the text at the cursor keeps the line count within the limit (after wrapping).</summary>
    private bool FitsLineLimit(string insert)
    {
        if (SelectedRow is not { } row)
        {
            return true;
        }

        string current = DetailText.Text.Replace("\r\n", "\n");
        int start = Math.Min(DetailText.SelectionStart, current.Length);
        int length = Math.Min(DetailText.SelectionLength, current.Length - start);
        string candidate = current.Remove(start, length).Insert(start, new string(insert.Where(IsAllowed).ToArray()));
        return TextWrapper.CountLines(TextWrapper.Wrap(candidate).Text) <= MaxLines(row);
    }

    private void OnDetailTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingTextBox || SelectedRow is not { } row)
        {
            return;
        }

        // Break lines that got too long (typing, deleting a line break, inserting a code).
        (_, IReadOnlyList<WrapOperation> operations) = TextWrapper.Wrap(DetailText.Text);
        if (operations.Count > 0)
        {
            ApplyWraps(operations);
        }

        bool wasUnsaved = row.IsUnsaved;
        row.Markup = DetailText.Text.Replace("\r\n", "\n");
        UpdateValidation(row);

        if (wasUnsaved != row.IsUnsaved)
        {
            UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Applies automatic line breaks while keeping the cursor at the same character.</summary>
    private void ApplyWraps(IReadOnlyList<WrapOperation> operations)
    {
        int caret = DetailText.CaretIndex;
        _updatingTextBox = true;
        try
        {
            foreach (WrapOperation op in operations)
            {
                DetailText.Select(op.Index, op.ReplacesSpace ? 1 : 0);
                DetailText.SelectedText = "\n";
                if (!op.ReplacesSpace && op.Index < caret)
                {
                    caret++;
                }
            }

            DetailText.SelectionLength = 0;
            DetailText.CaretIndex = Math.Min(caret, DetailText.Text.Length);
        }
        finally
        {
            _updatingTextBox = false;
        }
    }

    /// <summary>
    /// Shows a short message, and optionally the rejected character in red where it would have
    /// appeared. Both disappear after a few seconds.
    /// </summary>
    private void Flash(string message, char? character, int index)
    {
        ClearFlash();
        InputMessage.Text = message;
        InputMessage.Visibility = Visibility.Visible;

        if (character is { } c)
        {
            Rect place = DetailText.GetRectFromCharacterIndex(Math.Min(index, DetailText.Text.Length));
            if (!place.IsEmpty)
            {
                var glyph = new TextBlock
                {
                    Text = c.ToString(),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
                    FontFamily = DetailText.FontFamily,
                    FontSize = DetailText.FontSize,
                    FontWeight = FontWeights.Bold,
                };
                Canvas.SetLeft(glyph, place.X);
                Canvas.SetTop(glyph, place.Y);
                FlashLayer.Children.Add(glyph);
            }
        }

        _flashTimer.Start();
    }

    private void ClearFlash()
    {
        _flashTimer.Stop();
        FlashLayer.Children.Clear();
        InputMessage.Visibility = Visibility.Collapsed;
    }

    private void UpdateValidation(TextRow row)
    {
        UndoButton.IsEnabled = row.IsUnsaved;
        OriginalButton.IsEnabled = row.DiffersFromOriginal;

        if (_library is null)
        {
            return;
        }

        int maxLines = MaxLines(row);
        TextValidation result = TextValidator.Validate(_library.Codec, row.Markup, row.OriginalMarkup, maxLines);
        IssueList.ItemsSource = result.Issues;

        if (result.Layout is { } layout && result.OriginalLayout is { } original)
        {
            LayoutText.Text =
                $"{layout.Lines} of {maxLines} possible line(s), original {original.Lines}. " +
                $"Longest line {layout.LongestPiece} characters, original {original.LongestPiece}, " +
                $"game maximum {TextValidator.MaxCharactersPerPiece}.";
        }
        else
        {
            LayoutText.Text = string.Empty;
        }
    }

    private void OnInsertBold(object sender, RoutedEventArgs e)
    {
        string selected = DetailText.SelectedText;
        int start = DetailText.SelectionStart;
        DetailText.SelectedText = $"[b]{selected}[/b]";

        // Put the cursor inside the tags when nothing was selected, behind them otherwise.
        DetailText.SelectionLength = 0;
        DetailText.CaretIndex = selected.Length == 0 ? start + 3 : start + selected.Length + 7;
        DetailText.Focus();
    }

    private void OnInsertPosition(object sender, RoutedEventArgs e)
    {
        string? input = InputDialog.Ask(Window.GetWindow(this), "Column position",
            "Horizontal position where the rest of the line starts (0 = left edge, the screen is 320 wide):", "212");
        if (input is null)
        {
            return;
        }

        if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int x) || x > 320)
        {
            Ui.ShowError(Window.GetWindow(this), $"\"{input}\" is not a position between 0 and 320.");
            return;
        }

        InsertAtCursor($"[x={x}]");
    }

    private void OnInsertCode(object sender, RoutedEventArgs e)
    {
        string? input = InputDialog.Ask(Window.GetWindow(this), "Insert code",
            "Code number in hex, for example 5C (see the Codes list):", string.Empty);
        if (input is null)
        {
            return;
        }

        string hex = input.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase).TrimStart('#');
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) || code > 0xFC)
        {
            Ui.ShowError(Window.GetWindow(this), $"\"{input}\" is not a code between 00 and FC. Line breaks are entered with Enter.");
            return;
        }

        InsertAtCursor($"[#{code:X2}]");
    }

    private void InsertAtCursor(string text) => InsertChecked(text);

    private void OnUndoChanges(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
        {
            DetailText.Text = row.SavedMarkup; // triggers OnDetailTextChanged
        }
    }

    private void OnRestoreOriginal(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
        {
            DetailText.Text = row.OriginalMarkup;
        }
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
