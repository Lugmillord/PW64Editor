using System.Text;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Iff;

public class IffWriterTests
{
    [Fact]
    public void BuildForm_MatchesIndependentTestBuilder()
    {
        byte[] expected = IffBuilder.Form("UVTX", IffBuilder.Chunk("COMM", [1, 2, 3, 4]), IffBuilder.Chunk("PAD ", new byte[4]));

        byte[] actual = IffWriter.BuildForm("UVTX", [IffWriter.BuildChunk("COMM", [1, 2, 3, 4]), IffWriter.BuildChunk("PAD ", new byte[4])]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RecompressForm_PreservesContentOfAllChunks()
    {
        byte[] content = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("terrain data ", 100)));
        byte[] original = IffBuilder.Form("UVTR", IffBuilder.Chunk("PAD ", new byte[4]), IffBuilder.GzipChunk("COMM", content));

        byte[] recompressed = IffWriter.RecompressForm(original);
        IffForm form = IffForm.Parse(recompressed);

        Assert.Equal("UVTR", form.FormType);
        Assert.Equal(["PAD ", "GZIP"], form.Chunks.Select(c => c.Tag).ToArray());
        (string tag, byte[] data) = GzipChunk.ReadChunkData(recompressed, form.Chunks[1]);
        Assert.Equal("COMM", tag);
        Assert.Equal(content, data);
    }
}
