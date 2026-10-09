using PW64Editor.Core.Code;

namespace PW64Editor.Core.FileSystem;

/// <summary>
/// Location of the audio data and the code references pointing to it.
/// </summary>
/// <remarks>
/// <para>
/// The audio data consists of three consecutive libultra files that directly follow the game
/// file system: the sequence file (music, ".sbk"), the bank file (instruments, ".ctl") and the
/// wave table (samples, ".tbl"). They contain only offsets relative to their own start; the
/// game adds the base address when loading them (alSeqFileNew / alBnkfNew in libultra).
/// So the whole block can be moved, as long as the base addresses in the code are updated.
/// The block keeps its internal layout, so every part stays at the same relative position.
/// </para>
/// <para>
/// The code references were found by searching the game code for lui/addiu pairs that produce
/// these addresses, and matched against src/kernel/audio_manager.c of the decompilation.
/// </para>
/// </remarks>
/// <param name="SequenceOffset">Start of the sequence file (= original end of the file system).</param>
/// <param name="BankOffset">Start of the instrument bank file.</param>
/// <param name="TableOffset">Start of the sample table (= end of the bank file).</param>
/// <param name="EndOffset">End of the audio data. Everything after it is 0xFF padding.</param>
/// <param name="SequenceReferences">Code locations that load <paramref name="SequenceOffset"/>.</param>
/// <param name="BankReferences">Code locations that load <paramref name="BankOffset"/>.</param>
/// <param name="TableReferences">Code locations that load <paramref name="TableOffset"/>
/// (both as start of the samples and as end of the bank file).</param>
public sealed record AudioLayout(
    int SequenceOffset,
    int BankOffset,
    int TableOffset,
    int EndOffset,
    IReadOnlyList<MipsAddressReference> SequenceReferences,
    IReadOnlyList<MipsAddressReference> BankReferences,
    IReadOnlyList<MipsAddressReference> TableReferences)
{
    /// <summary>
    /// Required alignment when moving the audio block. The original block starts at a
    /// multiple of 16; keeping that preserves the alignment of every sample inside it.
    /// </summary>
    public const int Alignment = 16;

    /// <summary>Size of the complete audio block.</summary>
    public int Size => EndOffset - SequenceOffset;

    /// <summary>
    /// Audio of Pilotwings 64 (USA). Addresses from config/us/pilotwings64.us.yaml of the
    /// decompilation; the end was determined from the ROM (last byte before the 0xFF padding).
    /// </summary>
    /// <remarks>
    /// One more reference to 0x618B70 exists at 0x2F1A8/0x2F1AC (kernel/system.c), but it is the
    /// end of the file system in a debug message, not an audio address, so it is not patched.
    /// </remarks>
    public static readonly AudioLayout PilotwingsUsa = new(
        SequenceOffset: 0x618B70,
        BankOffset: 0x62D460,
        TableOffset: 0x6314D0,
        EndOffset: 0x6FCBE0,
        SequenceReferences:
        [
            new(0x49B4, 0x49B8, "sequence file header read"),
            new(0x49FC, 0x4A04, "sequence file read"),
            new(0x4A18, 0x4A1C, "sequence file base address"),
        ],
        BankReferences:
        [
            new(0x5508, 0x5510, "bank file start"),
        ],
        TableReferences:
        [
            new(0x550C, 0x5518, "bank file end (for its size)"),
            new(0x5550, 0x5554, "sample table base address"),
        ]);

    /// <summary>Size of the music file (sequences) including its padding up to the instrument bank.</summary>
    public int SequenceSize => BankOffset - SequenceOffset;

    /// <summary>
    /// All references together with the original address each one must contain.
    /// </summary>
    public IEnumerable<(MipsAddressReference Reference, int OriginalAddress)> AllReferences =>
        SequenceReferences.Select(r => (r, SequenceOffset))
            .Concat(BankReferences.Select(r => (r, BankOffset)))
            .Concat(TableReferences.Select(r => (r, TableOffset)));

    /// <summary>
    /// Reads where the audio block currently starts, according to the game code.
    /// For an unmodified ROM this is <see cref="SequenceOffset"/>.
    /// </summary>
    public int ReadCurrentOffset(ReadOnlySpan<byte> romData) =>
        (int)MipsAddressPatcher.ReadValue(romData, SequenceReferences[0]);
}
