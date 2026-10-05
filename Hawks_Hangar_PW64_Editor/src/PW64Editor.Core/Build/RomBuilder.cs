using PW64Editor.Core.Boot;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Build;

/// <summary>
/// Builds a new ROM from a base ROM and a (possibly modified) list of game files.
/// </summary>
/// <remarks>
/// <para>How a build works:</para>
/// <list type="number">
///   <item>Start with a copy of the base ROM. Code, audio and everything else stay untouched.</item>
///   <item>Validate all files: correct FORM header, type and size; game limits per file type.</item>
///   <item>Write the files back to back from the file system start. Space between the new end
///         and the audio data is filled with zeros.</item>
///   <item>If any file type or size differs from the base ROM, write a new file table.
///         Otherwise keep the original table bytes. This is what makes an unchanged rebuild
///         byte-identical: our MIO0 compressor works correctly but produces different bytes
///         than Nintendo's.</item>
///   <item>Update the boot checksum (skipped if the CIC type is unknown).</item>
/// </list>
/// <para>
/// File contents are copied exactly as given. Callers should keep the original bytes of
/// unchanged files, so a BPS patch only contains real changes.
/// </para>
/// </remarks>
public static class RomBuilder
{
    /// <summary>
    /// Builds a ROM.
    /// </summary>
    /// <param name="baseRom">The clean ROM to start from. It is not modified.</param>
    /// <param name="files">All game files in table order. Only <see cref="GameFile.FileType"/> and
    /// <see cref="GameFile.Data"/> are used; offsets and indices are recomputed.</param>
    /// <param name="layout">The fixed addresses of this game release.</param>
    /// <exception cref="InvalidDataException">A file is malformed.</exception>
    /// <exception cref="FileSystemFullException">The files or the table do not fit.</exception>
    public static RomBuildResult Build(N64Rom baseRom, IReadOnlyList<GameFile> files, RomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(baseRom);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);

        ValidateFiles(files);

        var newEntries = files.Select(f => new FileTableEntry(f.FileType, f.Size)).ToList();
        int totalSize = newEntries.Sum(e => e.Size);
        if (totalSize > layout.FileSystemCapacity)
        {
            throw new FileSystemFullException(
                $"Game files need {totalSize:N0} bytes, but only {layout.FileSystemCapacity:N0} bytes are " +
                $"available before the audio data (over by {totalSize - layout.FileSystemCapacity:N0} bytes).");
        }

        byte[] output = (byte[])baseRom.Data.Clone();

        // 1. File table: keep the original bytes whenever possible.
        IReadOnlyList<FileTableEntry> baseEntries = FileTable.Read(output.AsSpan(layout.FileTableOffset));
        bool tableChanged = !baseEntries.SequenceEqual(newEntries);
        if (tableChanged)
        {
            WriteFileTable(output, newEntries, layout);
        }

        // 2. Game files, back to back.
        int offset = layout.FileSystemOffset;
        foreach (GameFile file in files)
        {
            file.Data.CopyTo(output, offset);
            offset += file.Size;
        }

        // 3. Clear whatever is left of the old file system, up to the audio data.
        //    In an unchanged rebuild this range only holds the original zero padding.
        output.AsSpan(offset, layout.FileSystemLimit - offset).Clear();

        // 4. Boot checksum. Only changes if something inside the first MiB changed,
        //    but calling it always is cheap and safe. With an unknown CIC (only possible
        //    with homebrew or test data, never with a verified retail ROM) we cannot compute
        //    it, so we leave the header alone and report it in the result.
        N64Rom rom = N64Rom.FromBytes(output);
        CicType cic = rom.DetectCic();
        if (cic != CicType.Unknown)
        {
            rom.UpdateBootChecksum();
        }

        return new RomBuildResult(rom, tableChanged, offset, layout.FileSystemLimit - offset, cic);
    }

    private static void WriteFileTable(byte[] output, IReadOnlyList<FileTableEntry> entries, RomLayout layout)
    {
        int reservedSize = layout.FileSystemOffset - layout.FileTableOffset;
        byte[] tableForm = FileTableWriter.BuildForm(entries, reservedSize);

        Span<byte> region = output.AsSpan(layout.FileTableOffset, reservedSize);
        region.Clear();
        tableForm.CopyTo(region);
    }

    private static void ValidateFiles(IReadOnlyList<GameFile> files)
    {
        var groupCounts = new Dictionary<string, int>();

        for (int i = 0; i < files.Count; i++)
        {
            GameFile file = files[i];

            (string formType, int formSize) = IffForm.ReadHeader(file.Data);
            if (formType != file.FileType)
            {
                throw new InvalidDataException(
                    $"File {i}: declared as '{file.FileType}', but its FORM type is '{formType}'.");
            }

            if (formSize != file.Size)
            {
                throw new InvalidDataException(
                    $"File {i} ('{file.FileType}'): FORM header says {formSize:N0} bytes, data has {file.Size:N0}.");
            }

            // The game computes the next file's address by adding sizes; N64 DMA needs
            // at least 2-byte alignment and the game's own files are all multiples of 4.
            if (file.Size % 4 != 0)
            {
                throw new InvalidDataException(
                    $"File {i} ('{file.FileType}'): size {file.Size:N0} is not a multiple of 4.");
            }

            string group = FileTypeLimits.GetGroup(file.FileType);
            groupCounts[group] = groupCounts.GetValueOrDefault(group) + 1;
        }

        foreach ((string group, int count) in groupCounts)
        {
            int limit = FileTypeLimits.GetLimit(group);
            if (count > limit)
            {
                throw new InvalidDataException(
                    $"Too many files in group '{group}': {count}, the game supports at most {limit}.");
            }
        }
    }
}
