using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт работы с документами обслуживания. </summary>
public interface IMaintenanceDocumentService
{
    /// <summary> Обеспечивает номер акта выполненных работ для обслуживания. </summary>
    Task<int> EnsureWorkActNumber(string userId, Guid organizationId, Guid maintenanceId);

    /// <summary> Регистрирует факт формирования документа по обслуживанию. </summary>
    Task TrackDocumentGenerated(string userId, Guid? organizationId, Guid maintenanceId, string title, string? description = null);

    /// <summary> Добавляет документ к обслуживанию. </summary>
    Task<MaintenanceDocumentDto> AddDocument(string userId, Guid? organizationId, Guid maintenanceId, CreateMaintenanceDocumentDto dto);

    /// <summary> Возвращает документы обслуживания. </summary>
    Task<IReadOnlyList<MaintenanceDocumentDto>> GetDocuments(string userId, Guid? organizationId, Guid maintenanceId);

    /// <summary> Возвращает документ для скачивания. </summary>
    Task<MaintenanceDocumentDownloadDto> GetDocumentForDownload(string userId, Guid? organizationId, Guid documentId);
}
