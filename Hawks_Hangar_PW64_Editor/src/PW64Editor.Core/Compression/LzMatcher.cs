namespace PW64Editor.Core.Compression;

/// <summary>
/// Finds repeated byte sequences for LZ77-style compressors (MIO0, and later possibly Yay0).
/// </summary>
/// <remarks>
/// Uses "hash chains": every position is filed under a hash of its first three bytes, and
/// positions with the same hash are linked together, newest first. To find a match we only
/// look at earlier positions with the same three-byte prefix instead of the whole window.
/// <para>
/// Positions must be added with <see cref="Insert"/> in increasing order; a search only sees
/// positions that were inserted before it.
/// </para>
/// </remarks>
internal sealed class LzMatcher
{
    private const int HashBits = 15;
    private const int HashSize = 1 << HashBits;

    /// <summary>
    /// Upper limit for how many candidates are checked per search. Keeps compression fast on
    /// highly repetitive data (e.g. long runs of zeros) at a negligible cost in ratio.
    /// </summary>
    private const int MaxChainSteps = 256;

    private readonly byte[] _data;
    private readonly int _minLength;
    private readonly int _maxLength;
    private readonly int _maxDistance;

    // _head[hash] = most recent position with this hash (or -1).
    // _previous[pos] = the next older position with the same hash (or -1).
    private readonly int[] _head;
    private readonly int[] _previous;

    public LzMatcher(ReadOnlySpan<byte> data, int minLength, int maxLength, int maxDistance)
    {
        _data = data.ToArray();
        _minLength = minLength;
        _maxLength = maxLength;
        _maxDistance = maxDistance;
        _head = new int[HashSize];
        _previous = new int[data.Length];
        Array.Fill(_head, -1);
    }

    /// <summary>Adds a position to the search index.</summary>
    public void Insert(int pos)
    {
        if (pos + _minLength > _data.Length)
        {
            return; // too close to the end to start a match
        }

        int hash = Hash(pos);
        _previous[pos] = _head[hash];
        _head[hash] = pos;
    }

    /// <summary>
    /// Returns the longest match for the data at <paramref name="pos"/> among inserted positions,
    /// or length 0 if there is none of at least the minimum length.
    /// </summary>
    public (int Length, int Distance) FindLongestMatch(int pos)
    {
        if (pos + _minLength > _data.Length)
        {
            return (0, 0);
        }

        int maxLength = Math.Min(_maxLength, _data.Length - pos);
        int bestLength = 0;
        int bestDistance = 0;

        int candidate = _head[Hash(pos)];
        for (int steps = 0; candidate >= 0 && steps < MaxChainSteps; steps++)
        {
            int distance = pos - candidate;
            if (distance > _maxDistance)
            {
                break; // chain is sorted newest first, so all remaining ones are even farther away
            }

            if (distance > 0)
            {
                int length = 0;
                while (length < maxLength && _data[candidate + length] == _data[pos + length])
                {
                    length++;
                }

                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = distance;
                    if (length == maxLength)
                    {
                        break; // cannot get any better
                    }
                }
            }

            candidate = _previous[candidate];
        }

        return bestLength >= _minLength ? (bestLength, bestDistance) : (0, 0);
    }

    /// <summary>
    /// Hashes the three bytes at <paramref name="pos"/> into a table index.
    /// Multiplying by a large odd constant ("Fibonacci hashing") spreads similar inputs
    /// over the whole table; the top bits of the product are the best mixed ones.
    /// </summary>
    private int Hash(int pos)
    {
        uint value = (uint)((_data[pos] << 16) | (_data[pos + 1] << 8) | _data[pos + 2]);
        return (int)((value * 2654435761u) >> (32 - HashBits));
    }
}
