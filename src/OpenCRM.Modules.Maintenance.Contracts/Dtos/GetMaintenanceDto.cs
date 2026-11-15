using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Краткие данные об обслуживании. </summary>
public class GetMaintenanceDto
{
    /// <summary> Идентификатор записи. </summary>
    public required Guid Id { get; set; }

    /// <summary> Состояние обслуживания. </summary>
    public required MaintenanceStatus Status { get; set; }

    /// <summary> Тип обслуживания. </summary>
    public required MaintenanceType Type { get; set; }

    /// <summary> Основная дата записи. </summary>
    public required DateTime Date { get; set; }

    /// <summary> Плановая дата обслуживания. </summary>
    public required DateTime? PlannedDate { get; set; }

    /// <summary> Плановое время начала обслуживания. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания обслуживания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Фактическая дата выполнения. </summary>
    public required DateTime? CompletedDate { get; set; }

    /// <summary>Время готовности к выдаче в UTC.</summary>
    public DateTime? ReadyForHandoverAt { get; set; }

    /// <summary>Время передачи техники клиенту в UTC.</summary>
    public DateTime? HandedOverAt { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public required string? Location { get; set; }

    /// <summary> Исполнитель обслуживания. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Наименование исполнителя работ. </summary>
    public required string? Performer { get; set; }

    /// <summary> Пробег на момент обслуживания. </summary>
    public required int? Mileage { get; set; }

    /// <summary> Комментарий пользователя. </summary>
    public required string? Comment { get; set; }

    /// <summary> Общая стоимость обслуживания. </summary>
    public required decimal? Price { get; set; }

    /// <summary> Сумма продажи до скидки.</summary>
    public decimal GrossAmount { get; set; }

    /// <summary> Скидка по записи/заказу.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Оплаченная сумма.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary> Остаток долга.</summary>
    public decimal DebtAmount { get; set; }

    /// <summary> Себестоимость списанных материалов.</summary>
    public decimal MaterialCostAmount { get; set; }

    /// <summary> Себестоимость труда или выплат исполнителям.</summary>
    public decimal LaborCostAmount { get; set; }

    /// <summary> Управленческая прибыль по записи.</summary>
    public decimal ProfitAmount { get; set; }

    /// <summary> Статус оплаты: unpaid, partial, paid или overpaid.</summary>
    public string PaymentStatus { get; set; } = "unpaid";

    /// <summary> Идентификаторы фотографий. </summary>
    public required IEnumerable<Guid> PhotoIds { get; set; }

    /// <summary> Список работ и услуг. </summary>
    public required IEnumerable<MaintenanceDetailDto> MaintenanceDetails { get; set; }

    /// <summary> Связанные неисправности. </summary>
    public required IEnumerable<MaintenanceLinkedDefectDto> LinkedDefects { get; set; }

}

