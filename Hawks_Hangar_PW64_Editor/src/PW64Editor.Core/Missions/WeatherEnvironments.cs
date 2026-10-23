using System.Buffers.Binary;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Missions;

/// <summary>
/// An environment the game lacks: a time of day / weather on an island that has no environment for it in the original
/// game, made from the environment of the same condition on another island.
/// </summary>
/// <param name="Island">The island (COMM byte 3).</param>
/// <param name="Condition">The time of day / weather.</param>
/// <param name="Id">The new environment number (UVEN chunk, and level 0x70 + number in UVLV).</param>
/// <param name="Template">The environment it is copied from: its sky, fog colour and tint (env_802E1C1C).</param>
/// <param name="PaletteTemplate">The environment whose terrain palette it uses (envLoadTerrainPal).</param>
public sealed record AddedEnvironment(int Island, WeatherCondition Condition, int Id, int Template, int PaletteTemplate);

/// <summary>
/// The environments of the islands (time of day / weather, environment.c): which environment the game loads for a
/// condition on an island, and the four combinations the game lacks (Holiday Island dark overcast, Crescent Island
/// sunset, Ever-Frost Island dark overcast and sunset).
/// </summary>
/// <remarks>
/// <para><b>Data</b>: an environment is a chunk of the one UVEN file (the n-th COMM chunk is environment n; 24 in the game,
/// room for 32): the sky model (flag 8, follows the camera), the island's far model (flag 0) and the colours. Each one has a
/// level in the UVLV file (n-th COMM chunk = level n; level 0x70 + environment) that loads it with its models and
/// textures. A new environment is a copy of the template's chunk and level with the island's far model and its own
/// number; they are appended behind the game's, so no number moves.</para>
/// <para><b>Code</b> (game module, weather.c): envGetCurrentId takes the environment from the island table in the data
/// area (<see cref="TableAddress"/>); env_802E1444's calls of env_802E1A80 (level) and env_802E1C1C (tint) and the entry of
/// envLoadTerrainPal go through the module, which loads the level of a new environment and uses the tint and terrain
/// palette of its templates; the sunset's fog by heading (env_802E2060) tests a mask of the sunset environments instead
/// of the numbers 5 and 16.</para>
/// <para>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class WeatherEnvironments
{
    /// <summary>Islands of the game (COMM byte 3), each with its map number (sTaskMapLookup in task.c).</summary>
    public static IReadOnlyList<int> MapNumbers { get; } = [1, 3, 5, 10];

    /// <summary>Islands the table has room for (future islands included).</summary>
    public const int TableIslands = 8;

    /// <summary>The first number of a new environment (behind the game's 0–23).</summary>
    public const int FirstAddedId = 24;

    /// <summary>Environments the game can hold (LEVEL_ENVIRONMENT_COUNT).</summary>
    public const int MaxEnvironments = 32;

    /// <summary>The environment the game uses for a combination it does not have ("no sky").</summary>
    public const int NoEnvironment = 22;

    /// <summary>The island table in the data area: header +0x80 (its address is at header +0x20).</summary>
    public const uint TableAddress = Code.DataArea.Start + 0x80;

    /// <summary>Data area header offset of the island table's address (0 = none).</summary>
    public const int TableAddressOffset = 0x20;

    /// <summary>Size of the table: 8 islands × (map, 6 environments, 0) and 8 new environments × (tint, palette, 0, 0).</summary>
    public const int TableSize = (TableIslands * 8) + ((MaxEnvironments - FirstAddedId) * 4);

    private static readonly int?[,] GameEnvironments =
    {
        // clear day, cloudy day, overcast, dark overcast, sunset, night (envGetCurrentId)
        { 2, 3, 4, null, 5, 6 },
        { 7, 8, 9, 10, null, 11 },
        { 12, 13, 14, 15, 16, 17 },
        { 18, 19, 20, null, null, 21 },
    };

    /// <summary>The four environments the game lacks. Dark overcast copies Crescent Island's (fog 1E2D2D), sunset Holiday
    /// Island's; Ever-Frost Island keeps its own terrain palettes (overcast for the dark overcast, day for the sunset).</summary>
    public static IReadOnlyList<AddedEnvironment> Added { get; } =
    [
        new(0, WeatherCondition.DarkOvercast, 24, 10, 4),
        new(1, WeatherCondition.Sunset, 25, 5, 7),
        new(3, WeatherCondition.DarkOvercast, 26, 10, 20),
        new(3, WeatherCondition.Sunset, 27, 5, 18),
    ];

    /// <summary>The environment of a condition on an island in the original game, or null if the game has none.</summary>
    public static int? GameEnvironment(int island, WeatherCondition condition) =>
        island >= 0 && island < GameEnvironments.GetLength(0) && (int)condition is >= 0 and < 6 ? GameEnvironments[island, (int)condition] : null;

    /// <summary>The environment of a condition on an island with the added ones, or null.</summary>
    public static int? Environment(int island, WeatherCondition condition) =>
        GameEnvironment(island, condition) ?? Added.FirstOrDefault(a => a.Island == island && a.Condition == condition)?.Id;

    /// <summary>True if the combination needs one of the added environments.</summary>
    public static bool IsAdded(int island, WeatherCondition condition) =>
        GameEnvironment(island, condition) is null && Added.Any(a => a.Island == island && a.Condition == condition);

    /// <summary>The sunset environments (the fog colour follows the heading): the game's 5 and 16 and the added ones.</summary>
    public static IEnumerable<int> SunsetEnvironments =>
        [5, 16, .. Added.Where(a => a.Condition == WeatherCondition.Sunset).Select(a => a.Id)];

    /// <summary>The mask of the patched test in env_802E2060: bit 31 − environment for every sunset environment.</summary>
    public static uint SunsetMask => SunsetEnvironments.Aggregate(0u, (mask, env) => mask | (1u << (31 - env)));

    /// <summary>True if one of the mission files uses a combination the game lacks.</summary>
    public static bool Needed(IEnumerable<GameFile> files) => files
        .Where(f => f.FileType == MissionFile.FormType)
        .Select(f => MissionFile.Parse(f.Data).Comm)
        .Any(c => IsAdded(c.Island, c.Weather));

    /// <summary>The island table for the data area (<see cref="TableAddress"/>).</summary>
    public static byte[] Table()
    {
        byte[] table = new byte[TableSize];
        for (int island = 0; island < MapNumbers.Count; island++)
        {
            Span<byte> entry = table.AsSpan(island * 8, 8);
            entry[0] = (byte)MapNumbers[island];
            for (int c = 0; c < 6; c++)
            {
                entry[1 + c] = (byte)(Environment(island, (WeatherCondition)c) ?? 0xFF);
            }
        }

        foreach (AddedEnvironment added in Added)
        {
            Span<byte> entry = table.AsSpan((TableIslands * 8) + ((added.Id - FirstAddedId) * 4), 4);
            entry[0] = (byte)added.Template;
            entry[1] = (byte)added.PaletteTemplate;
        }

        return table;
    }

    /// <summary>The game files with the added environments: the UVEN file with their chunks, the UVLV file with their levels.</summary>
    /// <exception cref="InvalidDataException">The files are not those of the game.</exception>
    public static void AddTo(List<GameFile> files)
    {
        int envIndex = files.FindIndex(f => f.FileType == "UVEN");
        int levelIndex = files.FindIndex(f => f.FileType == "UVLV");
        if (envIndex < 0 || levelIndex < 0)
        {
            throw new InvalidDataException("The game has no environment or level file.");
        }

        files[envIndex] = files[envIndex] with { Data = WithEnvironments(files[envIndex].Data) };
        files[levelIndex] = files[levelIndex] with { Data = WithLevels(files[levelIndex].Data) };
    }

    /// <summary>The UVEN file with the added environments behind the game's (the file must have exactly the game's 24).</summary>
    public static byte[] WithEnvironments(byte[] uven)
    {
        List<byte[]> chunks = CommChunks(uven, "UVEN");
        if (chunks.Count != FirstAddedId)
        {
            throw new InvalidDataException($"The environment file has {chunks.Count} environments instead of {FirstAddedId}.");
        }

        var added = new List<byte[]>();
        foreach (AddedEnvironment env in Added)
        {
            byte[] chunk = (byte[])chunks[env.Template].Clone();
            int from = IslandModel(chunks[env.Template]), to = IslandModel(chunks[GameEnvironment(env.Island, WeatherCondition.ClearDay)!.Value]);
            int models = chunk[0];
            for (int m = 0; m < models; m++)
            {
                if (BinaryPrimitives.ReadUInt16BigEndian(chunk.AsSpan(1 + (m * 3))) == from)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(1 + (m * 3)), (ushort)to);
                }
            }

            added.Add(chunk);
        }

        return Append(uven, added);
    }

    /// <summary>The UVLV file with the levels of the added environments (level 0x70 + number) behind the game's 136.</summary>
    public static byte[] WithLevels(byte[] uvlv)
    {
        List<byte[]> chunks = CommChunks(uvlv, "UVLV");
        int first = LevelOf(FirstAddedId);
        if (chunks.Count != first)
        {
            throw new InvalidDataException($"The level file has {chunks.Count} levels instead of {first}.");
        }

        var added = new List<byte[]>();
        foreach (AddedEnvironment env in Added.OrderBy(a => a.Id))
        {
            byte[] level = (byte[])chunks[LevelOf(env.Template)].Clone();
            int from = LevelIslandModel(chunks[LevelOf(env.Template)]);
            int to = LevelIslandModel(chunks[LevelOf(GameEnvironment(env.Island, WeatherCondition.ClearDay)!.Value)]);
            ReplaceInList(level, 2, env.Template, env.Id);
            ReplaceInList(level, 3, from, to);
            added.Add(level);
        }

        return Append(uvlv, added);
    }

    /// <summary>The level that loads an environment.</summary>
    public static int LevelOf(int environment) => 0x70 + environment;

    // The far model of the island in an environment chunk: the model without the "follows the camera" flag.
    private static int IslandModel(byte[] chunk)
    {
        for (int m = 0; m < chunk[0]; m++)
        {
            if ((chunk[3 + (m * 3)] & 8) == 0)
            {
                return BinaryPrimitives.ReadUInt16BigEndian(chunk.AsSpan(1 + (m * 3)));
            }
        }

        throw new InvalidDataException("An environment of the game has no island model.");
    }

    // A level chunk is a row of lists (u16 count, then the u16 numbers): terrains, lights, environments, models, contours,
    // textures, sequences, animations, fonts, blits (ParsedUVLV). Returns where list n starts.
    private static int ListOffset(byte[] level, int list)
    {
        int offset = 0;
        for (int i = 0; i < list; i++)
        {
            offset += 2 + (2 * BinaryPrimitives.ReadUInt16BigEndian(level.AsSpan(offset)));
        }

        return offset;
    }

    private static List<int> ReadList(byte[] level, int list)
    {
        int offset = ListOffset(level, list);
        int count = BinaryPrimitives.ReadUInt16BigEndian(level.AsSpan(offset));
        return Enumerable.Range(0, count).Select(i => (int)BinaryPrimitives.ReadUInt16BigEndian(level.AsSpan(offset + 2 + (2 * i)))).ToList();
    }

    private static void ReplaceInList(byte[] level, int list, int from, int to)
    {
        int offset = ListOffset(level, list);
        int count = BinaryPrimitives.ReadUInt16BigEndian(level.AsSpan(offset));
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Span<byte> slot = level.AsSpan(offset + 2 + (2 * i), 2);
            if (BinaryPrimitives.ReadUInt16BigEndian(slot) == from)
            {
                BinaryPrimitives.WriteUInt16BigEndian(slot, (ushort)to);
                found = true;
            }
        }

        if (!found)
        {
            throw new InvalidDataException($"A level of the game does not list {from}.");
        }
    }

    // The island model of an environment's level: the model that is no sky model (0x160–0x165, 0x167 are skies).
    private static int LevelIslandModel(byte[] level)
    {
        List<int> models = ReadList(level, 3);
        int[] islands = [0x161, 0x166, 0x168, 0x169];
        return models.FirstOrDefault(islands.Contains, -1) is var model and >= 0
            ? model
            : throw new InvalidDataException("A level of an environment has no island model.");
    }

    private static List<byte[]> CommChunks(byte[] form, string formType)
    {
        IffForm parsed = IffForm.Parse(form);
        if (parsed.FormType != formType)
        {
            throw new InvalidDataException($"Expected a {formType} file.");
        }

        return parsed.Chunks.Where(c => c.Tag == "COMM").Select(c => form.AsSpan(c.DataOffset, c.DataSize).ToArray()).ToList();
    }

    // The FORM with more COMM chunks behind its own (all of the game's chunks have even sizes, so no padding is needed).
    private static byte[] Append(byte[] form, IEnumerable<byte[]> payloads)
    {
        List<byte[]> chunks = payloads.Select(p => IffWriter.BuildChunk("COMM", p)).ToList();
        int total = IffForm.ReadHeader(form).TotalSize;
        byte[] result = new byte[total + chunks.Sum(c => c.Length)];
        form.AsSpan(0, total).CopyTo(result);
        int offset = total;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }

        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }
}
