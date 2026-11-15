using System.Globalization;
using OpenCRM.Domain;
using OpenCRM.Modules.Documents.Pdf.Rendering;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace OpenCRM.Modules.Documents.Tests.Integration;

public sealed class IntakeDamageMarkerRenderingTests
{
    [Theory]
    [InlineData(1, 0.5, 0.5, 500)]
    [InlineData(12, 0.5, 0.5, 500)]
    [InlineData(105, 0.5, 0.5, 500)]
    [InlineData(105, 0.5, 0.5, 250)]
    [InlineData(1, 0, 0, 500)]
    [InlineData(12, 1, 0, 250)]
    [InlineData(105, 1, 1, 500)]
    [InlineData(1, 0, 1, 250)]
    public void NumberIsVisibleAndStaysWithinTheDiagram(int number, double x, double y, int width)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var height = width * 971f / 1619;
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(width + 20, height + 20);
            page.Margin(10);
            page.Content().Element(content => MaintenanceIntakeDamageDiagram.Render(
                content, MaintenanceIntakeDiagramVersions.SedanV1, "front",
                [new MaintenanceIntakeDamagePdfInfo { Number = number, X = x, Y = y }]));
        }));

        var pdfBytes = document.GeneratePdf();
        using var pdf = PdfDocument.Open(pdfBytes);
        var page = Assert.Single(pdf.GetPages());
        Assert.Equal(number.ToString(CultureInfo.InvariantCulture), page.Text);
        Assert.All(page.Letters, letter =>
        {
            Assert.InRange(letter.GlyphRectangle.Left, 9.9, width + 10.1);
            Assert.InRange(letter.GlyphRectangle.Right, 9.9, width + 10.1);
            Assert.InRange(letter.GlyphRectangle.Bottom, 9.9, height + 10.1);
            Assert.InRange(letter.GlyphRectangle.Top, 9.9, height + 10.1);
        });

        var imageBytes = Assert.Single(document.GenerateImages(new ImageGenerationSettings { RasterDpi = 72 }));
        using var bitmap = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
        var markerSize = 22 * width / 500d;
        var centerX = 10 + Math.Clamp(x * width - markerSize / 2, 0, width - markerSize) + markerSize / 2;
        var centerY = 10 + Math.Clamp(y * height - markerSize / 2, 0, height - markerSize) + markerSize / 2;
        var radius = Math.Max(2, (int)(6 * width / 500d));
        var darkPixels = 0;
        for (var row = (int)centerY - radius; row <= (int)centerY + radius; row++)
        {
            for (var column = (int)centerX - radius; column <= (int)centerX + radius; column++)
            {
                var pixel = bitmap[column, row];
                if (pixel.R < 128 && pixel.G < 128 && pixel.B < 128)
                {
                    darkPixels++;
                }
            }
        }

        Assert.True(darkPixels >= 2, $"Внутри метки № {number} отсутствует видимый номер: {darkPixels} тёмных пикселей.");
    }
}
