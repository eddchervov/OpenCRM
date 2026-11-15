using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Отрисовывает Word-отчёт по истории техники. </summary>
internal sealed class VehicleHistoryDocxRenderer(string projectName)
{
    /// <summary> Формирует Word-отчёт по истории техники, при необходимости с фотографиями. </summary>
    internal async Task<byte[]> GenerateVehicleHistoryDocxAsync(WordVehicleModel model, bool includePhotos = false)
    {
        using var memoryStream = new MemoryStream();

        using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            WordDocumentComponents.AddDocumentDefaults(mainPart);

            var body = mainPart.Document.AppendChild(new Body());
            WordDocumentComponents.AddPageHeader(body, "История обслуживания техники", WordDocumentComponents.GetVehicleTitle(model.Vehicle), model.GeneratedDate);
            WordDocumentComponents.AddGenerationPanel(body, model.GeneratedDate, includePhotos, projectName);

            if (includePhotos && model.Vehicle?.PhotoData?.Length > 0)
            {
                await WordPhotoComponents.AddPhotoWithCaption(body, model.Vehicle.PhotoData, "Фотография техники", wordDocument);
            }

            WordDocumentComponents.AddSectionHeading(body, "Техника");
            WordDocumentComponents.AddVehicleInfo(body, model.Vehicle);

            WordDocumentComponents.AddSectionHeading(body, "Сводка");
            WordDocumentComponents.AddStatistics(body, model.TotalMaintenanceCount, model.TotalSpent);

            WordDocumentComponents.AddSectionHeading(body, "История обслуживания");
            if (model.MaintenanceHistory?.Any() == true)
            {
                var index = 1;
                foreach (var maintenance in model.MaintenanceHistory)
                {
                    WordDocumentComponents.AddMaintenanceRecord(body, maintenance, index++, model.Vehicle?.MileageUnit ?? VehicleMileageUnit.Kilometers);

                    if (includePhotos && maintenance.Photos?.Any() == true)
                    {
                        await WordPhotoComponents.AddMaintenancePhotos(body, maintenance.Photos, wordDocument);
                    }
                }
            }
            else
            {
                WordDocumentComponents.AddEmptyState(body, "История обслуживания отсутствует");
            }

            WordDocumentComponents.AddFooter(body, $"Всего записей: {model.TotalMaintenanceCount}");
            WordDocumentComponents.AddSectionProperties(body);
            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

}
