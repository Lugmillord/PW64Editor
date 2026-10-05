using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.FileSystem;

/// <summary>
/// The complete set of game files of a Pilotwings 64 ROM, read through the file table.
/// </summary>
/// <remarks>
/// Reading works exactly like the game does it at startup (uvMemInitBlockHdr in
/// src/kernel/texture.c of the decompilation): start at the fixed file system offset, walk
/// the table and add up the sizes. Every file is additionally checked for a valid FORM
/// header whose size and type match its table entry, so a corrupt ROM is detected early.
/// </remarks>
public sealed class GameFileSystem
{
    private GameFileSystem(RomLayout layout, IReadOnlyList<GameFile> files, int skippedEntries)
    {
        Layout = layout;
        Files = files;
        SkippedEntries = skippedEntries;
    }

    /// <summary>The fixed addresses this file system was read with.</summary>
    public RomLayout Layout { get; }

    /// <summary>All files in table order.</summary>
    public IReadOnlyList<GameFile> Files { get; }

    /// <summary>Number of table entries with tag 0 (skipped by the game, none in the retail ROM).</summary>
    public int SkippedEntries { get; }

    /// <summary>Total size of all files in bytes.</summary>
    public int TotalSize => Files.Sum(f => f.Size);

    /// <summary>ROM offset right after the last file.</summary>
    public int EndOffset => Layout.FileSystemOffset + TotalSize;

    /// <summary>Bytes still free before the file system would run into the audio data.</summary>
    public int FreeSpace => Layout.FileSystemLimit - EndOffset;

    /// <summary>
    /// Reads the file system of a ROM.
    /// </summary>
    /// <exception cref="InvalidDataException">The file table or a file is corrupt.</exception>
    public static GameFileSystem Read(N64Rom rom, RomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentNullException.ThrowIfNull(layout);

        byte[] data = rom.Data;
        IReadOnlyList<FileTableEntry> entries = FileTable.Read(data.AsSpan(layout.FileTableOffset));

        var files = new List<GameFile>(entries.Count);
        var groupCounters = new Dictionary<string, int>();
        int offset = layout.FileSystemOffset;
        int skipped = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            FileTableEntry entry = entries[i];

            if (offset + entry.Size > data.Length)
            {
                throw new InvalidDataException(
                    $"File table entry {i} (size 0x{entry.Size:X}) points beyond the end of the ROM.");
            }

            if (entry.FileType is null)
            {
                // The game ignores entries with tag 0 but still advances by their size.
                skipped++;
                offset += entry.Size;
                continue;
            }

            ValidateFormHeader(data.AsSpan(offset), entry, i, offset);

            string group = FileTypeLimits.GetGroup(entry.FileType);
            int groupIndex = groupCounters.GetValueOrDefault(group);
            groupCounters[group] = groupIndex + 1;

            byte[] fileData = data.AsSpan(offset, entry.Size).ToArray();
            files.Add(new GameFile(i, entry.FileType, group, groupIndex, offset, fileData));

            offset += entry.Size;
        }

        return new GameFileSystem(layout, files, skipped);
    }

    /// <summary>
    /// Returns the file the game would load for a group and index, e.g. ("UVTX", 10).
    /// </summary>
    public GameFile? Find(string group, int groupIndex) =>
        Files.FirstOrDefault(f => f.Group == group && f.GroupIndex == groupIndex);

    /// <summary>
    /// Returns the number of files per lookup group, together with the game's limit.
    /// </summary>
    public IReadOnlyList<(string Group, int Count, int Limit)> GetGroupUsage()
    {
        return Files
            .GroupBy(f => f.Group)
            .Select(g => (g.Key, g.Count(), FileTypeLimits.GetLimit(g.Key)))
            .OrderBy(g => g.Key == FileTypeLimits.UserFileGroup) // user files last
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static void ValidateFormHeader(ReadOnlySpan<byte> fileData, FileTableEntry entry, int index, int offset)
    {
        string formType;
        int formSize;
        try
        {
            (formType, formSize) = IffForm.ReadHeader(fileData);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"File {index} at 0x{offset:X}: {ex.Message}", ex);
        }

        if (formType != entry.FileType)
        {
            throw new InvalidDataException(
                $"File {index} at 0x{offset:X}: table says '{entry.FileType}', FORM says '{formType}'.");
        }

        if (formSize != entry.Size)
        {
            throw new InvalidDataException(
                $"File {index} at 0x{offset:X}: table size 0x{entry.Size:X} differs from FORM size 0x{formSize:X}.");
        }
    }
}
