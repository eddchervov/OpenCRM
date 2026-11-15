using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

/// <summary> Фотография обслуживания. </summary>
public class MaintenancePhoto : BaseMedia
{
    /// <summary> Идентификатор обслуживания. </summary>
    /// <summary>Этап съёмки; старые фотографии остаются без категории.</summary>
    public MaintenancePhotoStage Stage { get; set; }

    public Guid MaintenanceId { get; set; }
    public Guid? DetailId { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Обслуживание фотографии. </summary>
    public virtual Maintenance Maintenance { get; set; } = default!;

    /// <summary> Техника фотографии. </summary>
    public virtual Vehicle Vehicle { get; set; } = default!;
}
