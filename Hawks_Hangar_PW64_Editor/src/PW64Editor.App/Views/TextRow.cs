using System.ComponentModel;
using System.Text.RegularExpressions;
using PW64Editor.App.Services;
using PW64Editor.Core.Text;

namespace PW64Editor.App.Views;

/// <summary>
/// A game text as the editor shows and edits it (a row of the text table). The Text tab owns the
/// rows; other tabs (Missions) edit the same rows, so an edit shows up everywhere. Public
/// properties, because WPF data binding reads them.
/// </summary>
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

    public string Area => L.T(Text.Category.Area);

    public int AreaOrder => Text.Category.AreaOrder;

    public string Section => L.T(Text.Category.Section);

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

    /// <summary>True for a text added in this session and not saved yet.</summary>
    public bool IsNew { get; init; }

    /// <summary>True for texts added by the user (they come after the original texts).</summary>
    public bool IsCustom => Text.IsCustom;

    public bool IsUnsaved => IsNew || _markup != _savedMarkup;

    public bool DiffersFromOriginal => _markup != OriginalMarkup;

    /// <summary>The text on one line, without markup, for the table.</summary>
    public string Preview => ToPreview(_markup);

    /// <summary>● for unsaved edits, ✎ for saved changes compared to the original game.</summary>
    public string StatusMark => IsUnsaved ? "●" : IsCustom ? "+" : DiffersFromOriginal ? "✎" : string.Empty;

    public string? StatusText => IsNew ? L.T("Added, not saved yet") : IsUnsaved ? L.T("Changed, not saved yet")
        : Text.IsPatchText ? L.T("Added by a game patch") : IsCustom ? L.T("Added text") : DiffersFromOriginal ? L.T("Changed compared to the original game") : null;

    /// <summary>
    /// Most lines this text may have: the room the game's screen offers (see TextLimits),
    /// or more if the saved text already has more lines (so it can still be shortened).
    /// </summary>
    public int MaxLines =>
        Math.Max(IsCustom ? CustomTexts.MaxLines : TextLimits.GetMaxLines(Name, OriginalMarkup),
            TextWrapper.CountLines(SavedMarkup));

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
