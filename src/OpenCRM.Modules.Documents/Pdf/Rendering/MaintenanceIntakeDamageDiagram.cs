using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using SixLabors.ImageSharp;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Печатает отдельные ракурсы по общей с клиентом геометрии. </summary>
internal static class MaintenanceIntakeDamageDiagram
{
    private static readonly Lazy<DiagramDefinition> s_definition = new(MaintenanceIntakeDamageDiagram.LoadDefinition);
    private static readonly IReadOnlyDictionary<string, Lazy<string>> s_images = new[] { "front", "rear", "left", "right" }
        .ToDictionary(view => view, view => new Lazy<string>(() => MaintenanceIntakeDamageDiagram.LoadImage(view)));

    internal static void Render(
        QuestPDF.Infrastructure.IContainer container,
        string? diagramVersion,
        string viewCode,
        IEnumerable<MaintenanceIntakeDamagePdfInfo> damages)
    {
        var markers = damages.ToArray();
        var svg = MaintenanceIntakeDamageDiagram.Create(diagramVersion, viewCode, markers);
        var definition = MaintenanceIntakeDamageDiagram.s_definition.Value;
        var scale = definition.Width / 500f;
        var markerSize = 22 * scale;

        container.Layers(layers =>
        {
            layers.PrimaryLayer().Svg(svg);

            // Нативный текст PDF не зависит от доступности системных шрифтов внутри SVG.
            layers.Layer().ScaleToFit().Width(definition.Width).Height(definition.Height).Layers(labels =>
            {
                labels.PrimaryLayer().Extend();
                foreach (var damage in markers)
                {
                    // Номер у края остаётся внутри ракурса, координаты самого повреждения не меняются.
                    var labelX = Math.Clamp((float)(damage.X * definition.Width) - markerSize / 2, 0, definition.Width - markerSize);
                    var labelY = Math.Clamp((float)(damage.Y * definition.Height) - markerSize / 2, 0, definition.Height - markerSize);

                    labels.Layer().Unconstrained()
                        .TranslateX(labelX).TranslateY(labelY)
                        .Width(markerSize).Height(markerSize).AlignCenter().AlignMiddle()
                        .Text(damage.Number.ToString(CultureInfo.InvariantCulture))
                        .FontFamily(PdfDocumentComponents.FontFamily).FontSize(10 * scale).Bold().FontColor("#000000");
                }
            });
        });
    }

    internal static string Create(string? diagramVersion, string viewCode, IEnumerable<MaintenanceIntakeDamagePdfInfo> damages)
    {
        if (!MaintenanceIntakeDiagramVersions.IsSupported(diagramVersion))
        {
            throw new InvalidOperationException("Сохранённая версия схемы осмотра недоступна. Формирование акта остановлено.");
        }

        if (!MaintenanceIntakeDamageDiagram.s_images.TryGetValue(viewCode, out var image))
        {
            throw new InvalidOperationException("Неизвестный ракурс схемы осмотра.");
        }

        var definition = MaintenanceIntakeDamageDiagram.s_definition.Value;
        var scale = definition.Width / 500d;
        var svg = new StringBuilder($"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="{definition.Width}" height="{definition.Height}" viewBox="0 0 {definition.Width} {definition.Height}" overflow="hidden">
              <image x="0" y="0" width="{definition.Width}" height="{definition.Height}" preserveAspectRatio="none"
                     xlink:href="data:image/png;base64,{image.Value}"/>
            """);

        foreach (var damage in damages)
        {
            if (!double.IsFinite(damage.X) || !double.IsFinite(damage.Y) || damage.X is < 0 or > 1 || damage.Y is < 0 or > 1)
            {
                throw new InvalidOperationException("Координаты повреждения находятся за пределами схемы.");
            }

            var x = damage.X * definition.Width;
            var y = damage.Y * definition.Height;
            svg.Append(FormattableString.Invariant($"""
                <circle cx="{x}" cy="{y}" r="{11 * scale}" fill="white" stroke="#202631" stroke-width="{1.5 * scale}"/>
                """));
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    private static DiagramDefinition LoadDefinition()
    {
        using var stream = typeof(MaintenanceIntakeDamageDiagram).Assembly
            .GetManifestResourceStream("OpenCRM.Intake.SedanV1.diagram.json")
            ?? throw new InvalidOperationException("Не найдено описание схемы осмотра.");
        var definition = JsonSerializer.Deserialize<DiagramDefinition>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (definition is not { Version: "sedan-v1", Width: > 0, Height: > 0, Views: not null }
            || definition.Views.Count != 4 || MaintenanceIntakeDamageDiagram.s_images.Keys.Any(view => !definition.Views.ContainsKey(view)))
        {
            throw new InvalidOperationException("Описание схемы осмотра повреждено.");
        }

        return definition;
    }

    private static string LoadImage(string view)
    {
        var definition = MaintenanceIntakeDamageDiagram.s_definition.Value;
        var projection = definition.Views[view];
        using var stream = typeof(MaintenanceIntakeDamageDiagram).Assembly
            .GetManifestResourceStream("OpenCRM.Intake.SedanV1." + projection.File)
            ?? throw new InvalidOperationException("Не найден рисунок ракурса схемы осмотра.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var dimensions = ImageDecoding.IdentifyDimensions(bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != projection.Sha256
            || dimensions.Width != definition.Width
            || dimensions.Height != definition.Height)
        {
            throw new InvalidOperationException("Рисунок ракурса схемы осмотра не соответствует закреплённой версии.");
        }

        return Convert.ToBase64String(bytes);
    }

    private sealed record DiagramDefinition(string Version, int Width, int Height, Dictionary<string, ProjectionDefinition> Views);

    private sealed record ProjectionDefinition(string File, string Sha256);
}
