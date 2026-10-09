namespace PW64Editor.Core.SaveGame;

/// <summary>
/// The cartridge's EEPROM as emulators store it (.eep / .eeprom): 512 bytes (4 kbit) with two
/// saved games of 256 bytes. Some emulators store 2 KB (16 kbit); the bytes behind the first
/// 512 are not used by the game and are kept as they are.
/// </summary>
public sealed class EepromFile
{
    /// <summary>The part of the EEPROM the game uses.</summary>
    public const int UsedSize = 2 * SaveSlot.Size;

    /// <summary>Largest EEPROM of an N64 cartridge (16 kbit).</summary>
    public const int MaxSize = 2048;

    private readonly byte[] _extra;

    private EepromFile(byte[] data, SaveGameLayout layout)
    {
        Slots = [new SaveSlot(data.AsSpan(0, SaveSlot.Size), layout), new SaveSlot(data.AsSpan(SaveSlot.Size, SaveSlot.Size), layout)];
        _extra = data[UsedSize..];
    }

    /// <summary>The two saved games ("File 1" and "File 2" in the game).</summary>
    public IReadOnlyList<SaveSlot> Slots { get; }

    /// <summary>Bytes behind the two saved games (not used by the game).</summary>
    public ReadOnlySpan<byte> ExtraBytes => _extra;

    /// <summary>Size of the file.</summary>
    public int Size => UsedSize + _extra.Length;

    /// <summary>True once a saved game was changed.</summary>
    public bool IsModified => Slots.Any(s => s.IsModified);

    /// <exception cref="InvalidDataException">The data is not an EEPROM of 512 to 2048 bytes.</exception>
    public static EepromFile Read(byte[] data, SaveGameLayout layout)
    {
        if (data.Length < UsedSize || data.Length > MaxSize)
        {
            throw new InvalidDataException(
                $"This is not an EEPROM save file: it has {data.Length} bytes, but should have {UsedSize} (or up to {MaxSize}).");
        }

        return new EepromFile(data, layout);
    }

    /// <exception cref="InvalidDataException">See <see cref="Read(byte[], SaveGameLayout)"/>.</exception>
    public static EepromFile Load(string path, SaveGameLayout layout) => Read(File.ReadAllBytes(path), layout);

    /// <summary>The whole file.</summary>
    public byte[] ToBytes() => [.. Slots[0].ToBytes(), .. Slots[1].ToBytes(), .. _extra];
}
