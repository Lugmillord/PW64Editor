using System.Numerics;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>Triangles of a mesh that share a texture and a drawing mode.</summary>
/// <param name="Texture">The texture, or null for vertex colours only.</param>
/// <param name="Indices">Three vertex numbers per triangle.</param>
/// <param name="Lit">The vertices carry normals; the colour is white, shaded by the light.</param>
/// <param name="Translucent">Drawn see-through over what is behind.</param>
/// <param name="CullBack">Back faces are not drawn.</param>
/// <param name="FlatColor">The batch's average colour (texture × vertex colour), for the overview.</param>
public sealed record RenderBatch(MapTexture? Texture, int[] Indices, bool Lit, bool Translucent, bool CullBack, uint FlatColor)
{
    /// <summary>A decal: drawn over the surface it lies on (the game's decal depth mode).</summary>
    public bool Decal { get; init; }
}

/// <summary>
/// A mesh ready for drawing: vertices (position, texture coordinates, colour, normal) and batches of triangles.
/// Built from a contour or from the most detailed level of a model (with its parts in their rest pose).
/// </summary>
public sealed class RenderMesh
{
    internal RenderMesh(Vector3[] positions, Vector2[] texCoords, uint[] colors, Vector3[] normals, RenderBatch[] batches, bool billboard)
    {
        Positions = positions;
        TexCoords = texCoords;
        Colors = colors;
        Normals = normals;
        Batches = batches;
        Billboard = billboard;
        Box3 bounds = Box3.Empty;
        foreach (RenderBatch batch in batches)
        {
            foreach (int i in batch.Indices)
            {
                bounds = bounds.Include(positions[i]);
            }
        }

        Bounds = bounds;
    }

    /// <summary>The positions.</summary>
    public Vector3[] Positions { get; }

    /// <summary>The texture coordinates as stored (s, t in 1/32 texel before the tile's shift).</summary>
    public Vector2[] TexCoords { get; }

    /// <summary>The vertex colours (0xAARRGGBB).</summary>
    public uint[] Colors { get; }

    /// <summary>The normals (for lit batches).</summary>
    public Vector3[] Normals { get; }

    /// <summary>The batches.</summary>
    public RenderBatch[] Batches { get; }

    /// <summary>True if the mesh turns about its z axis to face the camera.</summary>
    public bool Billboard { get; }

    /// <summary>The bounding box of the drawn vertices.</summary>
    public Box3 Bounds { get; }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Batches.Sum(b => b.Indices.Length / 3);

    /// <summary>The mesh of a contour (positions relative to its cell's centre).</summary>
    public static RenderMesh FromContour(Contour contour, Func<int, MapTexture?> textures) =>
        Build(contour.Vertices, contour.Materials.Select(m => (m, Matrix4x4.Identity)), textures, false);

    /// <summary>The mesh of a model's most detailed level in model units (parts in their rest pose).</summary>
    public static RenderMesh FromModel(GameModel model, Func<int, MapTexture?> textures) => FromModel(model, textures, null, null);

    /// <summary>
    /// The mesh of a model's most detailed level with some parts hidden and some turned in their own space (the variants
    /// the game shows, e.g. a ring's dial and clock hands).
    /// </summary>
    public static RenderMesh FromModel(GameModel model, Func<int, MapTexture?> textures, IReadOnlySet<int>? hiddenParts,
        IReadOnlyDictionary<int, Matrix4x4>? turns)
    {
        if (model.Lods.Count == 0)
        {
            return Build(model.Vertices, [], textures, false);
        }

        Matrix4x4[] parts = model.PartTransforms(0, turns);
        IEnumerable<(MeshMaterial, Matrix4x4)> materials = model.Lods[0].Parts
            .SelectMany((part, i) => hiddenParts?.Contains(i) == true ? [] : part.Materials.Select(m => (m, parts[i])));
        return Build(model.Vertices, materials, textures, model.Lods[0].Billboard);
    }

    /// <summary>A mesh from explicit triangles (for the editor's own markers).</summary>
    public static RenderMesh FromTriangles(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices, uint color, bool translucent = false)
    {
        var normals = new Vector3[positions.Count];
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            Vector3 n = Vector3.Cross(positions[indices[i + 1]] - positions[indices[i]], positions[indices[i + 2]] - positions[indices[i]]);
            for (int k = 0; k < 3; k++)
            {
                normals[indices[i + k]] += n;
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(normals[i]);
        }

        return new RenderMesh([.. positions], new Vector2[positions.Count], Enumerable.Repeat(color, positions.Count).ToArray(), normals,
            [new RenderBatch(null, [.. indices], true, translucent, false, color)], false);
    }

    private static RenderMesh Build(IReadOnlyList<MeshVertex> source, IEnumerable<(MeshMaterial Material, Matrix4x4 Part)> materials,
        Func<int, MapTexture?> textures, bool billboard)
    {
        // Vertices are copied per part, since one vertex may be used by several parts with different matrices.
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var colors = new List<uint>();
        var normals = new List<Vector3>();
        var batches = new List<RenderBatch>();
        var map = new Dictionary<(int Vertex, Matrix4x4 Part, bool Lit), int>();
        foreach ((MeshMaterial material, Matrix4x4 part) in materials)
        {
            MapTexture? texture = material.HasTexture ? textures(material.Texture) : null;
            bool lit = material.IsLit;
            var indices = new List<int>(material.Triangles.Count * 3);
            long r = 0, g = 0, b = 0;
            foreach (MeshTriangle t in material.Triangles)
            {
                if (t.A >= source.Count || t.B >= source.Count || t.C >= source.Count)
                {
                    continue;
                }

                foreach (int v in (ReadOnlySpan<int>)[t.A, t.B, t.C])
                {
                    if (!map.TryGetValue((v, part, lit), out int index))
                    {
                        MeshVertex vertex = source[v];
                        index = positions.Count;
                        positions.Add(Vector3.Transform(vertex.Position, part));
                        texCoords.Add(new Vector2(vertex.S, vertex.T));
                        colors.Add(lit ? 0xFFFFFFFF : (uint)vertex.A << 24 | (uint)vertex.R << 16 | (uint)vertex.G << 8 | vertex.B);
                        normals.Add(lit && vertex.Normal != Vector3.Zero ? Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, part)) : Vector3.UnitZ);
                        map[(v, part, lit)] = index;
                    }

                    indices.Add(index);
                    uint c = colors[index];
                    r += (c >> 16) & 0xFF;
                    g += (c >> 8) & 0xFF;
                    b += c & 0xFF;
                }
            }

            if (indices.Count == 0)
            {
                continue;
            }

            uint average = 0xFF000000 | (uint)(r / indices.Count) << 16 | (uint)(g / indices.Count) << 8 | (uint)(b / indices.Count);
            uint flat = texture is null ? average : Multiply(texture.AverageColor(), average);
            // Translucent modes with a cut-out texture (only fully clear or fully opaque texels, e.g. trees) are drawn
            // opaque with an alpha test, so they hide what is behind them.
            bool partialAlpha = texture?.BlendsAlpha ?? false;
            bool vertexAlpha = indices.Any(i => colors[i] >> 24 < 250);
            batches.Add(new RenderBatch(texture, [.. indices], lit, material.IsTranslucent && (partialAlpha || vertexAlpha), material.CullsBack, flat)
            {
                Decal = material.IsDecal,
            });
        }

        return new RenderMesh([.. positions], [.. texCoords], [.. colors], [.. normals], [.. batches], billboard);
    }

    /// <summary>Multiplies two colours channel by channel.</summary>
    public static uint Multiply(uint a, uint b) =>
        (((a >> 24) * (b >> 24) / 255) << 24)
        | ((((a >> 16) & 0xFF) * ((b >> 16) & 0xFF) / 255) << 16)
        | ((((a >> 8) & 0xFF) * ((b >> 8) & 0xFF) / 255) << 8)
        | ((a & 0xFF) * (b & 0xFF) / 255);
}
