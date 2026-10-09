using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Textures;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Textures;

public class GameTextureTests
{
    private static List<GameFile> TextureFiles() =>
        GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa).Files
            .Where(f => f.FileType == TextureFile.FormType).ToList();

    [RealRomFact]
    public void AllTexturesOfTheGame_CanBeReadAndWrittenBackUnchanged()
    {
        List<GameFile> files = TextureFiles();
        Assert.Equal(463, files.Count);

        foreach (GameFile file in files)
        {
            GameTexture texture = TextureFile.Parse(file.Data).Texture;
            Assert.True(texture.IsSupported, $"texture {file.GroupIndex}");
            Assert.Equal(texture.Width, texture.Levels[0].Width);

            // Writing the decoded main image (and its decoded smaller copies) back gives the same bytes.
            byte[] data = texture.Data;
            byte[] copy = data.ToArray();
            for (int level = 0; level < texture.Levels.Count; level++)
            {
                TexelCodec.Encode(texture.DecodeLevel(level), copy.AsSpan(0x14), texture.Levels[level]);
            }

            Assert.True(data.AsSpan().SequenceEqual(copy), $"texture {file.GroupIndex}");
        }
    }

    [RealRomFact]
    public void FormatsAndMipmaps_MatchTheGame()
    {
        List<GameTexture> textures = TextureFiles().Select(f => TextureFile.Parse(f.Data).Texture).ToList();

        Assert.Equal(256, textures.Count(t => t.Format == TextureFormat.Rgba16));
        Assert.Equal(100, textures.Count(t => t.Format == TextureFormat.I4));
        Assert.Equal(58, textures.Count(t => t.Format == TextureFormat.Ia8));
        Assert.Equal(315, textures.Count(t => t.MipmapCount > 0));

        // Texture 0: 64 × 16 RGBA16 with five smaller copies down to 2 × 1.
        GameTexture first = textures[0];
        Assert.Equal((64, 16, TextureFormat.Rgba16), (first.Width, first.Height, first.Format));
        Assert.Equal([(32, 8), (16, 4), (8, 2), (4, 1), (2, 1)], first.Levels.Skip(1).Select(l => (l.Width, l.Height)));
    }

    [RealRomFact]
    public void WithImage_ReplacesAllLevelsAndBuildsAValidFile()
    {
        GameFile file = TextureFiles()[0];
        TextureFile textureFile = TextureFile.Parse(file.Data);
        GameTexture texture = textureFile.Texture;
        var red = new RgbaImage(texture.Width, texture.Height, Enumerable.Range(0, texture.Width * texture.Height)
            .SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray());

        byte[] built = textureFile.Build(texture.WithImage(red));
        Assert.Equal(0, built.Length % 4);

        TextureFile again = TextureFile.Parse(built);
        Assert.True(again.IsCompressed);
        for (int level = 0; level < again.Texture.Levels.Count; level++)
        {
            RgbaImage image = again.Texture.DecodeLevel(level);
            Assert.All(Enumerable.Range(0, image.Width * image.Height), i => Assert.Equal(0xFF, image.Pixels[i * 4]));
        }
    }

    [RealRomFact]
    public void WithImage_RejectsAnotherSize()
    {
        GameTexture texture = TextureFile.Parse(TextureFiles()[0].Data).Texture;
        Assert.Throws<ArgumentException>(() => texture.WithImage(new RgbaImage(texture.Width + 1, texture.Height)));
    }

    [RealRomFact]
    public void Session_ImportingAllExportedImages_ChangesNothing()
    {
        // "Export all" followed by "Import all" must not put 463 textures into the project,
        // even if the image program dropped the color of invisible pixels.
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Texture Test", false, rom, TestRomLocator.RomPath!);

        foreach (TextureEntry texture in session.LoadTextures())
        {
            RgbaImage image = texture.Current.DecodeImage();
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                if (image.Pixels[i + 3] == 0)
                {
                    image.Pixels[i] = image.Pixels[i + 1] = image.Pixels[i + 2] = 0;
                }
            }

            Assert.Equal(TextureSaveResult.Unchanged, session.SaveTexture(texture.Number, image));
        }

        Assert.Empty(session.GetStatus());
    }

    [RealRomFact]
    public void Session_SavesRestoresAndBuildsTextures()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Texture Test", false, rom, TestRomLocator.RomPath!);

        TextureEntry texture = session.LoadTextures().First(t => t.Number == 4);
        RgbaImage original = texture.Original.DecodeImage();
        Assert.Equal(TextureSaveResult.Unchanged, session.SaveTexture(4, original));
        Assert.Empty(session.GetStatus());

        RgbaImage changed = new(original.Width, original.Height, original.Pixels.ToArray());
        changed[0, 0] = (0, 0, 0, 255);
        changed[1, 0] = (255, 255, 255, 255);
        Assert.Equal(TextureSaveResult.Changed, session.SaveTexture(4, changed));
        Assert.Equal(TextureSaveResult.Unchanged, session.SaveTexture(4, changed));

        TextureEntry saved = session.LoadTextures().First(t => t.Number == 4);
        Assert.True(saved.IsChanged);
        Assert.True(texture.Original.Quantize(changed).SamePixels(saved.Current.DecodeImage()));
        Assert.Contains(Assert.Single(session.GetStatus()).State, new[] { OverrideState.Modified, OverrideState.Resized });

        (_, string romPath, _) = session.Build(createRestorePoint: false);
        GameFile built = GameFileSystem.Read(N64Rom.Load(romPath), RomLayout.PilotwingsUsa).Files.First(f => f.TableIndex == texture.TableIndex);
        Assert.True(texture.Original.Quantize(changed).SamePixels(TextureFile.Parse(built.Data).Texture.DecodeImage()));

        // The original image again removes the replacement.
        Assert.Equal(TextureSaveResult.RestoredOriginal, session.SaveTexture(4, original));
        Assert.Empty(session.GetStatus());

        Assert.Equal(TextureSaveResult.Changed, session.SaveTexture(4, changed));
        Assert.True(session.RestoreTexture(4));
        Assert.False(session.LoadTextures().First(t => t.Number == 4).IsChanged);
    }
}
