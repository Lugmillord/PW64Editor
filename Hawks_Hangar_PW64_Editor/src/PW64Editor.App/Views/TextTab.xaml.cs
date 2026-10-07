using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using PW64Editor.App.Services;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Shows all game texts, grouped by where they appear in the game, with a search.
/// Read-only for now; editing will follow.
/// </summary>
public partial class TextTab : UserControl
{
    private ListCollectionView? _view;
    private GameTextLibrary? _library;
    private string _search = string.Empty;

    public TextTab()
    {
        InitializeComponent();
    }

    /// <summary>One line of the table. Public properties, because WPF data binding reads them.</summary>
    public sealed class TextRow
    {
        public TextRow(GameText text)
        {
            Text = text;
            Preview = ToPreview(text.Markup);
        }

        public GameText Text { get; }

        public int Index => Text.Index;

        public string Name => Text.Name;

        public string Area => Text.Category.Area;

        public int AreaOrder => Text.Category.AreaOrder;

        public string Section => Text.Category.Section;

        public int SectionOrder => Text.Category.SectionOrder;

        public string SortKey => Text.Category.SortKey;

        /// <summary>The text on one line, without markup, for the table.</summary>
        public string Preview { get; }
    }

    /// <summary>
    /// Loads the texts of a project (its own copy of the text file, or the original).
    /// Errors are shown inside the tab instead of interrupting the user.
    /// </summary>
    public void Load(EditorSession session)
    {
        GameTextLibrary library;
        try
        {
            library = session.LoadTexts();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            TextGrid.ItemsSource = null;
            _view = null;
            _library = null;
            ShowDetails(null);
            SourceText.Text = $"The texts could not be read: {ex.Message}";
            return;
        }

        _library = library;
        var view = new ListCollectionView(library.Texts.Select(t => new TextRow(t)).ToList());

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

        SourceText.Text = library.FromProject
            ? $"{library.Texts.Count} texts from the project's copy of the text file."
            : $"{library.Texts.Count} texts from the original game.";
    }

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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDetails(TextGrid.SelectedItem as TextRow);

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
        DetailText.Text = row.Text.Markup;
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
