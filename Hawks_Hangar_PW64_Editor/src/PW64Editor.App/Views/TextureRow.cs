using System.ComponentModel;
using System.Windows.Media.Imaging;
using PW64Editor.App.Services;
using PW64Editor.Core.Textures;

namespace PW64Editor.App.Views;

/// <summary>
/// One texture in the grid of the tab "Textures". Public properties, because WPF data binding reads them.
/// </summary>
public sealed class TextureRow : INotifyPropertyChanged
{
    private TextureRow(TextureEntry entry, RgbaImage? image)
    {
        Entry = entry;
        Image = image;
        Thumbnail = image is null ? null : TextureImages.ToBitmap(image);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The texture.</summary>
    public TextureEntry Entry { get; private set; }

    /// <summary>The current image, or null if it cannot be shown.</summary>
    public RgbaImage? Image { get; private set; }

    /// <summary>The current image as a bitmap.</summary>
    public BitmapSource? Thumbnail { get; private set; }

    public int Number => Entry.Number;

    /// <summary>The number as shown under the image, e.g. "042".</summary>
    public string Label => Entry.Number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);

    public bool IsChanged => Entry.IsChanged;

    /// <summary>Size and format, e.g. "64 × 32, RGBA16".</summary>
    public string ToolTip => L.F("Texture {0}: {1} × {2}, {3}", Label, Entry.Original.Width, Entry.Original.Height,
        TextureFormats.Name(Entry.Original.Format)) + (IsChanged ? " " + L.T("(changed)") : string.Empty);

    /// <summary>Size for the size filter, e.g. "64 × 32".</summary>
    public string SizeText => $"{Entry.Original.Width} × {Entry.Original.Height}";

    /// <summary>Creates a row and decodes its image (can run on any thread).</summary>
    public static TextureRow Create(TextureEntry entry) => new(entry, Decode(entry));

    /// <summary>Shows another version of the texture (after it was replaced or restored).</summary>
    public void Update(TextureEntry entry)
    {
        Entry = entry;
        Image = Decode(entry);
        Thumbnail = Image is null ? null : TextureImages.ToBitmap(Image);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static RgbaImage? Decode(TextureEntry entry) =>
        entry.Current.IsSupported ? entry.Current.DecodeImage() : null;
}
