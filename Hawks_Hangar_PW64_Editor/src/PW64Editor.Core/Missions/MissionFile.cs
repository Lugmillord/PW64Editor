using System.Text;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Missions;

/// <summary>
/// A mission file (FORM "UPWT"): one test of the game. Holds the text base name (JPTX), a name
/// (NAME), the COMM block (<see cref="MissionComm"/>) and one chunk per object type.
/// </summary>
/// <remarks>
/// The game reads it in taskLoadCommObj (src/app/task.c). In the original game no chunk of a
/// mission file is compressed. Only the COMM block is changed by the editor so far; it keeps its
/// size, so it is replaced in place and everything else stays byte for byte.
/// </remarks>
public sealed class MissionFile
{
    /// <summary>FORM type of mission files.</summary>
    public const string FormType = "UPWT";

    private static readonly string[] ObjectTags =
        ["THER", "LWIN", "TPAD", "LPAD", "LSTP", "RNGS", "BALS", "TARG", "HPAD", "BTGT", "PHTS", "FALC", "SDFM", "CNTG", "HOPD", "OBSV"];

    private readonly byte[] _file;
    private readonly IffChunk _commChunk;
    private readonly Dictionary<string, IffChunk> _chunks;

    private MissionFile(byte[] file, IffChunk commChunk, Dictionary<string, IffChunk> chunks, MissionComm comm, string textBase, string name)
    {
        _file = file;
        _commChunk = commChunk;
        _chunks = chunks;
        Comm = comm;
        TextBase = textBase;
        Name = name;
    }

    /// <summary>The COMM block as read from the file.</summary>
    public MissionComm Comm { get; }

    /// <summary>
    /// Base name of the mission's texts from the JPTX chunk, e.g. "E_GC_1": "_N" is the title, "_M"
    /// the task and "_H" the hint. All Cannonball targets of a level share the level's name.
    /// </summary>
    public string TextBase { get; }

    /// <summary>The internal name from the NAME chunk (not shown in the game).</summary>
    public string Name { get; }

    /// <summary>Reads a mission file.</summary>
    /// <exception cref="InvalidDataException">Not a mission file, or its COMM block is damaged.</exception>
    public static MissionFile Parse(byte[] file)
    {
        IffForm form = IffForm.Parse(file);
        if (form.FormType != FormType)
        {
            throw new InvalidDataException($"This is not a mission file (type {form.FormType}).");
        }

        var chunks = new Dictionary<string, IffChunk>();
        foreach (IffChunk chunk in form.Chunks)
        {
            if (chunk.Tag == GzipChunk.Tag)
            {
                throw new InvalidDataException("Compressed chunks in mission files are not supported.");
            }

            chunks.TryAdd(chunk.Tag, chunk);
        }

        IffChunk comm = chunks.GetValueOrDefault("COMM") ?? throw new InvalidDataException("The mission file has no COMM block.");
        return new MissionFile(
            file,
            comm,
            chunks,
            MissionComm.Parse(file.AsSpan(comm.DataOffset, comm.DataSize)),
            ReadString(file, chunks.GetValueOrDefault("JPTX")),
            ReadString(file, chunks.GetValueOrDefault("NAME")));
    }

    /// <summary>The raw data of an object chunk (empty if the mission has none of these objects).</summary>
    public ReadOnlySpan<byte> GetObjectData(MissionObjectKind kind) =>
        _chunks.TryGetValue(ObjectTags[(int)kind], out IffChunk? chunk)
            ? _file.AsSpan(chunk.DataOffset, chunk.DataSize)
            : [];

    /// <summary>The file with another COMM block (same size) and everything else unchanged.</summary>
    /// <exception cref="ArgumentException">The block has another size.</exception>
    public byte[] WithComm(MissionComm comm)
    {
        byte[] data = comm.ToArray();
        if (data.Length != _commChunk.DataSize)
        {
            throw new ArgumentException("The COMM block must keep its size.", nameof(comm));
        }

        byte[] result = (byte[])_file.Clone();
        data.CopyTo(result, _commChunk.DataOffset);
        return result;
    }

    /// <summary>Offset of the start fuel in the takeoff pad (TaskTPAD.unk2C, 0 to 1, Rocket Belt and Gyrocopter).</summary>
    public const int StartFuelOffset = 0x2C;

    /// <summary>The fuel at the start (first takeoff pad), or null without one.</summary>
    public float? StartFuel
    {
        get
        {
            ReadOnlySpan<byte> pad = GetObjectData(MissionObjectKind.TakeoffPad);
            return pad.Length >= 0x30 ? System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(pad[StartFuelOffset..]) : null;
        }
    }

    /// <summary>The file with another start fuel in its first takeoff pad (unchanged without one).</summary>
    public byte[] WithStartFuel(float fuel)
    {
        byte[] result = (byte[])_file.Clone();
        if (_chunks.TryGetValue("TPAD", out IffChunk? chunk) && chunk.DataSize >= 0x30)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(result.AsSpan(chunk.DataOffset + StartFuelOffset), fuel);
        }

        return result;
    }

    /// <summary>Size of a runway (TaskLSTP) in the LSTP chunk.</summary>
    public const int RunwaySize = 0x24;

    /// <summary>Offset of the width (landingAlignment, game units) in a runway.</summary>
    public const int RunwayWidthOffset = 0x20;

    /// <summary>The width of each runway for a good landing (game units; the game counts half of it to each side of the centre line).</summary>
    public IReadOnlyList<float> RunwayWidths
    {
        get
        {
            ReadOnlySpan<byte> data = GetObjectData(MissionObjectKind.Runway);
            int count = Math.Min(Comm.GetObjectCount(MissionObjectKind.Runway), data.Length / RunwaySize);
            var widths = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                widths.Add(System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(data[((i * RunwaySize) + RunwayWidthOffset)..]));
            }

            return widths;
        }
    }

    /// <summary>The file with other runway widths (in order; extra values are ignored, missing ones keep the file's).</summary>
    public byte[] WithRunwayWidths(IReadOnlyList<float> widths)
    {
        byte[] result = (byte[])_file.Clone();
        if (_chunks.TryGetValue("LSTP", out IffChunk? chunk))
        {
            int count = Math.Min(Math.Min(Comm.GetObjectCount(MissionObjectKind.Runway), chunk.DataSize / RunwaySize), widths.Count);
            for (int i = 0; i < count; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(
                    result.AsSpan(chunk.DataOffset + (i * RunwaySize) + RunwayWidthOffset), widths[i]);
            }
        }

        return result;
    }

    private static string ReadString(byte[] file, IffChunk? chunk)
    {
        if (chunk is null)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> data = file.AsSpan(chunk.DataOffset, chunk.DataSize);
        int end = data.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? data[..end] : data);
    }
}
