using System.Numerics;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>What a drawn thing is in the scene.</summary>
public enum SceneLayer
{
    /// <summary>The island's terrain.</summary>
    Terrain,

    /// <summary>Decoration objects (the models on the contours) and the island's own objects.</summary>
    Decoration,

    /// <summary>The objects of the missions.</summary>
    Mission,

    /// <summary>The pause menu's map model.</summary>
    MapModel,

    /// <summary>The sea and the far island around the island.</summary>
    Sea,

    /// <summary>Invisible things of the island: trigger zones, warp points, sound zones, paths.</summary>
    Invisible,

    /// <summary>The markers the pause menu's map shows for the mission objects.</summary>
    MapMarker,
}

/// <summary>A mesh placed in the scene.</summary>
/// <param name="Mesh">The mesh.</param>
/// <param name="World">Model → world.</param>
/// <param name="Layer">What it is.</param>
/// <param name="Pick">The number of the thing it belongs to (<see cref="MapScene.Picks"/>), or −1 if it cannot be chosen.</param>
/// <param name="Tint">A colour multiplied with the mesh (0xFFFFFFFF for none; alpha below 255 draws it see-through).</param>
/// <param name="DepthBias">Drawn in front of surfaces up to this many world units in front of it (flat objects lying on
/// the ground, e.g. landing pads, so that they do not flicker with the ground).</param>
public sealed record SceneItem(RenderMesh Mesh, Matrix4x4 World, SceneLayer Layer, int Pick, uint Tint = 0xFFFFFFFF, float DepthBias = 0);

/// <summary>A line in the scene (links between objects, outlines of objects without a model).</summary>
/// <param name="Width">Width in pixels.</param>
public readonly record struct SceneLine(Vector3 A, Vector3 B, uint Color, int Pick, SceneLayer Layer = SceneLayer.Mission, int Width = 1);

/// <summary>
/// Everything the map shows: placed meshes and lines, and the list of things that can be chosen with what each one is.
/// </summary>
public sealed class MapScene
{
    /// <summary>The placed meshes.</summary>
    public List<SceneItem> Items { get; } = [];

    /// <summary>The lines.</summary>
    public List<SceneLine> Lines { get; } = [];

    /// <summary>Lines drawn only while their pick is chosen (e.g. the path a toy moves along).</summary>
    public List<SceneLine> PathLines { get; } = [];

    /// <summary>The lines shown while a pick number is chosen.</summary>
    public IEnumerable<SceneLine> SelectionLines(int pick) => PathLines.Where(l => l.Pick == pick);

    /// <summary>The things that can be chosen (the pick numbers index this list).</summary>
    public List<object> Picks { get; } = [];

    /// <summary>The terrain the scene shows (for the tiles).</summary>
    public TerrainMap? Terrain { get; set; }

    /// <summary>The colour behind everything.</summary>
    public uint SkyColor { get; set; } = 0xFF9DC4EA;

    /// <summary>The bounding box of the terrain (or of everything).</summary>
    public Box3 Bounds { get; set; } = Box3.Empty;

    /// <summary>The ground: per used cell its centre and the contour's triangles, for the height of the ground.</summary>
    internal List<(Vector3 Center, Contour Contour)> Ground { get; } = [];

    /// <summary>Adds a thing that can be chosen and returns its pick number.</summary>
    public int AddPick(object thing)
    {
        Picks.Add(thing);
        return Picks.Count - 1;
    }

    /// <summary>The pick number of a thing (−1 if it is not in the scene).</summary>
    public int PickOf(object thing) => Picks.IndexOf(thing);

    /// <summary>The bounding box of everything that belongs to a pick number (world units).</summary>
    public Box3 BoundsOf(int pick)
    {
        Box3 box = Box3.Empty;
        foreach (SceneItem item in Items)
        {
            if (item.Pick == pick && !item.Mesh.Bounds.IsEmpty)
            {
                foreach (Vector3 corner in item.Mesh.Bounds.Corners())
                {
                    box = box.Include(Vector3.Transform(corner, item.World));
                }
            }
        }

        foreach (SceneLine line in Lines)
        {
            if (line.Pick == pick)
            {
                box = box.Include(line.A).Include(line.B);
            }
        }

        return box;
    }

    /// <summary>
    /// The height of the ground at a point: the highest terrain surface at (x, y) that is not above <paramref name="below"/>
    /// (or the highest of all if none is below it); null where there is no terrain.
    /// </summary>
    public float? GroundHeight(float x, float y, float below = float.MaxValue)
    {
        float? best = null, highest = null;
        foreach ((Vector3 center, Contour contour) in Ground)
        {
            float lx = x - center.X, ly = y - center.Y;
            foreach (MeshMaterial material in contour.Materials)
            {
                foreach (MeshTriangle t in material.Triangles)
                {
                    if (t.A >= contour.Vertices.Count || t.B >= contour.Vertices.Count || t.C >= contour.Vertices.Count)
                    {
                        continue;
                    }

                    Vector3 a = contour.Vertices[t.A].Position, b = contour.Vertices[t.B].Position, c = contour.Vertices[t.C].Position;
                    if (HeightIn(a, b, c, lx, ly) is not { } h)
                    {
                        continue;
                    }

                    h += center.Z;
                    highest = highest is null || h > highest ? h : highest;
                    if (h <= below + 0.01f && (best is null || h > best))
                    {
                        best = h;
                    }
                }
            }
        }

        return best ?? highest;
    }

    private static float? HeightIn(Vector3 a, Vector3 b, Vector3 c, float x, float y)
    {
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(d) < 1e-6f)
        {
            return null;
        }

        float u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
        float v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
        float w = 1 - u - v;
        const float eps = -1e-4f;
        return u >= eps && v >= eps && w >= eps ? u * a.Z + v * b.Z + w * c.Z : null;
    }
}
