using System.Globalization;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Xml.Linq;
using OpenCRM.Domain;
using OpenCRM.Modules.Documents.Pdf.Rendering;
using OpenCRM.Services.Interfaces.Pdf.Dtos;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class MaintenanceIntakeDiagramVersionTests
{
    [Theory]
    [InlineData("front", "19890012EF39414DA1A5D20B2DC7AAE16A808A3E27C96937918B236F55032627")]
    [InlineData("rear", "A0AEC36DF2F7510D320273D7A0DDBEE68C0CFD882AE05C0F63E9964BA9D1B499")]
    [InlineData("left", "583C3C35119AB0380FC4D8306F10DB2E64D4511A21BDF70341BF878F7091B5B9")]
    [InlineData("right", "E988B5C39D75E6DC41867C0B41D6206F8E7E44D9180C85EB5A173D2DB07E5E34")]
    public void SedanV1UsesImmutableSeparateProjectionWithExactGeometry(string view, string expectedHash)
    {
        var document = XDocument.Parse(MaintenanceIntakeDamageDiagram.Create(MaintenanceIntakeDiagramVersions.SedanV1, view, []));
        XNamespace ns = "http://www.w3.org/2000/svg";
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        var image = Assert.Single(document.Descendants(ns + "image"));
        Assert.Equal("0 0 1619 971", document.Root!.Attribute("viewBox")!.Value);
        Assert.Equal("0", image.Attribute("x")!.Value);
        Assert.Equal("0", image.Attribute("y")!.Value);
        Assert.Equal("1619", image.Attribute("width")!.Value);
        Assert.Equal("971", image.Attribute("height")!.Value);
        Assert.Empty(document.Descendants(ns + "clipPath"));
        var bytes = Convert.FromBase64String(image.Attribute(xlink + "href")!.Value.Split(',')[1]);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        using var bitmap = Image.Load<Rgba32>(bytes);
        Assert.Equal(1619, bitmap.Width);
        Assert.Equal(971, bitmap.Height);
        if (view is "left" or "right")
        {
            bitmap.ProcessPixelRows(accessor =>
            {
                for (var row = 0; row < 20; row++)
                {
                    foreach (var pixel in accessor.GetRowSpan(row))
                    {
                        Assert.Equal(0, pixel.A);
                    }
                }
            });
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1619, 971)]
    [InlineData(0.5, 0.5, 809.5, 485.5)]
    [InlineData(0.62, 0.45, 1003.78, 436.95)]
    public void SedanV1KeepsExactNormalizedPositionsIncludingViewportEdges(double x, double y, double expectedX, double expectedY)
    {
        var document = XDocument.Parse(MaintenanceIntakeDamageDiagram.Create(MaintenanceIntakeDiagramVersions.SedanV1, "rear",
            [new MaintenanceIntakeDamagePdfInfo { Number = 1, X = x, Y = y }]));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var marker = Assert.Single(document.Descendants(ns + "circle"));
        Assert.Equal(expectedX, double.Parse(marker.Attribute("cx")!.Value, CultureInfo.InvariantCulture), 8);
        Assert.Equal(expectedY, double.Parse(marker.Attribute("cy")!.Value, CultureInfo.InvariantCulture), 8);
    }
}
