using System.Numerics;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Map;
using PW64Editor.Core.Map.Rendering;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Map;

/// <summary>The map editor's readers, scene and drawing against the retail ROM.</summary>
public class MapTests
{
    private static GameFileSystem? _files;

    private static GameFileSystem Files() =>
        _files ??= GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

    private static MapLibrary Library()
    {
        GameFileSystem files = Files();
        return new MapLibrary(files.Files, i => files.Files[i].Data);
    }

    private static List<MissionFile> Missions() =>
        Files().Files.Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data)).ToList();

    [Fact]
    public void Placement_TurnsLikeTheGame()
    {
        // Heading 90: the object's +y (the direction a ring is flown through) points west (−x).
        Vector3 forward = Vector3.TransformNormal(Vector3.UnitY, MapMath.Rotation(90, 0, 0));
        Assert.True(Vector3.Distance(forward, -Vector3.UnitX) < 1e-5f);

        // Pitch 90: +y points up.
        Vector3 up = Vector3.TransformNormal(Vector3.UnitY, MapMath.Rotation(0, 90, 0));
        Assert.True(Vector3.Distance(up, Vector3.UnitZ) < 1e-5f);

        // The camera: heading 0 looks north, pitch −90 straight down with north up in the picture.
        var camera = new MapCamera { Heading = 0, Pitch = 0 };
        Assert.True(Vector3.Distance(camera.Forward, Vector3.UnitY) < 1e-4f);
        camera.SetAxisView(AxisView.Top, Vector3.Zero, 1000);
        Assert.True(camera.Forward.Z < -0.99f);
        Assert.True(camera.Up.Y > 0.99f);
        camera.SetAxisView(AxisView.West, Vector3.Zero, 1000);
        Assert.True(camera.Forward.X > 0.99f);
    }

    [Fact]
    public void CameraController_Zooms_Orbits_AndShowsAxisViews()
    {
        var camera = new MapCamera { Position = new Vector3(0, -1000, 1000), Heading = 0, Pitch = -45 };
        var controller = new MapCameraController(camera);
        Vector3 focus = controller.FocusPoint();
        Assert.True(MathF.Abs(focus.Z) < 0.01f); // where the view's middle meets the ground
        float before = Vector3.Distance(camera.Position, focus);
        controller.Zoom(1);
        Assert.True(Vector3.Distance(camera.Position, focus) < before);

        // Orbit: turning keeps the distance to the target and looks at it.
        var target = new Vector3(100, 200, 30);
        controller.Orbit = true;
        controller.Target = target;
        controller.LookAt(target, 300);
        controller.Rotate(120, -40);
        Assert.InRange(Vector3.Distance(camera.Position, target), 299f, 301f);
        Assert.True(Vector3.Distance(camera.Position + camera.Forward * 300, target) < 1f);

        // The panning mode ignores the target for zooming.
        controller.Orbit = false;
        Assert.NotEqual(target, controller.FocusPoint());

        controller.AxisView(AxisView.Top);
        Assert.True(camera.Orthographic);
        float height = camera.OrthographicHeight;
        controller.Zoom(2);
        Assert.True(camera.OrthographicHeight < height);
        controller.Rotate(10, 0); // leaving the axis view
        Assert.False(camera.Orthographic);

        // Keys: W moves forward at the same height, D to the right; the direction stays.
        var free = new MapCameraController(new MapCamera { Position = new Vector3(0, 0, 500), Heading = 90, Pitch = -30 });
        free.MoveByKeys(1, 0, 0.5f);
        Assert.True(free.Camera.Position.X < -1 && MathF.Abs(free.Camera.Position.Y) < 0.01f && free.Camera.Position.Z == 500);
        Assert.Equal(90, free.Camera.Heading);
        Vector3 before2 = free.Camera.Position;
        free.MoveByKeys(0, 1, 0.5f);
        Assert.True(free.Camera.Position.Y > before2.Y + 1); // heading 90 looks west, so right is north

        // Axis view from above: W goes north, D east.
        free.Camera.SetAxisView(AxisView.Top, Vector3.Zero, 1000);
        Vector3 top = free.Camera.Position;
        free.MoveByKeys(1, 1, 0.5f);
        Assert.True(free.Camera.Position.Y > top.Y + 1 && free.Camera.Position.X > top.X + 1);

        // Orbit: the keys go around the target and keep the distance.
        var orbit = new MapCameraController(new MapCamera { Heading = 0, Pitch = -20 }) { Orbit = true, Target = target };
        orbit.LookAt(target, 250);
        orbit.MoveByKeys(1, 1, 0.5f);
        Assert.InRange(Vector3.Distance(orbit.Camera.Position, target), 249f, 251f);
        Assert.True(orbit.Camera.Pitch < -20 && orbit.Camera.Heading > 0);

        var settings = new MapMouseSettings { Mode = MapMouseMode.SelectAndMove };
        Assert.Equal(MapMouseAction.Select, settings.Effective.Left);
        Assert.Equal(MapMouseAction.Rotate, settings.Effective.Right);
        Assert.Equal(MapMouseAction.Pan, settings.Effective.Middle);
        settings.Mode = MapMouseMode.Custom;
        settings.Left = MapMouseAction.Rotate;
        Assert.Equal(MapMouseAction.Rotate, settings.Effective.Left);
    }

    [RealRomFact]
    public void Terrains_AndIslandLevels()
    {
        MapLibrary library = Library();
        Assert.Equal(10, library.Terrains.Count);
        TerrainMap holiday = library.Terrains[0];
        Assert.Equal((2, 2, 4), (holiday.Width, holiday.Height, holiday.Cells.Count));
        Assert.Equal(new Vector2(600, 600), holiday.CellSize);
        Assert.Equal(-600, holiday.Bounds.Min.X);
        Assert.Equal(9, holiday.GridPoints().Count());
        Assert.Equal(45, library.Terrains[1].Cells.Count);
        Assert.Equal(38, library.Terrains[3].Cells.Count);
        Assert.Equal(10, library.Terrains[7].Cells.Count);

        Assert.Equal(136, library.Levels.Count);
        int[][] expected = [[1, 38, 4, 70], [3, 54, 47, 107], [5, 103, 38, 147], [10, 40, 11, 95]];
        foreach (int[] e in expected)
        {
            LevelList level = library.Levels[e[0]];
            Assert.Equal((e[1], e[2], e[3]), (level.Models.Count, level.Contours.Count, level.Textures.Count));
        }

        Assert.Equal([0], library.Levels[1].Terrains);
        Assert.Equal([1, 2], library.Levels[3].Terrains);
        Assert.Equal([7, 8], library.Levels[10].Terrains);

        int[] pads = [1, 6, 4, 5];
        foreach (MapIsland island in MapIsland.All)
        {
            IslandFile data = library.Island(island);
            Assert.Equal(pads[island.Number], data.PadPlaces.Count);
            Assert.Single(data.BonusStars);
        }

        Assert.Equal(0x161, library.Environments[2].Sea);
        Assert.Equal(0x160, library.Environments[2].Sky);
    }

    [RealRomFact]
    public void AllContoursAndModels_AreRead()
    {
        MapLibrary library = Library();
        int vertices = 0, placed = 0;
        for (int i = 0; i < 101; i++)
        {
            Contour contour = library.Contour(i) ?? throw new InvalidOperationException($"contour {i}");
            vertices += contour.Vertices.Count;
            placed += contour.Models.Count;
            Assert.All(contour.Materials, m => Assert.All(m.Triangles, t => Assert.True(t.A < contour.Vertices.Count && t.B < contour.Vertices.Count && t.C < contour.Vertices.Count)));
            Assert.All(contour.Models, m => Assert.InRange(m.Model, 0, 362));
        }

        Assert.Equal(29768, vertices);
        Assert.Equal(1364, placed);
        Assert.Null(library.Contour(101));

        Assert.Equal(363, library.ModelCount);
        for (int i = 0; i < 363; i++)
        {
            GameModel model = library.Model(i) ?? throw new InvalidOperationException($"model {i}");
            Assert.NotEmpty(model.Lods);
            Assert.All(model.Lods.SelectMany(l => l.Parts).SelectMany(p => p.Materials).SelectMany(m => m.Triangles),
                t => Assert.True(t.A < model.Vertices.Count && t.B < model.Vertices.Count && t.C < model.Vertices.Count));
        }

        // The pause menu's map models are whole islands in world units, drawn at 1/10 by the game.
        (int Model, int Triangles)[] maps = [(0x00, 2405), (0x28, 1420), (0x51, 5185), (0xA5, 3637)];
        foreach ((int number, int triangles) in maps)
        {
            GameModel map = library.Model(number)!;
            Assert.Equal(10f, map.InverseScale);
            Assert.Equal(triangles, map.Lods[0].Parts.Sum(p => p.Materials.Sum(m => m.Triangles.Count)));
        }

        Box3 crescent = library.Model(0x28)!.Bounds();
        Assert.InRange(crescent.Min.X, -2048, -1800);
        Assert.InRange(crescent.Max.Y, 1600, 2048);
    }

    [RealRomFact]
    public void MissionObjects_AreReadWithTheirPlaces()
    {
        var totals = new Dictionary<MissionObjectKind, int>();
        foreach (MissionFile mission in Missions())
        {
            IReadOnlyList<MapObject> objects = MapObject.ReadAll(mission);
            foreach (MapObject o in objects)
            {
                totals[o.Kind] = totals.GetValueOrDefault(o.Kind) + 1;
                Assert.Equal(o.Type.Size, o.Data.Length);
                foreach (MapField field in o.Type.Fields)
                {
                    Assert.NotNull(o.Format(field));
                }
            }

            Assert.Single(objects, o => o.Kind == MissionObjectKind.TakeoffPad);
        }

        Assert.Equal(122, totals[MissionObjectKind.Ring]);
        Assert.Equal(61, totals[MissionObjectKind.TakeoffPad]);
        Assert.Equal(43, totals[MissionObjectKind.Target]);
        Assert.Equal(16, totals[MissionObjectKind.HoverPad]);
        Assert.Equal(14, totals[MissionObjectKind.MecaHawk]);
        Assert.Equal(9, totals[MissionObjectKind.Runway]);
        Assert.Equal(11, totals[MissionObjectKind.LocalWind]);
        Assert.False(totals.ContainsKey(MissionObjectKind.Photo)); // no place in the world

        // RB Pilot 1 "Dark Cavern": two invisible check rings in the cave and the goal ring.
        MissionFile darkCavern = Missions().Single(m => m.Comm.Id == new TestId(3, Vehicle.RocketBelt, 0));
        List<MapObject> rings = MapObject.ReadAll(darkCavern).Where(o => o.Kind == MissionObjectKind.Ring).ToList();
        Assert.Equal(2, rings.Count(r => MapSceneBuilder.RingModel(r) is null));
        // The goal ring is locked at the start (grey lock model) until the check rings are passed.
        Assert.Equal(0xF0, MapSceneBuilder.RingModel(rings.Single(r => r.Byte(0x72) == 3)));
        Assert.All(rings.Where(r => r.Byte(0x72) == 2), r => Assert.Equal(1, r.Int(0x18))); // area 1 = the cave

        // A runway's position is the middle of its centre line.
        MapObject runway = MapObject.ReadAll(Missions().First(m => m.Comm.Id.Vehicle == Vehicle.Gyrocopter))
            .Single(o => o.Kind == MissionObjectKind.Runway);
        Assert.Equal((runway.Vector(0) + runway.Vector(12)) / 2, runway.Position);
    }

    [RealRomFact]
    public void GroundStarts_LieOnTheGround()
    {
        MapLibrary library = Library();
        var builder = new MapSceneBuilder(library);
        int checkedStarts = 0;
        foreach (MapIsland island in MapIsland.All)
        {
            MapScene scene = builder.Build(island, []);
            foreach (MissionFile mission in Missions().Where(m => m.Comm.Island == island.Number && m.Comm.Id.Vehicle == Vehicle.Gyrocopter))
            {
                MapObject start = MapObject.ReadAll(mission).Single(o => o.Kind == MissionObjectKind.TakeoffPad);
                if (start.Byte(0x28) != 0)
                {
                    continue;
                }

                float? ground = scene.GroundHeight(start.Position.X, start.Position.Y, start.Position.Z + 1);
                Assert.NotNull(ground);
                Assert.InRange(start.Position.Z - ground!.Value, -1.5f, 1.5f);
                checkedStarts++;
            }
        }

        Assert.True(checkedStarts >= 6);
    }

    [RealRomFact]
    public void Scenes_AreDrawnAndObjectsCanBeChosen()
    {
        MapLibrary library = Library();
        var builder = new MapSceneBuilder(library);
        var renderer = new MapRenderer();
        foreach (MapIsland island in MapIsland.All)
        {
            List<MissionFile> missions = Missions().Where(m => m.Comm.Island == island.Number).ToList();
            MapScene scene = builder.Build(island, missions.Select(m => new SceneMission(m, m.Comm.Id.Vehicle, MapObject.ReadAll(m))));
            Assert.NotNull(scene.Terrain);
            Assert.Contains(scene.Items, i => i.Layer == SceneLayer.Terrain);
            Assert.Contains(scene.Items, i => i.Layer == SceneLayer.Decoration);
            Assert.Contains(scene.Items, i => i.Layer == SceneLayer.MapModel);

            var camera = new MapCamera();
            camera.SetAxisView(AxisView.Top, scene.Bounds.Center, Math.Max(scene.Bounds.Size.X, scene.Bounds.Size.Y));
            foreach (MapViewMode mode in Enum.GetValues<MapViewMode>())
            {
                MapImage image = renderer.Render(scene, camera, new MapRenderSettings { ViewMode = mode, ShowTiles = true }, 320, 240);
                Assert.True(image.Pixels.Distinct().Count() > 20, $"{island.Name} {mode}");
            }

            // Every start of the island is drawn where it is and can be chosen there.
            MapImage top = renderer.Render(scene, camera, new MapRenderSettings(), 400, 400);
            Matrix4x4 viewProjection = camera.View * camera.Projection(1);
            int found = 0, starts = 0;
            for (int pick = 0; pick < scene.Picks.Count; pick++)
            {
                if (scene.Picks[pick] is not MissionObjectPick { Object.Kind: MissionObjectKind.TakeoffPad } start)
                {
                    continue;
                }

                starts++;
                Vector4 clip = Vector4.Transform(new Vector4(start.Object.Position, 1), viewProjection);
                int x = (int)((clip.X / clip.W + 1) * 200), y = (int)((1 - clip.Y / clip.W) * 200);
                int chosen = top.PickAt(x, y, 6);
                if (chosen >= 0 && scene.Picks[chosen] is MissionObjectPick { Object.Kind: MissionObjectKind.TakeoffPad })
                {
                    found++;
                }
            }

            Assert.True(starts > 0);
            Assert.True(found >= starts / 2, $"{island.Name}: {found} of {starts} starts found");

            // The map model laid over the island changes the picture, the map model alone too.
            MapImage both = renderer.Render(scene, camera, new MapRenderSettings { Layer = MapLayerMode.Both, MapOpacity = 1 }, 160, 120);
            MapImage plain = renderer.Render(scene, camera, new MapRenderSettings(), 160, 120);
            Assert.NotEqual(plain.Pixels, both.Pixels);
        }
    }

    [Fact]
    public void Camera_LeavesAxisViews_FitsAndFocuses()
    {
        var camera = new MapCamera();
        var controller = new MapCameraController(camera);
        camera.SetAxisView(AxisView.Top, new Vector3(100, 200, 0), 800);
        Assert.True(Vector3.Distance(controller.FocusPoint(), new Vector3(100, 200, 0)) < 0.5f);

        // Leaving the axis view keeps the point in the middle and the visible area.
        controller.LeaveAxisView();
        Assert.False(camera.Orthographic);
        Vector3 toFocus = new Vector3(100, 200, 0) - camera.Position;
        float distance = toFocus.Length();
        Assert.True(Vector3.Dot(Vector3.Normalize(toFocus), camera.Forward) > 0.999f);
        Assert.InRange(2 * distance * MathF.Tan(camera.FieldOfView * MapMath.DegreesToRadians / 2), 790, 810);

        // Fit and focus look at the box from the same direction.
        Vector3 forward = camera.Forward;
        var box = new Box3(new Vector3(-50, -50, 0), new Vector3(50, 50, 20));
        controller.FitAll(box);
        Assert.True(Vector3.Distance(camera.Forward, forward) < 1e-4f);
        Assert.True(Vector3.Dot(Vector3.Normalize(box.Center - camera.Position), forward) > 0.999f);
        float far = Vector3.Distance(camera.Position, box.Center);
        controller.Focus(new Box3(new Vector3(-2), new Vector3(2)));
        Assert.True(Vector3.Distance(camera.Position, Vector3.Zero) < far);

        // Zooming towards a point keeps it in the same place of the picture.
        var zoomCamera = new MapCamera { Position = new Vector3(0, -500, 400), Pitch = -35 };
        var zoomer = new MapCameraController(zoomCamera);
        var point = new Vector3(120, 80, 0);
        Vector3 before = Vector3.Normalize(point - zoomCamera.Position);
        float distanceBefore = Vector3.Distance(point, zoomCamera.Position);
        zoomer.ZoomToward(2, point);
        Assert.True(Vector3.Dot(before, Vector3.Normalize(point - zoomCamera.Position)) > 0.9999f);
        Assert.True(Vector3.Distance(point, zoomCamera.Position) < distanceBefore * 0.8f);
        zoomCamera.SetAxisView(AxisView.Top, Vector3.Zero, 1000);
        Vector3 side = Vector3.Dot(point - zoomCamera.Position, zoomCamera.Right) * zoomCamera.Right;
        zoomer.ZoomToward(1, point);
        Assert.InRange(Vector3.Dot(point - zoomCamera.Position, zoomCamera.Right) / zoomCamera.OrthographicHeight,
            Vector3.Dot(side, zoomCamera.Right) / 1000 - 1e-3f, Vector3.Dot(side, zoomCamera.Right) / 1000 + 1e-3f);

        // The old mode "navigate only" now chooses and moves too.
        Assert.Equal(new MapMouseSettings { Mode = MapMouseMode.SelectAndMove }.Effective, new MapMouseSettings { Mode = MapMouseMode.Navigate }.Effective);
    }

    [RealRomFact]
    public void Palettes_Areas_Rings_AndCodeObjects()
    {
        MapLibrary library = Library();

        // Ever-Frost's clear day loads texture palette 5 (snow-covered textures).
        Assert.Equal(5, MapLibrary.PaletteOfEnvironment(18));
        Assert.Null(MapLibrary.PaletteOfEnvironment(2));
        IReadOnlyDictionary<int, int> palette = library.Palette(5);
        Assert.NotEmpty(palette);
        (int original, int swapped) = palette.First(p => p.Key != p.Value);
        Assert.Same(library.Texture(swapped), library.Texture(original, 5));

        // The island pads' headings are radians.
        IslandFile holiday = library.Island(MapIsland.All[0]);
        IslandPadPlace pad = Assert.Single(holiday.PadPlaces);
        Assert.InRange(pad.Heading, pad.HeadingRadians * 57.29f, pad.HeadingRadians * 57.3f + 0.001f);

        // The sub-areas: Crescent's cave with its terrain switch points, Ever-Frost's ice cave with the bonus star.
        var builder = new MapSceneBuilder(library);
        Assert.Equal(6, MapIsland.WithAreas.Count);
        MapScene cave = builder.Build(MapIsland.WithAreas[2], []);
        Assert.Contains(cave.Items, i => i.Layer == SceneLayer.Terrain);
        Assert.Contains(cave.Picks, p => p is IslandObjectPick { Kind: IslandObjectKind.Warp });
        MapScene iceCave = builder.Build(MapIsland.WithAreas[5], []);
        Assert.Contains(iceCave.Picks, p => p is IslandObjectPick { Kind: IslandObjectKind.BonusStar });
        MapScene everFrost = builder.Build(MapIsland.All[3], []);
        Assert.DoesNotContain(everFrost.Picks, p => p is IslandObjectPick { Kind: IslandObjectKind.BonusStar });

        // The code-placed objects: movers with their circle as a path, fuel stations on Little States.
        MapScene states = builder.Build(MapIsland.All[2], []);
        Assert.Equal(3, states.Picks.Count(p => p is IslandExtraPick { Extra.Kind: IslandExtraKind.Box }));
        int ferry = states.Picks.FindIndex(p => p is IslandExtraPick { Extra.Model: 0x52 });
        Assert.True(ferry >= 0);
        Assert.NotEmpty(states.SelectionLines(ferry));
        Assert.Contains(states.Items, i => i.Pick == ferry);

        // Rings: one dial, hands only on rings that can become timed rings.
        foreach (MapObject ring in Missions().SelectMany(MapObject.ReadAll).Where(o => o.Kind == MissionObjectKind.Ring && RingModelIsNormal(o)))
        {
            ModelVariant variant = MapSceneBuilder.RingVariant(ring);
            Assert.NotNull(variant.Hidden);
            Assert.True(variant.Hidden!.Contains(1) ^ variant.Hidden.Contains(4));
            bool timed = ring.Byte(0x4D) != 0 && ring.Float(0x50) > 0;
            Assert.Equal(timed, !variant.Hidden.Contains(ring.Byte(0x71) != 0 ? 6 : 3));
            Assert.NotNull(builder.ModelMesh(MapSceneBuilder.RingModel(ring)!.Value, null, variant));
        }
    }

    private static bool RingModelIsNormal(MapObject ring) => MapSceneBuilder.RingModel(ring) is { } model && model < 0xEF;

    [RealRomFact]
    public void Renderer_Options_ChangeThePicture()
    {
        MapLibrary library = Library();
        MapScene scene = new MapSceneBuilder(library).Build(MapIsland.All[0], []);
        var camera = new MapCamera { Position = new Vector3(0, -600, 300), Pitch = -25 };
        var renderer = new MapRenderer();
        MapImage plain = renderer.Render(scene, camera, new MapRenderSettings(), 160, 120);
        MapImage smooth = renderer.Render(scene, camera, new MapRenderSettings { AntiAlias = true }, 160, 120);
        MapImage nearest = renderer.Render(scene, camera, new MapRenderSettings { Bilinear = false }, 160, 120);
        MapImage flat = renderer.Render(scene, camera, new MapRenderSettings { VertexColors = false, Lighting = false }, 160, 120);
        Assert.Equal(160, smooth.Width);
        Assert.Equal(120, smooth.Height);
        Assert.NotEqual(plain.Pixels, smooth.Pixels);
        Assert.NotEqual(plain.Pixels, nearest.Pixels);
        Assert.NotEqual(plain.Pixels, flat.Pixels);
        Assert.Contains(smooth.Picks, p => p >= 0);

        // The map info counts the island.
        MapStatistics stats = MapStatistics.Of(scene, library, MapIsland.All[0]);
        Assert.True(stats.Tiles >= 4 && stats.Contours <= stats.Tiles);
        Assert.True(stats.TerrainTriangles > 1000 && stats.DecorationObjects > 50 && stats.Textures > 20);
        Assert.True(stats.CodeObjects >= 6 && stats.IslandObjects >= 5);
        Assert.InRange(stats.LevelBytes, 100 * 1024, 2 * 1024 * 1024);
        Assert.Equal("Cactus", ModelNames.Of(0x09B));
    }

    [RealRomFact]
    public void Textures_UseTheirCombiner_PairedImages_AndDecals()
    {
        MapLibrary library = Library();

        // The Grand Canyon's texture 0xAD: its own grey detail image times the colour image of texture 0xAC.
        MapTexture canyon = library.Texture(0xAD)!;
        Assert.NotNull(canyon.Detail);
        Assert.Equal(library.Texture(0xAC)!.Width, canyon.Width);
        Assert.Equal(64, canyon.Detail!.Width);
        Vector4 brown = canyon.Combine.Evaluate(new Vector4(0.6f, 0.4f, 0.2f, 1), new Vector4(0.5f, 0.5f, 0.5f, 1), Vector4.One, Vector4.One, Vector4.One, 0);
        Assert.InRange(brown.X, 0.29f, 0.31f);
        Assert.InRange(brown.Z, 0.09f, 0.11f);

        // Textures whose drawn alpha comes from the vertex colour only are never cut out, even with clear texels.
        Assert.DoesNotContain(Enumerable.Range(0, 400).Select(n => library.Texture(n)).OfType<MapTexture>(), t => t.CutsOut && !t.Combine.AlphaUsesTexture);
        Assert.False(library.Texture(0xE2)!.Combine.AlphaUsesTexture);

        // The fire pictures add their primitive colour (T + (1 − T) × prim).
        MapTexture fire = library.Texture(0x45)!;
        Vector4 flame = fire.Combine.Evaluate(Vector4.Zero, Vector4.Zero, Vector4.One, fire.Primitive, fire.Environment, 0);
        Assert.InRange(flame.X, 0.56f, 0.58f);

        // Decals: the Wild West store and the landing pads have them.
        Assert.Contains(library.Model(0x9F)!.Lods[0].Parts.SelectMany(p => p.Materials), m => m.IsDecal);
        Assert.Contains(library.Model(0x102)!.Lods[0].Parts.SelectMany(p => p.Materials), m => m.IsDecal);
    }

    [RealRomFact]
    public void Paths_Fires_SoundEmitters_AndMapMarkers()
    {
        MapLibrary library = Library();

        // The ski lift starts at its first key and is at the top at t = 50; the planes stay above their lowest height.
        SplinePath lift = library.Path(4)!;
        Assert.True(Vector3.Distance(lift.LiftAt(0).Position, new Vector3(-312.846f, -986.79f, 75.31f)) < 0.01f);
        Assert.True(Vector3.Distance(lift.LiftAt(50).Position, new Vector3(-137.33f, -833.17f, 168.48f)) < 0.05f);
        Assert.InRange(lift.LiftAt(20).Heading, -46f, -44f);
        SplinePath plane = library.Path(109)!;
        Assert.True(Vector3.Distance(plane.PlaneAt(0).Position, plane.PlaneAt(100).Position) < 0.5f);

        var builder = new MapSceneBuilder(library);
        MapScene everFrost = builder.Build(MapIsland.All[3], []);
        int chairs = everFrost.Picks.FindIndex(p => p is IslandExtraPick { Extra.Model: 0xA7 });
        Assert.Equal(20, everFrost.Items.Count(i => i.Pick == chairs));
        Assert.NotEmpty(everFrost.SelectionLines(chairs));
        Assert.Contains(everFrost.Picks, p => p is IslandExtraPick { Extra.Kind: IslandExtraKind.Fire });

        // Crescent: the cave has its own fires and emitters.
        MapScene cave = builder.Build(MapIsland.WithAreas[2], []);
        Assert.Equal(37, cave.Picks.Count(p => p is IslandExtraPick { Extra.Kind: IslandExtraKind.Fire }));
        Assert.Contains(cave.Picks, p => p is IslandObjectPick { Kind: IslandObjectKind.SoundEmitter });
        IslandFile holiday = library.Island(MapIsland.All[0]);
        Assert.Equal(43, holiday.SoundEmitters.Count);
        Assert.Equal(38, holiday.SoundEmitters.Count(e => e.IsLine));

        // The pause map's markers: every ring of a ring mission gets one, yellow or blue.
        MissionFile mission = Missions().First(m => m.Comm.Id.Vehicle is not (Vehicle.Birdman or Vehicle.Cannonball)
            && MapObject.ReadAll(m).Count(o => o.Kind == MissionObjectKind.Ring) > 3);
        MapScene scene = builder.Build(MapIsland.All[mission.Comm.Island], [new SceneMission(mission, mission.Comm.Id.Vehicle, MapObject.ReadAll(mission))]);
        int rings = MapObject.ReadAll(mission).Count(o => o.Kind == MissionObjectKind.Ring && o.Byte(0x72) != 2 && o.Int(0x18) == 0);
        Assert.True(rings > 0);
        Assert.Equal(rings, scene.Items.Count(i => i.Layer == SceneLayer.MapMarker && scene.Picks[i.Pick] is MissionObjectPick { Object.Kind: MissionObjectKind.Ring }));
        Assert.True(new MapRenderSettings { Layer = MapLayerMode.MapModel }.Shows(SceneLayer.MapMarker));
        Assert.False(new MapRenderSettings { Layer = MapLayerMode.MapModel }.Shows(SceneLayer.Mission));
        Assert.False(new MapRenderSettings().Shows(SceneLayer.MapMarker));
    }
}
