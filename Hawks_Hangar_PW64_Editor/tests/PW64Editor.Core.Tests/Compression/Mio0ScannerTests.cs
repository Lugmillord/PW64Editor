using System.Text;
using PW64Editor.Core.Compression;

namespace PW64Editor.Core.Tests.Compression;

public class Mio0ScannerTests
{
    [Fact]
    public void FindAll_LocatesEmbeddedBlocks_AndSkipsFakeMagic()
    {
        byte[] blockA = Mio0.Compress(Encoding.ASCII.GetBytes("first block first block first block"));
        byte[] blockB = Mio0.Compress(new byte[500]);

        // Layout: 16 bytes padding, block A, a fake "MIO0" text with garbage, block B.
        byte[] fake = [.. Encoding.ASCII.GetBytes("MIO0"), 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] buffer = [.. new byte[16], .. blockA, .. fake, .. blockB];

        IReadOnlyList<Mio0BlockInfo> found = Mio0Scanner.FindAll(buffer);

        Assert.Equal(2, found.Count);
        Assert.Equal(16, found[0].Offset);
        Assert.Equal(16 + blockA.Length + fake.Length, found[1].Offset);
        Assert.Equal(500, found[1].DecompressedSize);
    }
}
