using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using System.Linq;

namespace Helpers;

#nullable disable

/// <summary> Утилиты для обработки и масштабирования изображений. </summary>
public class ImageUtils
{
    /// <summary> Масштабирует изображение до заданных ширины и высоты. </summary>
    public static Image ScaleToSize(Image imgPhoto, int width, int height)
    {
        return ScaleToSize(imgPhoto, width, height, false);
    }

    /// <summary> Масштабирует изображение с управлением возможностью увеличения. </summary>
    public static Image ScaleToSize(Image imgPhoto, int width, int height, bool enlarge)
    {
        int sourceWidth = imgPhoto.Width;
        int sourceHeight = imgPhoto.Height;
        double parW = width / (double)sourceWidth;
        double parH = height / (double)sourceHeight;

        int destWidth;
        int destHeight;

        if (parW < parH)
        {
            destWidth = width;
            destHeight = (int)(parW * sourceHeight);
        }
        else
        {
            destWidth = (int)(parH * sourceWidth);
            destHeight = height;
        }

        if (!enlarge)
        {
            if (destHeight > sourceHeight && destWidth > sourceWidth)
            {
                // Возвращаем оригинальное изображение (ImageSharp иммутабелен)
                return imgPhoto;
            }
        }

        return imgPhoto.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(destWidth, destHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));
    }

    // Метод для изменения размера с сохранением пропорций (максимальные размеры)
    /// <summary> Уменьшает изображение до максимальных ширины и высоты. </summary>
    public static Image ResizeToMaxDimensions(Image image, int maxWidth, int maxHeight)
    {
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        // Вычисляем новые размеры с сохранением пропорций
        double ratioX = (double)maxWidth / originalWidth;
        double ratioY = (double)maxHeight / originalHeight;
        double ratio = Math.Min(ratioX, ratioY);

        int newWidth = (int)(originalWidth * ratio);
        int newHeight = (int)(originalHeight * ratio);

        return image.Clone(x => x.Resize(newWidth, newHeight));
    }

    // Альтернативный вариант - изменение по максимальной ширине
    /// <summary> Уменьшает изображение до максимальной ширины. </summary>
    public static Image ResizeToMaxWidth(Image image, int maxWidth)
    {
        if (image.Width <= maxWidth)
            return image;

        double ratio = (double)maxWidth / image.Width;
        int newHeight = (int)(image.Height * ratio);

        return image.Clone(x => x.Resize(maxWidth, newHeight));
    }

    // Или для изменения по максимальной высоте
    /// <summary> Уменьшает изображение до максимальной высоты. </summary>
    public static Image ResizeToMaxHeight(Image image, int maxHeight)
    {
        if (image.Height <= maxHeight)
            return image;

        double ratio = (double)maxHeight / image.Height;
        int newWidth = (int)(image.Width * ratio);

        return image.Clone(x => x.Resize(newWidth, maxHeight));
    }

    /// <summary> Подбирает кодировщик изображений по MIME-типу. </summary>
    public static IImageEncoder? GetEncoder(string mimeType)
    {
        return mimeType switch
        {
            "image/jpeg" => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(),
            "image/png" => new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
            "image/gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
            "image/bmp" => new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder(),
            "image/webp" => new SixLabors.ImageSharp.Formats.Webp.WebpEncoder(),
            _ => null
        };
    }
}