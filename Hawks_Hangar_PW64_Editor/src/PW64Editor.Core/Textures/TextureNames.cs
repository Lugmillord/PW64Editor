using System.Text.RegularExpressions;

namespace PW64Editor.Core.Textures;

/// <summary>File names of exported textures, e.g. "Texture_042_64x32_RGBA16.png".</summary>
public static partial class TextureNames
{
    /// <summary>
    /// The file name for a texture: a word, the texture's number, its size and its format, so the
    /// needed size can be seen in any image program.
    /// </summary>
    /// <param name="word">The first part, e.g. "Texture" (may be translated).</param>
    public static string FileName(string word, int number, GameTexture texture) =>
        $"{word}_{number:D3}_{texture.Width}x{texture.Height}_{TextureFormats.Name(texture.Format)}.png";

    /// <summary>
    /// The texture number in a file name: the number in front of the size ("_042_64x32"), or else
    /// the first number in the name. Null if there is none.
    /// </summary>
    public static int? ParseNumber(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        Match match = NumberBeforeSize().Match(name);
        if (!match.Success)
        {
            match = FirstNumber().Match(name);
        }

        return match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : null;
    }

    [GeneratedRegex(@"(?:^|_)(\d{1,5})_\d+x\d+(?:_|$)")]
    private static partial Regex NumberBeforeSize();

    [GeneratedRegex(@"(\d{1,5})")]
    private static partial Regex FirstNumber();
}
