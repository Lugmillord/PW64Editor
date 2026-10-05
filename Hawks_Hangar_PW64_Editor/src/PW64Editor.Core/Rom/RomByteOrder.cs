namespace PW64Editor.Core.Rom;

/// <summary>
/// The byte order in which an N64 ROM image is stored on disk.
/// </summary>
/// <remarks>
/// The N64 CPU (MIPS R4300i) is big-endian, so the "native" layout is <see cref="BigEndian"/>.
/// The other two layouts are historical artifacts of old copier devices and dumping tools.
/// Internally the editor always works with big-endian data.
/// </remarks>
public enum RomByteOrder
{
    /// <summary>The byte order could not be determined.</summary>
    Unknown = 0,

    /// <summary>Native big-endian layout, usually stored as <c>.z64</c>. First bytes: 80 37 12 40.</summary>
    BigEndian,

    /// <summary>Every 16-bit word byte-swapped, usually stored as <c>.v64</c>. First bytes: 37 80 40 12.</summary>
    ByteSwapped,

    /// <summary>Every 32-bit word reversed, usually stored as <c>.n64</c>. First bytes: 40 12 37 80.</summary>
    LittleEndian,
}
