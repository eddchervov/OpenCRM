using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Отрисовывает Word-отчёт по обслуживанию. </summary>
internal sealed class MaintenanceReportDocxRenderer(string projectName)
{
    /// <summary> Формирует Word-отчёт по обслуживанию, при необходимости с фотографиями. </summary>
    internal async Task<byte[]> GenerateMaintenanceDocxAsync(WordMaintenanceModel model, bool includePhotos = false)
    {
        using var memoryStream = new MemoryStream();

        using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            WordDocumentComponents.AddDocumentDefaults(mainPart);

            var body = mainPart.Document.AppendChild(new Body());
            WordDocumentComponents.AddPageHeader(body, "Отчет об обслуживании техники", WordDocumentComponents.GetVehicleTitle(model.Vehicle), model.GeneratedDate);
            WordDocumentComponents.AddGenerationPanel(body, model.GeneratedDate, includePhotos, projectName);

            if (includePhotos && model.Vehicle?.PhotoData?.Length > 0)
            {
                await WordPhotoComponents.AddPhotoWithCaption(body, model.Vehicle.PhotoData, "Фотография техники", wordDocument);
            }

            WordDocumentComponents.AddSectionHeading(body, "Техника");
            WordDocumentComponents.AddVehicleInfo(body, model.Vehicle);

            WordDocumentComponents.AddSectionHeading(body, "Обслуживание");
            WordDocumentComponents.AddMaintenanceInfo(body, model.Maintenance, model.Vehicle?.MileageUnit ?? VehicleMileageUnit.Kilometers);

            if (model.Maintenance?.MaintenanceDetails?.Any() == true)
            {
                WordDocumentComponents.AddSectionHeading(body, "Работы и материалы");
                WordTableComponents.AddServicesTable(body, model.Maintenance.MaintenanceDetails);
            }

            if (!string.IsNullOrWhiteSpace(model.Maintenance?.Comment))
            {
                WordDocumentComponents.AddComment(body, model.Maintenance.Comment);
            }

            if (includePhotos && model.Maintenance?.Photos?.Any() == true)
            {
                await WordPhotoComponents.AddMaintenancePhotos(body, model.Maintenance.Photos, wordDocument);
            }

            WordDocumentComponents.AddFooter(body, model.CompanyInfo);
            WordDocumentComponents.AddSectionProperties(body);
            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

}
