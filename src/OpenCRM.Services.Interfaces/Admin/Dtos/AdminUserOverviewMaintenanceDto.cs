using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO обслуживания в обзоре пользователя. </summary>
public sealed class AdminUserOverviewMaintenanceDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; init; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; init; }

    /// <summary> Дата создания записи обслуживания. </summary>
    public DateTime DateTimeCreation { get; init; }

    /// <summary> Статус обслуживания. </summary>
    public MaintenanceStatus Status { get; init; }

    /// <summary> Название статуса обслуживания. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Тип обслуживания. </summary>
    public MaintenanceType Type { get; init; }

    /// <summary> Название типа обслуживания. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Название техники обслуживания. </summary>
    public string VehicleTitle { get; init; } = string.Empty;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; init; }

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; init; }

    /// <summary> Исполнитель обслуживания. </summary>
    public string? Performer { get; init; }

    /// <summary> Оплаченная сумма. </summary>
    public decimal PaidAmount { get; init; }

    /// <summary> Сумма затрат на материалы. </summary>
    public decimal MaterialCostAmount { get; init; }

    /// <summary> Сумма затрат на работы. </summary>
    public decimal LaborCostAmount { get; init; }

    /// <summary> Номер акта работ. </summary>
    public int? WorkActNumber { get; init; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; init; }
}
