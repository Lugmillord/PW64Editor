using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Code;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// The result tables of the game patch <see cref="GamePatches.BonusGames"/>: the three tables of resultInit
/// (results.c) moved into the data area and enlarged from [6 vehicles][4 classes][3 tests] to
/// [7][4][5], so the Birdman has entries and every class has room for five tests. With them the Sky Diving
/// levels get texts of their own (mission screen and result sheet) and the Jumble Hopper levels their own
/// result sheets; the game shares one text for all levels there.
/// </summary>
/// <remarks>
/// <para>Layout (at <see cref="Address"/> in the data area, written behind the mission entries):</para>
/// <code>
/// +0x000 u32[7][4][5] text names of the mission screen ("…_S1"), pointers (sResultTestNames1)
/// +0x230 u32[7][4][5] text names of the result sheet ("…_S2"), pointers (sResultTestNames2)
/// +0x460 u8[7][4][5][4] point rows of the result sheet, bottom row first (sResultPtTypes)
/// +0x690 the names of the new texts (ASCII, 0-terminated)
/// </code>
/// <para>The entries are the bytes the original code reads for the same vehicle, class and test, even where
/// it reads past an entry (tests 3 and 4 read the next class, as before), so nothing changes for the
/// missions of the game. The Birdman's names point to BD_ALL_S1 and BD_ALL_S2 (free flight, as the game), or for a
/// mission with points to its own texts (BD_E1_S1 …); its rows are 0 unless the project sets them (the game had no
/// entry for it and read the bytes behind the table). The patch lets resultInit take the Birdman's names from the
/// tables too.</para>
/// <para>The names of the game's functions and tables (resultInit, sResultTestNames1/2, sResultPtTypes) are those of
/// the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static partial class ResultTables
{
    /// <summary>Vehicles in the tables.</summary>
    public const int VehicleCount = 7;

    /// <summary>Classes (levels, stages) per vehicle.</summary>
    public const int Classes = 4;

    /// <summary>Tests per class.</summary>
    public const int Tests = 5;

    /// <summary>RAM address of the tables (data area, behind the room for 128 mission entries).</summary>
    public const uint Address = DataArea.MissionTableAddress + (DataArea.MissionCapacity * DataArea.MissionEntrySize);

    /// <summary>RAM address of the names of the mission screen texts.</summary>
    public const uint MissionScreenNamesAddress = Address;

    /// <summary>RAM address of the names of the result sheet texts.</summary>
    public const uint ResultSheetNamesAddress = Address + 0x230;

    /// <summary>RAM address of the point rows.</summary>
    public const uint RowsAddress = Address + 0x460;

    /// <summary>RAM address of the names of the new texts.</summary>
    public const uint StringsAddress = Address + 0x690;

    /// <summary>RAM address of "BD_ALL_S1" in the game's data (US), the Birdman's mission screen.</summary>
    public const uint BirdmanMissionScreenName = 0x80355C44;

    /// <summary>RAM address of "BD_ALL_S2" in the game's data (US), the Birdman's result sheet.</summary>
    public const uint BirdmanResultSheetName = 0x80355C38;

    /// <summary>Levels with texts of their own (the bonus games have three).</summary>
    public const int Levels = 3;

    /// <summary>
    /// The new texts: name and the text of the game they start as a copy of. Sky Diving gets mission screen and
    /// result sheet per level, the Jumble Hopper its result sheet (its mission screens are per level already).
    /// </summary>
    public static IReadOnlyList<(string Name, string Source)> NewTexts { get; } =
    [
        .. Enumerable.Range(1, Levels).Select(l => ($"SD_L{l}_S1", "SD_L123_S1")),
        .. Enumerable.Range(1, Levels).Select(l => ($"SD_L{l}_S2", "SD_L123_S2")),
        .. Enumerable.Range(1, Levels).Select(l => ($"HP_L{l}_S2", "HP_L123_S2")),
    ];

    /// <summary>True if the name is one of <see cref="NewTexts"/> or a text of a Birdman mission with points.</summary>
    public static bool IsLevelText(string name) => NewTexts.Any(t => t.Name == name) || BirdmanNamePattern().IsMatch(name) || IsGeneratedText(name);

    /// <summary>The class letters of the Birdman's text names (as in its mission names E_BD_1_N … P_BD_4_N).</summary>
    private const string BirdmanClassLetters = "EABP";

    /// <summary>
    /// The text of a Birdman mission with points (BD_E1_S1 … BD_P4_S2), or null outside the four stages.
    /// </summary>
    /// <param name="test">The mission.</param>
    /// <param name="resultSheet">True for the result sheet, false for the mission screen.</param>
    public static string? BirdmanTextName(TestId test, bool resultSheet) =>
        test.Vehicle == Vehicle.Birdman && test.Class is >= 0 and < Classes && test.Test is >= 0 and < Tests
            ? $"BD_{BirdmanClassLetters[test.Class]}{test.Test + 1}_S{(resultSheet ? 2 : 1)}"
            : null;

    /// <summary>The Birdman missions with points of the settings (game patch "Bonus games and Birdman").</summary>
    public static IReadOnlySet<TestId> BirdmanPointMissions(IEnumerable<MissionSettings> settings) =>
        settings.Where(m => m.BirdmanPoints && BirdmanTextName(m.Id, false) is not null).Select(m => m.Id).ToHashSet();

    /// <summary>
    /// Bit mask of the Birdman missions with points: bit (class × 4 + test). The game module and the patched
    /// result and test screens read it (data area header +0x14).
    /// </summary>
    public static ushort BirdmanPointsMask(IEnumerable<TestId> missions) =>
        (ushort)missions.Where(m => m.Vehicle == Vehicle.Birdman && m.Class is >= 0 and < 4 && m.Test is >= 0 and < 4)
            .Aggregate(0, (mask, m) => mask | (1 << ((m.Class * 4) + m.Test)));

    /// <summary>The missions of a mask made by <see cref="BirdmanPointsMask"/>.</summary>
    public static IReadOnlyList<TestId> BirdmanPointsFromMask(ushort mask) =>
        [.. Enumerable.Range(0, 16).Where(bit => (mask & (1 << bit)) != 0).Select(bit => new TestId(bit / 4, Vehicle.Birdman, bit % 4))];

    /// <summary>
    /// All texts the patch needs: <see cref="NewTexts"/> and the two texts of every Birdman mission with points (copies of
    /// BD_ALL_S1/S2).
    /// </summary>
    public static IReadOnlyList<(string Name, string Source)> TextsNeeded(IEnumerable<TestId> birdmanPoints) =>
    [
        .. NewTexts,
        .. birdmanPoints.OrderBy(m => (m.Class, m.Test)).SelectMany(m => new[]
        {
            (BirdmanTextName(m, false)!, "BD_ALL_S1"),
            (BirdmanTextName(m, true)!, "BD_ALL_S2"),
        }).Where(t => t.Item1 is not null),
    ];

    /// <summary>
    /// The name of the level's own text with the patch, or null where the game's text stays.
    /// </summary>
    /// <param name="test">The mission (for bonus games the class is the level).</param>
    /// <param name="resultSheet">True for the result sheet ("…_S2"), false for the mission screen ("…_S1").</param>
    public static string? LevelTextName(TestId test, bool resultSheet)
    {
        if (test.Class is < 0 or >= Levels || test.Test is < 0 or >= Tests)
        {
            return null;
        }

        return test.Vehicle switch
        {
            Vehicle.SkyDiving => $"SD_L{test.Class + 1}_{(resultSheet ? "S2" : "S1")}",
            Vehicle.JumbleHopper when resultSheet => $"HP_L{test.Class + 1}_S2",
            _ => null,
        };
    }

    /// <summary>Size of the tables in bytes without Birdman missions with points (a multiple of 8).</summary>
    public static int Size => SizeFor([]);

    /// <summary>Size of the tables for these Birdman missions with points.</summary>
    public static int SizeFor(IEnumerable<TestId> birdmanPoints) =>
        ((int)(StringsAddress - Address) + TextsNeeded(birdmanPoints).Sum(t => Align4(t.Name.Length + 1)) + 7) & ~7;

    [System.Text.RegularExpressions.GeneratedRegex("^BD_[EABP][1-5]_S[12]$")]
    private static partial System.Text.RegularExpressions.Regex BirdmanNamePattern();

    /// <summary>Builds the tables as they go into the data area.</summary>
    /// <param name="cleanRom">The clean ROM (its tables are the starting point).</param>
    /// <param name="layout">Where the game's tables are.</param>
    /// <param name="settings">The settings of the missions (their own point rows).</param>
    /// <exception cref="InvalidDataException">The point rows of a mission cannot be stored.</exception>
    public static byte[] Create(byte[] cleanRom, MissionLayout layout, IEnumerable<MissionSettings> settings)
    {
        List<MissionSettings> all = [.. settings];
        IReadOnlySet<TestId> birdman = BirdmanPointMissions(all);
        byte[] tables = new byte[SizeFor(birdman)];

        // The names of the new texts and where they are.
        var names = new Dictionary<string, uint>(StringComparer.Ordinal);
        int position = (int)(StringsAddress - Address);
        foreach ((string name, _) in TextsNeeded(birdman))
        {
            names[name] = Address + (uint)position;
            Encoding.ASCII.GetBytes(name).CopyTo(tables, position);
            position += Align4(name.Length + 1);
        }

        for (int v = 0; v < VehicleCount; v++)
        {
            for (int c = 0; c < Classes; c++)
            {
                for (int t = 0; t < Tests; t++)
                {
                    var id = new TestId(c, (Vehicle)v, t);
                    int entry = (((v * Classes) + c) * Tests) + t;
                    int original = (((v * MissionLayout.ResultClasses) + c) * MissionLayout.ResultTests) + t; // as the original code computes it

                    uint screen = BinaryPrimitives.ReadUInt32BigEndian(cleanRom.AsSpan(layout.MissionScreenNamesOffset + (original * 4)));
                    uint sheet = BinaryPrimitives.ReadUInt32BigEndian(cleanRom.AsSpan(layout.ResultSheetNamesOffset + (original * 4)));
                    if ((Vehicle)v == Vehicle.Birdman)
                    {
                        // Free flight: the game's text; with points the mission's own texts. Its rows: none, unless set.
                        bool points = birdman.Contains(id);
                        screen = points ? names[BirdmanTextName(id, false)!] : BirdmanMissionScreenName;
                        sheet = points ? names[BirdmanTextName(id, true)!] : BirdmanResultSheetName;
                    }

                    if (LevelTextName(id, false) is { } ownScreen)
                    {
                        screen = names[ownScreen];
                    }

                    if (LevelTextName(id, true) is { } ownSheet)
                    {
                        sheet = names[ownSheet];
                    }

                    BinaryPrimitives.WriteUInt32BigEndian(tables.AsSpan((int)(MissionScreenNamesAddress - Address) + (entry * 4)), screen);
                    BinaryPrimitives.WriteUInt32BigEndian(tables.AsSpan((int)(ResultSheetNamesAddress - Address) + (entry * 4)), sheet);
                    if ((Vehicle)v != Vehicle.Birdman)
                    {
                        cleanRom.AsSpan(layout.ResultRowsOffset + (original * MissionLayout.ResultRows), MissionLayout.ResultRows)
                            .CopyTo(tables.AsSpan(RowsOffset(id)!.Value));
                    }
                }
            }
        }

        foreach (MissionSettings mission in all.Where(m => m.ResultRows is not null))
        {
            List<int> rows = mission.ResultRows!;
            if (RowsOffset(mission.Id) is not { } offset || rows.Count > MissionLayout.ResultRows
                || rows.Any(r => r is < 0 or > 255 || !PointSources.IsKnown(r)))
            {
                throw new InvalidDataException($"The point rows of {MissionCatalog.Label(mission.Id)} cannot be stored.");
            }

            // Bottom row first; empty rows are 0.
            for (int i = 0; i < MissionLayout.ResultRows; i++)
            {
                tables[offset + i] = i < rows.Count ? (byte)rows[rows.Count - 1 - i] : (byte)0;
            }
        }

        return tables;
    }

    /// <summary>
    /// Adds the texts of <see cref="NewTexts"/> a text file lacks, as copies of the shared texts (into free slots behind
    /// the original texts, otherwise at the end).
    /// </summary>
    /// <param name="textFile">The text file (ADAT).</param>
    /// <returns>The new text file, or null if it has all of them.</returns>
    /// <exception cref="InvalidDataException">The text file is damaged or lacks a shared text.</exception>
    public static byte[]? AddLevelTexts(byte[] textFile) => AddLevelTexts(textFile, []);

    /// <summary>
    /// Adds the texts of <see cref="TextsNeeded"/> a text file lacks, as copies of the shared texts (into free slots behind
    /// the original texts, otherwise at the end).
    /// </summary>
    /// <param name="textFile">The text file (ADAT).</param>
    /// <param name="birdmanPoints">The Birdman missions with points.</param>
    /// <returns>The new text file, or null if it has all of them.</returns>
    /// <exception cref="InvalidDataException">The text file is damaged or lacks a shared text.</exception>
    /// <param name="encode">Turns markup into the data of a text (for the generated texts, <see cref="GeneratedTexts"/>), or
    /// null to leave those out.</param>
    public static byte[]? AddLevelTexts(byte[] textFile, IEnumerable<TestId> birdmanPoints, Func<string, byte[]>? encode = null)
    {
        var file = Text.GameTextFile.Parse(textFile);
        List<Text.TextFileEntry> texts = file.ToEntries();
        bool added = false;
        List<TestId> points = [.. birdmanPoints];

        void Put(string name, byte[] data)
        {
            int index = texts.FindIndex(t => t.Name == name);
            if (index >= 0)
            {
                if (!texts[index].Data.AsSpan().SequenceEqual(data))
                {
                    texts[index] = new Text.TextFileEntry(name, data);
                    added = true;
                }

                return;
            }

            int free = texts.FindIndex(Math.Min(Text.GameTextFile.RetailTextCount, texts.Count), t => t.IsFree);
            if (free < 0)
            {
                texts.Add(new Text.TextFileEntry(name, data));
            }
            else
            {
                texts[free] = new Text.TextFileEntry(name, data);
            }

            added = true;
        }

        if (encode is not null)
        {
            foreach ((string name, string markup) in GeneratedTexts(points))
            {
                Put(name, encode(markup));
            }
        }

        foreach ((string name, string source) in TextsNeeded(points))
        {
            if (texts.Any(t => t.Name == name))
            {
                continue;
            }

            Text.TextFileEntry copy = texts.FirstOrDefault(t => t.Name == source)
                ?? throw new InvalidDataException($"The text {source} is missing.");
            int free = texts.FindIndex(Math.Min(Text.GameTextFile.RetailTextCount, texts.Count), t => t.IsFree);
            if (free < 0)
            {
                texts.Add(new Text.TextFileEntry(name, copy.Data));
            }
            else
            {
                texts[free] = new Text.TextFileEntry(name, copy.Data);
            }

            added = true;
        }

        return added ? file.Build(texts) : null;
    }

    /// <summary>
    /// The texts the editor writes itself for the Birdman stages with points (always anew, like generated point texts):
    /// the medal texts of the total screen, BD_E_S3_BLONDS … BD_P_S3_GREAT, with the limits of the stage (70/80/90 points
    /// per mission with points, 100 for a perfect score) and one line per mission, laid out like the game's class texts
    /// (A_S3_…, B_S3_…); and "4 tests to go" (TRY4) for the stage screen if a stage has four missions with points
    /// (TRY1 … TRY3 are the game's).
    /// </summary>
    public static IReadOnlyList<(string Name, string Markup)> GeneratedTexts(IEnumerable<TestId> birdmanPoints)
    {
        var result = new List<(string, string)>();
        List<TestId> points = [.. birdmanPoints];
        for (int stage = 0; stage < 4; stage++)
        {
            List<int> tests = [.. points.Where(m => m.Vehicle == Vehicle.Birdman && m.Class == stage).Select(m => m.Test + 1).Distinct().Order()];
            int n = tests.Count;
            if (n == 0)
            {
                continue;
            }

            string prefix = $"BD_{BirdmanClassLetters[stage]}_S3_";
            result.Add((prefix + "BLONDS", MedalText(tests, Header(70 * n, "Bronze Badge", 38, 43), true)));
            result.Add((prefix + "SILVER", MedalText(tests, Header(80 * n, "Silver Badge", 45, 50), true)));
            result.Add((prefix + "GOLD", MedalText(tests, Header(90 * n, "Gold Badge", 50, 55), true)));
            result.Add((prefix + "PERFECT", MedalText(tests, $"[x=39][b]< {100 * n} pts[/b]. [b]for perfect score >[/b]", true)));
            result.Add((prefix + "GREAT", MedalText(tests, "[x=78][b]< Perfect score!!! >[/b]", false)));
        }

        if (Enumerable.Range(0, 4).Any(stage => points.Count(m => m.Vehicle == Vehicle.Birdman && m.Class == stage) == 4))
        {
            result.Add(("TRY4", "[b]4 tests to go[/b]"));
        }

        return result;

        // The header as the game's texts have it (centred with [x=…] by the width of the number).
        static string Header(int points, string badge, int xThreeDigits, int xTwoDigits) =>
            $"[x={(points >= 100 ? xThreeDigits : xTwoDigits)}][b]< {points} pts[/b]. [b]for {badge} >[/b]";
    }

    /// <summary>
    /// A medal text of the total screen: the header, empty lines so the missions stand where the game draws their numbers
    /// (bottom-aligned: 4 - n lines), one line per mission with its number in the stage ("Test 1", "Test 4"), the total
    /// (from two missions on) and "Points to go:".
    /// </summary>
    private static string MedalText(IReadOnlyList<int> tests, string header, bool pointsToGo)
    {
        int missions = tests.Count;
        var lines = new List<string> { header };
        lines.AddRange(Enumerable.Repeat(string.Empty, 4 - missions));
        lines.AddRange(tests.Select(t => $"  Test {t}"));
        if (missions > 1)
        {
            lines.Add("  [b]Total[/b]");
            if (pointsToGo)
            {
                lines.Add(string.Empty);
                lines.Add("Points to go:");
            }
        }
        else if (pointsToGo)
        {
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add("  Points to go:");
        }

        return string.Join("\n", lines);
    }

    /// <summary>True for a text of <see cref="GeneratedTexts"/>.</summary>
    public static bool IsGeneratedText(string name) => name == "TRY4" || BirdmanMedalPattern().IsMatch(name);

    [System.Text.RegularExpressions.GeneratedRegex("^BD_[EABP]_S3_(BLONDS|SILVER|GOLD|PERFECT|GREAT)$")]
    private static partial System.Text.RegularExpressions.Regex BirdmanMedalPattern();

    /// <summary>Offset of a mission's four point rows in the tables, or null outside them.</summary>
    public static int? RowsOffset(TestId test) =>
        (int)test.Vehicle is >= 0 and < VehicleCount && test.Class is >= 0 and < Classes && test.Test is >= 0 and < Tests
            ? (int)(RowsAddress - Address) + ((((((int)test.Vehicle * Classes) + test.Class) * Tests) + test.Test) * MissionLayout.ResultRows)
            : null;

    private static int Align4(int length) => (length + 3) & ~3;
}
