using System.Buffers.Binary;

namespace PW64Editor.Core.Code;

/// <summary>
/// Reads and changes 32-bit constants that are encoded as a <c>lui</c> + <c>addiu</c> pair.
/// </summary>
public static class MipsAddressPatcher
{
    private const uint OpcodeLui = 0x0F;
    private const uint OpcodeAddiu = 0x09;

    /// <summary>
    /// Reads the constant that the instruction pair currently produces.
    /// </summary>
    /// <exception cref="InvalidDataException">The instructions are not a matching lui/addiu pair,
    /// which means the ROM is not the version the reference was made for.</exception>
    public static uint ReadValue(ReadOnlySpan<byte> rom, MipsAddressReference reference)
    {
        (uint hiWord, uint loWord) = ReadAndValidate(rom, reference);

        uint upper = hiWord & 0xFFFF;
        short lower = (short)(loWord & 0xFFFF); // signed, see MipsAddressReference remarks
        return (upper << 16) + (uint)lower;
    }

    /// <summary>
    /// Changes the instruction pair so it produces <paramref name="value"/>.
    /// Registers and everything else in the instructions stay unchanged.
    /// </summary>
    public static void WriteValue(Span<byte> rom, MipsAddressReference reference, uint value)
    {
        (uint hiWord, uint loWord) = ReadAndValidate(rom, reference);

        uint lower = value & 0xFFFF;
        // If the lower half will be read as negative, pre-increment the upper half.
        uint upper = ((value >> 16) + (lower >= 0x8000 ? 1u : 0u)) & 0xFFFF;

        BinaryPrimitives.WriteUInt32BigEndian(rom[reference.HiOffset..], (hiWord & 0xFFFF0000) | upper);
        BinaryPrimitives.WriteUInt32BigEndian(rom[reference.LoOffset..], (loWord & 0xFFFF0000) | lower);
    }

    private static (uint Hi, uint Lo) ReadAndValidate(ReadOnlySpan<byte> rom, MipsAddressReference reference)
    {
        uint hiWord = BinaryPrimitives.ReadUInt32BigEndian(rom[reference.HiOffset..]);
        uint loWord = BinaryPrimitives.ReadUInt32BigEndian(rom[reference.LoOffset..]);

        uint hiTarget = (hiWord >> 16) & 0x1F;  // register written by lui (rt)
        uint loSource = (loWord >> 21) & 0x1F;  // register read by addiu (rs)

        if (hiWord >> 26 != OpcodeLui || loWord >> 26 != OpcodeAddiu || hiTarget != loSource)
        {
            throw new InvalidDataException(
                $"Expected a lui/addiu pair at 0x{reference.HiOffset:X}/0x{reference.LoOffset:X} " +
                $"({reference.Description}), found 0x{hiWord:X8}/0x{loWord:X8}.");
        }

        return (hiWord, loWord);
    }
}
