using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Map;

/// <summary>One part of a model detail level: its materials and its place in the part tree.</summary>
/// <param name="Materials">The materials with their triangles (vertex numbers of the model).</param>
/// <param name="Level">The attachment level: a part one level deeper than the one before hangs on it.</param>
public sealed record ModelPart(IReadOnlyList<MeshMaterial> Materials, int Level);

/// <summary>One detail level of a model.</summary>
/// <param name="Parts">The parts.</param>
/// <param name="Billboard">True if the model turns about its z axis to face the camera (trees and the like).</param>
/// <param name="Distance">The distance up to which this level is used.</param>
public sealed record ModelLod(IReadOnlyList<ModelPart> Parts, bool Billboard, float Distance);

/// <summary>
/// A model (UVMD): vertices, detail levels with parts and materials, the part matrices and the scale.
/// </summary>
/// <remarks>
/// <para>Layout of the COMM chunk: u16 vertex count, u8 detail levels, u8 parts, u8 count of 0x24-byte records, u8
/// transparency, 2 bytes; the vertices (<see cref="MeshVertex"/>); per level u8 part count, u8 billboard, per part u8
/// material count, u8, u8 attachment level, per material the header and commands of <see cref="MeshCommands"/>, then f32
/// the level's distance; the part matrices (4 × 4 f32 each); the 0x24-byte records; f32, f32 the inverse scale. The
/// game draws a model at 1 / inverse scale of its vertex units. Checked against all 363 models of the retail ROM. The
/// format follows the noclip viewer (https://github.com/magcius/noclip.website, MIT license).</para>
/// </remarks>
public sealed class GameModel
{
    private GameModel(IReadOnlyList<MeshVertex> vertices, IReadOnlyList<ModelLod> lods, IReadOnlyList<Matrix4x4> partMatrices,
        float inverseScale, bool hasTransparency)
    {
        Vertices = vertices;
        Lods = lods;
        PartMatrices = partMatrices;
        InverseScale = inverseScale;
        HasTransparency = hasTransparency;
    }

    /// <summary>The vertices.</summary>
    public IReadOnlyList<MeshVertex> Vertices { get; }

    /// <summary>The detail levels, the most detailed first.</summary>
    public IReadOnlyList<ModelLod> Lods { get; }

    /// <summary>The matrices of the parts (relative to the parent part).</summary>
    public IReadOnlyList<Matrix4x4> PartMatrices { get; }

    /// <summary>The game draws the model at 1 / this of its vertex units.</summary>
    public float InverseScale { get; }

    /// <summary>The model's transparency byte is set.</summary>
    public bool HasTransparency { get; }

    /// <summary>The scale the game draws the model with (1 / <see cref="InverseScale"/>).</summary>
    public float Scale => InverseScale > 0 ? 1f / InverseScale : 1f;

    /// <summary>Reads a model from its file.</summary>
    public static GameModel ParseFile(byte[] file) => Parse(MapChunks.ReadComm(file, "UVMD"));

    /// <summary>Reads a model from its COMM chunk.</summary>
    public static GameModel Parse(ReadOnlySpan<byte> data)
    {
        int vertexCount = BinaryPrimitives.ReadUInt16BigEndian(data);
        int lodCount = data[2], partCount = data[3], recordCount = data[4];
        bool transparency = (sbyte)data[5] > 0;
        int offset = 8;

        var vertices = new MeshVertex[vertexCount];
        for (int i = 0; i < vertexCount; i++, offset += MeshVertex.Size)
        {
            vertices[i] = MeshVertex.Read(data[offset..]);
        }

        var lods = new List<ModelLod>();
        for (int l = 0; l < lodCount; l++)
        {
            int parts = data[offset];
            bool billboard = data[offset + 1] != 0;
            offset += 2;
            var partList = new List<ModelPart>();
            for (int p = 0; p < parts; p++)
            {
                int materialCount = data[offset];
                int level = data[offset + 2];
                offset += 3;
                var materials = new List<MeshMaterial>();
                for (int m = 0; m < materialCount; m++)
                {
                    int mode = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                    int texture = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
                    int commands = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
                    offset += MeshCommands.HeaderSize;
                    materials.Add(new MeshMaterial(mode, texture, MeshCommands.Read(data, ref offset, commands)));
                }

                partList.Add(new ModelPart(materials, level));
            }

            float distance = BinaryPrimitives.ReadSingleBigEndian(data[offset..]);
            offset += 4;
            lods.Add(new ModelLod(partList, billboard, distance));
        }

        var matrices = new Matrix4x4[partCount];
        for (int i = 0; i < partCount; i++, offset += 0x40)
        {
            matrices[i] = MapMath.ReadFloatMatrix(data[offset..]);
        }

        offset += recordCount * 0x24;
        float inverseScale = offset + 8 <= data.Length ? BinaryPrimitives.ReadSingleBigEndian(data[(offset + 4)..]) : 1f;
        return new GameModel(vertices, lods, matrices, inverseScale, transparency);
    }

    /// <summary>
    /// The matrix of each part of a detail level in model space: the part's own matrix, then its parent's (the parts
    /// are listed depth first; a part one level deeper than the one before hangs on it).
    /// </summary>
    public Matrix4x4[] PartTransforms(int lod) => PartTransforms(lod, null);

    /// <summary>
    /// The parts' model matrices of a level, where some parts are turned in their own space first (as the game turns a
    /// ring's clock hands: the part's matrix rotated about its own axis).
    /// </summary>
    public Matrix4x4[] PartTransforms(int lod, IReadOnlyDictionary<int, Matrix4x4>? turns)
    {
        IReadOnlyList<ModelPart> parts = Lods[lod].Parts;
        var result = new Matrix4x4[parts.Count];
        var stack = new Dictionary<int, int>();
        int baseLevel = parts.Count > 0 ? parts[0].Level : 0;
        for (int i = 0; i < parts.Count; i++)
        {
            int level = parts[i].Level - baseLevel;
            Matrix4x4 own = i < PartMatrices.Count ? PartMatrices[i] : Matrix4x4.Identity;
            if (turns is not null && turns.TryGetValue(i, out Matrix4x4 turn))
            {
                own = turn * own;
            }

            result[i] = level > 0 && stack.TryGetValue(level - 1, out int parent) ? own * result[parent] : own;
            stack[level] = i;
        }

        return result;
    }

    /// <summary>The bounding box of the most detailed level in model units (before <see cref="Scale"/>).</summary>
    public Box3 Bounds()
    {
        if (Lods.Count == 0)
        {
            return Box3.Empty;
        }

        Matrix4x4[] parts = PartTransforms(0);
        Box3 box = Box3.Empty;
        for (int p = 0; p < Lods[0].Parts.Count; p++)
        {
            foreach (MeshMaterial material in Lods[0].Parts[p].Materials)
            {
                foreach (MeshTriangle t in material.Triangles)
                {
                    foreach (int v in (ReadOnlySpan<int>)[t.A, t.B, t.C])
                    {
                        if (v < Vertices.Count)
                        {
                            box = box.Include(Vector3.Transform(Vertices[v].Position, parts[p]));
                        }
                    }
                }
            }
        }

        return box;
    }
}
