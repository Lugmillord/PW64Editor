using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Text;

/// <summary>One text of the game's text file.</summary>
/// <param name="Index">Position in the file; the game requests texts by this number.</param>
/// <param name="Name">The developers' name for the text, e.g. "MINI_USA".</param>
/// <param name="Data">The raw DATA chunk (16-bit codes, end code, zero padding).</param>
public sealed record GameTextEntry(int Index, string Name, byte[] Data);

/// <summary>
/// The file holding all game texts (FORM "ADAT", user file 66 in the US version).
/// </summary>
/// <remarks>
/// <para>Layout, as read by textLoadBlock in src/app/text_data.c of the decompilation:</para>
/// <code>
/// FORM "ADAT"
///   PAD   4 bytes
///   SIZE  u32 number of texts, u32 0
///   NAME  "MINI_USA" (zero padded)   \  one pair
///   DATA  16-bit codes                /  per text
///   ...
/// </code>
/// <para>Texts are numbered in the order of their DATA chunks.</para>
/// </remarks>
public sealed class GameTextFile
{
    /// <summary>The game loads the texts with textLoadBlock(0x42): user file number 66.</summary>
    public const int UserFileIndex = 0x42;

    public const string FormType = "ADAT";

    private readonly string _formType;
    private readonly List<(string Tag, byte[] Data)> _chunks;
    private readonly List<int> _dataChunkIndices;

    private GameTextFile(string formType, List<(string, byte[])> chunks, List<int> dataChunkIndices, List<GameTextEntry> entries)
    {
        _formType = formType;
        _chunks = chunks;
        _dataChunkIndices = dataChunkIndices;
        Entries = entries;
    }

    /// <summary>All texts in game order.</summary>
    public IReadOnlyList<GameTextEntry> Entries { get; }

    /// <summary>Finds the text file in the game's file system.</summary>
    public static GameFile FindFile(GameFileSystem fileSystem) =>
        fileSystem.Find(FileTypeLimits.UserFileGroup, UserFileIndex) is { FileType: FormType } file
            ? file
            : throw new InvalidDataException($"User file {UserFileIndex} is not the text file ({FormType}).");

    /// <summary>
    /// Parses a text file.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a text file, or NAME and DATA chunks do not pair up.</exception>
    public static GameTextFile Parse(byte[] fileData)
    {
        IffForm form = IffForm.Parse(fileData);
        if (form.FormType != FormType)
        {
            throw new InvalidDataException($"Expected a text file ({FormType}), found '{form.FormType}'.");
        }

        var chunks = new List<(string, byte[])>();
        var dataIndices = new List<int>();
        var entries = new List<GameTextEntry>();
        string? pendingName = null;

        foreach (IffChunk chunk in form.Chunks)
        {
            // GZIP chunks are not used in the retail file, but the game would accept them.
            (string tag, byte[] data) = GzipChunk.ReadChunkData(fileData, chunk);
            chunks.Add((tag, data));

            if (tag == "NAME")
            {
                pendingName = Encoding.ASCII.GetString(data).TrimEnd('\0');
            }
            else if (tag == "DATA")
            {
                dataIndices.Add(chunks.Count - 1);
                entries.Add(new GameTextEntry(entries.Count, pendingName ?? $"(unnamed {entries.Count})", data));
                pendingName = null;
            }
        }

        return new GameTextFile(form.FormType, chunks, dataIndices, entries);
    }

    /// <summary>
    /// Builds the file with some texts replaced. Unchanged texts keep their exact bytes,
    /// so building without changes reproduces the original file.
    /// </summary>
    /// <param name="newData">New DATA chunk contents by text index.</param>
    public byte[] Build(IReadOnlyDictionary<int, byte[]> newData)
    {
        var chunks = _chunks.Select(c => c.Data).ToList();
        foreach ((int index, byte[] data) in newData)
        {
            if (index < 0 || index >= _dataChunkIndices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(newData), $"There is no text number {index}.");
            }

            if (data.Length % 4 != 0)
            {
                throw new ArgumentException($"Text {index}: the data size must be a multiple of 4.", nameof(newData));
            }

            chunks[_dataChunkIndices[index]] = data;
        }

        return IffWriter.BuildForm(_formType, _chunks.Select((c, i) => IffWriter.BuildChunk(c.Tag, chunks[i])));
    }

    /// <summary>The number of texts stored in the SIZE chunk, or null if there is none.</summary>
    public int? DeclaredCount
    {
        get
        {
            byte[]? size = _chunks.FirstOrDefault(c => c.Tag == "SIZE").Data;
            return size is { Length: >= 4 } ? (int)BinaryPrimitives.ReadUInt32BigEndian(size) : null;
        }
    }
}
