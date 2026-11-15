using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Services.Interfaces.Word;

/// <summary> Контракт генерации DOCX-документов по данным обслуживания и транспортного средства. </summary>
public interface IDocxGeneratorService
{
    /// <summary> Формирует DOCX-документ по обслуживанию и возвращает его содержимое в виде байтов. </summary>
    Task<byte[]> GenerateMaintenanceDocxAsync(WordMaintenanceModel model, bool includePhotos = false);

    /// <summary> Формирует DOCX-отчёт по истории обслуживания транспортного средства. </summary>
    Task<byte[]> GenerateVehicleHistoryDocxAsync(WordVehicleModel model, bool includePhotos = false);

    /// <summary> Формирует DOCX-акт работ по обслуживанию. </summary>
    Task<byte[]> GenerateMaintenanceWorkActDocxAsync(WordMaintenanceWorkActModel model);

    /// <summary> Формирует DOCX-ордер на работы по обслуживанию. </summary>
    Task<byte[]> GenerateMaintenanceWorkOrderDocxAsync(WordMaintenanceWorkActModel model);

    /// <summary> Формирует DOCX-документ по обслуживанию на основе сценария. </summary>
    Task<byte[]> GenerateScenarioMaintenanceDocumentDocxAsync(WordScenarioMaintenanceDocumentModel model);
}

