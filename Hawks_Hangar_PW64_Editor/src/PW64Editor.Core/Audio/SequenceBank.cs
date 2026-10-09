using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>
/// The file with all music sequences of the game (ALSeqFile, revision "S1").
/// </summary>
/// <remarks>
/// Layout: "S1", u16 number of sequences, then per sequence a u32 offset (from the start of the
/// file) and a u32 length, then the sequences. The game copies one sequence at a time into a
/// buffer as large as the longest one (computed when the game starts).
/// </remarks>
public sealed class SequenceBank
{
    /// <summary>Revision mark at the start of the file.</summary>
    public const ushort Revision = 0x5331; // "S1"

    /// <summary>Alignment of each sequence in the file (as in the original).</summary>
    public const int SequenceAlignment = 4;

    /// <summary>Alignment of the end of the file (the instrument bank follows).</summary>
    public const int FileAlignment = 16;

    private readonly byte[]? _original;
    private readonly int[] _originalOffsets;
    private readonly int[] _originalLengths;

    private SequenceBank(IReadOnlyList<byte[]> sequences, byte[]? original, int[] offsets, int[] lengths)
    {
        Sequences = sequences;
        _original = original;
        _originalOffsets = offsets;
        _originalLengths = lengths;
    }

    /// <summary>The sequences, by number.</summary>
    public IReadOnlyList<byte[]> Sequences { get; }

    /// <summary>Reads the file.</summary>
    /// <exception cref="InvalidDataException">Not a sequence file, or damaged.</exception>
    public static SequenceBank Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || BinaryPrimitives.ReadUInt16BigEndian(data) != Revision)
        {
            throw new InvalidDataException(CoreText.T("This is not the game's music file."));
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (4 + count * 8 > data.Length)
        {
            throw new InvalidDataException(CoreText.T("This is not the game's music file."));
        }

        var sequences = new List<byte[]>(count);
        int[] offsets = new int[count], lengths = new int[count];
        for (int i = 0; i < count; i++)
        {
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(4 + i * 8)..]);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(8 + i * 8)..]);
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new InvalidDataException(CoreText.T("This is not the game's music file."));
            }

            sequences.Add(data.Slice(offset, length).ToArray());
            offsets[i] = offset;
            lengths[i] = length;
        }

        return new SequenceBank(sequences, data.ToArray(), offsets, lengths);
    }

    /// <summary>A copy with other sequences.</summary>
    public SequenceBank With(IReadOnlyList<byte[]> sequences) => new(sequences, _original, _originalOffsets, _originalLengths);

    /// <summary>
    /// Writes the file. If every sequence still has its original length, the original file is
    /// kept and only the sequences are written into it (smallest possible patch). Otherwise the
    /// file is laid out anew: header, then each sequence at a multiple of 4, zero padded to a
    /// multiple of 16.
    /// </summary>
    public byte[] Build()
    {
        if (_original is not null && Sequences.Count == _originalLengths.Length
            && Sequences.Select(s => s.Length).SequenceEqual(_originalLengths))
        {
            byte[] copy = _original.ToArray();
            for (int i = 0; i < Sequences.Count; i++)
            {
                Sequences[i].CopyTo(copy, _originalOffsets[i]);
            }

            return copy;
        }

        int offset = Align(4 + Sequences.Count * 8, SequenceAlignment);
        var offsets = new List<int>();
        foreach (byte[] sequence in Sequences)
        {
            offsets.Add(offset);
            offset = Align(offset + sequence.Length, SequenceAlignment);
        }

        int lastEnd = Sequences.Count > 0 ? offsets[^1] + Sequences[^1].Length : offset;
        byte[] file = new byte[Align(lastEnd, FileAlignment)];
        BinaryPrimitives.WriteUInt16BigEndian(file, Revision);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(2), (ushort)Sequences.Count);
        for (int i = 0; i < Sequences.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4 + i * 8), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8 + i * 8), (uint)Sequences[i].Length);
            Sequences[i].CopyTo(file, offsets[i]);
        }

        return file;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
