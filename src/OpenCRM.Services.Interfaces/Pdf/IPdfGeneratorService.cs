using OpenCRM.Services.Interfaces.Pdf.Dtos;

namespace OpenCRM.Services.Interfaces.Pdf;

/// <summary> Контракт сервиса генерации PDF-документов. </summary>
public interface IPdfGeneratorService
{
    /// <summary> Генерирует PDF-отчёт по обслуживанию. </summary>
    Task<byte[]> GenerateMaintenancePdfAsync(MaintenancePdfModel model, bool includePhotos);

    /// <summary> Генерирует PDF акта приёмки автомобиля. </summary>
    Task<byte[]> GenerateMaintenanceIntakePdfAsync(MaintenanceIntakePdfModel model);

    /// <summary> Генерирует PDF-отчёт по автомобилю. </summary>
    Task<byte[]> GenerateVehiclePdfAsync(VehiclePdfModel model, bool includePhotos);

    /// <summary> Генерирует PDF акта выполненных работ. </summary>
    Task<byte[]> GenerateMaintenanceWorkActPdfAsync(MaintenanceWorkActPdfModel model);

    /// <summary> Генерирует PDF наряда на работы. </summary>
    Task<byte[]> GenerateMaintenanceWorkOrderPdfAsync(MaintenanceWorkActPdfModel model);

    /// <summary> Генерирует PDF документа по шаблону сценария. </summary>
    Task<byte[]> GenerateScenarioMaintenanceDocumentPdfAsync(ScenarioMaintenanceDocumentPdfModel model);
}

