using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Рендерер PDF-отчётов по обслуживанию. </summary>
internal sealed class MaintenanceReportPdfRenderer(string projectName)
{
    /// <summary> Генерирует PDF-отчёт по обслуживанию. </summary>
    internal async Task<byte[]> GenerateMaintenancePdfAsync(MaintenancePdfModel model, bool includePhotos)
    {
        return await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    PdfDocumentComponents.ConfigurePage(page);

                    page.Header().Element(header =>
                        PdfDocumentComponents.RenderDocumentHeader(header, "Отчет об обслуживании техники", PdfDocumentComponents.GetVehicleTitle(model.Vehicle), model.GeneratedDate, projectName));

                    page.Content().PaddingTop(18).Column(column =>
                    {
                        column.Spacing(16);
                        column.Item().Element(c => PdfDocumentComponents.RenderGenerationPanel(c, model.GeneratedDate, includePhotos, projectName));

                        if (includePhotos && model.Vehicle?.PhotoData is { Length: > 0 } photoData)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderVehiclePhoto(c, photoData));
                        }

                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Техника", section => PdfDocumentComponents.RenderVehicleInfo(section, model.Vehicle)));
                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Обслуживание", section => PdfDocumentComponents.RenderMaintenanceInfo(section, model.Maintenance, model.Vehicle?.MileageUnit ?? VehicleMileageUnit.Kilometers)));

                        if (model.Maintenance?.MaintenanceDetails?.Count > 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Работы и материалы", section => PdfTableComponents.RenderServices(section, model.Maintenance.MaintenanceDetails, currencyCode: model.Maintenance.CurrencyCode)));
                        }

                        if (!string.IsNullOrWhiteSpace(model.Maintenance?.IntakeNotes))
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Приёмка", section => PdfTableComponents.RenderComment(section, model.Maintenance.IntakeNotes)));
                        }

                        if (!string.IsNullOrWhiteSpace(model.Maintenance?.Comment))
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Комментарий", section => PdfTableComponents.RenderComment(section, model.Maintenance.Comment)));
                        }

                        if (includePhotos && model.Maintenance?.Photos?.Any() == true)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, $"Фотографии ({model.Maintenance.PhotosCount})", section => PdfPhotoComponents.RenderPhotos(section, model.Maintenance.Photos)));
                        }
                    });

                    page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                });
            });

            return document.GeneratePdf();
        });
    }
}
