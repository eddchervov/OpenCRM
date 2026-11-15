using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Helpers;

/// <summary>Настройки безопасного чтения фотографий без декодера TIFF.</summary>
public static class ImageDecoding
{
    private static readonly Configuration s_configuration = new(
    [
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new GifConfigurationModule(),
        new BmpConfigurationModule(),
        new WebpConfigurationModule()
    ]);

    /// <summary>Читает фотографию в формате JPEG, PNG, GIF, BMP или WebP.</summary>
    public static Image Load(ReadOnlySpan<byte> imageData)
    {
        return Image.Load(new DecoderOptions { Configuration = ImageDecoding.s_configuration }, imageData);
    }

    /// <summary>Асинхронно читает фотографию в формате JPEG, PNG, GIF, BMP или WebP.</summary>
    public static Task<Image> LoadAsync(Stream imageStream)
    {
        return Image.LoadAsync(new DecoderOptions { Configuration = ImageDecoding.s_configuration }, imageStream);
    }

    /// <summary>Асинхронно определяет MIME-тип JPEG, PNG, GIF, BMP или WebP.</summary>
    public static async Task<string?> IdentifyMimeTypeAsync(Stream imageStream)
    {
        var imageInfo = await Image.IdentifyAsync(
            new DecoderOptions { Configuration = ImageDecoding.s_configuration }, imageStream);

        return imageInfo?.Metadata.DecodedImageFormat?.DefaultMimeType;
    }

    /// <summary>Определяет размеры фотографии в формате JPEG, PNG, GIF, BMP или WebP.</summary>
    public static (int Width, int Height) IdentifyDimensions(ReadOnlySpan<byte> imageData)
    {
        var imageInfo = Image.Identify(new DecoderOptions { Configuration = ImageDecoding.s_configuration }, imageData);

        return (imageInfo.Width, imageInfo.Height);
    }
}
