using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Формирует PDF-отчёт по истории техники. </summary>
internal sealed class VehicleHistoryPdfRenderer(string projectName)
{
    /// <summary> Формирует PDF-отчёт по технике, при необходимости с фотографиями. </summary>
    internal async Task<byte[]> GenerateVehiclePdfAsync(VehiclePdfModel model, bool includePhotos)
    {
        return await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    PdfDocumentComponents.ConfigurePage(page);

                    page.Header().Element(header =>
                        PdfDocumentComponents.RenderDocumentHeader(header, "История обслуживания техники", PdfDocumentComponents.GetVehicleTitle(model.Vehicle), model.GeneratedDate, projectName));

                    page.Content().PaddingTop(18).Column(column =>
                    {
                        column.Spacing(16);
                        column.Item().Element(c => PdfDocumentComponents.RenderGenerationPanel(c, model.GeneratedDate, includePhotos, projectName));

                        if (includePhotos && model.Vehicle?.PhotoData?.Length > 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderVehiclePhoto(c, model.Vehicle.PhotoData));
                        }

                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Техника", section => PdfDocumentComponents.RenderVehicleInfo(section, model.Vehicle)));
                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Сводка", section => PdfDocumentComponents.RenderStatistics(section, model.TotalMaintenanceCount, model.TotalSpent)));

                        if (model.MaintenanceHistory?.Count == 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderEmptyState(c, "История обслуживания отсутствует"));
                        }
                    });

                    page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                });

                if (model.MaintenanceHistory?.Count > 0)
                {
                    var index = 1;
                    foreach (var maintenance in model.MaintenanceHistory)
                    {
                        var recordNumber = index++;
                        container.Page(page =>
                        {
                            PdfDocumentComponents.ConfigurePage(page);

                            page.Header().Element(header =>
                                PdfDocumentComponents.RenderDocumentHeader(header, $"Запись обслуживания #{recordNumber}", PdfDocumentComponents.GetVehicleTitle(model.Vehicle), maintenance.Date, projectName));

                            page.Content().PaddingTop(18).Element(content =>
                                PdfDocumentComponents.RenderMaintenanceRecord(content, maintenance, model.Vehicle?.MileageUnit ?? VehicleMileageUnit.Kilometers, includePhotos));

                            page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                        });
                    }
                }
            });

            return document.GeneratePdf();
        });
    }
}
