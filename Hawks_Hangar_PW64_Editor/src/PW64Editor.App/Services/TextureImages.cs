using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PW64Editor.Core.Textures;

namespace PW64Editor.App.Services;

/// <summary>Conversions between the core's images and WPF bitmaps and PNG files.</summary>
public static class TextureImages
{
    /// <summary>A frozen WPF bitmap of the image (can be used on any thread).</summary>
    public static BitmapSource ToBitmap(RgbaImage image)
    {
        byte[] bgra = new byte[image.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = image.Pixels[i + 2];
            bgra[i + 1] = image.Pixels[i + 1];
            bgra[i + 2] = image.Pixels[i];
            bgra[i + 3] = image.Pixels[i + 3];
        }

        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bgra, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The image as a PNG file (with transparency).</summary>
    public static byte[] ToPng(RgbaImage image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(image)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Reads an image file (PNG, but also other formats Windows can read). Returns null if the
    /// data is no readable image.
    /// </summary>
    public static RgbaImage? FromFile(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            byte[] bgra = new byte[width * height * 4];
            converted.CopyPixels(bgra, width * 4, 0);

            byte[] rgba = new byte[bgra.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }

            return new RgbaImage(width, height, rgba);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or IOException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
