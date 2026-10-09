using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Textures;

/// <summary>A file of a ZIP archive (or folder) to import.</summary>
/// <param name="Name">Its name, possibly with folders, e.g. "textures/Texture_042_64x32_RGBA16.png".</param>
/// <param name="Data">Its content.</param>
public sealed record TextureImportFile(string Name, byte[] Data);

/// <summary>A file that was not imported.</summary>
/// <param name="File">Its name.</param>
/// <param name="Reason">A short reason.</param>
public sealed record TextureImportRejection(string File, string Reason);

/// <summary>The result of <see cref="TextureImport.Check"/>.</summary>
/// <param name="Accepted">The images to import, by texture number, in file order.</param>
/// <param name="Rejected">The files that are not imported, in file order.</param>
public sealed record TextureImportCheck(IReadOnlyList<(int Number, RgbaImage Image)> Accepted, IReadOnlyList<TextureImportRejection> Rejected);

/// <summary>Checks many images at once (the import of a ZIP archive made by "Export all").</summary>
public static class TextureImport
{
    /// <summary>
    /// Finds the texture of every file by the number in its name and checks the image. A file is
    /// rejected if it is no PNG file, its name has no number or the number of no texture, the
    /// number came before, the image cannot be read, or its size differs from the texture's.
    /// Folders and hidden files (e.g. "__MACOSX/…", ".DS_Store") are skipped without a message.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="textures">All textures by number.</param>
    /// <param name="decode">Reads an image file; returns null if it is no readable image.</param>
    public static TextureImportCheck Check(IEnumerable<TextureImportFile> files, IReadOnlyDictionary<int, TextureEntry> textures,
        Func<byte[], RgbaImage?> decode)
    {
        var accepted = new List<(int, RgbaImage)>();
        var rejected = new List<TextureImportRejection>();
        var seen = new HashSet<int>();

        foreach (TextureImportFile file in files)
        {
            string name = file.Name.Replace('\\', '/');
            string fileName = name[(name.LastIndexOf('/') + 1)..];
            if (fileName.Length == 0 || fileName.StartsWith('.') || name.StartsWith("__MACOSX/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add(new(name, CoreText.T("Not a PNG file")));
                continue;
            }

            if (TextureNames.ParseNumber(fileName) is not { } number)
            {
                rejected.Add(new(name, CoreText.T("The file name contains no texture number")));
                continue;
            }

            if (!textures.TryGetValue(number, out TextureEntry? texture))
            {
                rejected.Add(new(name, CoreText.F("There is no texture with the number {0}", number)));
                continue;
            }

            if (!seen.Add(number))
            {
                rejected.Add(new(name, CoreText.F("Texture {0} appears more than once; only its first file was used", number)));
                continue;
            }

            if (texture.Problem is not null)
            {
                rejected.Add(new(name, texture.Problem));
                continue;
            }

            RgbaImage? image = decode(file.Data);
            if (image is null)
            {
                rejected.Add(new(name, CoreText.T("The image cannot be read")));
                continue;
            }

            if (image.Width != texture.Original.Width || image.Height != texture.Original.Height)
            {
                rejected.Add(new(name, CoreText.F("Wrong size: {0} × {1} instead of {2} × {3}",
                    image.Width, image.Height, texture.Original.Width, texture.Original.Height)));
                continue;
            }

            accepted.Add((number, image));
        }

        return new TextureImportCheck(accepted, rejected);
    }
}
