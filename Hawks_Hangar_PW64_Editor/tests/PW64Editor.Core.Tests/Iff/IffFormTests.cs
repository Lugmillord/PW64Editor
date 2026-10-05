using System.Text;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Iff;

public class IffFormTests
{
    [Fact]
    public void Parse_ListsAllChunks()
    {
        byte[] form = IffBuilder.Form("UVTX",
            IffBuilder.Chunk("PAD ", new byte[4]),
            IffBuilder.Chunk("COMM", new byte[10]),
            IffBuilder.Chunk("BITM", new byte[3])); // odd size: no padding in Pilotwings

        IffForm parsed = IffForm.Parse(form);

        Assert.Equal("UVTX", parsed.FormType);
        Assert.Equal(form.Length, parsed.TotalSize);
        Assert.Equal(3, parsed.Chunks.Count);
        Assert.Equal(new IffChunk("PAD ", 0x0C, 4), parsed.Chunks[0]);
        Assert.Equal(new IffChunk("COMM", 0x18, 10), parsed.Chunks[1]);
        Assert.Equal(new IffChunk("BITM", 0x2A, 3), parsed.Chunks[2]);
        Assert.Equal("COMM", parsed.FindChunk("COMM")?.Tag);
        Assert.Null(parsed.FindChunk("XXXX"));
    }

    [Fact]
    public void Parse_Throws_WithoutFormMagic()
    {
        Assert.Throws<InvalidDataException>(() => IffForm.Parse(new byte[16]));
    }

    [Fact]
    public void Parse_Throws_WhenChunkExtendsBeyondForm()
    {
        byte[] form = IffBuilder.Form("TEST", IffBuilder.Chunk("COMM", new byte[8]));
        form[0x0C + 7] = 0x20; // claim the chunk is 0x20 bytes instead of 8

        Assert.Throws<InvalidDataException>(() => IffForm.Parse(form));
    }

    [Fact]
    public void Parse_Throws_WhenDataIsShorterThanForm()
    {
        byte[] form = IffBuilder.Form("TEST", IffBuilder.Chunk("COMM", new byte[8]));

        Assert.Throws<InvalidDataException>(() => IffForm.Parse(form.AsSpan(0, form.Length - 1)));
    }

    [Fact]
    public void GzipChunk_ReadChunkData_DecompressesTransparently()
    {
        byte[] content = Encoding.ASCII.GetBytes("Hawk's Hangar compressed chunk content, repeated: content, content");
        byte[] form = IffBuilder.Form("TEST", IffBuilder.GzipChunk("COMM", content), IffBuilder.Chunk("RAW ", [1, 2, 3, 4]));
        IffForm parsed = IffForm.Parse(form);

        (string tag0, byte[] data0) = GzipChunk.ReadChunkData(form, parsed.Chunks[0]);
        (string tag1, byte[] data1) = GzipChunk.ReadChunkData(form, parsed.Chunks[1]);

        Assert.Equal("COMM", tag0);
        Assert.Equal(content, data0);
        Assert.Equal("RAW ", tag1);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, data1);
    }
}
