using PW64Editor.Core.Textures;

namespace PW64Editor.Core.Tests.Textures;

public class TextureImportTests
{
    [Theory]
    [InlineData("Texture_042_64x32_RGBA16.png", 42)]
    [InlineData("Textur_007_16x16_I4.png", 7)]
    [InlineData("テクスチャ_123_32x32_IA8.png", 123)]
    [InlineData("my grass 2_042_64x32_RGBA16.png", 42)]
    [InlineData("042.png", 42)]
    [InlineData("grass.png", null)]
    public void ParseNumber_FindsTheTextureNumber(string name, int? number)
    {
        Assert.Equal(number, TextureNames.ParseNumber(name));
    }

    [Fact]
    public void Check_AcceptsGoodFilesAndExplainsTheOthers()
    {
        // No real textures are needed: only their numbers and sizes matter.
        GameTexture texture = SyntheticTexture(8, 4);
        var textures = new Dictionary<int, TextureEntry>
        {
            [1] = new(1, 100, texture, texture, false, null),
            [2] = new(2, 101, texture, texture, false, null),
            [3] = new(3, 102, texture, texture, false, "broken"),
        };
        var good = new RgbaImage(8, 4);
        RgbaImage? Decode(byte[] data) => data.Length == 0 ? null : data[0] == 1 ? good : new RgbaImage(4, 4);

        TextureImportCheck check = TextureImport.Check(
        [
            new("textures/Texture_001_8x4_RGBA16.png", [1]),
            new("textures/", []),
            new("__MACOSX/textures/._Texture_001_8x4_RGBA16.png", [1]),
            new("textures/readme.txt", [1]),
            new("textures/grass.png", [1]),
            new("textures/Texture_009_8x4_RGBA16.png", [1]),
            new("textures/copy of Texture_001_8x4_RGBA16.png", [1]),
            new("textures/Texture_003_8x4_RGBA16.png", [1]),
            new("textures/Texture_002_8x4_RGBA16.png", [2]),
        ], textures, Decode);

        Assert.Equal(1, Assert.Single(check.Accepted).Number);
        Assert.Equal(
        [
            "textures/readme.txt", "textures/grass.png", "textures/Texture_009_8x4_RGBA16.png",
            "textures/copy of Texture_001_8x4_RGBA16.png", "textures/Texture_003_8x4_RGBA16.png", "textures/Texture_002_8x4_RGBA16.png",
        ], check.Rejected.Select(r => r.File));
        Assert.Equal("broken", check.Rejected[4].Reason);
        Assert.Contains("4 × 4", check.Rejected[5].Reason);
    }

    /// <summary>A texture of the given size in RGBA16 with a display list of one tile.</summary>
    internal static GameTexture SyntheticTexture(int width, int height)
    {
        int line = (width * 2 + 7) / 8;
        int imageSize = line * 8 * height;
        var data = new List<byte> { (byte)(imageSize >> 8), (byte)imageSize, 0, 2 };
        data.AddRange(new byte[16]);
        data.AddRange(new byte[imageSize]);

        // G_SETTILE tile 0: RGBA16, line, tmem 0; G_SETTILESIZE tile 0.
        ulong setTile = (0xF5UL << 56) | (0UL << 53) | (2UL << 51) | ((ulong)line << 41);
        ulong setSize = (0xF2UL << 56) | ((ulong)(uint)((width - 1) << 2) << 12) | (uint)((height - 1) << 2);
        foreach (ulong command in new[] { setTile, setSize })
        {
            for (int shift = 56; shift >= 0; shift -= 8)
            {
                data.Add((byte)(command >> shift));
            }
        }

        data.AddRange([(byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height]);
        data.AddRange(new byte[18]);
        return GameTexture.Parse(data.ToArray());
    }

    [Fact]
    public void SyntheticTexture_HasOneLevel()
    {
        GameTexture texture = SyntheticTexture(8, 4);
        Assert.Equal((8, 4, TextureFormat.Rgba16, 0), (texture.Width, texture.Height, texture.Format, texture.MipmapCount));
    }
}
