using PW64Editor.Core.Boot;
using PW64Editor.Core.Code;
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
///   <item>Validate all files: correct FORM header, type and size; game limits per file type.</item>
///   <item>Decide where the audio data goes. It stays at its original address if the files fit
///         in front of it; otherwise it moves directly behind the file system and the code
///         references to it are updated (see <see cref="AudioLayout"/>). If even then the data
///         does not fit, the ROM is expanded (only if allowed by the options).</item>
///   <item>Start with a copy of the base ROM. Code and everything else stay untouched.</item>
///   <item>Write the files back to back from the file system start, then the audio block.
///         Gaps are filled with zeros, the end of the ROM with 0xFF (like the original).</item>
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
/// <para>
/// The base ROM must be an unmodified ROM: the audio data and code references are read from
/// their original locations.
/// </para>
/// </remarks>
public static class RomBuilder
{
    /// <summary>ROM sizes used for expansion. Powers of two are handled best by flashcarts and emulators.</summary>
    private static readonly int[] ExpansionSizes = [16 * 1024 * 1024, 32 * 1024 * 1024, 64 * 1024 * 1024];

    /// <summary>Fill value for unused space at the end of the ROM (same as in the original).</summary>
    private const byte RomPadding = 0xFF;

    /// <summary>
    /// Builds a ROM with the default options.
    /// </summary>
    /// <inheritdoc cref="Build(N64Rom, IReadOnlyList{GameFile}, RomLayout, RomBuildOptions)"/>
    public static RomBuildResult Build(N64Rom baseRom, IReadOnlyList<GameFile> files, RomLayout layout) =>
        Build(baseRom, files, layout, RomBuildOptions.Default);

    /// <summary>
    /// Builds a ROM.
    /// </summary>
    /// <param name="baseRom">The clean ROM to start from. It is not modified.</param>
    /// <param name="files">All game files in table order. Only <see cref="GameFile.FileType"/> and
    /// <see cref="GameFile.Data"/> are used; offsets and indices are recomputed.</param>
    /// <param name="layout">The fixed addresses of this game release.</param>
    /// <param name="options">Build settings.</param>
    /// <exception cref="InvalidDataException">A file is malformed.</exception>
    /// <exception cref="FileSystemFullException">The data does not fit.</exception>
    public static RomBuildResult Build(N64Rom baseRom, IReadOnlyList<GameFile> files, RomLayout layout, RomBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseRom);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);

        ValidateFiles(files);

        var newEntries = files.Select(f => new FileTableEntry(f.FileType, f.Size)).ToList();
        int fileSystemEnd = layout.FileSystemOffset + newEntries.Sum(e => e.Size);

        AudioPlacement placement = PlanAudio(baseRom, layout, fileSystemEnd, options);

        byte[] output = new byte[placement.RomSize];
        baseRom.Data.AsSpan(0, Math.Min(baseRom.Size, output.Length)).CopyTo(output);
        if (output.Length > baseRom.Size)
        {
            output.AsSpan(baseRom.Size).Fill(RomPadding);
        }

        // 1. File table: keep the original bytes whenever possible.
        IReadOnlyList<FileTableEntry> baseEntries = FileTable.Read(output.AsSpan(layout.FileTableOffset));
        bool tableChanged = !baseEntries.SequenceEqual(newEntries);
        if (tableChanged)
        {
            WriteFileTable(output, newEntries, layout);
        }

        // 2. Audio data (only if it moves). Copied from the base ROM, so it does not matter
        //    that the files written below may overlap its original location.
        if (placement.Relocated)
        {
            AudioLayout audio = layout.Audio!;
            output.AsSpan(audio.SequenceOffset, audio.Size).Fill(RomPadding);
            baseRom.Data.AsSpan(audio.SequenceOffset, audio.Size).CopyTo(output.AsSpan(placement.AudioOffset));
            PatchAudioReferences(output, audio, placement.AudioOffset);
        }

        // 3. Game files, back to back.
        int offset = layout.FileSystemOffset;
        foreach (GameFile file in files)
        {
            file.Data.CopyTo(output, offset);
            offset += file.Size;
        }

        // 4. Zero the gap between the last file and the audio data. In an unchanged rebuild
        //    this range only holds the original zero padding (4 bytes).
        output.AsSpan(fileSystemEnd, placement.AudioOffset - fileSystemEnd).Clear();

        // 5. Boot checksum. The code, the file table and the first game files all lie inside
        //    the checksummed first MiB. With an unknown CIC (only possible with homebrew or
        //    test data, never with a verified retail ROM) we cannot compute it, so we leave the
        //    header alone and report it in the result.
        N64Rom rom = N64Rom.FromBytes(output);
        CicType cic = rom.DetectCic();
        if (cic != CicType.Unknown)
        {
            rom.UpdateBootChecksum();
        }

        return new RomBuildResult(
            rom,
            tableChanged,
            fileSystemEnd,
            placement.AudioOffset,
            placement.Relocated,
            ComputeFreeSpace(layout, fileSystemEnd, output.Length),
            cic)
        {
            Expanded = output.Length > baseRom.Size,
        };
    }

    /// <summary>Where the audio block goes and how large the ROM must be.</summary>
    private readonly record struct AudioPlacement(int AudioOffset, bool Relocated, int RomSize);

    private static AudioPlacement PlanAudio(N64Rom baseRom, RomLayout layout, int fileSystemEnd, RomBuildOptions options)
    {
        AudioLayout? audio = layout.Audio;
        bool fitsInPlace = fileSystemEnd <= layout.FileSystemLimit;

        if (audio is null)
        {
            if (!fitsInPlace)
            {
                throw new FileSystemFullException(
                    $"Game files need {fileSystemEnd - layout.FileSystemOffset:N0} bytes, but only " +
                    $"{layout.FileSystemCapacity:N0} are available (over by {fileSystemEnd - layout.FileSystemLimit:N0}).");
            }

            return new AudioPlacement(layout.FileSystemLimit, false, baseRom.Size);
        }

        if (audio.SequenceOffset != layout.FileSystemLimit)
        {
            throw new InvalidOperationException("Inconsistent layout: the audio data must start at the file system limit.");
        }

        if (fitsInPlace && !options.AlwaysRelocateAudio)
        {
            return new AudioPlacement(audio.SequenceOffset, false, baseRom.Size);
        }

        int audioOffset = AlignUp(fileSystemEnd, AudioLayout.Alignment);
        int requiredSize = audioOffset + audio.Size;
        int romSize = baseRom.Size;

        if (requiredSize > romSize)
        {
            if (!options.AllowExpansion)
            {
                throw new FileSystemFullException(
                    $"Game files and audio data need {requiredSize:N0} bytes, but the ROM has only {romSize:N0} " +
                    $"(over by {requiredSize - romSize:N0}). Enable ROM expansion to make room.");
            }

            romSize = ExpansionSizes.FirstOrDefault(size => size >= requiredSize);
            if (romSize == 0)
            {
                throw new FileSystemFullException(
                    $"Game files and audio data need {requiredSize:N0} bytes, more than the maximum ROM size of 64 MiB.");
            }
        }

        return new AudioPlacement(audioOffset, audioOffset != audio.SequenceOffset, romSize);
    }

    private static void PatchAudioReferences(byte[] output, AudioLayout audio, int newAudioOffset)
    {
        int delta = newAudioOffset - audio.SequenceOffset;

        foreach ((MipsAddressReference reference, int originalAddress) in audio.AllReferences)
        {
            // Make sure the base ROM really contains the expected values before changing them.
            uint current = MipsAddressPatcher.ReadValue(output, reference);
            if (current != (uint)originalAddress)
            {
                throw new InvalidDataException(
                    $"Code reference '{reference.Description}' contains 0x{current:X}, expected 0x{originalAddress:X}. " +
                    "The base ROM must be an unmodified ROM.");
            }

            MipsAddressPatcher.WriteValue(output, reference, (uint)(originalAddress + delta));
        }
    }

    /// <summary>
    /// Additional bytes of game files that fit into a ROM of <paramref name="romSize"/> bytes.
    /// </summary>
    private static int ComputeFreeSpace(RomLayout layout, int fileSystemEnd, int romSize)
    {
        if (layout.Audio is null)
        {
            return layout.FileSystemLimit - fileSystemEnd;
        }

        // The audio block can always be moved to the end of the ROM; at worst we lose a few
        // bytes to its alignment.
        int lastAudioStart = (romSize - layout.Audio.Size) / AudioLayout.Alignment * AudioLayout.Alignment;
        return lastAudioStart - fileSystemEnd;
    }

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

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
