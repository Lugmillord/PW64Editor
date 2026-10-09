using System.Text.RegularExpressions;
using System.Text;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Text;

/// <summary>A text the import may change: its current state and its limits.</summary>
/// <param name="Index">The text's ID.</param>
/// <param name="Name">The text's name, e.g. "A_HG_1_M" (for the checks of number placeholders).</param>
/// <param name="OriginalMarkup">The text of the original game (for the comparison the validator makes).</param>
/// <param name="CurrentMarkup">The text as it is now in the editor.</param>
/// <param name="MaxLines">Most lines the text may have.</param>
public sealed record TextImportTarget(int Index, string Name, string OriginalMarkup, string CurrentMarkup, int MaxLines);

/// <summary>A row of the CSV file that was not imported.</summary>
/// <param name="Id">The ID as written in the file (or "line n" if the row has none).</param>
/// <param name="Reason">A short reason, e.g. "Too long: 6 lines, at most 4".</param>
public sealed record TextImportRejection(string Id, string Reason);

/// <summary>The result of <see cref="TextCsv.Import"/>.</summary>
/// <param name="Changes">The texts to change: ID → new markup.</param>
/// <param name="Unchanged">Valid rows whose text is the same as now.</param>
/// <param name="Rejected">Rows that were not imported, in file order.</param>
public sealed record TextImportResult(IReadOnlyDictionary<int, string> Changes, int Unchanged, IReadOnlyList<TextImportRejection> Rejected);

/// <summary>
/// Exports all texts as a CSV table (separator ';', columns ID and Text) and imports such a
/// table again. Line breaks are written as [enter], because one text has to stay in one row.
/// </summary>
/// <remarks>
/// The import treats every text like typed text in the editor: long lines are broken
/// automatically, and the text has to pass the same checks as before saving. A row is rejected
/// (the current text stays) if the ID is not a number, no text has this ID, the ID came before in
/// the file, the text has characters or tags the game cannot show, it has more lines than its
/// screen allows, or another check of the editor fails (lines and [x=...] parts per screen,
/// positions 254/255, number placeholders).
/// </remarks>
public static partial class TextCsv
{
    /// <summary>What a line break looks like in the file.</summary>
    public const string LineBreakTag = "[enter]";

    /// <summary>The separator between the columns.</summary>
    public const char Separator = ';';

    private const string HeaderId = "ID";

    /// <summary>Builds the file: a header row and one row per text, sorted by ID.</summary>
    public static string Export(IEnumerable<(int Index, string Markup)> texts)
    {
        var csv = new StringBuilder();
        csv.Append(HeaderId).Append(Separator).Append("Text").Append("\r\n");
        foreach ((int index, string markup) in texts.OrderBy(t => t.Index))
        {
            string text = markup.Replace("\r\n", "\n").Replace("\n", LineBreakTag);
            csv.Append(index).Append(Separator).Append(Quote(text)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>The file's bytes as UTF-8 with a byte order mark, so spreadsheet programs read the characters right.</summary>
    public static byte[] ToFileBytes(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    /// <summary>
    /// Reads a file's bytes: UTF-8 (with or without byte order mark), or Windows Latin-1 if the bytes
    /// are no valid UTF-8 (spreadsheet programs often save CSV files like that).
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.StartsWith(Encoding.UTF8.GetPreamble()))
        {
            data = data[3..];
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    /// <summary>Checks all rows and returns the texts to change and the rows that are rejected.</summary>
    /// <param name="content">The file's text.</param>
    /// <param name="codec">The game's text codec.</param>
    /// <param name="targets">All texts that exist, by ID.</param>
    public static TextImportResult Import(string content, TextCodec codec, IReadOnlyDictionary<int, TextImportTarget> targets)
    {
        var changes = new Dictionary<int, string>();
        var rejected = new List<TextImportRejection>();
        var seen = new HashSet<int>();
        int unchanged = 0;

        foreach ((int line, List<string> fields) in ReadRecords(content))
        {
            string idText = fields[0].Trim();
            if (fields.Count == 1 && idText.Length == 0)
            {
                continue; // empty row
            }

            if (seen.Count == 0 && changes.Count == 0 && rejected.Count == 0 && unchanged == 0
                && idText.Equals(HeaderId, StringComparison.OrdinalIgnoreCase))
            {
                continue; // header row
            }

            string id = idText.Length > 0 ? idText : CoreText.F("line {0}", line);
            if (fields.Count < 2)
            {
                rejected.Add(new(id, CoreText.F("No '{0}' between ID and text", Separator)));
                continue;
            }

            if (!int.TryParse(idText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                rejected.Add(new(id, CoreText.T("The ID is not a number")));
                continue;
            }

            if (!targets.TryGetValue(index, out TextImportTarget? target))
            {
                rejected.Add(new(id, CoreText.T("There is no text with this ID")));
                continue;
            }

            if (!seen.Add(index))
            {
                rejected.Add(new(id, CoreText.T("The ID appears more than once; only its first row was used")));
                continue;
            }

            // A ';' inside an unquoted text splits it into more fields: put them together again.
            string text = LineBreakTagRegex().Replace(string.Join(Separator, fields.Skip(1)).Replace("\r\n", "\n"), "\n");
            if (text == target.CurrentMarkup)
            {
                // Unchanged texts are kept as they are (some original texts have longer lines than the editor allows).
                unchanged++;
                continue;
            }

            string markup = TextWrapper.Wrap(text).Text;
            if (Check(codec, markup, target) is { } problem)
            {
                rejected.Add(new(id, problem));
            }
            else if (markup == target.CurrentMarkup)
            {
                unchanged++;
            }
            else
            {
                changes[index] = markup;
            }
        }

        return new TextImportResult(changes, unchanged, rejected);
    }

    /// <summary>The first problem of a text in short words, or null if it can be used.</summary>
    private static string? Check(TextCodec codec, string markup, TextImportTarget target)
    {
        TextEncodeResult encoded = codec.Encode(markup);
        if (encoded.Errors.Count > 0)
        {
            TextEncodeError error = encoded.Errors[0];
            int line = 1 + markup.Take(Math.Min(error.Position, markup.Length)).Count(c => c == '\n');
            return CoreText.F("Line {0}: {1}", line, error.Message);
        }

        int lines = TextWrapper.CountLines(markup);
        if (lines > target.MaxLines)
        {
            return CoreText.F("Too long: {0} lines (after automatic line breaks), at most {1}", lines, target.MaxLines);
        }

        TextValidation validation = TextValidator.Validate(codec, markup, target.OriginalMarkup, target.MaxLines, target.Name);
        return validation.Issues.FirstOrDefault(i => i.IsError)?.Message;
    }

    /// <summary>
    /// Splits the file into records and fields (separator ';', fields may be quoted with '"',
    /// "" inside quotes is one '"', quoted fields may contain line breaks).
    /// </summary>
    /// <returns>The line number where each record starts, and its fields.</returns>
    private static IEnumerable<(int Line, List<string> Fields)> ReadRecords(string content)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool fieldStarted = false;
        int line = 1;
        int recordLine = 1;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when !fieldStarted:
                    quoted = true;
                    fieldStarted = true;
                    break;
                case Separator:
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    yield return (recordLine, fields);
                    fields = [];
                    field.Clear();
                    fieldStarted = false;
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (fieldStarted || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (recordLine, fields);
        }
    }

    /// <summary>Quotes a field if it contains the separator, quotes or line breaks, or starts or ends with a space.</summary>
    private static string Quote(string field) =>
        field.IndexOfAny([Separator, '"', '\n', '\r']) >= 0 || field.StartsWith(' ') || field.EndsWith(' ')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;

    [GeneratedRegex(@"\[enter\]", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTagRegex();
}
