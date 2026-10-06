using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Hashing;

namespace PW64Editor.Core.Patching;

/// <summary>
/// Creates BPS patches.
/// </summary>
/// <remarks>
/// <para>
/// This is a "delta" encoder: it finds data from the source file anywhere, not only at the same
/// position. That matters for ROM hacks, because when one game file grows, every following file
/// shifts. A "linear" patch (compare byte by byte at the same offset) would then contain almost
/// the whole ROM; a delta patch just says "copy this block from 72 bytes further back".
/// </para>
/// <para>
/// For every output position the encoder considers these candidates and picks the one that
/// saves the most bytes:
/// </para>
/// <list type="bullet">
///   <item>SourceRead: the source has the same bytes at the same position (cheapest).</item>
///   <item>SourceCopy: the bytes exist elsewhere in the source. Found via a hash index of
///         the source, plus a check of the position right after the previous copy.</item>
///   <item>TargetCopy: the bytes were already written earlier in the target (good for runs).</item>
/// </list>
/// <para>If nothing pays off, the byte is stored literally (TargetRead).</para>
/// </remarks>
public static class BpsWriter
{
    /// <summary>
    /// Creates a patch that turns <paramref name="source"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The original file (for ROM hacks: the clean ROM).</param>
    /// <param name="target">The modified file.</param>
    /// <param name="metadata">Optional text stored in the patch (e.g. hack name and version).
    /// Most patching tools ignore it.</param>
    public static byte[] Create(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, string? metadata = null)
    {
        var encoder = new DeltaEncoder(source.ToArray(), target.ToArray());
        return encoder.Encode(metadata ?? string.Empty);
    }

    /// <summary>
    /// The actual encoder. A class instead of static methods because it keeps a lot of state:
    /// the search indices, the output and the BPS read pointers.
    /// </summary>
    private sealed class DeltaEncoder
    {
        /// <summary>Matches are found through the hash of their first 4 bytes.</summary>
        private const int KeyLength = 4;

        private const int HashBits = 20;

        /// <summary>Candidates checked per position. Limits time on repetitive data.</summary>
        private const int MaxChainSteps = 32;

        /// <summary>Once a match is this long, stop looking for a longer one.</summary>
        private const int GoodEnoughLength = 1 << 16;

        /// <summary>
        /// A match must save at least this many bytes compared to storing the data literally,
        /// otherwise interrupting a literal run (which costs a new action header) is not worth it.
        /// </summary>
        private const int MinGain = 2;

        private readonly byte[] _source;
        private readonly byte[] _target;
        private readonly List<byte> _output = new();

        // Hash chains (see LzMatcher for an explanation): newest position first.
        private readonly int[] _sourceHead = new int[1 << HashBits];
        private readonly int[] _sourcePrevious;
        private readonly int[] _targetHead = new int[1 << HashBits];
        private readonly int[] _targetPrevious;

        // The BPS read pointers, mirrored exactly as the patch applier will track them.
        private long _sourceRelativeOffset;
        private long _targetRelativeOffset;

        public DeltaEncoder(byte[] source, byte[] target)
        {
            _source = source;
            _target = target;
            _sourcePrevious = new int[source.Length];
            _targetPrevious = new int[target.Length];
            Array.Fill(_sourceHead, -1);
            Array.Fill(_targetHead, -1);

            // The whole source is known in advance, so index it completely.
            for (int i = 0; i + KeyLength <= source.Length; i++)
            {
                int hash = Hash(source, i);
                _sourcePrevious[i] = _sourceHead[hash];
                _sourceHead[hash] = i;
            }
        }

        public byte[] Encode(string metadata)
        {
            WriteHeader(metadata);

            int pos = 0;
            int literalStart = -1; // start of the current run of literal bytes, or -1

            while (pos < _target.Length)
            {
                Match best = FindBestMatch(pos);

                if (best.Gain < MinGain)
                {
                    // Nothing worth referencing: keep the byte as a literal.
                    if (literalStart < 0)
                    {
                        literalStart = pos;
                    }

                    IndexTargetPosition(pos);
                    pos++;
                    continue;
                }

                if (literalStart >= 0)
                {
                    WriteTargetRead(literalStart, pos - literalStart);
                    literalStart = -1;
                }

                WriteMatch(best);
                for (int i = 0; i < best.Length; i++)
                {
                    IndexTargetPosition(pos + i);
                }

                pos += best.Length;
            }

            if (literalStart >= 0)
            {
                WriteTargetRead(literalStart, pos - literalStart);
            }

            WriteFooter();
            return _output.ToArray();
        }

        private Match FindBestMatch(int pos)
        {
            Match best = default;

            // 1. SourceRead: same bytes at the same position.
            if (pos < _source.Length)
            {
                int length = MatchLength(_source, pos, pos);
                Consider(ref best, new Match(BpsFormat.ActionSourceRead, length, 0, Cost(length, null)));
            }

            if (best.Length >= GoodEnoughLength)
            {
                return best;
            }

            // 2. SourceCopy continuing exactly where the previous SourceCopy stopped (offset 0).
            if (_sourceRelativeOffset < _source.Length)
            {
                int candidate = (int)_sourceRelativeOffset;
                int length = MatchLength(_source, candidate, pos);
                Consider(ref best, new Match(BpsFormat.ActionSourceCopy, length, candidate, Cost(length, 0)));
            }

            // 3. TargetCopy continuing where the previous TargetCopy stopped.
            if (_targetRelativeOffset < pos)
            {
                int candidate = (int)_targetRelativeOffset;
                int length = MatchLength(_target, candidate, pos);
                Consider(ref best, new Match(BpsFormat.ActionTargetCopy, length, candidate, Cost(length, 0)));
            }

            if (pos + KeyLength > _target.Length)
            {
                return best;
            }

            int hash = Hash(_target, pos);

            // 4. SourceCopy anywhere in the source.
            int steps = 0;
            for (int c = _sourceHead[hash]; c >= 0 && steps < MaxChainSteps && best.Length < GoodEnoughLength; c = _sourcePrevious[c], steps++)
            {
                int length = MatchLength(_source, c, pos);
                long offset = c - _sourceRelativeOffset;
                Consider(ref best, new Match(BpsFormat.ActionSourceCopy, length, c, Cost(length, offset)));
            }

            // 5. TargetCopy from anything already written.
            steps = 0;
            for (int c = _targetHead[hash]; c >= 0 && steps < MaxChainSteps && best.Length < GoodEnoughLength; c = _targetPrevious[c], steps++)
            {
                int length = MatchLength(_target, c, pos);
                long offset = c - _targetRelativeOffset;
                Consider(ref best, new Match(BpsFormat.ActionTargetCopy, length, c, Cost(length, offset)));
            }

            return best;
        }

        /// <summary>
        /// Counts how many bytes of <paramref name="data"/> starting at <paramref name="from"/>
        /// equal the target starting at <paramref name="pos"/>.
        /// </summary>
        /// <remarks>
        /// For TargetCopy, from + length may run past pos (overlapping copy). That is valid:
        /// the applier copies byte by byte, so those bytes are written just before they are read.
        /// </remarks>
        private int MatchLength(byte[] data, int from, int pos)
        {
            int max = Math.Min(data.Length - from, _target.Length - pos);
            return _target.AsSpan(pos, max).CommonPrefixLength(data.AsSpan(from, max));
        }

        /// <summary>
        /// Patch bytes needed for a match: action header plus offset (if the action has one).
        /// </summary>
        private static int Cost(int length, long? offset)
        {
            if (length == 0)
            {
                return 0;
            }

            int cost = BpsFormat.GetNumberLength((ulong)(length - 1) << 2);
            if (offset is { } value)
            {
                cost += BpsFormat.GetNumberLength(BpsFormat.EncodeOffset(value));
            }

            return cost;
        }

        private static void Consider(ref Match best, Match candidate)
        {
            if (candidate.Gain > best.Gain)
            {
                best = candidate;
            }
        }

        private void IndexTargetPosition(int pos)
        {
            if (pos + KeyLength > _target.Length)
            {
                return;
            }

            int hash = Hash(_target, pos);
            _targetPrevious[pos] = _targetHead[hash];
            _targetHead[hash] = pos;
        }

        private void WriteHeader(string metadata)
        {
            byte[] metadataBytes = Encoding.UTF8.GetBytes(metadata);
            _output.AddRange(BpsFormat.Magic);
            BpsFormat.WriteNumber(_output, (ulong)_source.Length);
            BpsFormat.WriteNumber(_output, (ulong)_target.Length);
            BpsFormat.WriteNumber(_output, (ulong)metadataBytes.Length);
            _output.AddRange(metadataBytes);
        }

        private void WriteTargetRead(int start, int length)
        {
            BpsFormat.WriteNumber(_output, ((ulong)(length - 1) << 2) | BpsFormat.ActionTargetRead);
            _output.AddRange(_target.AsSpan(start, length));
        }

        private void WriteMatch(Match match)
        {
            BpsFormat.WriteNumber(_output, ((ulong)(match.Length - 1) << 2) | (uint)match.Action);

            switch (match.Action)
            {
                case BpsFormat.ActionSourceCopy:
                    BpsFormat.WriteNumber(_output, BpsFormat.EncodeOffset(match.From - _sourceRelativeOffset));
                    _sourceRelativeOffset = match.From + match.Length;
                    break;

                case BpsFormat.ActionTargetCopy:
                    BpsFormat.WriteNumber(_output, BpsFormat.EncodeOffset(match.From - _targetRelativeOffset));
                    _targetRelativeOffset = match.From + match.Length;
                    break;
            }
        }

        private void WriteFooter()
        {
            Span<byte> crc = stackalloc byte[4];

            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(_source));
            _output.AddRange(crc);
            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(_target));
            _output.AddRange(crc);

            // The patch checksum covers everything written so far, including the two CRCs above.
            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_output)));
            _output.AddRange(crc);
        }

        private static int Hash(byte[] data, int pos)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos, KeyLength));
            return (int)((key * 2654435761u) >> (32 - HashBits));
        }

        /// <summary>A candidate action.</summary>
        /// <param name="Action">One of the BpsFormat.Action* values.</param>
        /// <param name="Length">Number of target bytes it produces.</param>
        /// <param name="From">Read position in source or target (unused for SourceRead).</param>
        /// <param name="Cost">Patch bytes it needs.</param>
        private readonly record struct Match(int Action, int Length, int From, int Cost)
        {
            /// <summary>Bytes saved compared to storing the data literally.</summary>
            public int Gain => Length - Cost;
        }
    }
}
