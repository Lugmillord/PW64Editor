using System.Text.RegularExpressions;

namespace PW64Editor.Core.Text;

/// <summary>
/// How many lines each text may have, based on the room the game's screens offer.
/// </summary>
/// <remarks>
/// <para>Room per kind of text (measured in the game):</para>
/// <code>
/// Tutorial pages             HG_6_A, RP_1_A            7 lines
/// Mission descriptions/hints A_HG_2_M, B_GC_1_H, ...   7 lines (also bonus games: A_EX_1_M)
/// Score sheets, sheet 1      HG_B3_S1, SD_L123_S1      10 lines (also Birdman: BD_ALL_S1)
/// Score sheets, sheet 2      HG_B3_S2, CB_L123_S2      7 lines (also Birdman: BD_ALL_S2)
/// Birdman BD_ALL_S3          (apparently unused)       1 line
/// Everything else            as many as the original text
/// </code>
/// <para>
/// The limit is never lower than the original text's own line count, so every original text
/// stays valid.
/// </para>
/// </remarks>
public static partial class TextLimits
{
    public const int TutorialLines = 7;
    public const int MissionTextLines = 7;
    public const int ScoreSheet1Lines = 10;
    public const int ScoreSheet2Lines = 7;
    public const int ScoreSheet3Lines = 1;

    /// <summary>
    /// Returns the most lines a text may have.
    /// </summary>
    /// <param name="name">The text's name, e.g. "A_HG_2_M".</param>
    /// <param name="originalMarkup">The text of the original game.</param>
    public static int GetMaxLines(string name, string originalMarkup)
    {
        int original = TextWrapper.CountLines(originalMarkup);
        int room = GetRoom(name) ?? original;
        return Math.Max(room, original);
    }

    /// <summary>The fixed room for a kind of text, or null if the text has no fixed rule.</summary>
    public static int? GetRoom(string name)
    {
        if (TutorialPattern().IsMatch(name))
        {
            return TutorialLines;
        }

        if (MissionTextPattern().IsMatch(name))
        {
            return MissionTextLines;
        }

        if (ScoreSheetPattern().Match(name) is { Success: true } sheet)
        {
            return sheet.Groups["sheet"].Value switch
            {
                "1" => ScoreSheet1Lines,
                "2" => ScoreSheet2Lines,
                _ => ScoreSheet3Lines,
            };
        }

        return null;
    }

    [GeneratedRegex(@"^(HG|RP|GC)_\d+_A$")]
    private static partial Regex TutorialPattern();

    [GeneratedRegex(@"^(E|B|A|P)_(HG|RP|GC|BD|EX)_\d+_(M|H)$")]
    private static partial Regex MissionTextPattern();

    [GeneratedRegex(@"^(?:(?:HG|RP|GC)_(?:E|B|A|P)\d?|(?:SD|CB|HP)_L\d+)_S(?<sheet>[12])$|^BD_ALL_S(?<sheet>[123])$")]
    private static partial Regex ScoreSheetPattern();
}
