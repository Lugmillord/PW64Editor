using System.Globalization;
using System.Text.RegularExpressions;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Classes;

/// <summary>
/// The medal texts of the total screen for classes with own limits. The game shares one text per class for the three
/// license vehicles (E_S3_GOLD, A_S3_GOLD, …) and one for all bonus games and levels (BONUS_S3_GOLD); their header
/// names the limit ("&lt; 180 pts. for Gold Badge &gt;"). A class with own limits gets its own bronze, silver and gold
/// texts (HG_A_S3_GOLD, SD_L2_S3_SILVER, …), written by the editor from the shared text of the project with the new
/// number; the game module shows them (module_tot_medal_text). The texts for a perfect score and "Perfect score!!!" do
/// not name a limit and stay shared. The Birdman's stage texts are written by <see cref="Missions.ResultTables"/>.
/// A class of a main vehicle with another number of tests than in the game (more or fewer missions) gets all five texts,
/// written anew with one line per test (the game's list its tests), also without own limits.
/// </summary>
public static partial class ClassTexts
{
    /// <summary>The medal texts with a limit: bronze ("BLONDS" in the game's names), silver, gold.</summary>
    public static IReadOnlyList<string> Medals { get; } = ["BLONDS", "SILVER", "GOLD"];

    /// <summary>All five medal texts of a total screen: bronze, silver, gold, perfect score and "Perfect score!!!".</summary>
    public static IReadOnlyList<string> AllMedals { get; } = ["BLONDS", "SILVER", "GOLD", "PERFECT", "GREAT"];

    private static readonly string[] Prefixes = ["HG", "RP", "GC", "CB", "SD", "HP"];

    private const string ClassLetters = "EABP";

    /// <summary>True for the vehicles whose classes get own texts (not the Birdman: its stage texts are generated anyway).</summary>
    public static bool HasOwnTexts(Vehicle vehicle) => vehicle != Vehicle.Birdman;

    /// <summary>Name of a class's own medal text (medal 0 bronze, 1 silver, 2 gold), or null for the Birdman.</summary>
    public static string? TextName(Vehicle vehicle, int classIndex, int medal) =>
        !HasOwnTexts(vehicle) || medal is < 0 or > 4 || classIndex is < 0 or > 3 || (medal > 2 && !Vehicles.IsMain(vehicle))
            ? null
            : Vehicles.IsMain(vehicle)
                ? $"{Prefixes[(int)vehicle]}_{ClassLetters[classIndex]}_S3_{AllMedals[medal]}"
                : $"{Prefixes[(int)vehicle]}_L{classIndex + 1}_S3_{AllMedals[medal]}";

    /// <summary>
    /// The classes of the main vehicles whose number of tests differs from the game's (they get all five texts of their own),
    /// with their tests.
    /// </summary>
    /// <param name="missions">The missions of the game, or null for the game's own.</param>
    public static IReadOnlyList<(Vehicle Vehicle, int Class, int Tests)> ChangedClasses(IEnumerable<TestId>? missions) =>
        missions is null
            ? []
            : [.. missions.Where(m => Vehicles.IsMain(m.Vehicle) && m.Class is >= 0 and < 4).GroupBy(m => (m.Vehicle, m.Class))
                .Select(g => (g.Key.Vehicle, g.Key.Class, Tests: g.Select(m => m.Test).Distinct().Count()))
                .Where(c => c.Tests != Missions.MissionPositions.GameTestCount(c.Vehicle, c.Class))
                .OrderBy(c => (c.Vehicle, c.Class))];

    /// <summary>The game's text the own text is made from.</summary>
    public static string TemplateName(Vehicle vehicle, int classIndex, int medal) =>
        Vehicles.IsMain(vehicle) ? $"{ClassLetters[Math.Clamp(classIndex, 0, 3)]}_S3_{Medals[medal]}" : $"BONUS_S3_{Medals[medal]}";

    /// <summary>True for the name of a class's own medal text.</summary>
    public static bool IsClassText(string name) => NamePattern().IsMatch(name);

    [GeneratedRegex("^((HG|RP|GC)_[EABP]_S3_(BLONDS|SILVER|GOLD|PERFECT|GREAT)|(CB|SD|HP)_L[123]_S3_(BLONDS|SILVER|GOLD))$")]
    private static partial Regex NamePattern();

    // The header: "[x=50][b]< 180 pts[/b]…" (the position centres the line by the width of the number).
    [GeneratedRegex(@"^\[x=(\d+)\](.*?<\s*)(\d+)( ?pts)")]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// The text with another number in its header, moved so the line stays centred (5 pixels per digit of difference).
    /// Null if the text has no such header.
    /// </summary>
    public static string? WithLimit(string markup, int limit)
    {
        Match match = HeaderPattern().Match(markup);
        if (!match.Success)
        {
            return null;
        }

        string number = limit.ToString(CultureInfo.InvariantCulture);
        int x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + (5 * (match.Groups[3].Value.Length - number.Length));
        return $"[x={Math.Max(0, x)}]{match.Groups[2].Value}{number}{match.Groups[4].Value}{markup[match.Length..]}";
    }

    /// <summary>The own texts of the classes with own limits: name, markup (from the templates of the project's texts).</summary>
    /// <param name="classes">The settings of the classes.</param>
    /// <param name="markupOf">The markup of a text of the project by name, or null if it does not exist.</param>
    /// <param name="missions">The missions of the game (null: the game's own): classes with another number of tests.</param>
    public static IReadOnlyList<(string Name, string Markup)> Generated(IEnumerable<ClassSettings> classes, Func<string, string?> markupOf,
        IEnumerable<TestId>? missions = null)
    {
        var result = new List<(string, string)>();
        List<ClassSettings> classList = [.. classes];
        IReadOnlyList<(Vehicle Vehicle, int Class, int Tests)> changed = ChangedClasses(missions);
        foreach ((Vehicle vehicle, int classIndex, int tests) in changed)
        {
            IReadOnlyList<int> limits = classList.FirstOrDefault(c => c.VehicleId == vehicle && c.Class == classIndex)?.Medals is { Count: 3 } own
                ? own
                : ClassLimits.Original(vehicle, classIndex, false);
            string prefix = $"{Prefixes[(int)vehicle]}_{ClassLetters[classIndex]}_S3_";
            result.AddRange(Missions.ResultTables.MedalTexts(prefix, [.. Enumerable.Range(1, tests)], limits));
        }

        foreach (ClassSettings settings in classList.Where(c => c.Medals is { Count: 3 } && HasOwnTexts(c.VehicleId) && c.Vehicle is >= 0 and < 6
                && !changed.Any(x => x.Vehicle == c.VehicleId && x.Class == c.Class))
            .OrderBy(c => (c.Vehicle, c.Class)))
        {
            for (int medal = 0; medal < 3; medal++)
            {
                if (TextName(settings.VehicleId, settings.Class, medal) is { } name
                    && markupOf(TemplateName(settings.VehicleId, settings.Class, medal)) is { } template
                    && WithLimit(template, settings.Medals![medal]) is { } markup)
                {
                    result.Add((name, markup));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Writes the own medal texts of the classes into a text file (always anew; new ones into free slots behind the
    /// original texts, otherwise at the end).
    /// </summary>
    /// <param name="textFile">The text file (ADAT).</param>
    /// <param name="classes">The settings of the classes.</param>
    /// <param name="font">The font of the game (to read and write the texts).</param>
    /// <returns>The new text file, or null if nothing changed.</returns>
    /// <exception cref="InvalidDataException">The text file is damaged, or a generated text cannot be encoded.</exception>
    /// <param name="missions">The missions of the game (null: the game's own).</param>
    public static byte[]? Add(byte[] textFile, IEnumerable<ClassSettings> classes, TextFont font, IReadOnlyList<TestId>? missions = null)
    {
        List<ClassSettings> own = [.. classes.Where(c => c.Medals is { Count: 3 })];
        if (own.Count == 0 && ChangedClasses(missions).Count == 0)
        {
            return null;
        }

        var file = GameTextFile.Parse(textFile);
        List<TextFileEntry> texts = file.ToEntries();
        var codec = new TextCodec(font);
        string? MarkupOf(string name) => texts.FirstOrDefault(t => t.Name == name) is { } entry ? codec.Decode(entry.Data) : null;

        bool changed = false;
        foreach ((string name, string markup) in Generated(own, MarkupOf, missions))
        {
            TextEncodeResult encoded = codec.Encode(markup);
            if (!encoded.Success)
            {
                throw new InvalidDataException($"{name}: {encoded.Errors[0].Message}");
            }

            byte[] data = TextCodec.ToChunkData(encoded.Codes, 0);
            int index = texts.FindIndex(t => t.Name == name);
            if (index >= 0)
            {
                if (!texts[index].Data.AsSpan().SequenceEqual(data))
                {
                    texts[index] = new TextFileEntry(name, data);
                    changed = true;
                }

                continue;
            }

            int free = texts.FindIndex(Math.Min(GameTextFile.RetailTextCount, texts.Count), t => t.IsFree);
            if (free < 0)
            {
                texts.Add(new TextFileEntry(name, data));
            }
            else
            {
                texts[free] = new TextFileEntry(name, data);
            }

            changed = true;
        }

        return changed ? file.Build(texts) : null;
    }
}
