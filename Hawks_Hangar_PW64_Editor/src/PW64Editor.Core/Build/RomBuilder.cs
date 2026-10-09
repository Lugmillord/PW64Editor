using PW64Editor.Core.Boot;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Localization;
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
///   <item>Apply the selected code fixes (<see cref="RomBuildOptions.CodeFixes"/>). Each one checks
///         that the original code is really there before changing it.</item>
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

        // 2. Audio data (only if it moves or the music changed). Copied from the base ROM, so it
        //    does not matter that the files written below may overlap its original location.
        if (placement.Relocated || options.SequenceFile is not null)
        {
            WriteAudio(output, baseRom, layout.Audio!, placement, options.SequenceFile);
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

        // 5. Code fixes. They only exchange instructions inside the game code, which never moves.
        foreach (CodeFix fix in options.CodeFixes ?? [])
        {
            fix.Apply(output, CodeFixes.CodeStart, CodeFixes.CodeEnd(layout));
        }

        // 6. Boot checksum. The code, the file table and the first game files all lie inside
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

    /// <summary>Where the audio block goes, how much the music file grew and how large the ROM must be.</summary>
    private readonly record struct AudioPlacement(int AudioOffset, bool Relocated, int RomSize, int SequenceGrowth = 0);

    /// <summary>
    /// Writes the audio block at its new place: the music file (new or original) and behind it the
    /// instrument bank and the samples from the base ROM. Then the code references are updated.
    /// </summary>
    private static void WriteAudio(byte[] output, N64Rom baseRom, AudioLayout audio, AudioPlacement placement, byte[]? sequenceFile)
    {
        if (placement.Relocated)
        {
            output.AsSpan(audio.SequenceOffset, audio.Size).Fill(RomPadding);
        }

        int sequenceRegion = audio.SequenceSize + placement.SequenceGrowth;
        Span<byte> sequences = output.AsSpan(placement.AudioOffset, sequenceRegion);
        if (sequenceFile is null)
        {
            baseRom.Data.AsSpan(audio.SequenceOffset, audio.SequenceSize).CopyTo(sequences);
        }
        else
        {
            sequences.Clear();
            sequenceFile.CopyTo(sequences);
        }

        int bankOffset = placement.AudioOffset + sequenceRegion;
        baseRom.Data.AsSpan(audio.BankOffset, audio.EndOffset - audio.BankOffset).CopyTo(output.AsSpan(bankOffset));
        PatchAudioReferences(output, audio, placement.AudioOffset, bankOffset);
    }

    private static AudioPlacement PlanAudio(N64Rom baseRom, RomLayout layout, int fileSystemEnd, RomBuildOptions options)
    {
        AudioLayout? audio = layout.Audio;
        bool fitsInPlace = fileSystemEnd <= layout.FileSystemLimit;
        int growth = audio is null || options.SequenceFile is null
            ? 0
            : Math.Max(0, AlignUp(options.SequenceFile.Length, AudioLayout.Alignment) - audio.SequenceSize);

        if (audio is null)
        {
            if (!fitsInPlace)
            {
                throw new FileSystemFullException(
                    CoreText.F("Game files need {0:N0} bytes, but only {1:N0} are available (over by {2:N0}).",
                        fileSystemEnd - layout.FileSystemOffset, layout.FileSystemCapacity, fileSystemEnd - layout.FileSystemLimit));
            }

            return new AudioPlacement(layout.FileSystemLimit, false, baseRom.Size);
        }

        if (audio.SequenceOffset != layout.FileSystemLimit)
        {
            throw new InvalidOperationException("Inconsistent layout: the audio data must start at the file system limit.");
        }

        bool inPlace = fitsInPlace && !options.AlwaysRelocateAudio;
        if (inPlace && growth == 0)
        {
            return new AudioPlacement(audio.SequenceOffset, false, baseRom.Size);
        }

        int audioOffset = inPlace ? audio.SequenceOffset : AlignUp(fileSystemEnd, AudioLayout.Alignment);
        int requiredSize = audioOffset + audio.Size + growth;
        int romSize = baseRom.Size;

        if (requiredSize > romSize)
        {
            if (!options.AllowExpansion)
            {
                throw new FileSystemFullException(
                    CoreText.F("Game files and audio data need {0:N0} bytes, but the ROM has only {1:N0} (over by " +
                        "{2:N0}). Enable ROM expansion to make room.", requiredSize, romSize, requiredSize - romSize));
            }

            romSize = ExpansionSizes.FirstOrDefault(size => size >= requiredSize);
            if (romSize == 0)
            {
                throw new FileSystemFullException(
                    CoreText.F("Game files and audio data need {0:N0} bytes, more than the maximum ROM size of " +
                        "64 MiB.", requiredSize));
            }
        }

        return new AudioPlacement(audioOffset, audioOffset != audio.SequenceOffset, romSize, growth);
    }

    private static void PatchAudioReferences(byte[] output, AudioLayout audio, int newAudioOffset, int newBankOffset)
    {
        foreach ((MipsAddressReference reference, int originalAddress) in audio.AllReferences)
        {
            int delta = originalAddress == audio.SequenceOffset
                ? newAudioOffset - audio.SequenceOffset
                : newBankOffset - audio.BankOffset;
            // Make sure the base ROM really contains the expected values before changing them.
            uint current = MipsAddressPatcher.ReadValue(output, reference);
            if (current != (uint)originalAddress)
            {
                throw new InvalidDataException(
                    CoreText.F("Code reference '{0}' contains 0x{1:X}, expected 0x{2:X}. The base ROM must be an " +
                        "unmodified ROM.", reference.Description, current, originalAddress));
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
                    CoreText.F("File {0}: declared as '{1}', but its FORM type is '{2}'.", i, file.FileType, formType));
            }

            if (formSize != file.Size)
            {
                throw new InvalidDataException(
                    CoreText.F("File {0} ('{1}'): FORM header says {2:N0} bytes, data has {3:N0}.",
                        i, file.FileType, formSize, file.Size));
            }

            // The game computes the next file's address by adding sizes; N64 DMA needs
            // at least 2-byte alignment and the game's own files are all multiples of 4.
            if (file.Size % 4 != 0)
            {
                throw new InvalidDataException(
                    CoreText.F("File {0} ('{1}'): size {2:N0} is not a multiple of 4.", i, file.FileType, file.Size));
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
                    CoreText.F("Too many files in group '{0}': {1}, the game supports at most {2}.", group, count, limit));
            }
        }
    }
}
