using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Core.Text;

/// <summary>One text of the game's text file.</summary>
/// <param name="Index">Position in the file; the game requests texts by this number.</param>
/// <param name="Name">The developers' name for the text, e.g. "MINI_USA". Empty for a free slot.</param>
/// <param name="Data">The raw DATA chunk (16-bit codes, end code, zero padding).</param>
public sealed record GameTextEntry(int Index, string Name, byte[] Data)
{
    /// <summary>
    /// True for a free slot: a removed custom text whose number is kept free, so that the texts
    /// behind it keep their numbers. It has no name and an empty text.
    /// </summary>
    public bool IsFree => Name.Length == 0;
}

/// <summary>A text as written by <see cref="GameTextFile.Build(IReadOnlyList{TextFileEntry})"/>.</summary>
/// <param name="Name">The name; empty for a free slot.</param>
/// <param name="Data">The DATA chunk (size a multiple of 4).</param>
public sealed record TextFileEntry(string Name, byte[] Data)
{
    /// <summary>The data of a free slot: a line break and the end code.</summary>
    public static byte[] FreeSlotData => [0x00, 0xFE, 0x00, 0xFF];

    /// <summary>A free slot (see <see cref="GameTextEntry.IsFree"/>).</summary>
    public static TextFileEntry FreeSlot => new(string.Empty, FreeSlotData);

    public bool IsFree => Name.Length == 0;
}

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
/// <para>Texts are numbered in the order of their DATA chunks. NAME chunks are zero padded to a
/// multiple of 8 bytes. The game ignores the SIZE chunk; it is kept up to date anyway.</para>
/// <para>The retail game has 439 texts and no room for more (see Code.CodeFixes.ExpandedTexts).</para>
/// </remarks>
public sealed class GameTextFile
{
    /// <summary>The game loads the texts with textLoadBlock(0x42): user file number 66.</summary>
    public const int UserFileIndex = 0x42;

    public const string FormType = "ADAT";

    /// <summary>Number of texts in the retail game: the size of the game's own text tables.</summary>
    public const int RetailTextCount = 439;

    private readonly string _formType;
    private readonly List<(string Tag, byte[] Data)> _chunks;
    private readonly List<int> _dataChunkIndices;
    private readonly List<int> _nameChunkIndices;

    private GameTextFile(string formType, List<(string, byte[])> chunks, List<int> dataChunkIndices, List<int> nameChunkIndices,
        List<GameTextEntry> entries)
    {
        _formType = formType;
        _chunks = chunks;
        _dataChunkIndices = dataChunkIndices;
        _nameChunkIndices = nameChunkIndices;
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
        var nameIndices = new List<int>();
        var entries = new List<GameTextEntry>();
        string? pendingName = null;
        int pendingNameChunk = -1;

        foreach (IffChunk chunk in form.Chunks)
        {
            // GZIP chunks are not used in the retail file, but the game would accept them.
            (string tag, byte[] data) = GzipChunk.ReadChunkData(fileData, chunk);
            chunks.Add((tag, data));

            if (tag == "NAME")
            {
                string name = Encoding.ASCII.GetString(data);
                int end = name.IndexOf('\0');
                pendingName = end >= 0 ? name[..end] : name;
                pendingNameChunk = chunks.Count - 1;
            }
            else if (tag == "DATA")
            {
                dataIndices.Add(chunks.Count - 1);
                nameIndices.Add(pendingNameChunk);
                entries.Add(new GameTextEntry(entries.Count, pendingName ?? $"(unnamed {entries.Count})", data));
                pendingName = null;
                pendingNameChunk = -1;
            }
        }

        return new GameTextFile(form.FormType, chunks, dataIndices, nameIndices, entries);
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

    /// <summary>
    /// Builds the file with a complete new list of texts: changed, added and removed ones.
    /// Texts at their old position with unchanged name and data keep their exact bytes.
    /// </summary>
    /// <param name="texts">All texts in game order. The first ones normally are the existing texts.</param>
    /// <exception cref="ArgumentException">A data size is not a multiple of 4, or a name is not ASCII.</exception>
    public byte[] Build(IReadOnlyList<TextFileEntry> texts)
    {
        // The retail layout: PAD, SIZE, then NAME/DATA pairs. Chunks before the first pair and after
        // the last one are kept as they are; only the pairs are written anew.
        int firstPair = _nameChunkIndices.Count > 0 && _nameChunkIndices[0] >= 0 ? _nameChunkIndices[0]
            : _dataChunkIndices.Count > 0 ? _dataChunkIndices[0] : _chunks.Count;
        int afterPairs = _dataChunkIndices.Count > 0 ? _dataChunkIndices[^1] + 1 : _chunks.Count;

        var output = new List<byte[]>();
        for (int i = 0; i < firstPair; i++)
        {
            (string tag, byte[] data) = _chunks[i];
            if (tag == "SIZE" && data.Length >= 4)
            {
                data = data.ToArray();
                BinaryPrimitives.WriteUInt32BigEndian(data, (uint)texts.Count);
            }

            output.Add(IffWriter.BuildChunk(tag, data));
        }

        for (int i = 0; i < texts.Count; i++)
        {
            TextFileEntry text = texts[i];
            if (text.Data.Length % 4 != 0)
            {
                throw new ArgumentException($"Text {i}: the data size must be a multiple of 4.", nameof(texts));
            }

            bool sameName = i < Entries.Count && Entries[i].Name == text.Name && _nameChunkIndices[i] >= 0;
            output.Add(IffWriter.BuildChunk("NAME", sameName ? _chunks[_nameChunkIndices[i]].Data : EncodeName(text.Name)));
            output.Add(IffWriter.BuildChunk("DATA", text.Data));
        }

        for (int i = afterPairs; i < _chunks.Count; i++)
        {
            output.Add(IffWriter.BuildChunk(_chunks[i].Tag, _chunks[i].Data));
        }

        return IffWriter.BuildForm(_formType, output);
    }

    /// <summary>The current texts as input for <see cref="Build(IReadOnlyList{TextFileEntry})"/>.</summary>
    public List<TextFileEntry> ToEntries() => Entries.Select(e => new TextFileEntry(e.Name, e.Data)).ToList();

    /// <summary>A NAME chunk: ASCII, zero terminated and padded to a multiple of 8 (like the retail file).</summary>
    private static byte[] EncodeName(string name)
    {
        if (name.Any(c => c > 0x7E || c < 0x20))
        {
            throw new ArgumentException($"The text name '{name}' may only contain plain ASCII characters.", nameof(name));
        }

        byte[] data = new byte[(name.Length / 8 + 1) * 8];
        Encoding.ASCII.GetBytes(name, data);
        return data;
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
