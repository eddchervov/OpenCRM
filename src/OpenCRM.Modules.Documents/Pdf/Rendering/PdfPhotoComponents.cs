using Helpers;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using InvalidImageContentException = SixLabors.ImageSharp.InvalidImageContentException;
using UnknownImageFormatException = SixLabors.ImageSharp.UnknownImageFormatException;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Общие компоненты отрисовки фотографий в PDF-документах. </summary>
internal sealed class PdfPhotoComponents
{
    /// <summary> Отрисовывает изображение с ограничением максимальных размеров. </summary>
    internal static void RenderImage(IContainer container, byte[] imageData, float maxWidth, float maxHeight)
    {
        try
        {
            using var image = ImageDecoding.Load(imageData);

            container
                .AlignCenter()
                .MaxWidth(maxWidth)
                .MaxHeight(maxHeight)
                .Image(imageData)
                .FitArea();
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            container
                .Background(Colors.Red.Lighten5)
                .Padding(10)
                .Text("Не удалось отобразить фотографию")
                .FontSize(9)
                .FontColor(PdfDocumentComponents.DangerColor);
        }
    }

    /// <summary> Отрисовывает карточку фотографии с номером по порядку. </summary>
    internal static void RenderPhotoCard(IContainer container, PhotoInfo? photo, int index)
    {
        if (photo?.ImageData is not { Length: > 0 } imageData)
        {
            return;
        }

        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(6);
            column.Item().Text(string.IsNullOrWhiteSpace(photo.Caption) ? $"Фото {index}" : $"{photo.Caption} · Фото {index}")
                .FontSize(9)
                .SemiBold()
                .FontColor(PdfDocumentComponents.MutedColor);

            RenderImage(column.Item(), imageData, 500, 330);
        });
    }

    /// <summary> Отрисовывает список фотографий. </summary>
    internal static void RenderPhotos(IContainer container, IEnumerable<PhotoInfo?> photos)
    {
        var photoList = photos.Where(photo => photo?.ImageData is { Length: > 0 }).ToList();
        if (photoList.Count == 0)
        {
            PdfDocumentComponents.RenderEmptyState(container, "Фотографии отсутствуют");
            return;
        }

        container.Column(column =>
        {
            column.Spacing(10);

            for (var i = 0; i < photoList.Count; i++)
            {
                var photo = photoList[i];
                column.Item().Element(c => RenderPhotoCard(c, photo, i + 1));
            }
        });
    }
}
