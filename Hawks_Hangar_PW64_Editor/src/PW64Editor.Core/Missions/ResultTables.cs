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
/// missions of the game. Only the Birdman's names point to BD_ALL_S1 and BD_ALL_S2, which the game uses
/// for it anyway (resultInit does not look into the tables for the Birdman).</para>
/// <para>The names of the game's functions and tables (resultInit, sResultTestNames1/2, sResultPtTypes) are those of
/// the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class ResultTables
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

    /// <summary>True if the name is one of <see cref="NewTexts"/>.</summary>
    public static bool IsLevelText(string name) => NewTexts.Any(t => t.Name == name);

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

    /// <summary>Size of the tables in bytes (a multiple of 8).</summary>
    public static int Size => (int)(StringsAddress - Address) + NewTexts.Sum(t => Align4(t.Name.Length + 1));

    /// <summary>Builds the tables as they go into the data area.</summary>
    /// <param name="cleanRom">The clean ROM (its tables are the starting point).</param>
    /// <param name="layout">Where the game's tables are.</param>
    /// <param name="settings">The settings of the missions (their own point rows).</param>
    /// <exception cref="InvalidDataException">The point rows of a mission cannot be stored.</exception>
    public static byte[] Create(byte[] cleanRom, MissionLayout layout, IEnumerable<MissionSettings> settings)
    {
        byte[] tables = new byte[(Size + 7) & ~7];

        // The names of the new texts and where they are.
        var names = new Dictionary<string, uint>(StringComparer.Ordinal);
        int position = (int)(StringsAddress - Address);
        foreach ((string name, _) in NewTexts)
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
                        screen = BirdmanMissionScreenName;
                        sheet = BirdmanResultSheetName;
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
                    cleanRom.AsSpan(layout.ResultRowsOffset + (original * MissionLayout.ResultRows), MissionLayout.ResultRows)
                        .CopyTo(tables.AsSpan(RowsOffset(id)!.Value));
                }
            }
        }

        foreach (MissionSettings mission in settings.Where(m => m.ResultRows is not null))
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
    public static byte[]? AddLevelTexts(byte[] textFile)
    {
        var file = Text.GameTextFile.Parse(textFile);
        List<Text.TextFileEntry> texts = file.ToEntries();
        bool added = false;
        foreach ((string name, string source) in NewTexts)
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

    /// <summary>Offset of a mission's four point rows in the tables, or null outside them.</summary>
    public static int? RowsOffset(TestId test) =>
        (int)test.Vehicle is >= 0 and < VehicleCount && test.Class is >= 0 and < Classes && test.Test is >= 0 and < Tests
            ? (int)(RowsAddress - Address) + ((((((int)test.Vehicle * Classes) + test.Class) * Tests) + test.Test) * MissionLayout.ResultRows)
            : null;

    private static int Align4(int length) => (length + 3) & ~3;
}
