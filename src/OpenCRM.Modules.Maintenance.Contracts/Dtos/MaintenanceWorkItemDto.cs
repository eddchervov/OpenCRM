using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные работы из списка работ исполнителя. </summary>
public sealed class MaintenanceWorkItemDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Идентификатор строки работы. </summary>
    public Guid DetailId { get; set; }

    /// <summary> Наименование работы. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание работы. </summary>
    public string? Description { get; set; }

    /// <summary> Цена работы. </summary>
    public decimal? Price { get; set; }

    /// <summary> Количество. </summary>
    public int Quantity { get; set; }

    /// <summary> Наименование транспортного средства. </summary>
    public string? VehicleName { get; set; }

    /// <summary> Регистрационный знак транспортного средства. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; set; }

    /// <summary> Плановая дата обслуживания. </summary>
    public DateTime? PlannedDate { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановая дата окончания. </summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary> Заметки приёмки. </summary>
    public string? IntakeNotes { get; set; }

    /// <summary> Комплектность при приёмке. </summary>
    public string? IntakeCompleteness { get; set; }

    /// <summary> Видимые повреждения при приёмке. </summary>
    public string? IntakeVisibleDamage { get; set; }

    /// <summary> Комментарий к работе. </summary>
    public string? WorkComment { get; set; }

    /// <summary> Причина возврата на доработку. </summary>
    public string? ReworkReason { get; set; }

    /// <summary> Статус работы. </summary>
    public MaintenanceWorkStatus Status { get; set; }

    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; set; }

    /// <summary> Дата начала работы. </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary> Дата завершения работы. </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary> Активное время работы. </summary>
    public TimeSpan ActiveTime { get; set; }
}
