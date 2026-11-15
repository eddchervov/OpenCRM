using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Contracts.Photos;

/// <summary>Фотография обслуживания с этапом съёмки.</summary>
public sealed record MaintenancePhotoDto(Guid PhotoId, string Path, MaintenancePhotoStage Stage, DateTime CreatedAt, Guid? DetailId = null);
