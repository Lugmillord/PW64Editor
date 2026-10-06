using System.Buffers.Binary;
using PW64Editor.Core.Code;

namespace PW64Editor.Core.Tests.Code;

public class MipsAddressPatcherTests
{
    // lui $a1, 0x0062 / addiu $a1, $a1, 0x8B70 -> 0x00618B70 (the original sequence address).
    private const uint LuiA1 = 0x3C050062;
    private const uint AddiuA1 = 0x24A58B70;

    private static readonly MipsAddressReference Reference = new(0x0, 0x8, "test");

    [Fact]
    public void ReadValue_HandlesNegativeLowerHalf()
    {
        byte[] code = Code(LuiA1, 0x00000000, AddiuA1);

        Assert.Equal(0x00618B70u, MipsAddressPatcher.ReadValue(code, Reference));
    }

    [Theory]
    [InlineData(0x00614170u)] // lower half positive
    [InlineData(0x00628A60u)] // lower half >= 0x8000: upper half must be incremented
    [InlineData(0x00FF8000u)] // exactly on the sign boundary
    [InlineData(0x01000000u)] // beyond 16 MiB (expanded ROM)
    public void WriteValue_ThenReadValue_RoundTrips(uint value)
    {
        byte[] code = Code(LuiA1, 0x00000000, AddiuA1);

        MipsAddressPatcher.WriteValue(code, Reference, value);

        Assert.Equal(value, MipsAddressPatcher.ReadValue(code, Reference));
    }

    [Fact]
    public void WriteValue_KeepsRegistersAndOtherInstructions()
    {
        byte[] code = Code(LuiA1, 0x12345678, AddiuA1);

        MipsAddressPatcher.WriteValue(code, Reference, 0x00700000);

        Assert.Equal(0x3C050070u, BinaryPrimitives.ReadUInt32BigEndian(code)); // same opcode and register
        Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(4))); // untouched
        Assert.Equal(0x24A50000u, BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(8)));
    }

    [Fact]
    public void ReadValue_Throws_WhenInstructionsAreNotLuiAddiu()
    {
        byte[] code = Code(0x00000000, 0x00000000, AddiuA1);

        Assert.Throws<InvalidDataException>(() => MipsAddressPatcher.ReadValue(code, Reference));
    }

    [Fact]
    public void ReadValue_Throws_WhenRegistersDoNotMatch()
    {
        // lui $a1 followed by addiu $a2, $a2: not a pair.
        byte[] code = Code(LuiA1, 0x00000000, 0x24C68B70);

        Assert.Throws<InvalidDataException>(() => MipsAddressPatcher.ReadValue(code, Reference));
    }

    private static byte[] Code(params uint[] words)
    {
        byte[] data = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4), words[i]);
        }

        return data;
    }
}
