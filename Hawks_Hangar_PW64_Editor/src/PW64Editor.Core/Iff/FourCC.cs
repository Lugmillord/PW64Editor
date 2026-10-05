using System.Buffers.Binary;
using System.Text;

namespace PW64Editor.Core.Iff;

/// <summary>
/// Helpers for four-character codes ("FourCC") such as "FORM", "UVTX" or "GZIP".
/// </summary>
/// <remarks>
/// IFF files identify everything by four ASCII characters stored as a big-endian 32-bit value.
/// We use plain strings in the API because they are easy to read in the debugger and in output.
/// </remarks>
public static class FourCC
{
    /// <summary>Reads four bytes as a FourCC string.</summary>
    public static string Read(ReadOnlySpan<byte> data)
    {
        return Encoding.Latin1.GetString(data[..4]);
    }

    /// <summary>Writes a four-character string as four bytes.</summary>
    /// <exception cref="ArgumentException">The string is not exactly four characters.</exception>
    public static void Write(Span<byte> destination, string fourCC)
    {
        if (fourCC.Length != 4)
        {
            throw new ArgumentException($"FourCC must be exactly 4 characters: '{fourCC}'.", nameof(fourCC));
        }

        Encoding.Latin1.GetBytes(fourCC, destination[..4]);
    }

    /// <summary>
    /// Returns true if the value is all zero bytes. The game's file table uses tag 0 for
    /// entries that should be skipped.
    /// </summary>
    public static bool IsZero(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data) == 0;
}
