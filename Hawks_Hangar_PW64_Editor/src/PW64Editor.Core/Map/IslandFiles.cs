using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Map;

/// <summary>
/// One level of the UVLV file: the terrains, models, contours, textures … the game loads for it.
/// </summary>
/// <remarks>Layout: ten lists, each a u16 count and that many u16 numbers: terrains, lights, environments, models,
/// contours, textures, sequences, animations, fonts, blits.</remarks>
public sealed record LevelList(
    IReadOnlyList<int> Terrains,
    IReadOnlyList<int> Models,
    IReadOnlyList<int> Contours,
    IReadOnlyList<int> Textures)
{
    /// <summary>Reads one level.</summary>
    public static LevelList Parse(ReadOnlySpan<byte> data)
    {
        var lists = new List<int[]>();
        int offset = 0;
        for (int i = 0; i < 10 && offset + 2 <= data.Length; i++)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            var list = new int[count];
            for (int k = 0; k < count; k++)
            {
                list[k] = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2 + k * 2)..]);
            }

            lists.Add(list);
            offset += 2 + count * 2;
        }

        while (lists.Count < 10)
        {
            lists.Add([]);
        }

        return new LevelList(lists[0], lists[3], lists[4], lists[5]);
    }

    /// <summary>Reads all levels of the UVLV file.</summary>
    public static IReadOnlyList<LevelList> ParseFile(byte[] file) =>
        MapChunks.Read(file, "UVLV").Where(c => c.Tag == "COMM").Select(c => Parse(c.Data)).ToList();
}

/// <summary>A landing pad place of an island (UPWL LPAD, 0x18 bytes: position, f32 heading in radians, s32, u8 type).</summary>
/// <param name="Position">The pad's position.</param>
/// <param name="HeadingRadians">The pad's heading as stored: in radians (pads.c hands it to the matrix without converting).</param>
public sealed record IslandPadPlace(Vector3 Position, float HeadingRadians)
{
    /// <summary>The heading in degrees.</summary>
    public float Heading => HeadingRadians * 180f / MathF.PI;
}

/// <summary>
/// A terrain switch or warp point of an island (UPWL TPTS, 0x34 bytes). Inside the sphere the side of the plane through it
/// (facing heading + 90°) chooses the area: front or back; when the area changes, the offset is added to the player's
/// position. Area −1 only warps (task.c of the decompilation).
/// </summary>
/// <param name="Mode">0 = on entering; 1 = only when crossing from the back to the front.</param>
/// <param name="Position">The centre.</param>
/// <param name="Heading">Degrees.</param>
/// <param name="Radius">The sphere's radius.</param>
/// <param name="FrontArea">The area on the front side (0 = main terrain, 1 = sub-area, −1 = only warp).</param>
/// <param name="BackArea">The area on the back side.</param>
/// <param name="Offset">Added to the player's position on a change.</param>
public sealed record IslandWarp(int Mode, Vector3 Position, float Heading, float Radius, int FrontArea, int BackArea, Vector3 Offset)
{
    /// <summary>The direction of the front side.</summary>
    public Vector3 Front => new(MathF.Cos((Heading + 90) * MathF.PI / 180), MathF.Sin((Heading + 90) * MathF.PI / 180), 0);
}

/// <summary>
/// A sound zone of an island (UPWL APTS, 0x20 bytes). Type 1: flying under something (a ray of the radius upwards) plays a
/// whoosh; type 0: the volume of the surroundings fades between two values on both sides (env_sound.c).
/// </summary>
public sealed record IslandSoundZone(Vector3 Position, float Heading, float Radius, int Type, float ValueA, float ValueB);

/// <summary>A wind object of an island (UPWL WOBJ, 0x10 bytes: position, u8 type: 0 windsock, 1 wind turbine, 2 simple windsock).</summary>
public sealed record IslandWindObject(Vector3 Position, int Type);

/// <summary>
/// A sound emitter of an island (UPWL ESND, 0x78 bytes; env_sound.c, audio_emitter.c): a point (the matrix's translation at
/// 0x30) or, with attribute bit 0x2, a line from A (0x40) to B (0x4C); sound number 0x58, near and far radius 0x68 / 0x6C
/// (the volume falls off between them), zone 0x70 (the area, 10 = everywhere).
/// </summary>
public sealed record IslandSoundEmitter(Vector3 Position, Vector3 A, Vector3 B, bool IsLine, int Sound, float Near, float Far, int Zone)
{
    /// <summary>The middle of the emitter.</summary>
    public Vector3 Center => IsLine ? (A + B) / 2 : Position;
}

/// <summary>
/// The data of an island (UPWL, user files 0–3): landing pad places, wind objects, toys, the bonus star.
/// </summary>
/// <remarks>Read only (editing the island data comes in a step of its own). The layout follows
/// docs/pilotwings64_filesystem.md of the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</remarks>
public sealed class IslandFile
{
    private IslandFile(IReadOnlyList<IslandPadPlace> pads, IReadOnlyList<IslandWindObject> windObjects, IReadOnlyList<Vector3> toys,
        IReadOnlyList<Vector3> bonusStars, IReadOnlyList<int> bonusStarAreas, IReadOnlyList<IslandWarp> warps, IReadOnlyList<IslandSoundZone> soundZones,
        IReadOnlyList<IslandSoundEmitter> soundEmitters)
    {
        SoundEmitters = soundEmitters;
        PadPlaces = pads;
        WindObjects = windObjects;
        Toys = toys;
        BonusStars = bonusStars;
        BonusStarAreas = bonusStarAreas;
        Warps = warps;
        SoundZones = soundZones;
    }

    /// <summary>The places a mission's landing pad can snap to (within 100 units).</summary>
    public IReadOnlyList<IslandPadPlace> PadPlaces { get; }

    /// <summary>Windsocks and wind turbines.</summary>
    public IReadOnlyList<IslandWindObject> WindObjects { get; }

    /// <summary>The positions of the island's toys (their models and movement come from the game's code).</summary>
    public IReadOnlyList<Vector3> Toys { get; }

    /// <summary>The Birdman bonus stars.</summary>
    public IReadOnlyList<Vector3> BonusStars { get; }

    /// <summary>Per bonus star the area it appears in (0 = main terrain, 1 = sub-area).</summary>
    public IReadOnlyList<int> BonusStarAreas { get; }

    /// <summary>The terrain switch and warp points.</summary>
    public IReadOnlyList<IslandWarp> Warps { get; }

    /// <summary>The sound emitters (surf, bells, merry-go-round music …).</summary>
    public IReadOnlyList<IslandSoundEmitter> SoundEmitters { get; }

    /// <summary>The sound zones.</summary>
    public IReadOnlyList<IslandSoundZone> SoundZones { get; }

    /// <summary>Reads an island file.</summary>
    public static IslandFile ParseFile(byte[] file)
    {
        var pads = new List<IslandPadPlace>();
        var winds = new List<IslandWindObject>();
        var toys = new List<Vector3>();
        var stars = new List<Vector3>();
        var starAreas = new List<int>();
        var warps = new List<IslandWarp>();
        var zones = new List<IslandSoundZone>();
        var emitters = new List<IslandSoundEmitter>();
        foreach ((string tag, byte[] data) in MapChunks.Read(file, "UPWL"))
        {
            switch (tag)
            {
                case "LPAD":
                    for (int o = 0; o + 0x18 <= data.Length; o += 0x18)
                    {
                        pads.Add(new IslandPadPlace(MapMath.ReadVector(data.AsSpan(o)), BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 12))));
                    }

                    break;
                case "WOBJ":
                    for (int o = 0; o + 0x10 <= data.Length; o += 0x10)
                    {
                        winds.Add(new IslandWindObject(MapMath.ReadVector(data.AsSpan(o)), data[o + 12]));
                    }

                    break;
                case "TOYS":
                    for (int o = 0; o + 0x10 <= data.Length; o += 0x10)
                    {
                        toys.Add(MapMath.ReadVector(data.AsSpan(o)));
                    }

                    break;
                case "BNUS":
                    for (int o = 0; o + 0x1C <= data.Length; o += 0x1C)
                    {
                        stars.Add(MapMath.ReadVector(data.AsSpan(o)));
                        starAreas.Add(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o + 0x18)));
                    }

                    break;
                case "TPTS":
                    for (int o = 0; o + 0x34 <= data.Length; o += 0x34)
                    {
                        warps.Add(new IslandWarp(data[o], MapMath.ReadVector(data.AsSpan(o + 4)), Float(data, o + 0x10), Float(data, o + 0x14),
                            BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o + 0x18)), BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o + 0x20)),
                            MapMath.ReadVector(data.AsSpan(o + 0x28))));
                    }

                    break;
                case "ESND":
                    for (int o = 0; o + 0x78 <= data.Length; o += 0x78)
                    {
                        emitters.Add(new IslandSoundEmitter(MapMath.ReadVector(data.AsSpan(o + 0x30)), MapMath.ReadVector(data.AsSpan(o + 0x40)),
                            MapMath.ReadVector(data.AsSpan(o + 0x4C)), (BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o + 0x74)) & 2) != 0,
                            data[o + 0x58], Float(data, o + 0x68), Float(data, o + 0x6C), data[o + 0x70]));
                    }

                    break;
                case "APTS":
                    for (int o = 0; o + 0x20 <= data.Length; o += 0x20)
                    {
                        zones.Add(new IslandSoundZone(MapMath.ReadVector(data.AsSpan(o)), Float(data, o + 0x0C), Float(data, o + 0x10),
                            BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o + 0x14)), Float(data, o + 0x18), Float(data, o + 0x1C)));
                    }

                    break;
            }
        }

        return new IslandFile(pads, winds, toys, stars, starAreas, warps, zones, emitters);
    }

    private static float Float(byte[] data, int offset) => BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset));
}

/// <summary>
/// The models of an environment (UVEN COMM chunk): with two models the first is the sky, the last the sea and far
/// island; then the clear and fog colours.
/// </summary>
public sealed record EnvironmentModels(int? Sky, int? Sea, bool SeaFollowsCamera, uint ClearColor)
{
    /// <summary>Reads all environments of the UVEN file.</summary>
    public static IReadOnlyList<EnvironmentModels> ParseFile(byte[] file) =>
        MapChunks.Read(file, "UVEN").Where(c => c.Tag == "COMM").Select(c => Parse(c.Data)).ToList();

    /// <summary>Reads one environment.</summary>
    public static EnvironmentModels Parse(ReadOnlySpan<byte> data)
    {
        int count = data[0];
        int offset = 1;
        int? sky = null, sea = null;
        bool follows = false;
        for (int i = 0; i < count; i++, offset += 3)
        {
            int model = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            if (i == count - 1)
            {
                sea = model;
                follows = (data[offset + 2] & 8) != 0;
            }
            else
            {
                sky = model;
            }
        }

        uint clear = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        return new EnvironmentModels(sky, sea, follows, clear);
    }
}

/// <summary>The four islands: which level, terrains, map model and environment belong to each.</summary>
/// <param name="Number">The island number of the mission files (COMM byte 3): 0 Holiday, 1 Crescent, 2 Little States, 3 Ever-Frost.</param>
/// <param name="Name">The island's name (as the game writes it).</param>
/// <param name="Level">The level in the UVLV file that loads the island.</param>
/// <param name="MapModel">The pause menu's map model of the island (vertices in world units).</param>
/// <param name="Environment">The environment of the clear day (condition 0).</param>
/// <param name="IslandFile">The user file number of the island's data (UPWL).</param>
/// <param name="Terrain">The terrain of the UVTR file that is shown.</param>
/// <param name="Area">0 for the island, 1 for its sub-area (a cave the game switches to at a terrain switch point).</param>
public sealed record MapIsland(int Number, string Name, int Level, int MapModel, int Environment, int IslandFile, int Terrain, int Area = 0)
{
    /// <summary>The islands in the game's order.</summary>
    public static IReadOnlyList<MapIsland> All { get; } =
    [
        new(0, "Holiday Island", 1, 0x00, 2, 0, 0),
        new(1, "Crescent Island", 3, 0x28, 7, 1, 1),
        new(2, "Little States", 5, 0x51, 12, 2, 3),
        new(3, "Ever-Frost Island", 10, 0xA5, 18, 3, 7),
    ];

    /// <summary>
    /// The islands and their sub-areas (task.c of the decompilation maps the area to the terrain: Crescent 1 → 2, Ever-Frost
    /// 1 → 8). Little States' terrains 4–6 belong to the award ceremony and cannot be flown to.
    /// </summary>
    public static IReadOnlyList<MapIsland> WithAreas { get; } =
    [
        All[0],
        All[1],
        All[1] with { Name = "Crescent Island: cave and volcano", Terrain = 2, Area = 1 },
        All[2],
        All[3],
        All[3] with { Name = "Ever-Frost Island: ice cave", Terrain = 8, Area = 1 },
    ];
}
