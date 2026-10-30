using System.Numerics;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>How the surfaces are drawn.</summary>
public enum MapViewMode
{
    /// <summary>Textures and vertex colours, as in the game.</summary>
    Textured,

    /// <summary>White surfaces shaded by the light, without textures.</summary>
    White,

    /// <summary>Only the edges of the triangles.</summary>
    Wireframe,
}

/// <summary>Which of the island's models is shown.</summary>
public enum MapLayerMode
{
    /// <summary>The island (terrain and decoration).</summary>
    Island,

    /// <summary>The pause menu's map model instead of the island.</summary>
    MapModel,

    /// <summary>The island with the map model laid over it.</summary>
    Both,
}

/// <summary>Which handles the chosen object gets.</summary>
public enum GizmoMode
{
    /// <summary>Arrows along the axes.</summary>
    Move,

    /// <summary>Rings about the axes.</summary>
    Rotate,
}

/// <summary>What and how to draw.</summary>
public sealed record MapRenderSettings
{
    /// <summary>How surfaces are drawn.</summary>
    public MapViewMode ViewMode { get; init; } = MapViewMode.Textured;

    /// <summary>Island, map model or both.</summary>
    public MapLayerMode Layer { get; init; } = MapLayerMode.Island;

    /// <summary>How opaque the map model is when laid over the island (0–1).</summary>
    public float MapOpacity { get; init; } = 0.5f;

    /// <summary>Draws vertical lines at the tiles' corners.</summary>
    public bool ShowTiles { get; init; }

    /// <summary>Textures are filtered between texels (bilinear) instead of the nearest texel.</summary>
    public bool Bilinear { get; init; } = true;

    /// <summary>The vertex colours are applied (the shading and colours the game stores per vertex).</summary>
    public bool VertexColors { get; init; } = true;

    /// <summary>Lit models are shaded by the light (the game's hardware lighting of vehicles, pilots and some objects).</summary>
    public bool Lighting { get; init; } = true;

    /// <summary>Smooth edges: the picture is drawn at twice the size and scaled down.</summary>
    public bool AntiAlias { get; init; }

    /// <summary>The pick number of the chosen thing (−1 for none).</summary>
    public int Selected { get; init; } = -1;

    /// <summary>The handles of the chosen thing.</summary>
    public GizmoMode Gizmo { get; init; } = GizmoMode.Move;

    /// <summary>The position of the chosen thing (where its handles start).</summary>
    public Vector3? SelectedPosition { get; init; }

    /// <summary>The rotation of the handles (the object's own axes), or null for the world axes.</summary>
    public Matrix4x4? GizmoRotation { get; init; }

    /// <summary>Hides the mission objects (decoration only).</summary>
    public bool HideMissionObjects { get; init; }

    /// <summary>Shows the island's invisible things (trigger zones, warp points, sound zones).</summary>
    public bool ShowInvisible { get; init; } = true;

    /// <summary>The layers whose things are drawn with the current <see cref="Layer"/>.</summary>
    public bool Shows(SceneLayer layer) => layer switch
    {
        SceneLayer.MapModel => Layer == MapLayerMode.MapModel,
        SceneLayer.Terrain or SceneLayer.Decoration => Layer != MapLayerMode.MapModel,
        SceneLayer.Mission => !HideMissionObjects && Layer != MapLayerMode.MapModel,
        SceneLayer.MapMarker => !HideMissionObjects && Layer == MapLayerMode.MapModel,
        SceneLayer.Invisible => ShowInvisible && Layer != MapLayerMode.MapModel,
        _ => true,
    };
}

/// <summary>A drawn picture: pixels, and per pixel what was drawn there.</summary>
public sealed class MapImage
{
    internal MapImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
        Picks = new int[width * height];
        Depth = new float[width * height];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels as 0xAARRGGBB (the layout of WPF's Bgra32 on little-endian machines), row by row.</summary>
    public uint[] Pixels { get; }

    /// <summary>Per pixel the pick number of what is drawn there (−1 for nothing that can be chosen).</summary>
    public int[] Picks { get; }

    /// <summary>Per pixel the depth (0 near … 1 far).</summary>
    public float[] Depth { get; }

    /// <summary>The pick number at a pixel, or of the nearest pick within <paramref name="radius"/> pixels.</summary>
    public int PickAt(int x, int y, int radius = 3)
    {
        int best = -1;
        int bestDistance = int.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= Width || py >= Height)
                {
                    continue;
                }

                int pick = Picks[py * Width + px];
                int distance = dx * dx + dy * dy;
                if (pick >= 0 && distance < bestDistance)
                {
                    best = pick;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>The pixels as RGBA bytes (for saving as PNG in tests).</summary>
    public byte[] ToRgba()
    {
        var bytes = new byte[Pixels.Length * 4];
        for (int i = 0; i < Pixels.Length; i++)
        {
            uint p = Pixels[i];
            bytes[i * 4] = (byte)(p >> 16);
            bytes[i * 4 + 1] = (byte)(p >> 8);
            bytes[i * 4 + 2] = (byte)p;
            bytes[i * 4 + 3] = 0xFF;
        }

        return bytes;
    }

    /// <summary>A picture of half the size: each pixel the average of four, picks and depth of the top-left one.</summary>
    internal MapImage HalfSize()
    {
        var result = new MapImage(Width / 2, Height / 2);
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                int i = 2 * y * Width + 2 * x;
                uint a = Pixels[i], b = Pixels[i + 1], c = Pixels[i + Width], d = Pixels[i + Width + 1];
                uint r = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF) + ((c >> 16) & 0xFF) + ((d >> 16) & 0xFF) + 2) / 4;
                uint g = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF) + ((c >> 8) & 0xFF) + ((d >> 8) & 0xFF) + 2) / 4;
                uint bl = ((a & 0xFF) + (b & 0xFF) + (c & 0xFF) + (d & 0xFF) + 2) / 4;
                int o = y * result.Width + x;
                result.Pixels[o] = 0xFF000000 | r << 16 | g << 8 | bl;
                int pick = Picks[i] >= 0 ? Picks[i] : Picks[i + 1] >= 0 ? Picks[i + 1] : Picks[i + Width] >= 0 ? Picks[i + Width] : Picks[i + Width + 1];
                result.Picks[o] = pick;
                result.Depth[o] = Math.Min(Math.Min(Depth[i], Depth[i + 1]), Math.Min(Depth[i + Width], Depth[i + Width + 1]));
            }
        }

        return result;
    }
}

/// <summary>
/// Draws a <see cref="MapScene"/> in software: perspective-correct textured triangles with a depth buffer, lines, the
/// tiles, the map model laid over the island and the handles of the chosen object.
/// </summary>
/// <remarks>The editor's own drawing (the user's choice): the same picture on every computer, and it can be checked
/// without a graphics card. Triangles are clipped at the near plane; textures are sampled with the game's wrap, mirror
/// and clamp settings, at the nearest texel or filtered between four (the N64 filters, too); colours are texture ×
/// vertex colour (lit materials: white × light). The picture is drawn in horizontal bands on all processor cores.</remarks>
public sealed class MapRenderer
{
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.35f, -0.45f, 0.82f));

    private uint[] _color = [];
    private float[] _depth = [];
    private int[] _picks = [];
    private int _width, _height;
    private int _top, _bottom;
    private Matrix4x4 _viewProjection;
    private MapCamera _camera = new();
    private MapRenderSettings _settings = new();
    private MapViewMode _mode;
    private uint[]? _overlayColor;
    private float[]? _overlayDepth;
    private int[]? _overlayPicks;
    private float _lineScale = 1;

    // Scratch buffers for the transformed vertices of one item.
    private Vector4[] _clip = [];
    private float[] _shade = [];
    private SceneItem? _prepared;
    private readonly Vertex[] _polygon = new Vertex[8];
    private readonly Vertex[] _polygon2 = new Vertex[8];

    private struct Vertex
    {
        public Vector4 Clip;
        public Vector2 Uv;
        public Vector4 Color; // 0–1 channels, alpha in W
    }

    /// <summary>Draws the scene (in horizontal bands on all processor cores).</summary>
    public MapImage Render(MapScene scene, MapCamera camera, MapRenderSettings settings, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (settings.AntiAlias)
        {
            return RenderAt(scene, camera, settings, width * 2, height * 2, 2).HalfSize();
        }

        return RenderAt(scene, camera, settings, width, height, 1);
    }

    private static MapImage RenderAt(MapScene scene, MapCamera camera, MapRenderSettings settings, int width, int height, float lineScale)
    {
        var image = new MapImage(width, height);
        bool overlay = settings.Layer == MapLayerMode.Both;
        uint[]? overlayColor = overlay ? new uint[width * height] : null;
        float[]? overlayDepth = overlay ? new float[width * height] : null;
        int[]? overlayPicks = overlay ? new int[width * height] : null;
        int bands = Math.Clamp(Math.Min(Environment.ProcessorCount, height / 24), 1, 16);
        Parallel.For(0, bands, band =>
        {
            var worker = new MapRenderer { _overlayColor = overlayColor, _overlayDepth = overlayDepth, _overlayPicks = overlayPicks, _lineScale = lineScale };
            worker.RenderBand(image, scene, camera, settings, height * band / bands, height * (band + 1) / bands);
        });
        return image;
    }

    private void RenderBand(MapImage image, MapScene scene, MapCamera camera, MapRenderSettings settings, int top, int bottom)
    {
        _top = top;
        _bottom = bottom;
        _color = image.Pixels;
        _depth = image.Depth;
        _picks = image.Picks;
        _width = image.Width;
        _height = image.Height;
        _camera = camera;
        _settings = settings;
        _mode = settings.ViewMode;
        _viewProjection = camera.View * camera.Projection((float)_width / _height);

        Clear(_mode == MapViewMode.Textured ? scene.SkyColor : 0xFF46505A);
        var translucent = new List<(SceneItem Item, RenderBatch Batch)>();
        foreach (SceneItem item in scene.Items)
        {
            if (settings.Shows(item.Layer))
            {
                DrawItem(item, translucent);
            }
        }

        // See-through surfaces: far ones first, so nearer ones blend over them.
        foreach ((SceneItem item, RenderBatch batch) in translucent.OrderByDescending(t => DistanceOf(t.Item)))
        {
            DrawBatch(item, batch, true);
        }

        if (settings.Layer == MapLayerMode.Both)
        {
            DrawOverlay(scene, settings.MapOpacity);
        }

        foreach (SceneLine line in scene.Lines)
        {
            if (settings.Shows(line.Layer))
            {
                DrawLine(line.A, line.B, line.Color, line.Pick, line.Width, true);
            }
        }

        if (settings.ShowTiles && scene.Terrain is { } terrain)
        {
            float lowest = terrain.Bounds.Min.Z - 50, highest = Math.Max(terrain.Bounds.Max.Z + 200, 600);
            foreach (Vector2 p in terrain.GridPoints())
            {
                DrawLine(new Vector3(p, lowest), new Vector3(p, highest), 0xFFFFD040, -1, 1, true);
            }
        }

        if (settings.Selected >= 0)
        {
            DrawSelection(scene, settings);
        }
    }

    private float DistanceOf(SceneItem item) => _camera.Orthographic
        ? -Vector3.Dot(item.World.Translation, _camera.Forward)
        : Vector3.DistanceSquared(item.World.Translation, _camera.Position);

    private void Clear(uint sky)
    {
        // A soft gradient: darker at the top.
        for (int y = _top; y < _bottom; y++)
        {
            float f = 0.75f + 0.25f * y / _height;
            uint row = Scale(sky, f);
            Array.Fill(_color, row, y * _width, _width);
            Array.Fill(_depth, 1f, y * _width, _width);
            Array.Fill(_picks, -1, y * _width, _width);
        }
    }

    private void DrawOverlay(MapScene scene, float opacity)
    {
        // The map model is drawn into its own buffers and laid over the island.
        uint[] color = _color;
        float[] depth = _depth;
        int[] picks = _picks;
        _color = _overlayColor ?? new uint[color.Length];
        _depth = _overlayDepth ?? new float[depth.Length];
        _picks = _overlayPicks ?? new int[picks.Length];
        Array.Fill(_depth, 1f, _top * _width, (_bottom - _top) * _width);
        var translucent = new List<(SceneItem, RenderBatch)>();
        foreach (SceneItem item in scene.Items)
        {
            if (item.Layer == SceneLayer.MapModel)
            {
                DrawItem(item, translucent);
            }
        }

        uint[] overlay = _color;
        float[] overlayDepth = _depth;
        _color = color;
        _depth = depth;
        _picks = picks;
        float a = Math.Clamp(opacity, 0, 1);
        for (int i = _top * _width; i < _bottom * _width; i++)
        {
            if (overlayDepth[i] < 1f)
            {
                _color[i] = Blend(_color[i], overlay[i], a);
            }
        }
    }

    private void DrawItem(SceneItem item, List<(SceneItem, RenderBatch)> translucent)
    {
        Matrix4x4 world = item.Mesh.Billboard ? Billboarded(item.World) : item.World;
        if (!item.Mesh.Bounds.IsEmpty && IsOutside(item.Mesh.Bounds, world))
        {
            return;
        }

        item = item with { World = world };
        Prepare(item);
        bool seeThrough = item.Tint >> 24 < 255;
        foreach (RenderBatch batch in item.Mesh.Batches)
        {
            if ((batch.Translucent || seeThrough) && _mode != MapViewMode.Wireframe)
            {
                translucent.Add((item, batch));
            }
            else
            {
                DrawBatch(item, batch, false, prepared: true);
            }
        }
    }

    /// <summary>Turns a billboard about z so that its +y axis points to the camera.</summary>
    private Matrix4x4 Billboarded(Matrix4x4 world)
    {
        Vector3 position = world.Translation;
        float scale = new Vector3(world.M11, world.M12, world.M13).Length();
        float scaleZ = new Vector3(world.M31, world.M32, world.M33).Length();
        Vector3 toCamera = _camera.Orthographic ? -_camera.Forward : _camera.Position - position;
        float angle = MathF.Atan2(-toCamera.X, toCamera.Y);
        return Matrix4x4.CreateScale(scale, scale, scaleZ) * Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(position);
    }

    private bool IsOutside(Box3 bounds, Matrix4x4 world)
    {
        Matrix4x4 m = world * _viewProjection;
        bool allLeft = true, allRight = true, allTop = true, allBottom = true, allNear = true;
        foreach (Vector3 corner in bounds.Corners())
        {
            Vector4 c = Vector4.Transform(new Vector4(corner, 1), m);
            allLeft &= c.X < -c.W;
            allRight &= c.X > c.W;
            allBottom &= c.Y < -c.W;
            allTop &= c.Y > c.W;
            allNear &= c.Z < 0;
        }

        if (allLeft || allRight || allTop || allBottom || allNear)
        {
            return true;
        }

        // Outside this band's rows?
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 corner in bounds.Corners())
        {
            Vector4 c = Vector4.Transform(new Vector4(corner, 1), m);
            if (c.W <= 0.01f)
            {
                return false;
            }

            float y = (1 - c.Y / c.W) * 0.5f * _height;
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        return maxY < _top - 1 || minY > _bottom + 1;
    }

    private void Prepare(SceneItem item)
    {
        RenderMesh mesh = item.Mesh;
        int n = mesh.Positions.Length;
        if (_clip.Length < n)
        {
            _clip = new Vector4[n];
            _shade = new float[n];
        }

        Matrix4x4 m = item.World * _viewProjection;
        for (int i = 0; i < n; i++)
        {
            _clip[i] = Vector4.Transform(new Vector4(mesh.Positions[i], 1), m);
            if (item.DepthBias > 0)
            {
                _clip[i].Z = BiasedDepth(_clip[i], item.DepthBias);
            }

            Vector3 normal = Vector3.TransformNormal(mesh.Normals[i], item.World);
            float length = normal.Length();
            _shade[i] = length > 0 ? 0.5f + 0.5f * Math.Max(0, Vector3.Dot(normal / length, Light)) : 1f;
        }

        _prepared = item;
    }

    /// <summary>
    /// The clip-space depth of a point moved <paramref name="bias"/> units towards the camera (only the depth changes, not
    /// where it is drawn), so that flat things lying on the ground win against it.
    /// </summary>
    /// <summary>How far decals are drawn in front of the surface below (world units, only the depth changes).</summary>
    private const float DecalBias = 0.4f;

    private float BiasedDepth(Vector4 clip, float bias)
    {
        if (_camera.Orthographic)
        {
            return clip.Z - bias / (2 * MapCamera.Far) * clip.W;
        }

        const float n = MapCamera.Near, f = MapCamera.Far;
        float w = clip.W;
        if (w <= n)
        {
            return clip.Z;
        }

        float shifted = Math.Max(n, w - bias);
        return f / (f - n) * (1 - n / shifted) * w;
    }

    private void DrawBatch(SceneItem item, RenderBatch batch, bool blend, bool prepared = false)
    {
        if (!prepared || !ReferenceEquals(_prepared, item))
        {
            Prepare(item);
        }

        RenderMesh mesh = item.Mesh;
        int[] idx = batch.Indices;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            int a = idx[i], b = idx[i + 1], c = idx[i + 2];
            if (_mode == MapViewMode.Wireframe)
            {
                uint wire = item.Layer switch
                {
                    SceneLayer.Mission => 0xFFFFE060,
                    SceneLayer.MapModel => 0xFF60E0FF,
                    SceneLayer.Decoration => 0xFFE0E0E0,
                    _ => 0xFF90C090,
                };
                DrawClipLine(_clip[a], _clip[b], wire, item.Pick);
                DrawClipLine(_clip[b], _clip[c], wire, item.Pick);
                DrawClipLine(_clip[c], _clip[a], wire, item.Pick);
                continue;
            }

            uint flatShade = 0;
            if (_mode == MapViewMode.White)
            {
                Vector3 pa = Vector3.Transform(mesh.Positions[a], item.World);
                Vector3 n = Vector3.Cross(Vector3.Transform(mesh.Positions[b], item.World) - pa, Vector3.Transform(mesh.Positions[c], item.World) - pa);
                float len = n.Length();
                float s = len > 0 ? 0.35f + 0.65f * MathF.Abs(Vector3.Dot(n / len, Light)) : 1f;
                flatShade = Tinted(Scale(item.Layer == SceneLayer.Sea ? 0xFF8FA9C0 : 0xFFF2F2F2, s), item.Tint);
            }

            ClipAndDraw(item, batch, a, b, c, blend, flatShade);
        }
    }

    private Vertex MakeVertex(SceneItem item, RenderBatch batch, int index)
    {
        RenderMesh mesh = item.Mesh;
        uint c;
        if (batch.Lit)
        {
            c = _settings.Lighting ? Scale(0xFFFFFFFF, _shade[index]) : 0xFFFFFFFF;
        }
        else
        {
            c = _settings.VertexColors ? mesh.Colors[index] : (mesh.Colors[index] & 0xFF000000) | 0x00FFFFFF;
        }

        c = Tinted(c, item.Tint);
        Vector2 uv = mesh.TexCoords[index];
        if (batch.Texture is { } t)
        {
            uv = new Vector2(uv.X * t.ScaleS, uv.Y * t.ScaleT);
        }

        return new Vertex
        {
            Clip = _clip[index],
            Uv = uv,
            Color = new Vector4(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, (c >> 24) / 255f),
        };
    }

    private void ClipAndDraw(SceneItem item, RenderBatch batch, int a, int b, int c, bool blend, uint flatShade)
    {
        Vertex va = MakeVertex(item, batch, a), vb = MakeVertex(item, batch, b), vc = MakeVertex(item, batch, c);
        if (batch.Decal)
        {
            // The game draws decals with its decal depth mode (on the surface below at the same depth).
            va.Clip.Z = BiasedDepth(va.Clip, DecalBias);
            vb.Clip.Z = BiasedDepth(vb.Clip, DecalBias);
            vc.Clip.Z = BiasedDepth(vc.Clip, DecalBias);
        }

        bool inA = va.Clip.Z >= 0, inB = vb.Clip.Z >= 0, inC = vc.Clip.Z >= 0;
        if (inA && inB && inC)
        {
            Raster(va, vb, vc, item, batch, blend, flatShade);
            return;
        }

        if (!inA && !inB && !inC)
        {
            return;
        }

        // Clip against the near plane (z = 0 in clip space).
        _polygon[0] = va;
        _polygon[1] = vb;
        _polygon[2] = vc;
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            Vertex p = _polygon[i], q = _polygon[(i + 1) % 3];
            bool pin = p.Clip.Z >= 0, qin = q.Clip.Z >= 0;
            if (pin)
            {
                _polygon2[count++] = p;
            }

            if (pin != qin)
            {
                float t = p.Clip.Z / (p.Clip.Z - q.Clip.Z);
                _polygon2[count++] = new Vertex
                {
                    Clip = Vector4.Lerp(p.Clip, q.Clip, t),
                    Uv = Vector2.Lerp(p.Uv, q.Uv, t),
                    Color = Vector4.Lerp(p.Color, q.Color, t),
                };
            }
        }

        for (int i = 1; i + 1 < count; i++)
        {
            Raster(_polygon2[0], _polygon2[i], _polygon2[i + 1], item, batch, blend, flatShade);
        }
    }

    private void Raster(Vertex v0, Vertex v1, Vertex v2, SceneItem item, RenderBatch batch, bool blend, uint flatShade)
    {
        if (v0.Clip.W <= 0 || v1.Clip.W <= 0 || v2.Clip.W <= 0)
        {
            return;
        }

        float iw0 = 1 / v0.Clip.W, iw1 = 1 / v1.Clip.W, iw2 = 1 / v2.Clip.W;
        float x0 = (v0.Clip.X * iw0 + 1) * 0.5f * _width, y0 = (1 - v0.Clip.Y * iw0) * 0.5f * _height;
        float x1 = (v1.Clip.X * iw1 + 1) * 0.5f * _width, y1 = (1 - v1.Clip.Y * iw1) * 0.5f * _height;
        float x2 = (v2.Clip.X * iw2 + 1) * 0.5f * _width, y2 = (1 - v2.Clip.Y * iw2) * 0.5f * _height;
        float z0 = v0.Clip.Z * iw0, z1 = v1.Clip.Z * iw1, z2 = v2.Clip.Z * iw2;

        float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (MathF.Abs(area) < 1e-8f)
        {
            return;
        }

        // In screen space (y down) a triangle that is counter-clockwise in the world shows a negative area.
        if (batch.CullBack && area > 0)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
        int maxX = Math.Min(_width - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
        int minY = Math.Max(_top, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))));
        int maxY = Math.Min(_bottom - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        float invArea = 1 / area;
        MapTexture? texture = _mode == MapViewMode.Textured ? batch.Texture : null;
        bool white = _mode == MapViewMode.White;
        bool bilinear = _settings.Bilinear;
        Vector2 uw0 = v0.Uv * iw0, uw1 = v1.Uv * iw1, uw2 = v2.Uv * iw2;
        Vector4 cw0 = v0.Color * iw0, cw1 = v1.Color * iw1, cw2 = v2.Color * iw2;
        int pick = item.Pick;
        MapTexture? detail = texture?.Detail;
        bool cutOut = texture is not null && (batch.Lit ? texture.HasAlpha : texture.CutsOut);
        Vector2 detailScale = detail is not null && texture is not null
            ? new Vector2(detail.ScaleS / texture.ScaleS, detail.ScaleT / texture.ScaleT)
            : Vector2.One;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * _width;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * invArea;
                float w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * invArea;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0)
                {
                    continue;
                }

                float z = w0 * z0 + w1 * z1 + w2 * z2;
                int i = row + x;
                if (z >= _depth[i] || z < -1)
                {
                    continue;
                }

                uint color;
                float alpha;
                if (white)
                {
                    color = flatShade;
                    alpha = (flatShade >> 24) / 255f;
                }
                else
                {
                    float iw = w0 * iw0 + w1 * iw1 + w2 * iw2;
                    float w = 1 / iw;
                    Vector4 shade = (cw0 * w0 + cw1 * w1 + cw2 * w2) * w;
                    if (texture is not null)
                    {
                        // The texture's colour combiner: texels, vertex colour, primitive and environment colours.
                        Vector2 uv = (uw0 * w0 + uw1 * w1 + uw2 * w2) * w;
                        Vector4 texel0 = bilinear ? SampleBilinear(texture, uv.X, uv.Y) : Unpack(Sample(texture, uv.X, uv.Y));
                        Vector4 texel1 = texel0;
                        if (detail is not null)
                        {
                            Vector2 duv = uv * detailScale;
                            texel1 = bilinear ? SampleBilinear(detail, duv.X, duv.Y) : Unpack(Sample(detail, duv.X, duv.Y));
                        }

                        Vector4 result = batch.Lit
                            ? new Vector4(texel0.X * shade.X, texel0.Y * shade.Y, texel0.Z * shade.Z, texel0.W * shade.W)
                            : texture.Combine.Evaluate(texel0, texel1, shade, texture.Primitive, texture.Environment, texture.PrimitiveLod);
                        alpha = result.W;
                        color = Pack(result.X, result.Y, result.Z, alpha);
                    }
                    else
                    {
                        alpha = shade.W;
                        color = Pack(shade.X, shade.Y, shade.Z, shade.W);
                    }
                }

                if (blend)
                {
                    if (alpha < 0.02f)
                    {
                        continue;
                    }

                    _color[i] = Blend(_color[i], color, alpha);

                    // A see-through surface can be chosen where it is in front of what is behind it.
                    if (pick >= 0)
                    {
                        _picks[i] = pick;
                    }
                }
                else
                {
                    // Opaque surfaces cut out clear texels only where the drawn alpha comes from the texture.
                    if (cutOut && alpha < 0.5f)
                    {
                        continue;
                    }

                    _color[i] = color | 0xFF000000;
                    _depth[i] = z;
                    _picks[i] = pick;
                }
            }
        }
    }

    private static uint Sample(MapTexture t, float s, float u)
    {
        int x = Wrap((int)MathF.Floor(s), t.WrapS, t.Width, t.MirrorS, t.ClampS);
        int y = Wrap((int)MathF.Floor(u), t.WrapT, t.Height, t.MirrorT, t.ClampT);
        return t.Pixels[y * t.Width + x];
    }

    private static Vector4 SampleBilinear(MapTexture t, float s, float u)
    {
        s -= 0.5f;
        u -= 0.5f;
        float fs = MathF.Floor(s), fu = MathF.Floor(u);
        float ds = s - fs, du = u - fu;
        int x0 = (int)fs, y0 = (int)fu;
        int xa = Wrap(x0, t.WrapS, t.Width, t.MirrorS, t.ClampS), xb = Wrap(x0 + 1, t.WrapS, t.Width, t.MirrorS, t.ClampS);
        int ya = Wrap(y0, t.WrapT, t.Height, t.MirrorT, t.ClampT), yb = Wrap(y0 + 1, t.WrapT, t.Height, t.MirrorT, t.ClampT);
        Vector4 a = Unpack(t.Pixels[ya * t.Width + xa]), b = Unpack(t.Pixels[ya * t.Width + xb]);
        Vector4 c = Unpack(t.Pixels[yb * t.Width + xa]), d = Unpack(t.Pixels[yb * t.Width + xb]);
        return Vector4.Lerp(Vector4.Lerp(a, b, ds), Vector4.Lerp(c, d, ds), du);
    }

    private static Vector4 Unpack(uint c) =>
        new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, (c >> 24) / 255f);

    private static int Wrap(int value, int period, int size, bool mirror, bool clamp)
    {
        if (clamp)
        {
            return Math.Clamp(value, 0, size - 1);
        }

        period = Math.Max(1, period);
        if (mirror)
        {
            int m = ((value % (2 * period)) + 2 * period) % (2 * period);
            value = m < period ? m : 2 * period - 1 - m;
        }
        else
        {
            value = ((value % period) + period) % period;
        }

        return Math.Min(value, size - 1);
    }

    private void DrawSelection(MapScene scene, MapRenderSettings settings)
    {
        int pick = settings.Selected;
        uint yellow = 0xFFFFE600;
        foreach (SceneItem item in scene.Items)
        {
            if (item.Pick == pick && !item.Mesh.Bounds.IsEmpty)
            {
                Matrix4x4 world = item.Mesh.Billboard ? Billboarded(item.World) : item.World;
                Vector3[] c = item.Mesh.Bounds.Corners().Select(p => Vector3.Transform(p, world)).ToArray();
                int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
                for (int e = 0; e < edges.Length; e += 2)
                {
                    DrawLine(c[edges[e]], c[edges[e + 1]], yellow, -1, 1, false);
                }
            }
        }

        foreach (SceneLine line in scene.Lines)
        {
            if (line.Pick == pick)
            {
                DrawLine(line.A, line.B, yellow, -1, Math.Max(2, line.Width), false);
            }
        }

        foreach (SceneLine line in scene.SelectionLines(pick))
        {
            DrawLine(line.A, line.B, line.Color, -1, line.Width, false);
        }

        if (settings.SelectedPosition is not { } origin)
        {
            return;
        }

        float distance = _camera.Orthographic ? 1 : Vector3.Distance(_camera.Position, origin);
        float length = 70 * _lineScale * _camera.UnitsPerPixel(distance, _height);
        Matrix4x4 rotation = settings.GizmoRotation ?? Matrix4x4.Identity;
        (Vector3 Axis, uint Color)[] axes =
        [
            (Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, rotation)), 0xFFE53935),
            (Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rotation)), 0xFF43A047),
            (Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, rotation)), 0xFF1E88E5),
        ];
        if (settings.Gizmo == GizmoMode.Move)
        {
            foreach ((Vector3 axis, uint color) in axes)
            {
                Vector3 tip = origin + axis * length;
                DrawLine(origin, tip, color, -1, 3, false);
                Vector3 cross = Vector3.Cross(axis, MathF.Abs(Vector3.Dot(axis, _camera.Forward)) > 0.9f ? _camera.Up : _camera.Forward);
                Vector3 side = cross.LengthSquared() > 1e-8f ? Vector3.Normalize(cross) : Vector3.UnitX;
                DrawLine(tip, tip - axis * length * 0.2f + side * length * 0.08f, color, -1, 3, false);
                DrawLine(tip, tip - axis * length * 0.2f - side * length * 0.08f, color, -1, 3, false);
            }
        }
        else
        {
            foreach ((Vector3 axis, uint color) in axes)
            {
                Vector3 helper = MathF.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
                Vector3 u = Vector3.Normalize(Vector3.Cross(axis, helper));
                Vector3 v = Vector3.Cross(axis, u);
                Vector3 previous = origin + u * length;
                for (int k = 1; k <= 48; k++)
                {
                    float angle = k * MathF.Tau / 48;
                    Vector3 next = origin + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * length;
                    DrawLine(previous, next, color, -1, 2, false);
                    previous = next;
                }
            }
        }
    }

    /// <summary>Draws a world line.</summary>
    private void DrawLine(Vector3 a, Vector3 b, uint color, int pick, int width, bool depthTest)
    {
        Vector4 ca = Vector4.Transform(new Vector4(a, 1), _viewProjection);
        Vector4 cb = Vector4.Transform(new Vector4(b, 1), _viewProjection);
        DrawClipLine(ca, cb, color, pick, Math.Max(1, (int)MathF.Round(width * _lineScale)), depthTest);
    }

    private void DrawClipLine(Vector4 a, Vector4 b, uint color, int pick, int width = 1, bool depthTest = true)
    {
        if (a.Z < 0 && b.Z < 0)
        {
            return;
        }

        if (a.Z < 0 || b.Z < 0)
        {
            float t = a.Z / (a.Z - b.Z);
            Vector4 cut = Vector4.Lerp(a, b, t);
            if (a.Z < 0)
            {
                a = cut;
            }
            else
            {
                b = cut;
            }
        }

        if (a.W <= 0 || b.W <= 0)
        {
            return;
        }

        float ax = (a.X / a.W + 1) * 0.5f * _width, ay = (1 - a.Y / a.W) * 0.5f * _height, az = a.Z / a.W;
        float bx = (b.X / b.W + 1) * 0.5f * _width, by = (1 - b.Y / b.W) * 0.5f * _height, bz = b.Z / b.W;
        if (Math.Max(ay, by) < _top - width || Math.Min(ay, by) > _bottom + width)
        {
            return;
        }

        float dx = bx - ax, dy = by - ay;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));
        if (steps > 40000)
        {
            return; // far outside the picture
        }

        int half = width / 2;
        for (int s = 0; s <= steps; s++)
        {
            float t = steps == 0 ? 0 : (float)s / steps;
            int x = (int)(ax + dx * t), y = (int)(ay + dy * t);
            float z = az + (bz - az) * t - 1e-4f;
            for (int oy = -half; oy <= half; oy++)
            {
                for (int ox = -half; ox <= half; ox++)
                {
                    int px = x + ox, py = y + oy;
                    if (px < 0 || py < _top || px >= _width || py >= _bottom)
                    {
                        continue;
                    }

                    int i = py * _width + px;
                    if (depthTest && z > _depth[i])
                    {
                        continue;
                    }

                    _color[i] = color;
                    if (pick >= 0)
                    {
                        _picks[i] = pick;
                    }
                }
            }
        }
    }

    private static uint Pack(float r, float g, float b, float a) =>
        (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (uint)(Math.Clamp(r, 0, 1) * 255) << 16 | (uint)(Math.Clamp(g, 0, 1) * 255) << 8 | (uint)(Math.Clamp(b, 0, 1) * 255);

    private static uint Scale(uint c, float f) => Pack(((c >> 16) & 0xFF) / 255f * f, ((c >> 8) & 0xFF) / 255f * f, (c & 0xFF) / 255f * f, (c >> 24) / 255f);

    private static uint Tinted(uint c, uint tint) => tint == 0xFFFFFFFF ? c : RenderMesh.Multiply(c, tint);

    private static uint Blend(uint under, uint over, float a)
    {
        a = Math.Clamp(a, 0, 1);
        float r = ((under >> 16) & 0xFF) * (1 - a) + ((over >> 16) & 0xFF) * a;
        float g = ((under >> 8) & 0xFF) * (1 - a) + ((over >> 8) & 0xFF) * a;
        float b = (under & 0xFF) * (1 - a) + (over & 0xFF) * a;
        return 0xFF000000 | (uint)r << 16 | (uint)g << 8 | (uint)b;
    }
}
