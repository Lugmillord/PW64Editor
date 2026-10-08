using System.Text.RegularExpressions;
using PW64Editor.Core.Code;

namespace PW64Editor.Core.Text;

/// <summary>
/// Rules for texts the user adds to the game ("custom texts").
/// </summary>
/// <remarks>
/// <para>
/// The game finds texts by number (their position in the text file) or by name. The original 439
/// texts are never removed or moved, because the game's code uses them by number. Custom texts
/// come after them. Each new text gets the lowest free number; removing a custom text leaves a
/// free slot behind (an empty entry without a name), so all texts after it keep their numbers.
/// Free slots at the very end are dropped.
/// </para>
/// <para>
/// More than the 439 retail texts need the code fix <see cref="CodeFixes.ExpandedTexts"/>, which
/// gives the game room for <see cref="CodeFixes.ExpandedTextCapacity"/> texts.
/// </para>
/// </remarks>
public static partial class CustomTexts
{
    /// <summary>Area name under which custom texts are grouped.</summary>
    public const string AreaName = "Custom texts";

    /// <summary>Lines a custom text may have. Where it is shown later, less may fit.</summary>
    public const int MaxLines = 10;

    /// <summary>Longest allowed name.</summary>
    public const int MaxNameLength = 24;

    /// <summary>Total number of texts the game can hold with the code fix.</summary>
    public static int Capacity => CodeFixes.ExpandedTextCapacity;

    /// <summary>The category of a custom text, whatever its name looks like.</summary>
    public static TextCategory Category(string name) =>
        new(AreaName, 100, AreaName, 0, "Added text, not used by the game yet", name, []);

    /// <summary>
    /// Checks a proposed name. Returns null if it is fine, otherwise the reason.
    /// </summary>
    /// <param name="name">The name as entered.</param>
    /// <param name="existingNames">All names already used (original and custom texts).</param>
    public static string? CheckName(string name, IEnumerable<string> existingNames)
    {
        if (name.Length == 0)
        {
            return "The name must not be empty.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"The name may have at most {MaxNameLength} characters.";
        }

        if (!NamePattern().IsMatch(name))
        {
            return "The name may only contain the capital letters A-Z, the digits 0-9 and '_'.";
        }

        if (existingNames.Contains(name, StringComparer.Ordinal))
        {
            return $"There already is a text named {name}. Every name may be used only once.";
        }

        return null;
    }

    /// <summary>
    /// The lowest text number that is free for a new text, or null if the game has no room left.
    /// </summary>
    /// <param name="usedIndices">Numbers of all texts that exist (free slots not included).</param>
    /// <param name="originalCount">Number of original texts; new texts always come after them.</param>
    public static int? NextFreeIndex(IEnumerable<int> usedIndices, int originalCount)
    {
        var used = new HashSet<int>(usedIndices);
        for (int index = originalCount; index < Capacity; index++)
        {
            if (!used.Contains(index))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that the game can hold this many texts. Returns null if it can, otherwise the reason.
    /// </summary>
    /// <param name="count">Number of texts in the file (including free slots).</param>
    /// <param name="originalCount">Number of texts of the original game, the size of its own tables.</param>
    /// <param name="expandedTablesApplied">Whether the project applies <see cref="CodeFixes.ExpandedTexts"/>.</param>
    public static string? CountProblem(int count, int originalCount, bool expandedTablesApplied)
    {
        if (count > Capacity)
        {
            return $"The game can hold at most {Capacity} texts; the project has {count}.";
        }

        if (count > originalCount && !expandedTablesApplied)
        {
            return $"The project has {count} texts, but the game only has room for {originalCount} and would crash. " +
                   $"Apply the code fix \"{CodeFixes.ExpandedTexts.Name}\" in File › Project settings.";
        }

        return null;
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex NamePattern();
}
