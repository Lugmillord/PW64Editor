using System.Buffers.Binary;
using System.Numerics;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Map;

/// <summary>Reads the chunks of a game file, unpacking GZIP chunks.</summary>
internal static class MapChunks
{
    /// <summary>The chunks of the file with their content (GZIP chunks unpacked, PAD chunks left out).</summary>
    /// <exception cref="InvalidDataException">The file is not of the expected type.</exception>
    public static List<(string Tag, byte[] Data)> Read(byte[] file, string formType)
    {
        IffForm form = IffForm.Parse(file);
        if (form.FormType != formType)
        {
            throw new InvalidDataException($"This is not a {formType} file (type {form.FormType}).");
        }

        var chunks = new List<(string, byte[])>();
        foreach (IffChunk chunk in form.Chunks)
        {
            if (chunk.Tag != "PAD ")
            {
                chunks.Add(GzipChunk.ReadChunkData(file, chunk));
            }
        }

        return chunks;
    }

    /// <summary>The single COMM chunk of a file.</summary>
    public static byte[] ReadComm(byte[] file, string formType) =>
        Read(file, formType).FirstOrDefault(c => c.Tag == "COMM").Data
        ?? throw new InvalidDataException($"The {formType} file has no COMM chunk.");
}

/// <summary>One used cell of a terrain: its centre and the contour placed there.</summary>
public sealed record TerrainCell(int Column, int Row, Vector3 Center, int Contour);

/// <summary>
/// A terrain (one COMM chunk of the UVTR file): a grid of cells, each used cell places one contour at its centre.
/// </summary>
/// <remarks>
/// <para>Layout: bounding box (six f32: min x, y, z, max x, y, z), u8 grid width, u8 grid height, f32 cell size x, f32
/// cell size y, f32 the cell's bounding radius; then per cell (row by row) a flag byte, and for a used cell 0x43 more
/// bytes: a 4 × 3 identity matrix (unused), f32 x, y, z of the centre, 1.0, a rotation byte (always 0), u16 the contour
/// number. Checked against the ten terrains of the retail ROM.</para>
/// </remarks>
public sealed class TerrainMap
{
    private TerrainMap(Box3 bounds, int width, int height, Vector2 cellSize, IReadOnlyList<TerrainCell> cells)
    {
        Bounds = bounds;
        Width = width;
        Height = height;
        CellSize = cellSize;
        Cells = cells;
    }

    /// <summary>The terrain's bounding box.</summary>
    public Box3 Bounds { get; }

    /// <summary>Number of cells from west to east.</summary>
    public int Width { get; }

    /// <summary>Number of cells from south to north.</summary>
    public int Height { get; }

    /// <summary>Size of one cell in game units.</summary>
    public Vector2 CellSize { get; }

    /// <summary>The used cells.</summary>
    public IReadOnlyList<TerrainCell> Cells { get; }

    /// <summary>The corners of all cells (grid points), for drawing the tiles.</summary>
    public IEnumerable<Vector2> GridPoints()
    {
        for (int row = 0; row <= Height; row++)
        {
            for (int column = 0; column <= Width; column++)
            {
                yield return new Vector2(Bounds.Min.X + column * CellSize.X, Bounds.Min.Y + row * CellSize.Y);
            }
        }
    }

    /// <summary>Reads one terrain.</summary>
    public static TerrainMap Parse(ReadOnlySpan<byte> data)
    {
        Box3 bounds = new(MapMath.ReadVector(data), MapMath.ReadVector(data[12..]));
        int width = data[0x18], height = data[0x19];
        var cellSize = new Vector2(BinaryPrimitives.ReadSingleBigEndian(data[0x1A..]), BinaryPrimitives.ReadSingleBigEndian(data[0x1E..]));
        var cells = new List<TerrainCell>();
        int offset = 0x26;
        for (int i = 0; i < width * height; i++)
        {
            int flag = data[offset++];
            if (flag == 0)
            {
                continue;
            }

            Vector3 center = MapMath.ReadVector(data[(offset + 0x30)..]);
            int contour = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 0x41)..]);
            cells.Add(new TerrainCell(i % width, i / width, center, contour));
            offset += 0x43;
        }

        return new TerrainMap(bounds, width, height, cellSize, cells);
    }

    /// <summary>Reads all terrains of the UVTR file.</summary>
    public static IReadOnlyList<TerrainMap> ParseFile(byte[] file) =>
        MapChunks.Read(file, "UVTR").Where(c => c.Tag == "COMM").Select(c => Parse(c.Data)).ToList();
}

/// <summary>A model standing on a contour (a decoration object), with its placement relative to the contour's centre.</summary>
/// <param name="Model">The model number.</param>
/// <param name="Placement">The placement matrix (first matrix of the record).</param>
/// <param name="CollisionCells">The 4 × 4 sub-cells of the contour the model's collision is checked in (bit 15 − (4 × row
/// + column)), the u16 behind the position in the record.</param>
public sealed record ContourModel(int Model, Matrix4x4 Placement, int CollisionCells);

/// <summary>
/// A contour (UVCT): one piece of terrain with its vertices, triangles, materials and the models standing on it.
/// </summary>
/// <remarks>
/// <para>Layout of the COMM chunk: u16 vertex count, triangle count, model count, material count; the vertices
/// (<see cref="MeshVertex"/>); the triangles (3 × u16 vertex, u16 collision flags of the 4 × 4 sub-cells); per model a
/// count byte, that many N64 fixed-point matrices, s16 model (−1 = none), f32 x, y, z, u16 sub-cells, u16; per material
/// the header of <see cref="MeshCommands"/>, the drawing commands, u16 first triangle, u16 solid, u16 sub-cells and 0x12
/// more bytes; at the end the bounding sphere (f32 x, y, z, radius). Checked against all 101 contours of the retail
/// ROM. The format follows the noclip viewer (https://github.com/magcius/noclip.website, MIT license).</para>
/// </remarks>
public sealed class Contour
{
    private Contour(IReadOnlyList<MeshVertex> vertices, IReadOnlyList<MeshMaterial> materials, IReadOnlyList<ContourModel> models)
    {
        Vertices = vertices;
        Materials = materials;
        Models = models;
    }

    /// <summary>The vertices, relative to the cell's centre.</summary>
    public IReadOnlyList<MeshVertex> Vertices { get; }

    /// <summary>The materials with their triangles.</summary>
    public IReadOnlyList<MeshMaterial> Materials { get; }

    /// <summary>The models standing on the contour.</summary>
    public IReadOnlyList<ContourModel> Models { get; }

    /// <summary>Reads a contour from its file.</summary>
    public static Contour ParseFile(byte[] file) => Parse(MapChunks.ReadComm(file, "UVCT"));

    /// <summary>Reads a contour from its COMM chunk.</summary>
    public static Contour Parse(ReadOnlySpan<byte> data)
    {
        int vertexCount = BinaryPrimitives.ReadUInt16BigEndian(data);
        int triangleCount = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        int modelCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        int materialCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        int offset = 8;

        var vertices = new MeshVertex[vertexCount];
        for (int i = 0; i < vertexCount; i++, offset += MeshVertex.Size)
        {
            vertices[i] = MeshVertex.Read(data[offset..]);
        }

        var triangles = new MeshTriangle[triangleCount];
        for (int i = 0; i < triangleCount; i++, offset += 8)
        {
            triangles[i] = new MeshTriangle(
                BinaryPrimitives.ReadUInt16BigEndian(data[offset..]),
                BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]),
                BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 4)..]));
        }

        var models = new List<ContourModel>();
        for (int i = 0; i < modelCount; i++)
        {
            int matrices = data[offset++];
            Matrix4x4 placement = Matrix4x4.Identity;
            for (int m = 0; m < matrices; m++, offset += 64)
            {
                if (m == 0)
                {
                    placement = MapMath.ReadFixedMatrix(data[offset..]);
                }
            }

            int model = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
            int cells = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 14)..]);
            offset += 0x12;
            if (model >= 0 && matrices > 0)
            {
                models.Add(new ContourModel(model, placement, cells));
            }
        }

        var materials = new List<MeshMaterial>();
        for (int i = 0; i < materialCount; i++)
        {
            int mode = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            int texture = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            int count = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 6)..]);
            int commands = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
            offset += MeshCommands.HeaderSize;
            MeshCommands.Skip(data, ref offset, commands);
            int first = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 0x18;
            int end = Math.Min(first + count, triangles.Length);
            materials.Add(new MeshMaterial(mode, texture, first < end ? triangles[first..end] : []));
        }

        return new Contour(vertices, materials, models);
    }
}
