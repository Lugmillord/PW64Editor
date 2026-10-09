namespace PW64Editor.Core.SaveGame;

/// <summary>
/// One photo of the album as stored in a saved game: 172 bits of a packed record
/// (Unk8033F050 in src/app/snap.h, big-endian bit fields).
/// </summary>
/// <remarks>
/// The game copies the record bit by bit, bit n of byte k being record bit 8k + n. Without the
/// code fix "Correct saving of photos" the last 4 stored bits come from an unused part of the
/// record, so the instance numbers of the 5th and 6th object are lost; this class only shows
/// values that are stored in both cases.
/// </remarks>
public sealed class SavePhoto
{
    /// <summary>Bytes needed for the 172 bits.</summary>
    public const int RecordBytes = 22;

    private readonly byte[] _record;

    public SavePhoto(byte[] record)
    {
        if (record.Length != RecordBytes)
        {
            throw new ArgumentException($"A photo record has {RecordBytes} bytes.", nameof(record));
        }

        _record = record.ToArray();
    }

    /// <summary>The stored bits (172 bits, the last 4 bits of the last byte are 0).</summary>
    public ReadOnlySpan<byte> Record => _record;

    /// <summary>An empty slot (the game stores zeros; the first object type is 0).</summary>
    public bool IsEmpty => FirstObjectType == 0;

    /// <summary>Camera position in world units.</summary>
    public (short X, short Y, short Z) Position =>
        ((short)((_record[0] << 8) | _record[1]), (short)((_record[2] << 8) | _record[3]), (short)((_record[4] << 8) | _record[5]));

    /// <summary>Camera direction in degrees (stored with a step of 1.40625°).</summary>
    public (double X, double Y, double Z) Direction => (_record[6] * 1.40625, _record[7] * 1.40625, _record[8] * 1.40625);

    private uint Word8 => (uint)((_record[8] << 24) | (_record[9] << 16) | (_record[10] << 8) | _record[11]);

    private uint Word16 => (uint)((_record[16] << 24) | (_record[17] << 16) | (_record[18] << 8) | _record[19]);

    /// <summary>Class (for bonus games the level) in which the photo was taken.</summary>
    public int Class => (int)((Word8 >> 20) & 3);

    /// <summary>Test in which the photo was taken.</summary>
    public int Test => (int)((Word8 >> 22) & 3);

    /// <summary>Vehicle with which the photo was taken.</summary>
    public Vehicle Vehicle => (Vehicle)((Word16 >> 9) & 7);

    /// <summary>Number of objects on the photo (0-6).</summary>
    public int ObjectCount => (int)((Word16 >> 6) & 7);

    private int FirstObjectType => (int)((Word8 >> 16) & 15);

    /// <summary>The object types on the photo (see <see cref="ObjectName"/>).</summary>
    public IReadOnlyList<int> ObjectTypes
    {
        get
        {
            int[] all =
            [
                (int)((Word8 >> 16) & 15), (int)((Word8 >> 12) & 15), (int)((Word8 >> 8) & 15),
                (int)((Word8 >> 4) & 15), (int)(Word8 & 15), (int)((Word16 >> 12) & 15),
            ];
            return all.Take(Math.Min(ObjectCount, all.Length)).ToArray();
        }
    }

    /// <summary>Name of a photo object type (snap.c).</summary>
    public static string ObjectName(int type) => type switch
    {
        1 => "Space shuttle",
        2 => "Ferry",
        3 => "Missile",
        4 => "Whale",
        5 => "Fountain",
        6 => "Flame",
        7 => "Whale pod",
        8 => "Plane",
        9 => "Glider",
        10 => "Boat",
        11 => "Nothing",
        _ => $"Object {type}",
    };
}
