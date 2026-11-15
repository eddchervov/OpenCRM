using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Запись показания счётчика техники. </summary>
public class VehicleCounterEntry
{
    /// <summary> Идентификатор записи показания. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Техника, к которой относится показание. </summary>
    public Vehicle Vehicle { get; set; } = default!;

    /// <summary> Идентификатор пользователя, внёсшего показание. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Пользователь, внёсший показание. </summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary> Идентификатор записи об обслуживании, связанной с показанием. </summary>
    public Guid? MaintenanceId { get; set; }

    /// <summary> Запись об обслуживании, связанная с показанием. </summary>
    public Maintenance? Maintenance { get; set; }

    /// <summary> Значение показания счётчика. </summary>
    public int Value { get; set; }

    /// <summary> Дата и время регистрации показания. </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary> Комментарий к показанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Тип источника показания счётчика. </summary>
    public string SourceType { get; set; } = VehicleCounterSourceTypes.Manual;

    /// <summary> Признак пометки показания удалённым. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Дата пометки показания удалённым. </summary>
    public DateTime? DeletionDate { get; set; }

    /// <summary> Помечает показание счётчика удалённым с указанной датой. </summary>
    public void Delete(DateTime deletionDate)
    {
        IsDeleted = true;
        DeletionDate = deletionDate;
    }
}
