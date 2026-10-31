using ImageMagick;

namespace Orange.Core;

/// <summary>Image conversions and tools on top of Magick.NET (reads HEIC, AVIF, WebP, SVG, TIFF…).</summary>
public static class Images
{
    private static readonly Dictionary<string, MagickFormat> Formats = new()
    {
        ["jpg"] = MagickFormat.Jpeg,
        ["png"] = MagickFormat.Png,
        ["webp"] = MagickFormat.WebP,
        ["avif"] = MagickFormat.Avif,
        ["tiff"] = MagickFormat.Tiff,
        ["bmp"] = MagickFormat.Bmp,
        ["gif"] = MagickFormat.Gif,
        ["ico"] = MagickFormat.Ico,
        ["pdf"] = MagickFormat.Pdf,
    };

    public static IReadOnlyCollection<string> Targets => Formats.Keys;

    /// <summary>Decodes an image from the phone for the clipboard, or returns null if the bytes aren't a usable photo format.</summary>
    public static System.Windows.Media.Imaging.BitmapSource? ForClipboard(byte[] data)
    {
        try
        {
            var info = new MagickImageInfo(data);
            if (info.Format is not (MagickFormat.Jpeg or MagickFormat.Png or MagickFormat.Heic or MagickFormat.Heif or MagickFormat.Avif
                    or MagickFormat.WebP or MagickFormat.Gif or MagickFormat.Tiff or MagickFormat.Bmp)
                || (ulong)info.Width * info.Height > 100_000_000) return null;

            using var img = new MagickImage(data);
            img.AutoOrient();
            // Many Windows programs paste clipboard transparency as black.
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaAction.Remove);

            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(img.ToByteArray(MagickFormat.Png));
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // decoded on a server thread, used on the UI thread
            return bitmap;
        }
        catch (MagickException)
        {
            return null;
        }
    }

    /// <summary>Encodes a clipboard image as PNG for the phone. Alpha is dropped: screenshots on the clipboard
    /// often report every pixel as transparent, which would arrive as a blank picture.</summary>
    public static byte[] FromClipboard(System.Windows.Media.Imaging.BitmapSource image)
    {
        var opaque = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgr24, null, 0);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(opaque));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static Task<string> ConvertAsync(string input, string ext) => Task.Run(() =>
    {
        var output = Paths.Output(input, ext);
        using var img = Load(input);

        // Formats without transparency get a white background instead of black.
        if (ext is "jpg" or "bmp" or "pdf")
        {
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaAction.Remove);
        }
        if (ext == "ico")
            img.Resize(new MagickGeometry("256x256>"));
        if (ext is "jpg" or "webp" or "avif")
            img.Quality = (uint)Settings.Current.ImageQuality;

        img.Write(output, Formats[ext]);
        return output;
    });

    /// <summary>Applies an edit and saves next to the source, keeping the original format.</summary>
    public static Task<string> EditAsync(string input, string suffix, Action<MagickImage> edit) => Task.Run(() =>
    {
        var ext = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        // Formats we can't write back (HEIC, SVG) are saved as PNG.
        if (ext is "heic" or "heif" or "svg") ext = "png";
        if (ext == "jpeg") ext = "jpg";
        if (ext == "tif") ext = "tiff";

        var output = Paths.Output(input, ext, suffix);
        using var img = Load(input);
        edit(img);
        img.Write(output);
        return output;
    });

    public static void Compress(MagickImage img)
    {
        img.Strip();
        img.Quality = 70;
        img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
    }

    public static Task<string> ToPdfAsync(IReadOnlyList<string> inputs) => Task.Run(() =>
    {
        var output = Paths.Output(inputs[0], "pdf", inputs.Count > 1 ? "merged" : null);
        using var pages = new MagickImageCollection();
        foreach (var path in inputs)
        {
            var img = Load(path);
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaAction.Remove);
            pages.Add(img);
        }
        pages.Write(output, MagickFormat.Pdf);
        return output;
    });

    /// <summary>One-page PDF of an image in the temp folder (used when merging images with PDFs).</summary>
    public static string ToTempPdf(string input)
    {
        var output = Path.Combine(Path.GetTempPath(), $"orange-{Guid.NewGuid():N}.pdf");
        using var img = Load(input);
        img.BackgroundColor = MagickColors.White;
        img.Alpha(AlphaAction.Remove);
        img.Write(output, MagickFormat.Pdf);
        return output;
    }

    private static MagickImage Load(string path)
    {
        var img = new MagickImage(path);
        img.AutoOrient(); // respect the camera's EXIF rotation
        return img;
    }
}
