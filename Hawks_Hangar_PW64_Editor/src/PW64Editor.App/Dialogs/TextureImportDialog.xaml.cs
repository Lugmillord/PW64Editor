using System.Windows;
using PW64Editor.App.Services;
using PW64Editor.App.Views;
using PW64Editor.Core.Textures;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Shows a texture next to the image that replaces it, as the game will show it (converted to
/// the texture's format), with notes on what the conversion changes. "Replace" returns true.
/// </summary>
public partial class TextureImportDialog : Window
{
    public TextureImportDialog(TextureRow row, RgbaImage image)
    {
        InitializeComponent();
        GameTexture original = row.Entry.Original;
        RgbaImage stored = original.Quantize(image);

        HeaderText.Text = L.F("Texture {0}: {1} × {2} pixels, {3} – {4}", row.Label, original.Width, original.Height,
            TextureFormats.Name(original.Format), TexturesTab.DescribeFormat(original.Format));
        CurrentImage.Source = row.Thumbnail;
        NewImage.Source = TextureImages.ToBitmap(stored);
        NoteList.ItemsSource = Notes(original, image, stored, row.Image);
    }

    /// <summary>What the conversion changes, only the points that apply to this image.</summary>
    private static List<string> Notes(GameTexture texture, RgbaImage image, RgbaImage stored, RgbaImage? current)
    {
        var notes = new List<string>();
        byte[] p = image.Pixels;
        bool hasColor = false, hasTransparency = false, hasPartialTransparency = false;
        for (int i = 0; i < p.Length; i += 4)
        {
            hasColor |= p[i] != p[i + 1] || p[i + 1] != p[i + 2];
            hasTransparency |= p[i + 3] < 255;
            hasPartialTransparency |= p[i + 3] is > 0 and < 255;
        }

        TextureFormat format = texture.Format;
        if (TextureFormats.IsGreyscale(format))
        {
            notes.Add(hasColor
                ? L.T("This texture is greyscale: the game colors it itself. The colors of the image become grey.")
                : L.T("This texture is greyscale: the game colors it itself."));
        }
        else
        {
            notes.Add(L.T("Colors are stored with 32 levels per color channel, so fine color gradients can get slight steps."));
        }

        if (hasTransparency && !TextureFormats.HasTransparency(format))
        {
            notes.Add(L.T("This format has no transparency: transparent parts of the image become visible."));
        }
        else if (hasPartialTransparency && TextureFormats.HasOnOffTransparency(format))
        {
            notes.Add(L.T("Each pixel is either visible or transparent: pixels that are less than half visible become transparent, the others fully visible."));
        }

        if (texture.MipmapCount > 0)
        {
            notes.Add(L.F("The {0} smaller copies for distant surfaces are computed from the image automatically.", texture.MipmapCount));
        }

        if (current is not null && stored.LooksLike(current))
        {
            notes.Add(L.T("The image looks exactly like the current texture."));
        }

        notes.Add(L.T("The game's own coloring and lighting are not shown here."));
        return notes;
    }

    private void OnReplace(object sender, RoutedEventArgs e) => DialogResult = true;
}
