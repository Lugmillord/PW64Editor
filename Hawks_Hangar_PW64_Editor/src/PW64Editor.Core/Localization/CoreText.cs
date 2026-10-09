using System.Globalization;

namespace PW64Editor.Core.Localization;

/// <summary>
/// Translation of the messages of the core library (errors, checks, notes of the text preview) into
/// the language of the editor.
/// </summary>
/// <remarks>
/// The core library itself knows no languages: it writes its messages in English and hands them to
/// <see cref="Translator"/>, which the editor sets when it starts (to its own translation table).
/// Without a translator, for example in tests, every message stays English. The keys are the
/// English texts; formats use the placeholders {0}, {1}, … of <see cref="string.Format(IFormatProvider, string, object[])"/>.
/// Technical messages about damaged data or errors of the program itself stay English, because
/// they are meant for developers.
/// </remarks>
public static class CoreText
{
    /// <summary>Translates an English text; null keeps every text English.</summary>
    public static Func<string, string>? Translator { get; set; }

    /// <summary>Culture for numbers in formatted messages (thousands separators, for example).</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>The text in the editor's language.</summary>
    public static string T(string english) => Translator?.Invoke(english) ?? english;

    /// <summary>
    /// A text with placeholders {0}, {1}, … in the editor's language, filled in. A translation with
    /// broken placeholders falls back to the English text instead of failing.
    /// </summary>
    public static string F(string englishFormat, params object?[] args)
    {
        try
        {
            return string.Format(Culture, T(englishFormat), args);
        }
        catch (FormatException)
        {
            return string.Format(Culture, englishFormat, args);
        }
    }

    /// <summary>
    /// Marks an English text for the language files without translating it. Used for texts that
    /// are stored once and translated where they are shown (see <see cref="T"/>).
    /// </summary>
    public static string K(string english) => english;
}
