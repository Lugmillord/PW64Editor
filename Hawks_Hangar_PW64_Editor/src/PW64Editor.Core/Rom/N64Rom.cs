using System.Security.Cryptography;

namespace PW64Editor.Core.Rom;

/// <summary>
/// An N64 ROM image held in memory, always in big-endian (native) byte order.
/// </summary>
/// <remarks>
/// Use <see cref="Load"/> or <see cref="FromBytes"/> to create an instance. Regardless of the
/// on-disk format (.z64/.v64/.n64), the data is normalized to big-endian on load, and the
/// original layout is remembered in <see cref="OriginalByteOrder"/>.
/// </remarks>
public sealed class N64Rom
{
    /// <summary>
    /// The smallest file we accept: header (0x40) + IPL3 boot code (up to 0x1000)
    /// + the 1 MiB that the boot checksum covers.
    /// </summary>
    public const int MinimumSize = 0x101000;

    /// <summary>
    /// The largest file we accept. 64 MiB is the practical limit of the cartridge address space
    /// used by ROM hacks; anything larger is almost certainly not an N64 ROM.
    /// </summary>
    public const int MaximumSize = 64 * 1024 * 1024;

    private N64Rom(byte[] data, RomByteOrder originalByteOrder, string? filePath)
    {
        Data = data;
        OriginalByteOrder = originalByteOrder;
        FilePath = filePath;
        Header = RomHeader.Parse(data);
    }

    /// <summary>The complete ROM contents in big-endian order.</summary>
    public byte[] Data { get; }

    /// <summary>The byte order the ROM had on disk before normalization.</summary>
    public RomByteOrder OriginalByteOrder { get; }

    /// <summary>The path the ROM was loaded from, or <c>null</c> if created from memory.</summary>
    public string? FilePath { get; }

    /// <summary>The parsed ROM header.</summary>
    public RomHeader Header { get; }

    /// <summary>Size of the ROM in bytes.</summary>
    public int Size => Data.Length;

    /// <summary>
    /// Loads a ROM file from disk and normalizes it to big-endian.
    /// </summary>
    /// <exception cref="InvalidRomException">The file is not a valid N64 ROM.</exception>
    public static N64Rom Load(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("ROM file not found.", path);
        }

        // Check the size before reading, so we never load a multi-gigabyte file by accident.
        if (fileInfo.Length > MaximumSize)
        {
            throw new InvalidRomException(
                $"File is too large to be an N64 ROM ({fileInfo.Length:N0} bytes, maximum is {MaximumSize:N0}).");
        }

        byte[] data = File.ReadAllBytes(path);
        return FromBytesInternal(data, path);
    }

    /// <summary>
    /// Creates a ROM from raw bytes (in any of the three byte orders).
    /// The array is copied, so the caller's buffer is left untouched.
    /// </summary>
    /// <exception cref="InvalidRomException">The data is not a valid N64 ROM.</exception>
    public static N64Rom FromBytes(ReadOnlySpan<byte> rawData)
    {
        return FromBytesInternal(rawData.ToArray(), filePath: null);
    }

    /// <summary>
    /// Computes the SHA-1 hash of the big-endian data as a lowercase hex string.
    /// This is the value used to identify clean ROM dumps (e.g. in the No-Intro database).
    /// </summary>
    public string ComputeSha1()
    {
        return Convert.ToHexStringLower(SHA1.HashData(Data));
    }

    /// <summary>
    /// Writes the ROM to disk. By default it is written as big-endian (.z64),
    /// which is the format all modern tools and emulators expect.
    /// </summary>
    public void Save(string path, RomByteOrder targetOrder = RomByteOrder.BigEndian)
    {
        // Convert a copy so that Data itself always stays big-endian.
        byte[] output = (byte[])Data.Clone();
        ByteOrderConverter.ConvertFromBigEndian(output, targetOrder);
        File.WriteAllBytes(path, output);
    }

    private static N64Rom FromBytesInternal(byte[] data, string? filePath)
    {
        if (data.Length < MinimumSize)
        {
            throw new InvalidRomException(
                $"File is too small to be an N64 ROM ({data.Length:N0} bytes, minimum is {MinimumSize:N0}).");
        }

        if (data.Length % 4 != 0)
        {
            throw new InvalidRomException(
                $"ROM size must be a multiple of 4 bytes, but is {data.Length:N0} bytes.");
        }

        RomByteOrder order = ByteOrderConverter.Detect(data);
        if (order == RomByteOrder.Unknown)
        {
            throw new InvalidRomException(
                "Unrecognized file format: the first four bytes do not match any known N64 ROM layout.");
        }

        ByteOrderConverter.ConvertToBigEndian(data, order);
        return new N64Rom(data, order, filePath);
    }
}
