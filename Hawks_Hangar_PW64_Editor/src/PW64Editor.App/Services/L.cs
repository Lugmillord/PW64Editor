using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using PW64Editor.Core.Localization;

namespace PW64Editor.App.Services;

/// <summary>A language of the editor's controls.</summary>
/// <param name="Code">Short code and file name of the language, e.g. "de" for Languages/de.json.</param>
/// <param name="NativeName">The language's name in itself, e.g. "Deutsch".</param>
/// <param name="Culture">The culture for fonts and formats, e.g. "de-DE".</param>
public sealed record UiLanguage(string Code, string NativeName, string Culture);

/// <summary>
/// Translations of the editor's controls (not of the game's content).
/// </summary>
/// <remarks>
/// <para>English is written directly in the code and in the XAML files. Every other language is a
/// file Languages/&lt;code&gt;.json (in the source code), which maps the English texts to the
/// translated ones: <c>{ "Open project…": "Projekt öffnen…" }</c>. The files are built into the
/// program, so nothing has to be shipped with it. A file with the same name in the editor's data
/// folder (%AppData%\HawksHangar\Languages) replaces single translations, so a translation can be
/// tried or improved without building the program. A text without translation stays English.</para>
/// <para>Texts in code use <see cref="T"/> and <see cref="F"/>; the core library uses
/// <see cref="CoreText"/>, which <see cref="Load"/> connects to the same table. Texts in XAML are translated
/// automatically when their control is laid out for the first time (<see cref="InstallAutoTranslation"/>):
/// the texts of text blocks, buttons, headers, menu items, tool tips and window titles, unless
/// they come from a data binding.</para>
/// <para>The language is chosen at the start of the program; changing it restarts the editor.</para>
/// </remarks>
public static class L
{
    /// <summary>The languages the editor offers, English first.</summary>
    public static IReadOnlyList<UiLanguage> Languages { get; } =
    [
        new("en", "English", "en-US"),
        new("de", "Deutsch", "de-DE"),
        new("fr", "Français", "fr-FR"),
        new("ja", "日本語", "ja-JP"),
    ];

    private static Dictionary<string, string> _table = new(StringComparer.Ordinal);

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>Code of the current language.</summary>
    public static string Code { get; private set; } = "en";

    /// <summary>The current language.</summary>
    public static UiLanguage Current => Find(Code);

    /// <summary>Folder for language files that replace translations of the built-in ones.</summary>
    public static string OverrideFolder => Path.Combine(EditorContext.Storage.RootFolder, "Languages");

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, object> Translated = new();

    /// <summary>
    /// Sets the language for this run of the program: loads its file, and sets the culture used
    /// for fonts (Japanese characters) and formats. The messages of the core library are translated
    /// with the same table (<see cref="CoreText"/>).
    /// </summary>
    public static void Load(string? code)
    {
        UiLanguage language = Find(code ?? "en");
        Code = language.Code;
        _table = Read(language.Code);

        var culture = CultureInfo.GetCultureInfo(language.Culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        _culture = culture;
        CoreText.Translator = language.Code == "en" ? null : T;
        CoreText.Culture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    /// <summary>The text in the current language (the English text if there is no translation).</summary>
    public static string T(string english) =>
        _table.TryGetValue(english, out string? text) && text.Length > 0 ? text : english;

    /// <summary>
    /// A text with placeholders {0}, {1}, … in the current language, filled in (numbers in its
    /// format). A translation with broken placeholders (for example in a file of the user) falls
    /// back to the English text instead of failing.
    /// </summary>
    public static string F(string englishFormat, params object?[] args)
    {
        try
        {
            return string.Format(_culture, T(englishFormat), args);
        }
        catch (FormatException)
        {
            return string.Format(_culture, englishFormat, args);
        }
    }

    /// <summary>Marks an English text for the language files without translating it (for texts used in several languages).</summary>
    public static string K(string english) => english;

    /// <summary>The text in another language (for the dialog that announces a change of language).</summary>
    public static string TIn(string code, string english) =>
        Read(code).TryGetValue(english, out string? text) && text.Length > 0 ? text : english;

    /// <summary>Finds a language by its code; unknown codes give English.</summary>
    public static UiLanguage Find(string code) =>
        Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];

    private static Dictionary<string, string> Read(string code)
    {
        if (code == "en")
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        // The built-in file (an embedded resource of the program).
        using (Stream? stream = typeof(L).Assembly.GetManifestResourceStream($"Languages.{code}.json"))
        {
            if (stream is not null)
            {
                Merge(table, () => JsonSerializer.Deserialize<Dictionary<string, string>>(stream));
            }
        }

        // An optional file in the data folder replaces single translations.
        string path = Path.Combine(OverrideFolder, $"{code}.json");
        if (File.Exists(path))
        {
            Merge(table, () => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)));
        }

        return table;
    }

    private static void Merge(Dictionary<string, string> table, Func<Dictionary<string, string>?> read)
    {
        try
        {
            foreach ((string english, string text) in read() ?? [])
            {
                table[english] = text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken language file leaves its texts in English; the editor stays usable.
        }
    }

    // ----------------------------------------------------------------- XAML texts

    /// <summary>
    /// Translates the texts of every control when it is loaded, also of controls that are made from
    /// templates or in code later. Does nothing for English.
    /// </summary>
    public static void InstallAutoTranslation()
    {
        if (Code == "en")
        {
            return;
        }

        // WPF raises Loaded only for elements that have a Loaded handler of their own, but every
        // element gets SizeChanged when it is laid out for the first time (also items of menus and
        // tool tips when they open). Each element is translated once.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((sender, _) => TranslateOnce((DependencyObject)sender)), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => TranslateOnce((DependencyObject)sender)), true);
    }

    private static void TranslateOnce(DependencyObject element)
    {
        if (Translated.TryGetValue(element, out _))
        {
            return;
        }

        Translated.Add(element, element);
        Translate(element);
    }

    /// <summary>Translates the fixed texts of one control (not its children).</summary>
    public static void Translate(DependencyObject element)
    {
        switch (element)
        {
            case Window window:
                window.Title = T(window.Title);
                break;
            case TextBlock block:
                TranslateTextBlock(block);
                break;
            case HeaderedContentControl headered when headered.Header is string header && !IsBound(headered, HeaderedContentControl.HeaderProperty):
                headered.Header = T(header);
                break;
            case HeaderedItemsControl menu when menu.Header is string header && !IsBound(menu, HeaderedItemsControl.HeaderProperty):
                menu.Header = T(header);
                break;
            case ListView { View: GridView grid }:
                foreach (GridViewColumn column in grid.Columns)
                {
                    if (column.Header is string header)
                    {
                        column.Header = T(header);
                    }
                }

                break;
            case DataGrid table:
                foreach (DataGridColumn column in table.Columns)
                {
                    if (column.Header is string header)
                    {
                        column.Header = T(header);
                    }
                }

                break;
        }

        if (element is ContentControl content and not HeaderedContentControl && content.Content is string text
            && !IsBound(content, ContentControl.ContentProperty))
        {
            content.Content = T(text);
        }

        if (element is FrameworkElement framework && framework.ToolTip is string tip && !IsBound(framework, FrameworkElement.ToolTipProperty))
        {
            framework.ToolTip = T(tip);
        }
    }

    /// <summary>Translates a text block: a plain text as a whole, mixed texts run by run (bound runs stay).</summary>
    private static void TranslateTextBlock(TextBlock block)
    {
        if (IsBound(block, TextBlock.TextProperty))
        {
            return;
        }

        List<Run> runs = block.Inlines.OfType<Run>().ToList();
        if (runs.Count == 1 && block.Inlines.Count == 1)
        {
            Run run = runs[0];
            if (!IsBound(run, Run.TextProperty) && run.Text.Length > 0)
            {
                string translated = T(run.Text);
                if (translated != run.Text)
                {
                    run.Text = translated;
                }
            }

            return;
        }

        foreach (Run run in runs.Where(r => !IsBound(r, Run.TextProperty) && r.Text.Length > 0))
        {
            run.Text = T(run.Text);
        }
    }

    private static bool IsBound(DependencyObject element, DependencyProperty property) =>
        BindingOperations.IsDataBound(element, property);
}
