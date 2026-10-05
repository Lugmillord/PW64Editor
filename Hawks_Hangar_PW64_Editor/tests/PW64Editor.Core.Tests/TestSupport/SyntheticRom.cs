using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Tests.TestSupport;

/// <summary>
/// Builds small fake ROM images in memory, so most tests can run without the real game.
/// The content is meaningless except for the header fields we set.
/// </summary>
internal static class SyntheticRom
{
    /// <summary>
    /// Creates a big-endian ROM of the minimum valid size with a filled-in header.
    /// </summary>
    public static byte[] Create(
        string gameCode = "NPWE",
        byte version = 0,
        string internalName = "Pilot Wings64",
        int size = N64Rom.MinimumSize)
    {
        byte[] data = new byte[size];

        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x00), 0x80371240); // PI config / magic
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x04), 0x0000000F); // clock rate
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x08), 0x80200050); // boot address
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x10), 0x11223344); // CRC1
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x14), 0x55667788); // CRC2

        // Internal name: 20 bytes, padded with spaces.
        byte[] name = Encoding.ASCII.GetBytes(internalName.PadRight(20));
        name.CopyTo(data, 0x20);

        // Game code goes into 0x3B-0x3E, followed by the version byte.
        Encoding.ASCII.GetBytes(gameCode).CopyTo(data, 0x3B);
        data[0x3F] = version;

        // Fill the rest with a simple pattern so byte swapping actually changes something.
        for (int i = RomHeader.Size; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        return data;
    }
}
