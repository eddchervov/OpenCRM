using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Helpers;
using OpenCRM.Services.Interfaces.Word.Dtos;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using InvalidImageContentException = SixLabors.ImageSharp.InvalidImageContentException;
using UnknownImageFormatException = SixLabors.ImageSharp.UnknownImageFormatException;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Компоненты отрисовки фотографий в Word-документах. </summary>
internal sealed class WordPhotoComponents
{
    /// <summary> Добавляет изображение в документ. </summary>
    internal static void AddImage(Body body, byte[] imageData, WordprocessingDocument document)
    {
        try
        {
            using var image = ImageDecoding.Load(imageData);
            var mainPart = document.MainDocumentPart!;
            var imagePart = mainPart.AddImagePart(image.Metadata.DecodedImageFormat!.DefaultMimeType);

            using (var stream = new MemoryStream(imageData))
            {
                imagePart.FeedData(stream);
            }

            var imageId = mainPart.GetIdOfPart(imagePart);
            var drawingId = mainPart.Document
                .Descendants<DW.DocProperties>()
                .Select(properties => properties.Id!.Value)
                .DefaultIfEmpty()
                .Max() + 1;
            var drawing = CreateImageElement(imageId, image.Width, image.Height, drawingId);

            var paragraph = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(drawing)
            );
            body.AppendChild(paragraph);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            body.AppendChild(new Paragraph(new Run(
                new RunProperties(new Italic(), new Color { Val = WordDocumentComponents.MutedColor }),
                new Text("[Фотографию не удалось добавить в документ]")
            )));
        }
    }

    /// <summary> Добавляет фотографии обслуживания в документ. </summary>
    internal static async Task AddMaintenancePhotos(Body body, IEnumerable<WordPhotoInfo> photos, WordprocessingDocument document)
    {
        var photoList = photos.Where(x => x?.ImageData?.Length > 0).ToList();
        if (photoList.Count == 0) return;

        WordDocumentComponents.AddSectionHeading(body, $"Фотографии ({photoList.Count})");

        for (var i = 0; i < photoList.Count; i++)
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "80", After = "60" }),
                new Run(
                    new RunProperties(new Bold(), new Color { Val = WordDocumentComponents.MutedColor }),
                    new Text($"Фото {i + 1}")
                )
            ));

            AddImage(body, photoList[i].ImageData, document);
            WordDocumentComponents.AddSpacer(body, 120);
        }

        await Task.CompletedTask;
    }

    /// <summary> Создаёт элемент Drawing для вставки изображения. </summary>
    internal static Drawing CreateImageElement(string relationshipId, int imageWidth, int imageHeight, uint drawingId)
    {
        const long emuPerCm = 360000L;
        const long maxWidthEmu = emuPerCm * 15;
        const long maxHeightEmu = emuPerCm * 9;
        var ratio = Math.Min(maxWidthEmu / (double)imageWidth, maxHeightEmu / (double)imageHeight);
        var widthEmu = (long)(imageWidth * ratio);
        var heightEmu = (long)(imageHeight * ratio);

        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent
                {
                    LeftEdge = 0L,
                    TopEdge = 0L,
                    RightEdge = 0L,
                    BottomEdge = 0L
                },
                new DW.DocProperties
                {
                    Id = drawingId,
                    Name = "Photo"
                },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }
                ),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties
                                {
                                    Id = drawingId,
                                    Name = "Image"
                                },
                                new PIC.NonVisualPictureDrawingProperties()
                            ),
                            new PIC.BlipFill(
                                new A.Blip
                                {
                                    Embed = relationshipId,
                                    CompressionState = A.BlipCompressionValues.Print
                                },
                                new A.Stretch(new A.FillRectangle())
                            ),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }
                                ),
                                new A.PresetGeometry(new A.AdjustValueList())
                                {
                                    Preset = A.ShapeTypeValues.Rectangle
                                }
                            )
                        )
                    )
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
                    }
                )
            )
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            }
        );
    }

    /// <summary> Добавляет фотографию с подписью в документ. </summary>
    internal static async Task AddPhotoWithCaption(Body body, byte[] imageData, string caption, WordprocessingDocument document)
    {
        WordDocumentComponents.AddSectionHeading(body, caption);
        AddImage(body, imageData, document);
        await Task.CompletedTask;
        WordDocumentComponents.AddSpacer(body, 120);
    }
}
