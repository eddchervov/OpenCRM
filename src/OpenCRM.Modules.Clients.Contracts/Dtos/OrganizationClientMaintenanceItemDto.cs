using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Обслуживание в списке клиента организации. </summary>
public class OrganizationClientMaintenanceItemDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Названия услуг и работ обслуживания. </summary>
    public IReadOnlyList<string> ServiceNames { get; set; } = [];

    /// <summary> Сумма строк заказа до скидки. </summary>
    public decimal? GrossAmount { get; set; }

    /// <summary> Сумма строк заказа после скидки. </summary>
    public decimal? NetAmount { get; set; }

    /// <summary> Оплаченная сумма по действующим платежам. </summary>
    public decimal? PaidAmount { get; set; }

    /// <summary> Время готовности обслуживания к выдаче в UTC. </summary>
    public DateTime? ReadyForHandoverAt { get; set; }

    /// <summary> Время передачи техники клиенту в UTC. </summary>
    public DateTime? HandedOverAt { get; set; }

    /// <summary> Тип обслуживания. </summary>
    public MaintenanceType Type { get; set; }

    /// <summary> Статус обслуживания. </summary>
    public MaintenanceStatus Status { get; set; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; set; }

    /// <summary> Плановая дата обслуживания. </summary>
    public DateTime? PlannedDate { get; set; }

    /// <summary> Дата завершения обслуживания. </summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Пробег при обслуживании. </summary>
    public int? Mileage { get; set; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Ссылка на страницу обслуживания. </summary>
    public string DetailUrl { get; set; } = string.Empty;

    /// <summary> Ссылка на страницу транспортного средства. </summary>
    public string VehicleUrl { get; set; } = string.Empty;
}
