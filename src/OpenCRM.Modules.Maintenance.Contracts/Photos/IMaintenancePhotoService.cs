using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Contracts.Photos;

/// <summary>Фото обслуживания и назначенных работ.</summary>
public interface IMaintenancePhotoService
{
    /// <summary> Возвращает фотографии обслуживания. </summary>
    Task<IReadOnlyList<MaintenancePhotoDto>> GetMaintenancePhotosAsync(string userId, Guid? organizationId, Guid maintenanceId);

    /// <summary> Возвращает фотографии указанной работы. </summary>
    Task<IReadOnlyList<MaintenancePhotoDto>> GetOperationPhotosAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId);

    /// <summary> Загружает фотографии для работы обслуживания. </summary>
    Task UploadOperationPhotosAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId,
        IEnumerable<(Stream Stream, string FileName)> files, MaintenancePhotoStage stage);

    /// <summary> Задаёт этап фотографии обслуживания. </summary>
    Task SetMaintenancePhotoStageAsync(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        Guid photoId,
        MaintenancePhotoStage stage);

    /// <summary> Возвращает идентификаторы фотографий обслуживания. </summary>
    Task<IEnumerable<Guid>> GetMaintenancePhotoIdsAsync(string userId, Guid maintenanceId);

    /// <summary> Возвращает идентификаторы фотографий обслуживания в контексте организации. </summary>
    Task<IEnumerable<Guid>> GetMaintenancePhotoIdsAsync(string userId, Guid? organizationId, Guid maintenanceId);

    /// <summary> Загружает фотографии обслуживания. </summary>
    Task UploadMaintenancePhotosAsync(
        string userId,
        Guid maintenanceId,
        IEnumerable<(Stream Stream, string FileName)> files,
        MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified);

    /// <summary> Загружает фотографии обслуживания в контексте организации. </summary>
    Task UploadMaintenancePhotosAsync(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        IEnumerable<(Stream Stream, string FileName)> files,
        MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified);

    /// <summary> Удаляет фотографию обслуживания. </summary>
    Task DeleteMaintenancePhotoAsync(string userId, Guid maintenanceId, Guid photoId);

    /// <summary> Удаляет фотографию обслуживания в контексте организации. </summary>
    Task DeleteMaintenancePhotoAsync(string userId, Guid? organizationId, Guid maintenanceId, Guid photoId);
}
