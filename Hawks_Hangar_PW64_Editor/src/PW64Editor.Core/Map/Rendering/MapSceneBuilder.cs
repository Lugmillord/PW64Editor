using System.Collections.Concurrent;
using System.Numerics;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>A decoration object: a model standing on a contour of a terrain cell.</summary>
/// <param name="Cell">The terrain cell.</param>
/// <param name="Index">Its number among the contour's models.</param>
/// <param name="Model">The model.</param>
public sealed record DecorationPick(TerrainCell Cell, int Index, ContourModel Model);

/// <summary>An object of the island's own data (landing pad place, wind object, bonus star).</summary>
/// <param name="Kind">Which list it is in.</param>
/// <param name="Index">Its number in the list.</param>
/// <param name="Position">Its position.</param>
/// <param name="Data">Its record (<see cref="IslandPadPlace"/>, <see cref="IslandWarp"/>, <see cref="IslandSoundZone"/> …), if any.</param>
public sealed record IslandObjectPick(IslandObjectKind Kind, int Index, Vector3 Position, object? Data = null);

/// <summary>The lists of the island's own data.</summary>
public enum IslandObjectKind
{
    /// <summary>A place a mission's landing pad can snap to.</summary>
    PadPlace,

    /// <summary>A windsock or wind turbine.</summary>
    WindObject,

    /// <summary>The Birdman bonus star.</summary>
    BonusStar,

    /// <summary>The place of an animated decoration object (merry-go-round, pump jack …).</summary>
    Toy,

    /// <summary>A terrain switch or warp point.</summary>
    Warp,

    /// <summary>A sound zone.</summary>
    SoundZone,

    /// <summary>A sound emitter (a point or a line).</summary>
    SoundEmitter,
}

/// <summary>An object the game places in its code (see <see cref="IslandExtra"/>).</summary>
/// <param name="Extra">The object.</param>
/// <param name="Place">Where it is drawn at the start (a path's first point), if not at <see cref="IslandExtra.StartPosition"/>.</param>
public sealed record IslandExtraPick(IslandExtra Extra, Vector3? Place = null)
{
    /// <summary>Where the object is drawn at the start.</summary>
    public Vector3 Position => Place ?? Extra.StartPosition;
}

/// <summary>An object of a mission in the scene.</summary>
/// <param name="Mission">The mission (the caller's object, e.g. its <see cref="MissionInfo"/>).</param>
/// <param name="Vehicle">The mission's vehicle.</param>
/// <param name="Object">The object.</param>
public sealed record MissionObjectPick(object Mission, Vehicle Vehicle, MapObject Object);

/// <summary>The objects of one mission to show.</summary>
/// <param name="Mission">The mission (the caller's object; it comes back in <see cref="MissionObjectPick"/>).</param>
/// <param name="Vehicle">The mission's vehicle (for the start's model).</param>
/// <param name="Objects">Its objects.</param>
public sealed record SceneMission(object Mission, Vehicle Vehicle, IReadOnlyList<MapObject> Objects);

/// <summary>
/// Builds the scene of an island: terrain, decoration objects, the island's own objects, the map model, the sea and the
/// objects of the missions, each with the model the game uses for it.
/// </summary>
/// <remarks>
/// <para>Model numbers of the mission objects as the game chooses them (rings.c, balls.c, targets.c, hover_pads.c,
/// pads.c, thermals.c, wind.c, ball_target.c, code_82520.c, falco.c of the decompilation,
/// https://github.com/gcsmith/Pilotwings64Decomp, MIT license; see Map_Editor.md section 2). Objects the game draws
/// without a model (runways, invisible check rings, the Meca Hawk's domains, camera points) get lines.</para>
/// </remarks>
public sealed class MapSceneBuilder
{
    private readonly MapLibrary _library;
    private readonly ConcurrentDictionary<(int Model, int? Palette, string Variant), RenderMesh?> _modelMeshes = new();
    private readonly ConcurrentDictionary<(int Contour, int? Palette), RenderMesh?> _contourMeshes = new();
    private RenderMesh? _fireMesh;

    /// <summary>Drawn in front of the ground up to this many units in front of it (landing pads and runways lie on it).</summary>
    private const float PadDepthBias = 1.5f;
    private const uint InvisibleColor = 0xFFFF9030;
    private const uint WarpColor = 0xFF30E0E0;
    private const uint SoundColor = 0xFFB070FF;
    private const uint PathColor = 0xFFFFD040;

    // The texture palette of the scene being built (builds run one after another per builder).
    [ThreadStatic]
    private static int? _palette;

    /// <summary>Creates a builder (meshes are kept between builds).</summary>
    public MapSceneBuilder(MapLibrary library) => _library = library;

    /// <summary>The library the builder reads from.</summary>
    public MapLibrary Library => _library;

    /// <summary>The mesh of a model (null if it cannot be read), with the textures of a texture palette.</summary>
    public RenderMesh? ModelMesh(int model, int? palette = null) => ModelMesh(model, palette, ModelVariant.Whole);

    /// <summary>The mesh of a model with some parts hidden or turned (cached per variant).</summary>
    public RenderMesh? ModelMesh(int model, int? palette, ModelVariant variant) => _modelMeshes.GetOrAdd((model, palette, variant.Key), key =>
        _library.Model(key.Model) is { } m
            ? RenderMesh.FromModel(m, n => _library.Texture(n, key.Palette), variant.Hidden, variant.Turns)
            : null);

    /// <summary>The mesh of a contour, with the textures of a texture palette.</summary>
    public RenderMesh? ContourMesh(int contour, int? palette = null) => _contourMeshes.GetOrAdd((contour, palette), key =>
        _library.Contour(key.Contour) is { } c ? RenderMesh.FromContour(c, n => _library.Texture(n, key.Palette)) : null);

    /// <summary>The scene of an island with the objects of the given missions.</summary>
    public MapScene Build(MapIsland island, IEnumerable<SceneMission> missions)
    {
        var scene = new MapScene();
        int? palette = MapLibrary.PaletteOfEnvironment(island.Environment);
        _palette = palette;
        TerrainMap? terrain = island.Terrain < _library.Terrains.Count ? _library.Terrains[island.Terrain] : null;
        scene.Terrain = terrain;
        scene.Bounds = terrain?.Bounds ?? Box3.Empty;

        if (_library.Environments.ElementAtOrDefault(island.Environment) is { } environment)
        {
            uint c = environment.ClearColor;
            scene.SkyColor = 0xFF000000 | (c >> 8);
            if (island.Area == 0 && environment.Sea is { } sea && ModelMesh(sea, palette) is { } seaMesh && _library.Model(sea) is { } seaModel)
            {
                scene.Items.Add(new SceneItem(seaMesh, Matrix4x4.CreateScale(seaModel.Scale), SceneLayer.Sea, -1));
            }
        }

        if (terrain is not null)
        {
            foreach (TerrainCell cell in terrain.Cells)
            {
                Matrix4x4 at = Matrix4x4.CreateTranslation(cell.Center);
                if (ContourMesh(cell.Contour, palette) is { } mesh)
                {
                    scene.Items.Add(new SceneItem(mesh, at, SceneLayer.Terrain, -1));
                }

                if (_library.Contour(cell.Contour) is not { } contour)
                {
                    continue;
                }

                scene.Ground.Add((cell.Center, contour));
                for (int i = 0; i < contour.Models.Count; i++)
                {
                    ContourModel placed = contour.Models[i];
                    if (ModelMesh(placed.Model, palette) is { } modelMesh)
                    {
                        int pick = scene.AddPick(new DecorationPick(cell, i, placed));
                        scene.Items.Add(new SceneItem(modelMesh, placed.Placement * at, SceneLayer.Decoration, pick));
                    }
                }
            }
        }

        if (island.Area != 0)
        {
            // A sub-area's terrain box is the island's; the cave itself is much smaller.
            Box3 used = Box3.Empty;
            foreach (SceneItem item in scene.Items.Where(i => i.Layer == SceneLayer.Terrain && !i.Mesh.Bounds.IsEmpty))
            {
                foreach (Vector3 corner in item.Mesh.Bounds.Corners())
                {
                    used = used.Include(Vector3.Transform(corner, item.World));
                }
            }

            scene.Bounds = used.IsEmpty ? scene.Bounds : used;
        }

        if (island.Area == 0 && _library.Model(island.MapModel) is not null && ModelMesh(island.MapModel) is { } mapMesh)
        {
            // The map model's vertices are world units; the game shrinks it for the pause menu's map.
            scene.Items.Add(new SceneItem(mapMesh, Matrix4x4.Identity, SceneLayer.MapModel, -1));
        }

        IslandFile? islandData = null;
        try
        {
            islandData = _library.Island(island);
        }
        catch (InvalidDataException)
        {
        }

        List<SceneMission> missionList = [.. missions];
        var usedPadPlaces = new HashSet<int>();
        foreach (SceneMission mission in missionList)
        {
            foreach (MapObject o in mission.Objects.Where(o => o.Kind == MissionObjectKind.LandingPad))
            {
                if (SnapPad(islandData, o.Position) is { } place)
                {
                    usedPadPlaces.Add(place);
                }
            }
        }

        if (islandData is not null)
        {
            AddIslandData(scene, islandData, island.Area, usedPadPlaces);
        }

        foreach (IslandExtra extra in IslandExtra.Of(island.Number, island.Area))
        {
            AddExtra(scene, extra);
        }

        foreach (SceneMission mission in missionList)
        {
            AddMission(scene, mission, islandData, island.Area);
        }

        return scene;
    }

    private void AddIslandData(MapScene scene, IslandFile data, int area, HashSet<int> usedPadPlaces)
    {
        if (area == 0)
        {
            for (int i = 0; i < data.PadPlaces.Count; i++)
            {
                IslandPadPlace place = data.PadPlaces[i];
                int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.PadPlace, i, place.Position, place));
                if (!usedPadPlaces.Contains(i))
                {
                    AddModel(scene, 0xD4, place.Position, new Vector3(place.Heading, 0, 0), Vector3.One, SceneLayer.Decoration, pick, depthBias: PadDepthBias);
                }
            }

            for (int i = 0; i < data.WindObjects.Count; i++)
            {
                IslandWindObject wind = data.WindObjects[i];
                int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.WindObject, i, wind.Position, wind));
                AddModel(scene, wind.Type == 1 ? 0x53 : 0x40, wind.Position, Vector3.Zero, Vector3.One, SceneLayer.Decoration, pick);
            }

            for (int i = 0; i < data.Toys.Count; i++)
            {
                int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.Toy, i, data.Toys[i]));
                AddMarker(scene, data.Toys[i], 3, InvisibleColor, pick);
            }
        }

        for (int i = 0; i < data.BonusStars.Count; i++)
        {
            if (data.BonusStarAreas.ElementAtOrDefault(i) == area)
            {
                int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.BonusStar, i, data.BonusStars[i]));
                AddModel(scene, 0xF2, data.BonusStars[i], Vector3.Zero, Vector3.One, SceneLayer.Decoration, pick);
            }
        }

        for (int i = 0; i < data.Warps.Count; i++)
        {
            IslandWarp warp = data.Warps[i];
            bool here = warp.FrontArea == area || warp.BackArea == area || (area == 0 && warp.FrontArea < 0 && warp.BackArea < 0);
            if (!here)
            {
                continue;
            }

            int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.Warp, i, warp.Position, warp));
            AddSphere(scene, warp.Position, warp.Radius, WarpColor, pick, SceneLayer.Invisible);
            // The front side: an arrow out of the sphere.
            Vector3 front = warp.Front;
            AddArrowAlong(scene, warp.Position, front, warp.Radius * 1.6f, WarpColor, pick, SceneLayer.Invisible);
            if (warp.Offset != Vector3.Zero)
            {
                scene.PathLines.Add(new SceneLine(warp.Position, warp.Position + warp.Offset, WarpColor, pick, SceneLayer.Invisible, 2));
            }
        }

        for (int i = 0; i < data.SoundEmitters.Count; i++)
        {
            IslandSoundEmitter emitter = data.SoundEmitters[i];
            // The zone is the area (10: everywhere); only Crescent switches by it, elsewhere 0 and 1 still tell
            // the island from its cave.
            bool here = emitter.Zone == 10 || emitter.Zone == area || (emitter.Zone > 1 && area == 0);
            if (!here)
            {
                continue;
            }

            int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.SoundEmitter, i, emitter.Center, emitter));
            if (emitter.IsLine)
            {
                scene.Lines.Add(new SceneLine(emitter.A, emitter.B, SoundColor, pick, SceneLayer.Invisible, 2));
            }
            else
            {
                AddBox(scene, emitter.Position, new Vector3(3), 45, SoundColor, pick, SceneLayer.Invisible);
            }

            AddCircle(scene, emitter.Center, Math.Max(emitter.Near, 1), Vector3.UnitZ, SoundColor, pick, SceneLayer.Invisible, path: true);
            AddCircle(scene, emitter.Center, Math.Max(emitter.Far, 1), Vector3.UnitZ, SoundColor, pick, SceneLayer.Invisible, path: true);
        }

        for (int i = 0; i < data.SoundZones.Count; i++)
        {
            IslandSoundZone zone = data.SoundZones[i];
            if (zone.Radius <= 0)
            {
                continue; // the base entry at the origin
            }

            int pick = scene.AddPick(new IslandObjectPick(IslandObjectKind.SoundZone, i, zone.Position, zone));
            if (zone.Type == 1)
            {
                // Flying under something: the game looks upwards by the radius.
                AddCircle(scene, zone.Position, 4, Vector3.UnitZ, SoundColor, pick, SceneLayer.Invisible);
                scene.Lines.Add(new SceneLine(zone.Position, zone.Position + new Vector3(0, 0, zone.Radius), SoundColor, pick, SceneLayer.Invisible));
            }
            else
            {
                AddSphere(scene, zone.Position, zone.Radius, SoundColor, pick, SceneLayer.Invisible);
            }
        }
    }

    private void AddExtra(MapScene scene, IslandExtra extra)
    {
        if (extra.Kind == IslandExtraKind.Path)
        {
            AddPathExtra(scene, extra);
            return;
        }

        int pick = scene.AddPick(new IslandExtraPick(extra));
        switch (extra.Kind)
        {
            case IslandExtraKind.Fire:
                if (FireMesh() is { } fire)
                {
                    scene.Items.Add(new SceneItem(fire, Matrix4x4.CreateScale(extra.Size) * Matrix4x4.CreateTranslation(extra.Position), SceneLayer.Decoration, pick));
                }

                break;
            case IslandExtraKind.Mover:
                AddModel(scene, extra.Model ?? 0, extra.StartPosition, new Vector3(extra.StartHeading, 0, extra.Roll), Vector3.One, SceneLayer.Decoration, pick);
                AddCircle(scene, extra.Position, extra.Radius, Vector3.UnitZ, PathColor, pick, SceneLayer.Invisible, path: true);
                break;
            case IslandExtraKind.Fixed:
                AddModel(scene, extra.Model ?? 0, extra.Position, new Vector3(extra.Heading, 0, 0), Vector3.One, SceneLayer.Decoration, pick);
                break;
            case IslandExtraKind.Sphere:
                AddSphere(scene, extra.Position, extra.Radius, InvisibleColor, pick, SceneLayer.Invisible);
                break;
            case IslandExtraKind.Box:
                AddBox(scene, extra.Position, extra.Size, extra.Heading, InvisibleColor, pick, SceneLayer.Invisible);
                break;
        }
    }

    /// <summary>
    /// The planes and the ski lift: their models at the start (planes at t = 0, chairs at t = 5 × i) and the path, shown
    /// while they are chosen.
    /// </summary>
    private void AddPathExtra(MapScene scene, IslandExtra extra)
    {
        if (_library.Path(extra.PathFile) is not { } path)
        {
            return;
        }

        bool lift = extra.Count > 1;
        (Vector3 Position, float Heading) At(float t)
        {
            (Vector3 position, float heading) = lift ? path.LiftAt(t) : path.PlaneAt(t);
            return (position with { Z = Math.Max(position.Z, extra.MinHeight) }, heading);
        }

        (Vector3 start, _) = At(0);
        int pick = scene.AddPick(new IslandExtraPick(extra, start));
        for (int i = 0; i < extra.Count; i++)
        {
            (Vector3 position, float heading) = At(100f * i / extra.Count);
            AddModel(scene, extra.Model ?? 0, position, new Vector3(heading, 0, 0), Vector3.One, SceneLayer.Decoration, pick);
        }

        Vector3 previous = start;
        for (int k = 1; k <= 400; k++)
        {
            (Vector3 next, _) = At(k * 0.25f);
            scene.PathLines.Add(new SceneLine(previous, next, PathColor, pick, SceneLayer.Invisible, 2));
            previous = next;
        }
    }

    /// <summary>
    /// The fires' picture: a quad standing on its base (1 wide, 1 high) that turns to the camera about the vertical axis
    /// (fx.c), with the first flame picture (texture 0x45; the game shows 16 pictures one after another).
    /// </summary>
    private RenderMesh? FireMesh()
    {
        if (_fireMesh is not null)
        {
            return _fireMesh;
        }

        if (_library.Texture(0x45) is not { } texture)
        {
            return null;
        }

        float s = 16 * 32, t = 32 * 32; // texture units: 1/32 texel
        Vector3[] positions = [new(-0.5f, 0, 0), new(0.5f, 0, 0), new(0.5f, 0, 1), new(-0.5f, 0, 1)];
        Vector2[] uv = [new(0, t), new(s, t), new(s, 0), new(0, 0)];
        uint[] colors = [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF];
        Vector3[] normals = [-Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY];
        _fireMesh = new RenderMesh(positions, uv, colors, normals, [new RenderBatch(texture, [0, 1, 2, 0, 2, 3], false, true, false, 0xFFC08040)], true);
        return _fireMesh;
    }

    /// <summary>The island pad place a mission's landing pad snaps to (the nearest within 100 units, as pads.c does).</summary>
    public static int? SnapPad(IslandFile? island, Vector3 position)
    {
        if (island is null)
        {
            return null;
        }

        int? best = null;
        float bestDistance = 100;
        for (int i = 0; i < island.PadPlaces.Count; i++)
        {
            float d = Vector3.Distance(island.PadPlaces[i].Position, position);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }

        return best;
    }

    private void AddMission(MapScene scene, SceneMission mission, IslandFile? island, int area)
    {
        // Rings, balloons and Floating Pads belong to an area (0 the island, 1 its cave): the game shows them only there.
        IReadOnlyList<MapObject> objects = mission.Objects.Where(o => AreaOf(o) is not { } a || a == area).ToList();
        var picks = new Dictionary<MapObject, int>();
        foreach (MapObject o in objects)
        {
            picks[o] = scene.AddPick(new MissionObjectPick(mission.Mission, mission.Vehicle, o));
        }

        foreach (MapObject o in objects)
        {
            AddMapMarker(scene, mission.Vehicle, o, picks[o]);
        }

        foreach (MapObject o in objects)
        {
            int pick = picks[o];
            Vector3 p = o.Position, angles = o.Angles;
            switch (o.Kind)
            {
                case MissionObjectKind.Ring:
                    AddRing(scene, o, pick);
                    foreach (MapField field in o.Type.Fields.Where(f => f.Type == MapFieldType.IndexList))
                    {
                        uint color = field.Offset == 0x20 ? 0xFFFFE060u : 0xFF60C0FFu;
                        foreach (int child in o.IndexList(field))
                        {
                            if (objects.FirstOrDefault(r => r.Kind == MissionObjectKind.Ring && r.Index == child) is { } target)
                            {
                                scene.Lines.Add(new SceneLine(p, target.Position, color, -1));
                            }
                        }
                    }

                    break;
                case MissionObjectKind.HoverPad:
                    int padModel = o.Byte(0x3C) == 0 ? 0xFA : o.Byte(0x1C) == 0 ? 0xFC : 0xFB;
                    AddModel(scene, padModel, p, angles, Vector3.One, SceneLayer.Mission, pick);
                    foreach (int next in o.IndexList(o.Type.Fields.First(f => f.Type == MapFieldType.IndexList)))
                    {
                        if (objects.FirstOrDefault(r => r.Kind == MissionObjectKind.HoverPad && r.Index == next) is { } target)
                        {
                            scene.Lines.Add(new SceneLine(p, target.Position, 0xFF60FF90, -1));
                        }
                    }

                    break;
                case MissionObjectKind.Balloon:
                    float scale = o.Float(0x30);
                    AddModel(scene, 0xF4 + Math.Min(o.Byte(0x20), 2), p, Vector3.Zero, new Vector3(scale > 0 ? scale : 1), SceneLayer.Mission, pick);
                    break;
                case MissionObjectKind.Target:
                    int type = o.Byte(0x18);
                    AddModel(scene, type <= 2 ? 0xF9 - type : 0xF9, p, angles, Vector3.One, SceneLayer.Mission, pick);
                    break;
                case MissionObjectKind.Thermal:
                    float radius = o.Float(0x0C), height = o.Float(0x10);
                    AddModel(scene, 0x101, p, Vector3.Zero, new Vector3(radius, radius, height), SceneLayer.Mission, pick, 0xA0FFFFFF);
                    break;
                case MissionObjectKind.TakeoffPad:
                    AddModel(scene, PilotModel(mission.Vehicle), p, angles, Vector3.One, SceneLayer.Mission, pick);
                    AddArrow(scene, p, angles.X, 0xFF40FF40, pick);
                    break;
                case MissionObjectKind.LandingPad:
                    int padType = Math.Min(o.Byte(0x2C), 2);
                    if (SnapPad(island, p) is { } place && island is not null)
                    {
                        IslandPadPlace target = island.PadPlaces[place];
                        AddModel(scene, 0x102 + padType, target.Position, new Vector3(target.Heading, 0, 0), Vector3.One, SceneLayer.Mission, pick, depthBias: PadDepthBias);
                    }
                    else
                    {
                        // Dead pad (no island pad within 100 units): the game shows nothing there.
                        AddCircle(scene, p, 21, Vector3.UnitZ, 0xFFFF4040, pick);
                    }

                    break;
                case MissionObjectKind.Runway:
                    AddRunway(scene, o, pick);
                    break;
                case MissionObjectKind.BallTarget:
                    float r = o.Float(0x10), h = o.Float(0x14);
                    AddModel(scene, 0xF3, p, Vector3.Zero, new Vector3(r, r, h), SceneLayer.Mission, pick);
                    break;
                case MissionObjectKind.HopperGoal:
                    float gr = o.Float(0x14), gh = o.Float(0x18);
                    AddModel(scene, 0x109, p, Vector3.Zero, new Vector3(gr, gr, gh), SceneLayer.Mission, pick);
                    break;
                case MissionObjectKind.CannonTarget:
                    AddModel(scene, 0x106 + Math.Min(o.Byte(0x18), 2), p, angles, Vector3.One, SceneLayer.Mission, pick);
                    break;
                case MissionObjectKind.MecaHawk:
                    AddSphere(scene, p, o.Float(0x0C), 0xFFFF8040, pick);
                    if (o.Byte(0x10) != 0)
                    {
                        AddModel(scene, 0xD3, p, Vector3.Zero, Vector3.One, SceneLayer.Mission, pick);
                    }

                    break;
                case MissionObjectKind.LocalWind:
                    AddWind(scene, o, pick);
                    break;
                case MissionObjectKind.Observer:
                    AddSphere(scene, p, o.Float(0x0C), 0xFFC080FF, pick);
                    break;
            }
        }
    }

    /// <summary>The area of an object that has one (rings, balloons, Floating Pads: s32 at 0x18), else null.</summary>
    public static int? AreaOf(MapObject o) =>
        o.Kind is MissionObjectKind.Ring or MissionObjectKind.Balloon or MissionObjectKind.HoverPad ? o.Int(0x18) : null;

    /// <summary>
    /// The marker of an object on the pause menu's map (map3d.c of the decompilation, MIT license): the game puts small
    /// models at the objects' places on the map model (map units are world units × 0.1, so a marker's own size counts ten
    /// times in the world). Shown as at the start of the mission: active rings and Floating Pads yellow (0xCA), inactive
    /// ones blue (0xCC); landing pads and runways an arrow 0xC9 and the start an arrow 0xC8 (both at 0.3); thermals the
    /// cylinder 0xC7; goals, the Meca Hawk and the Cannonball target their own models. With the Cannonball only its
    /// targets are marked, with the Birdman nothing.
    /// </summary>
    private void AddMapMarker(MapScene scene, Vehicle vehicle, MapObject o, int pick)
    {
        if (vehicle == Vehicle.Birdman || (vehicle == Vehicle.Cannonball && o.Kind != MissionObjectKind.CannonTarget))
        {
            return;
        }

        const float map = 10; // a map unit in world units
        Vector3 p = o.Position;
        switch (o.Kind)
        {
            case MissionObjectKind.Ring when o.Byte(0x72) != 2:
                Marker(o.Byte(0x55) != 0 ? 0xCA : 0xCC, p, Vector3.Zero, new Vector3(map));
                break;
            case MissionObjectKind.HoverPad:
                Marker(o.Byte(0x3C) != 0 ? 0xCA : 0xCC, p, Vector3.Zero, new Vector3(map));
                break;
            case MissionObjectKind.Target:
            case MissionObjectKind.Balloon:
                Marker(0xCA, p, Vector3.Zero, new Vector3(map));
                break;
            case MissionObjectKind.LandingPad:
            case MissionObjectKind.Runway:
                Marker(0xC9, p, Vector3.Zero, new Vector3(map * 0.3f));
                break;
            case MissionObjectKind.TakeoffPad:
                Marker(0xC8, p, new Vector3(o.Angles.X, 0, 0), new Vector3(map * 0.3f));
                break;
            case MissionObjectKind.Thermal:
                float width = 2 * o.Float(0x0C) / 1000 * 4 * map, height = o.Float(0x10) / 1000 * 4 * map;
                Marker(0xC7, p, Vector3.Zero, new Vector3(width, width, height));
                break;
            case MissionObjectKind.HopperGoal:
                Marker(0xD1, p, Vector3.Zero, new Vector3(o.Float(0x14), o.Float(0x14), o.Float(0x18)));
                break;
            case MissionObjectKind.BallTarget:
                Marker(0xD2, p, Vector3.Zero, new Vector3(o.Float(0x10), o.Float(0x10), o.Float(0x14)));
                break;
            case MissionObjectKind.MecaHawk when o.Byte(0x10) != 0:
                Marker(0xD3, p, Vector3.Zero, Vector3.One);
                break;
            case MissionObjectKind.CannonTarget:
                Marker(0xD0, p, o.Angles, new Vector3(map));
                break;
        }

        void Marker(int model, Vector3 position, Vector3 angles, Vector3 scale) =>
            AddModel(scene, model, position, angles, scale, SceneLayer.MapMarker, pick);
    }

    /// <summary>The model of the pilot on the vehicle (the noclip viewer's playerModel, second pilot).</summary>
    public static int PilotModel(Vehicle vehicle) => vehicle switch
    {
        Vehicle.HangGlider => 276,
        Vehicle.RocketBelt => 290,
        Vehicle.Gyrocopter => 299,
        Vehicle.Cannonball => 310,
        Vehicle.SkyDiving => 325,
        Vehicle.JumbleHopper => 338,
        _ => 318,
    };

    /// <summary>
    /// The model of a ring as rings.c chooses it: active rings by size, colour (yellow/red without child rings,
    /// blue/white with) and type; inactive ones get a locked model; invisible check rings none.
    /// </summary>
    public static int? RingModel(MapObject ring)
    {
        int scoreType = Math.Min((int)ring.Byte(0x72), 3);
        bool active = ring.Byte(0x55) != 0;
        if (!active)
        {
            return scoreType == 2 ? null : scoreType == 0 ? 0xEF : 0xF0;
        }

        int size = ring.Byte(0x54) >= 5 ? 2 : ring.Byte(0x54);
        int hasChildren = ring.Byte(0x1D) != 0 ? 1 : 0;
        return scoreType switch
        {
            2 => null,
            3 => 0xF1,
            _ => 0xD9 + 4 * size + 2 * hasChildren + scoreType,
        };
    }

    /// <summary>
    /// The parts of a ring model the game shows (rings.c): one of the two dials (part 1 or 4, chosen by the direction
    /// byte), and the clock hands (parts 2/3 or 5/6) only on a timed ring: one at zero, one at the timer (18° per
    /// second). The editor shows the hands of every ring that can become a timed ring.
    /// </summary>
    public static ModelVariant RingVariant(MapObject ring)
    {
        if (RingModel(ring) is not { } model || model is 0xEF or 0xF0 or 0xF1)
        {
            return ModelVariant.Whole;
        }

        bool otherDial = ring.Byte(0x71) != 0;
        var hidden = new HashSet<int> { otherDial ? 1 : 4, 2, 3, 5, 6 };
        var turns = new Dictionary<int, Matrix4x4>();
        float timer = ring.Float(0x50);
        if (ring.Byte(0x4D) != 0 && timer > 0)
        {
            int zeroHand = otherDial ? 5 : 2, timeHand = otherDial ? 6 : 3;
            hidden.Remove(zeroHand);
            hidden.Remove(timeHand);
            float angle = Math.Clamp(timer * 18f, 0, 360);
            turns[timeHand] = Matrix4x4.CreateRotationY(angle * MapMath.DegreesToRadians);
        }

        return new ModelVariant(hidden, turns);
    }

    /// <summary>The radius of a ring (rings.c: 7.5, 10, 12.5, 17.5, 25 units by size; other sizes count as 2).</summary>
    public static float RingRadius(MapObject ring) => ring.Byte(0x54) switch
    {
        0 => 7.5f,
        1 => 10f,
        3 => 17.5f,
        4 => 25f,
        _ => 12.5f,
    };

    private void AddRing(MapScene scene, MapObject ring, int pick)
    {
        Vector3 p = ring.Position;
        if (RingModel(ring) is { } model)
        {
            AddModel(scene, model, p, ring.Angles, Vector3.One, SceneLayer.Mission, pick, variant: RingVariant(ring));
        }
        else
        {
            // Invisible check ring: a dashed-looking circle in the ring's plane.
            Matrix4x4 rotation = MapMath.Rotation(ring.Angles.X, ring.Angles.Y, ring.Angles.Z);
            AddCircle(scene, p, RingRadius(ring), Vector3.TransformNormal(Vector3.UnitY, rotation), 0xFFB0B0FF, pick);
        }
    }

    private void AddRunway(MapScene scene, MapObject runway, int pick)
    {
        Vector3 a = runway.Vector(0x00), b = runway.Vector(0x0C);
        float half = runway.Float(0x20) / 2;
        Vector3 along = b - a;
        if (along.LengthSquared() < 1e-6f)
        {
            return;
        }

        Vector3 side = Vector3.Normalize(new Vector3(-along.Y, along.X, 0)) * half;
        Vector3 up = new(0, 0, 0.3f);
        Vector3[] corners = [a + side + up, b + side + up, b - side + up, a - side + up];
        for (int i = 0; i < 4; i++)
        {
            scene.Lines.Add(new SceneLine(corners[i], corners[(i + 1) % 4], 0xFFFF60FF, pick));
        }

        scene.Lines.Add(new SceneLine(a + up, b + up, 0xFFFF60FF, pick));
        scene.Items.Add(new SceneItem(RenderMesh.FromTriangles(corners, [0, 1, 2, 0, 2, 3], 0x60FF60FF, true), Matrix4x4.Identity, SceneLayer.Mission, pick, 0x60FFFFFF, PadDepthBias));
    }

    private void AddWind(MapScene scene, MapObject wind, int pick)
    {
        uint color = 0xFF70B0FF;
        switch (wind.WindShape)
        {
            case 2:
                Vector3 size = wind.Vector(0x30);
                Matrix4x4 m = Matrix4x4.CreateScale(size / 2) * MapMath.Rotation(wind.Angles.X, wind.Angles.Y, wind.Angles.Z) * Matrix4x4.CreateTranslation(wind.Position);
                Vector3[] c = new Box3(-Vector3.One, Vector3.One).Corners().Select(x => Vector3.Transform(x, m)).ToArray();
                int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
                for (int e = 0; e < edges.Length; e += 2)
                {
                    scene.Lines.Add(new SceneLine(c[edges[e]], c[edges[e + 1]], color, pick));
                }

                break;
            case 1:
                Vector3 a = wind.Vector(0x00), b = wind.Vector(0x0C);
                float r = wind.Float(0x4C);
                Vector3 axis = b - a;
                if (axis.LengthSquared() > 1e-6f)
                {
                    AddCircle(scene, a, r, Vector3.Normalize(axis), color, pick);
                    AddCircle(scene, b, r, Vector3.Normalize(axis), color, pick);
                    scene.Lines.Add(new SceneLine(a, b, color, pick));
                }

                break;
            default:
                AddSphere(scene, wind.Vector(0x00), wind.Float(0x4C), color, pick);
                break;
        }

        // The wind's direction from the centre.
        Vector3 v = wind.Vector(0x40);
        if (v.LengthSquared() > 1e-6f)
        {
            Vector3 start = wind.Position;
            scene.Lines.Add(new SceneLine(start, start + Vector3.Normalize(v) * 40, 0xFF3070FF, pick));
        }
    }

    private void AddModel(MapScene scene, int model, Vector3 position, Vector3 angles, Vector3 scale, SceneLayer layer, int pick,
        uint tint = 0xFFFFFFFF, float depthBias = 0, ModelVariant? variant = null)
    {
        if (ModelMesh(model, _palette, variant ?? ModelVariant.Whole) is not { } mesh || _library.Model(model) is not { } m)
        {
            AddSphere(scene, position, 5, 0xFFFF00FF, pick);
            return;
        }

        scene.Items.Add(new SceneItem(mesh, MapMath.Placement(position, angles, scale * m.Scale), layer, pick, tint, depthBias));
    }

    /// <summary>A thick arrow along the start's heading (the direction the vehicle faces).</summary>
    private static void AddArrow(MapScene scene, Vector3 position, float heading, uint color, int pick)
    {
        Vector3 forward = Vector3.TransformNormal(Vector3.UnitY, MapMath.Rotation(heading, 0, 0));
        AddArrowAlong(scene, position + new Vector3(0, 0, 1), forward, 40, color, pick, SceneLayer.Mission, 3);
    }

    private static void AddArrowAlong(MapScene scene, Vector3 start, Vector3 direction, float length, uint color, int pick, SceneLayer layer, int width = 2)
    {
        Vector3 forward = Vector3.Normalize(direction);
        Vector3 side = Vector3.Cross(forward, Vector3.UnitZ);
        side = side.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(side);
        Vector3 tip = start + forward * length;
        scene.Lines.Add(new SceneLine(start, tip, color, pick, layer, width));
        scene.Lines.Add(new SceneLine(tip, tip - forward * length * 0.28f + side * length * 0.2f, color, pick, layer, width));
        scene.Lines.Add(new SceneLine(tip, tip - forward * length * 0.28f - side * length * 0.2f, color, pick, layer, width));
    }

    /// <summary>A small cube of lines for things without a model.</summary>
    private static void AddMarker(MapScene scene, Vector3 center, float half, uint color, int pick) =>
        AddBox(scene, center, new Vector3(half * 2), 0, color, pick, SceneLayer.Invisible);

    private static void AddBox(MapScene scene, Vector3 center, Vector3 size, float heading, uint color, int pick, SceneLayer layer)
    {
        Matrix4x4 m = Matrix4x4.CreateScale(size / 2) * MapMath.Rotation(heading, 0, 0) * Matrix4x4.CreateTranslation(center);
        Vector3[] c = new Box3(-Vector3.One, Vector3.One).Corners().Select(x => Vector3.Transform(x, m)).ToArray();
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        for (int e = 0; e < edges.Length; e += 2)
        {
            scene.Lines.Add(new SceneLine(c[edges[e]], c[edges[e + 1]], color, pick, layer));
        }
    }

    private static void AddCircle(MapScene scene, Vector3 center, float radius, Vector3 normal, uint color, int pick,
        SceneLayer layer = SceneLayer.Mission, bool path = false)
    {
        Vector3 u = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        Vector3 v = Vector3.Cross(normal, u);
        const int segments = 32;
        Vector3 previous = center + u * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            Vector3 next = center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            (path ? scene.PathLines : scene.Lines).Add(new SceneLine(previous, next, color, pick, layer, path ? 2 : 1));
            previous = next;
        }
    }

    private static void AddSphere(MapScene scene, Vector3 center, float radius, uint color, int pick, SceneLayer layer = SceneLayer.Mission)
    {
        radius = Math.Max(radius, 1);
        AddCircle(scene, center, radius, Vector3.UnitZ, color, pick, layer);
        AddCircle(scene, center, radius, Vector3.UnitX, color, pick, layer);
        AddCircle(scene, center, radius, Vector3.UnitY, color, pick, layer);
    }
}

/// <summary>Which parts of a model are hidden and which are turned in their own space (e.g. a ring's dial and hands).</summary>
/// <param name="Hidden">Hidden parts.</param>
/// <param name="Turns">Per part a rotation applied in the part's own space.</param>
public sealed record ModelVariant(IReadOnlySet<int>? Hidden, IReadOnlyDictionary<int, Matrix4x4>? Turns)
{
    /// <summary>All parts in their rest pose.</summary>
    public static ModelVariant Whole { get; } = new(null, null);

    /// <summary>A text that tells variants apart (for caches).</summary>
    public string Key =>
        string.Join(",", (Hidden ?? new HashSet<int>()).Order()) + "|" + string.Join(",", (Turns ?? new Dictionary<int, Matrix4x4>()).OrderBy(t => t.Key).Select(t => $"{t.Key}:{t.Value.M11:0.####}:{t.Value.M13:0.####}"));
}
