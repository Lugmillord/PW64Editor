using System.Buffers.Binary;
using System.Collections.Concurrent;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Textures;

namespace PW64Editor.Core.Map;

/// <summary>
/// The game files the map editor draws: terrains, contours, models, textures, the island data and the level lists,
/// read from the project's current files (so replaced textures or models appear) and kept once read.
/// </summary>
public sealed class MapLibrary
{
    private readonly Func<int, byte[]> _currentData;
    private readonly Dictionary<string, List<GameFile>> _groups;
    private readonly ConcurrentDictionary<int, Contour?> _contours = new();
    private readonly ConcurrentDictionary<int, GameModel?> _models = new();
    private readonly ConcurrentDictionary<int, MapTexture?> _textures = new();
    private readonly ConcurrentDictionary<int, SplinePath?> _paths = new();
    private readonly ConcurrentDictionary<int, IReadOnlyDictionary<int, int>> _palettes = new();
    private IReadOnlyList<TerrainMap>? _terrains;
    private IReadOnlyList<LevelList>? _levels;
    private IReadOnlyList<EnvironmentModels>? _environments;

    /// <summary>Creates the library.</summary>
    /// <param name="files">The game's files (the clean file system: their table index and group number).</param>
    /// <param name="currentData">The current content of a file by table index (the project's file if it has one).</param>
    public MapLibrary(IReadOnlyList<GameFile> files, Func<int, byte[]> currentData)
    {
        _currentData = currentData;
        _groups = files.GroupBy(f => FileTypeLimits.GetGroup(f.FileType)).ToDictionary(g => g.Key, g => g.OrderBy(f => f.GroupIndex).ToList());
    }

    /// <summary>Number of models of the game.</summary>
    public int ModelCount => Group("UVMD").Count;

    /// <summary>The terrains of the UVTR file.</summary>
    public IReadOnlyList<TerrainMap> Terrains => _terrains ??= TerrainMap.ParseFile(Current("UVTR", 0) ?? throw new InvalidDataException("The game has no terrain file."));

    /// <summary>The levels of the UVLV file.</summary>
    public IReadOnlyList<LevelList> Levels => _levels ??= LevelList.ParseFile(Current("UVLV", 0) ?? throw new InvalidDataException("The game has no level file."));

    /// <summary>The environments of the UVEN file.</summary>
    public IReadOnlyList<EnvironmentModels> Environments => _environments ??=
        Current("UVEN", 0) is { } file ? EnvironmentModels.ParseFile(file) : [];

    /// <summary>A contour by number (null if it does not exist or cannot be read).</summary>
    public Contour? Contour(int number) => _contours.GetOrAdd(number, n => Read(Current("UVCT", n), Map.Contour.ParseFile));

    /// <summary>A model by number (null if it does not exist or cannot be read).</summary>
    public GameModel? Model(int number) => _models.GetOrAdd(number, n => Read(Current("UVMD", n), GameModel.ParseFile));

    /// <summary>A texture by number (null if it does not exist or the editor cannot read it).</summary>
    public MapTexture? Texture(int number) => _textures.GetOrAdd(number, n =>
        Read(Current("UVTX", n), data => MapTexture.From(TextureFile.Parse(data).Texture, MainImage)));

    /// <summary>The main image of a texture by number (for textures that load a paired texture's image).</summary>
    private MapTexture.Image? MainImage(int number) =>
        Read(Current("UVTX", number), data => MapTexture.DecodeMain(TextureFile.Parse(data).Texture) is { } image ? new ImageBox(image) : null)?.Image;

    private sealed record ImageBox(MapTexture.Image Image);

    /// <summary>A texture by number as it looks with a texture palette (null for none): swapped textures are replaced.</summary>
    public MapTexture? Texture(int number, int? palette) =>
        Texture(palette is { } p && Palette(p).TryGetValue(number, out int swapped) ? swapped : number);

    /// <summary>
    /// A texture palette: which textures are replaced by which (e.g. snow-covered ones). The game's only UVTP file holds
    /// the palettes as COMM chunks one after another; each has a u16 count, then pairs of u16 (original, replacement)
    /// (texture.c _uvParseUVTP of the decompilation, https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
    /// </summary>
    public IReadOnlyDictionary<int, int> Palette(int number) => _palettes.GetOrAdd(number, n =>
    {
        var swaps = new Dictionary<int, int>();
        if (Current("UVTP", 0) is not { } file)
        {
            return swaps;
        }

        try
        {
            byte[]? data = MapChunks.Read(file, "UVTP").Where(c => c.Tag == "COMM").Select(c => c.Data).ElementAtOrDefault(n);
            if (data is null)
            {
                return swaps;
            }

            int count = data.Length >= 2 ? data[0] << 8 | data[1] : 0;
            for (int i = 0; i < count && 2 + 4 * i + 4 <= data.Length; i++)
            {
                int o = 2 + 4 * i;
                swaps[data[o] << 8 | data[o + 1]] = data[o + 2] << 8 | data[o + 3];
            }
        }
        catch (InvalidDataException)
        {
        }

        return swaps;
    });

    /// <summary>
    /// The texture palette the game loads for an environment (environment.c envLoadTerrainPal of the decompilation, MIT
    /// license), or null for none.
    /// </summary>
    public static int? PaletteOfEnvironment(int environment) => environment switch
    {
        6 => 0,
        11 => 1,
        17 => 2,
        18 or 19 => 5,
        20 => 4,
        21 => 3,
        _ => null,
    };

    /// <summary>The size of a file as stored (bytes; 0 if it does not exist).</summary>
    public int StoredSize(string group, int number) => Current(group, number)?.Length ?? 0;

    /// <summary>A spline path (SPTH user file) by user file number (null if it cannot be read).</summary>
    public SplinePath? Path(int userFile) => _paths.GetOrAdd(userFile, n => Read(Current(FileTypeLimits.UserFileGroup, n), SplinePath.ParseFile));

    /// <summary>The data of an island (UPWL user file 0–3).</summary>
    public IslandFile Island(MapIsland island) =>
        IslandFile.ParseFile(Current(FileTypeLimits.UserFileGroup, island.IslandFile) ?? throw new InvalidDataException("The island file is missing."));

    private List<GameFile> Group(string group) => _groups.GetValueOrDefault(group) ?? [];

    private byte[]? Current(string group, int number)
    {
        List<GameFile> files = Group(group);
        return number >= 0 && number < files.Count ? _currentData(files[number].TableIndex) : null;
    }

    private static T? Read<T>(byte[]? data, Func<byte[], T?> parse) where T : class
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            return parse(data);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
