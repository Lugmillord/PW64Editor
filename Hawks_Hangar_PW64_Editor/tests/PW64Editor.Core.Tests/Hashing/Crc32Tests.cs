using System.Text;
using PW64Editor.Core.Hashing;

namespace PW64Editor.Core.Tests.Hashing;

public class Crc32Tests
{
    [Fact]
    public void Compute_MatchesStandardCheckValue()
    {
        // "123456789" -> 0xCBF43926 is the official check value of CRC-32/ISO-HDLC,
        // used to verify every CRC-32 implementation.
        byte[] data = Encoding.ASCII.GetBytes("123456789");

        Assert.Equal(0xCBF43926u, Crc32.Compute(data));
    }

    [Fact]
    public void Compute_OfEmptyData_IsZero()
    {
        Assert.Equal(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Append_InChunks_EqualsComputeAtOnce()
    {
        byte[] data = Encoding.ASCII.GetBytes("Pilotwings 64 - Hawk's Hangar");

        uint atOnce = Crc32.Compute(data);
        uint inChunks = Crc32.Append(Crc32.Compute(data.AsSpan(0, 10)), data.AsSpan(10));

        Assert.Equal(atOnce, inChunks);
    }
}
