using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для создания записи об обслуживании. </summary>
public class CreateMaintenanceDto
{
    /// <summary> Статус обслуживания. </summary>
    public required MaintenanceStatus Status { get; set; }

    /// <summary> Тип обслуживания. </summary>
    public required MaintenanceType Type { get; set; }

    /// <summary> Плановая дата обслуживания. </summary>
    public required DateTime? PlannedDate { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Дата завершения обслуживания. </summary>
    public required DateTime? CompletedDate { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public required string? Location { get; set; }

    /// <summary> Идентификатор сотрудника-исполнителя. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Наименование исполнителя работ. </summary>
    public required string? Performer { get; set; }

    /// <summary> Пробег транспортного средства. </summary>
    public required int? Mileage { get; set; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public required string? Comment { get; set; }

    /// <summary> Идентификатор клиента-покупателя. </summary>
    public Guid? BuyerClientId { get; set; }

    /// <summary> Признак наличия выбора покупателя. </summary>
    public bool HasBuyerSelection { get; set; }

    /// <summary> Сумма скидки. </summary>
    public decimal? DiscountAmount { get; set; }

    /// <summary> Оплаченная сумма. </summary>
    public decimal? PaidAmount { get; set; }

    /// <summary> Сумма стоимости работ. </summary>
    public decimal? LaborCostAmount { get; set; }

    /// <summary> Список строк работ и услуг. </summary>
    public required IEnumerable<CreateMaintenanceDetailDto> Details { get; set; }

    /// <summary> Идентификаторы связанных дефектов. </summary>
    public IEnumerable<Guid>? LinkedDefectIds { get; set; }
}

