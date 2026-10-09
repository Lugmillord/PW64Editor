namespace PW64Editor.Core.SaveGame;

/// <summary>State of a saved game, from its two magic bytes.</summary>
public enum SaveSlotState
{
    /// <summary>"pw": prepared by the game, but nothing saved yet (the game shows it as empty).</summary>
    Empty,

    /// <summary>"PW": a saved game.</summary>
    InUse,

    /// <summary>Anything else: the game prepares the slot anew when it starts.</summary>
    Invalid,
}

/// <summary>
/// One of the two saved games of the EEPROM (256 bytes, PilotwingsSaveFile in src/app/save.c):
/// magic "PW", one 7-bit result per test, the photo album (6 × 172 bits), and a checksum
/// (the sum of bytes 0-254) in the last byte. All values are packed as a bit stream, bit n of
/// a byte being 1 &lt;&lt; n.
/// </summary>
/// <remarks>
/// Unchanged slots are written back byte for byte. Changed slots keep all bits the editor does
/// not know (the unused bytes behind the album), and get a new checksum.
/// </remarks>
public sealed class SaveSlot
{
    /// <summary>Size of a saved game.</summary>
    public const int Size = 256;

    /// <summary>Result value of a test without a result (the game's 0x7F).</summary>
    public const int NoResult = 127;

    private readonly byte[] _raw;
    private readonly int[] _results;
    private readonly List<SavePhoto> _photos = [];

    public SaveSlot(ReadOnlySpan<byte> data, SaveGameLayout layout)
    {
        if (data.Length != Size)
        {
            throw new ArgumentException($"A saved game has {Size} bytes.", nameof(data));
        }

        Layout = layout;
        _raw = data.ToArray();
        _results = new int[layout.SavedTests.Count];
        ReadValues();
    }

    public SaveGameLayout Layout { get; }

    /// <summary>True once anything was changed.</summary>
    public bool IsModified { get; private set; }

    public SaveSlotState State => (_raw[0], _raw[1]) switch
    {
        ((byte)'P', (byte)'W') => SaveSlotState.InUse,
        ((byte)'p', (byte)'w') => SaveSlotState.Empty,
        _ => SaveSlotState.Invalid,
    };

    /// <summary>The checksum stored in the last byte.</summary>
    public byte StoredChecksum => _raw[Size - 1];

    /// <summary>The checksum the bytes 0-254 need (the game refuses to load the slot otherwise).</summary>
    public byte ComputedChecksum => Checksum(_raw);

    /// <summary>Bytes behind the photo album up to the checksum that are not 0 (the game writes zeros there).</summary>
    public int UnusedBytesInUse
    {
        get
        {
            int first = (Layout.DataEndBit + 7) / 8;
            int count = 0;
            for (int i = first; i < SaveGameLayout.DataBytes; i++)
            {
                if (_raw[i] != 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The stored value of a test: 0-127, <see cref="NoResult"/> if none; <see cref="NoResult"/> for tests that are not saved.</summary>
    public int GetResult(TestId test)
    {
        int index = Layout.IndexOf(test);
        return index < 0 ? NoResult : _results[index];
    }

    /// <summary>Sets the stored value of a test (0-127). Tests that are not saved are ignored.</summary>
    public void SetResult(TestId test, int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, NoResult);
        int index = Layout.IndexOf(test);
        if (index < 0 || _results[index] == value)
        {
            return;
        }

        _results[index] = value;
        IsModified = true;
    }

    /// <summary>The photo album (always 6 records; empty ones have <see cref="SavePhoto.IsEmpty"/>).</summary>
    public IReadOnlyList<SavePhoto> Photos => _photos;

    /// <summary>Removes a photo; the later photos move up, and the last record becomes empty.</summary>
    public void DeletePhoto(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _photos.Count);
        _photos.RemoveAt(index);
        _photos.Add(new SavePhoto(new byte[SavePhoto.RecordBytes]));
        IsModified = true;
    }

    /// <summary>
    /// Makes the slot a new saved game, as the game writes it at the first save: magic "PW",
    /// no results, no photos.
    /// </summary>
    public void StartNew()
    {
        Array.Clear(_raw);
        _raw[0] = (byte)'P';
        _raw[1] = (byte)'W';
        Array.Fill(_results, NoResult);
        for (int i = 0; i < _photos.Count; i++)
        {
            _photos[i] = new SavePhoto(new byte[SavePhoto.RecordBytes]);
        }

        IsModified = true;
    }

    /// <summary>Erases the slot as the game's "Erase" does: all zeros and the magic "pw".</summary>
    public void Erase()
    {
        Array.Clear(_raw);
        _raw[0] = (byte)'p';
        _raw[1] = (byte)'w';
        Array.Fill(_results, 0);
        for (int i = 0; i < _photos.Count; i++)
        {
            _photos[i] = new SavePhoto(new byte[SavePhoto.RecordBytes]);
        }

        IsModified = true;
    }

    /// <summary>The 256 bytes of the slot. A changed slot in use gets a new checksum.</summary>
    public byte[] ToBytes()
    {
        if (!IsModified)
        {
            return _raw.ToArray();
        }

        byte[] data = _raw.ToArray();
        if (State == SaveSlotState.Empty)
        {
            // The game's erase writes zeros and no checksum (saveFileInit).
            return data;
        }

        int bit = SaveGameLayout.ResultsStartBit;
        foreach (int value in _results)
        {
            WriteBits(data, ref bit, value, SaveGameLayout.ResultBits);
        }

        foreach (SavePhoto photo in _photos)
        {
            for (int j = 0; j < SaveGameLayout.PhotoBits; j++)
            {
                WriteBits(data, ref bit, (photo.Record[j / 8] >> (j % 8)) & 1, 1);
            }
        }

        data[Size - 1] = Checksum(data);
        return data;
    }

    private void ReadValues()
    {
        int bit = SaveGameLayout.ResultsStartBit;
        for (int i = 0; i < _results.Length; i++)
        {
            _results[i] = ReadBits(_raw, ref bit, SaveGameLayout.ResultBits);
        }

        for (int p = 0; p < SaveGameLayout.PhotoCount; p++)
        {
            var record = new byte[SavePhoto.RecordBytes];
            for (int j = 0; j < SaveGameLayout.PhotoBits; j++)
            {
                record[j / 8] |= (byte)(ReadBits(_raw, ref bit, 1) << (j % 8));
            }

            _photos.Add(new SavePhoto(record));
        }
    }

    /// <summary>Sum of bytes 0-254, the low 8 bits.</summary>
    public static byte Checksum(ReadOnlySpan<byte> slot)
    {
        int sum = 0;
        for (int i = 0; i < SaveGameLayout.DataBytes; i++)
        {
            sum += slot[i];
        }

        return (byte)sum;
    }

    /// <summary>Reads a value of <paramref name="count"/> bits (saveFilePack).</summary>
    public static int ReadBits(ReadOnlySpan<byte> data, ref int bit, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++, bit++)
        {
            value |= ((data[bit / 8] >> (bit % 8)) & 1) << i;
        }

        return value;
    }

    /// <summary>Writes a value of <paramref name="count"/> bits, replacing the bits that were there.</summary>
    public static void WriteBits(Span<byte> data, ref int bit, int value, int count)
    {
        for (int i = 0; i < count; i++, bit++)
        {
            int mask = 1 << (bit % 8);
            if (((value >> i) & 1) != 0)
            {
                data[bit / 8] |= (byte)mask;
            }
            else
            {
                data[bit / 8] &= (byte)~mask;
            }
        }
    }
}
