using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Code;
using PW64Editor.Core.Text;
using PW64Editor.Core.Text.Preview;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>What a <see cref="TextEditorPanel"/> needs from the tab that owns the texts.</summary>
public interface ITextEditorHost
{
    /// <summary>The loaded texts (codec and font), or null if they could not be read.</summary>
    GameTextLibrary? Library { get; }

    /// <summary>Draws texts like the game; null if the game's fonts could not be read.</summary>
    TextPreviewRenderer? PreviewRenderer { get; }

    /// <summary>Why there is no preview, if <see cref="PreviewRenderer"/> is null.</summary>
    string? PreviewProblem { get; }

    /// <summary>The open project, to know which code fixes the preview has to take into account.</summary>
    EditorSession? Session { get; }

    /// <summary>The current version of another text, encoded, for screens that show several texts.</summary>
    IReadOnlyList<ushort>? OtherTextCodes(string name);

    /// <summary>Called after the user edited a text in a panel.</summary>
    /// <param name="row">The edited text.</param>
    /// <param name="wasUnsaved">Whether the text had unsaved edits before.</param>
    void OnTextEdited(TextRow row, bool wasUnsaved);

    /// <summary>Called when the user wants to remove an added text (only offered if <see cref="TextEditorPanel.AllowRemove"/>).</summary>
    void RemoveText(TextRow row);
}

/// <summary>
/// The editor of one game text: a text box that only accepts what the game can show, formatting
/// buttons, the checks and the in-game preview. The Text tab and the Missions tab both use it and
/// edit the same <see cref="TextRow"/> objects, so an edit is visible in both.
/// </summary>
public partial class TextEditorPanel : UserControl
{
    private ITextEditorHost? _host;
    private TextRow? _row;

    /// <summary>Name of the text the preview picture currently shows.</summary>
    private string? _previewName;

    /// <summary>Set while the text box is filled by code, so that does not count as an edit.</summary>
    private bool _updatingTextBox;

    /// <summary>Set while this panel writes the row, so its own change notification is ignored.</summary>
    private bool _writingRow;

    /// <summary>Removes the message about rejected input (and the red character) after a few seconds.</summary>
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public TextEditorPanel()
    {
        InitializeComponent();
        _flashTimer.Tick += (_, _) => ClearFlash();

        // Pasting goes through our own check, like typing.
        DataObject.AddPastingHandler(DetailText, OnDetailPasting);
    }

    /// <summary>True to show name, category and usage of the text above the text box.</summary>
    public bool ShowHeader
    {
        get => HeaderPanel.Visibility == Visibility.Visible;
        set => HeaderPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True to offer "Remove text" for added texts.</summary>
    public bool AllowRemove { get; set; }

    /// <summary>True to only show the text and its preview (no editing, no buttons).</summary>
    public bool IsReadOnly
    {
        get => DetailText.IsReadOnly;
        set
        {
            DetailText.IsReadOnly = value;
            ButtonPanel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Caption above the text box (default "Text").</summary>
    public string Caption
    {
        get => TextLabel.Text;
        set => TextLabel.Text = value;
    }

    /// <summary>A note above the text box (null or empty: none).</summary>
    public string? Note
    {
        get => NoteText.Text;
        set
        {
            NoteText.Text = value ?? string.Empty;
            NoteText.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Starting height of the text box in pixels (it can be dragged).</summary>
    public double TextBoxHeight
    {
        get => TextBoxRow.Height.Value;
        set => TextBoxRow.Height = new GridLength(value);
    }

    /// <summary>The text shown, or null.</summary>
    public TextRow? Row => _row;

    /// <summary>Connects the panel to the tab that owns the texts.</summary>
    public void Attach(ITextEditorHost host) => _host = host;

    /// <summary>Shows a text for editing (null: nothing).</summary>
    public void Show(TextRow? row)
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
        }

        _row = row;
        ClearFlash();
        if (row is null)
        {
            _updatingTextBox = true;
            DetailText.Text = string.Empty;
            _updatingTextBox = false;
            DetailText.IsEnabled = false;
            ButtonPanel.IsEnabled = false;
            PreviewImage.Source = null;
            LayoutText.Text = string.Empty;
            IssueList.ItemsSource = null;
            PreviewInfo.Text = string.Empty;
            PreviewIssueList.ItemsSource = null;
            PreviewNote.Text = string.Empty;
            PreviewTitle.Text = string.Empty;
            return;
        }

        row.PropertyChanged += OnRowChanged;
        DetailText.IsEnabled = true;
        ButtonPanel.IsEnabled = true;

        TextCategory category = row.Text.Category;
        DetailName.Text = row.Name;
        DetailCategory.Text = category.Description.Length > 0
            ? L.F("Text {0}: {1} › {2} › {3}", row.Index, L.T(category.Area), L.T(category.Section), DescriptionOf(category))
            : L.F("Text {0}: {1} › {2}", row.Index, L.T(category.Area), L.T(category.Section));
        DetailUsage.Text = row.IsCustom
            ? L.T("Added text. The game finds it by its number or its name, but does not use it anywhere yet.")
            : category.SourceFiles.Count > 0
                ? L.F("Used in the game code: {0}", string.Join(", ", category.SourceFiles.Select(Path.GetFileName)))
                : L.T("Looked up by a name the game builds while running.");
        if (TextNumberSlots.Find(row.Name) is { } slot)
        {
            DetailUsage.Text += " " + L.F("The game writes a number into {0} of this text.", TextNumberSlots.Describe(slot));
        }

        CustomWarning.Visibility = row.IsCustom ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = AllowRemove && row.IsCustom ? Visibility.Visible : Visibility.Collapsed;

        _updatingTextBox = true;
        DetailText.Text = row.Markup;
        _updatingTextBox = false;

        UpdateValidation(row);
    }

    /// <summary>Puts the keyboard focus into the text box.</summary>
    public void FocusText() => DetailText.Focus();

    /// <summary>Draws the preview again (for example after a code fix changed how texts are drawn).</summary>
    public void RefreshPreview()
    {
        if (_row is { } row)
        {
            UpdatePreview(row);
        }
    }

    /// <summary>The description of a text's category in the editor's language.</summary>
    public static string DescriptionOf(TextCategory category) =>
        category.DescriptionPattern.Length > 0
            ? L.F(category.DescriptionPattern, category.DescriptionArgs.Select(a => (object?)L.T(a)).ToArray())
            : L.T(category.Description);

    /// <summary>The text was changed elsewhere (other panel, CSV import, undo): show the new version.</summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_writingRow || sender != _row || _row is not { } row || e.PropertyName != nameof(TextRow.Markup))
        {
            return;
        }

        if (DetailText.Text.Replace("\r\n", "\n") != row.Markup)
        {
            _updatingTextBox = true;
            DetailText.Text = row.Markup;
            _updatingTextBox = false;
        }

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

    private bool IsAllowed(char c) =>
        _host?.Library is { } library
        && (c == '\n' || TagCharacters.Contains(c) || library.Font.TryGetGlyph(c, false, out _) || library.Font.TryGetGlyph(c, true, out _));

    private void OnDetailPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_row is null || e.Text.Length == 0)
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
        if (e.Key == Key.Enter && _row is not null && !DetailText.IsReadOnly)
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
        if (_row is not { } row || DetailText.IsReadOnly)
        {
            return;
        }

        int position = DetailText.SelectionStart;
        string allowed = new(input.Where(IsAllowed).ToArray());
        char? rejected = input.FirstOrDefault(c => !IsAllowed(c)) is char r and not '\0' ? r : null;

        if (!FitsLineLimit(allowed))
        {
            Flash(L.F("This text can have at most {0} line(s); the game's screen has no room for more.", row.MaxLines), null, position);
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
                    ? L.F("'{0}' is not in the game's font.", c)
                    : L.F("{0} characters are not in the game's font and were left out.", count),
                c,
                position + allowed.Length);
        }

        DetailText.Focus();
    }

    /// <summary>True if inserting the text at the cursor keeps the line count within the limit (after wrapping).</summary>
    private bool FitsLineLimit(string insert)
    {
        if (_row is not { } row)
        {
            return true;
        }

        string current = DetailText.Text.Replace("\r\n", "\n");
        int start = Math.Min(DetailText.SelectionStart, current.Length);
        int length = Math.Min(DetailText.SelectionLength, current.Length - start);
        string candidate = current.Remove(start, length).Insert(start, new string(insert.Where(IsAllowed).ToArray()));
        return TextWrapper.CountLines(TextWrapper.Wrap(candidate).Text) <= row.MaxLines;
    }

    private void OnDetailTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingTextBox || _row is not { } row || DetailText.IsReadOnly)
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
        _writingRow = true;
        try
        {
            row.Markup = DetailText.Text.Replace("\r\n", "\n");
        }
        finally
        {
            _writingRow = false;
        }

        UpdateValidation(row);
        _host?.OnTextEdited(row, wasUnsaved);
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
        UndoButton.IsEnabled = row.IsUnsaved && !row.IsNew;
        OriginalButton.IsEnabled = row.DiffersFromOriginal && !row.IsCustom;

        if (_host?.Library is not { } library)
        {
            return;
        }

        int maxLines = row.MaxLines;
        TextValidation result = TextValidator.Validate(library.Codec, row.Markup, row.OriginalMarkup, maxLines, row.Name);
        IssueList.ItemsSource = result.Issues;

        if (result.Layout is { } layout && result.OriginalLayout is { } original)
        {
            LayoutText.Text =
                L.F("{0} of {1} possible line(s), original {2}. Longest line {3} characters, original {4}, allowed maximum {5}.",
                    layout.Lines, maxLines, original.Lines, layout.LongestPiece, original.LongestPiece, TextValidator.MaxCharactersPerPiece);
        }
        else
        {
            LayoutText.Text = string.Empty;
        }

        UpdatePreview(row);
    }

    // ----------------------------------------------------------------- in-game preview

    /// <summary>Draws the text the way the game shows it.</summary>
    private void UpdatePreview(TextRow row)
    {
        if (_host?.Library is not { } library || _host.PreviewRenderer is not { } renderer)
        {
            PreviewImage.Source = null;
            PreviewTitle.Text = string.Empty;
            PreviewInfo.Text = _host?.PreviewProblem ?? string.Empty;
            PreviewIssueList.ItemsSource = null;
            PreviewNote.Text = string.Empty;
            return;
        }

        TextEncodeResult encoded = library.Codec.Encode(row.Markup);
        if (!encoded.Success)
        {
            // Keep the last picture of this text, so it does not flicker while typing a tag.
            if (_previewName != row.Name)
            {
                PreviewImage.Source = null;
            }

            PreviewInfo.Text = L.T("The text has errors (see above). The preview shows the last version without errors.");
            PreviewIssueList.ItemsSource = null;
            return;
        }

        bool safeText = _host.Session?.Project.Settings.AppliedCodeFixes.Contains(CodeFixes.SafeText.Id) == true;
        var options = new TextPreviewOptions(ShowTextArea: PreviewAreaCheck.IsChecked == true, SafeTextFix: safeText);
        TextPreview preview;
        try
        {
            preview = renderer.Render(row.Name, row.Text.Category, encoded.Codes, _host.OtherTextCodes, options);
        }
        catch (Exception ex)
        {
            // The preview is only a help; a problem in it must never stop the user from editing.
            PreviewImage.Source = null;
            PreviewInfo.Text = L.F("The preview could not be drawn: {0}", ex.Message);
            PreviewIssueList.ItemsSource = null;
            return;
        }

        BitmapSource bitmap = BitmapSource.Create(TextPreview.Width, TextPreview.Height, 96, 96,
            PixelFormats.Bgra32, null, preview.Pixels, TextPreview.Width * 4);
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
        _previewName = row.Name;

        PreviewTitle.Text = preview.ScreenTitle;
        PreviewNote.Text = preview.ScreenNote;
        PreviewIssueList.ItemsSource = preview.Issues;
        PreviewInfo.Text = DescribeRoom(preview);
    }

    /// <summary>A sentence about the widest line and the room on the screen.</summary>
    private static string DescribeRoom(TextPreview preview)
    {
        string pieces = L.F("Pieces on this screen: {0} of {1}.", preview.PiecesOnScreen, TextValidator.MaxPiecesPerFrame);
        if (preview.WidestLine is not { } widest || widest.Width == 0)
        {
            return pieces;
        }

        string text = L.F("Widest line: line {0}, {1} pixels (ends at x = {2}).", widest.Line, widest.Width, widest.Right);
        if (preview.TextArea is { } area)
        {
            int left = area.Right - widest.Right;
            text += left >= 0
                ? " " + L.F("Room up to x = {0}: {1} pixel(s) left.", area.Right, left)
                : " " + L.F("Room up to x = {0}: {1} pixel(s) too wide.", area.Right, -left);
        }

        return $"{text} {pieces}";
    }

    /// <summary>
    /// Passes mouse wheel turns over the preview on to the scroller around the panel. The
    /// preview's own scroller only scrolls sideways, but would otherwise swallow the wheel.
    /// </summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null and not ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        (parent as UIElement)?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        });
    }

    private void OnPreviewOptionChanged(object sender, RoutedEventArgs e)
    {
        // Also raised while the XAML is loaded, before all elements exist.
        if (PreviewImage is null || PreviewScroller is null || PreviewZoom is null)
        {
            return;
        }

        int zoom = PreviewZoom.SelectedIndex;
        if (zoom <= 0)
        {
            // Fit: as wide as the panel, keeping the 4:3 shape (the height follows the width).
            PreviewScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewImage.Stretch = Stretch.Uniform;
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
        }
        else
        {
            // Fixed size; if it is wider than the panel, the preview scrolls sideways.
            PreviewScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            PreviewImage.Stretch = Stretch.Fill;
            PreviewImage.Width = TextPreview.Width * zoom;
            PreviewImage.Height = TextPreview.Height * zoom;
        }

        if (_row is { } row)
        {
            UpdatePreview(row);
        }
    }

    // ----------------------------------------------------------------- buttons

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
        string? input = InputDialog.Ask(Window.GetWindow(this), L.T("Column position"),
            L.T("Horizontal position where the rest of the line starts (0 = left edge, the screen is 320 wide):"), "212");
        if (input is null)
        {
            return;
        }

        if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int x) || x > 320)
        {
            Ui.ShowError(Window.GetWindow(this), L.F("\"{0}\" is not a position between 0 and 320.", input));
            return;
        }

        if (x is TextCodec.CodeLineBreak or TextCodec.CodeEnd)
        {
            Ui.ShowError(Window.GetWindow(this),
                L.F("The game cannot use position {0}: while loading, it turns 254 and 255 into its line break and end codes. Use {1} or {2}.", x, x - 2, x + 2));
            return;
        }

        InsertChecked($"[x={x}]");
    }

    private void OnInsertCode(object sender, RoutedEventArgs e)
    {
        string? input = InputDialog.Ask(Window.GetWindow(this), L.T("Insert code"),
            L.T("Code number in hex, for example 5C (see the Codes list):"), string.Empty);
        if (input is null)
        {
            return;
        }

        string hex = input.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase).TrimStart('#');
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) || code > 0xFC)
        {
            Ui.ShowError(Window.GetWindow(this), L.F("\"{0}\" is not a code between 00 and FC. Line breaks are entered with Enter.", input));
            return;
        }

        InsertChecked($"[#{code:X2}]");
    }

    private void OnUndoChanges(object sender, RoutedEventArgs e)
    {
        if (_row is { } row)
        {
            DetailText.Text = row.SavedMarkup; // triggers OnDetailTextChanged
        }
    }

    private void OnRestoreOriginal(object sender, RoutedEventArgs e)
    {
        if (_row is { } row)
        {
            DetailText.Text = row.OriginalMarkup;
        }
    }

    private void OnRemoveText(object sender, RoutedEventArgs e)
    {
        if (_row is { } row)
        {
            _host?.RemoveText(row);
        }
    }
}
