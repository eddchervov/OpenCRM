using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Запись об обслуживании техники. </summary>
public class Maintenance
{
    /// <summary>Код валюты денежных значений обслуживания на момент создания.</summary>
    public string CurrencyCode { get; set; } = CurrencyCodes.Rub;
    /// <summary> Идентификатор записи. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Версия строки PostgreSQL для защиты от одновременного изменения.</summary>
    public uint Version { get; set; }

    public bool VisitReminderEnabled { get; set; } = true;

    /// <summary> Состояние обслуживания. </summary>
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Completed;

    /// <summary> Тип обслуживания. </summary>
    public MaintenanceType Type { get; set; }

    /// <summary> Основная дата записи для сортировки и обратной совместимости. </summary>
    public DateTime Date { get; set; }

    /// <summary> Плановая дата обслуживания. </summary>
    public DateTime? PlannedDate { get; set; }

    /// <summary> Плановое время начала обслуживания. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания обслуживания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Фактическая дата выполнения обслуживания. </summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Время готовности к выдаче в UTC; неизвестно для старых выполненных записей.</summary>
    public DateTime? ReadyForHandoverAt { get; set; }

    /// <summary>Время передачи техники клиенту в UTC.</summary>
    public DateTime? HandedOverAt { get; set; }

    /// <summary> Идентификатор организации, в рамках которой присвоен номер акта. </summary>
    public Guid? WorkActOrganizationId { get; set; }

    /// <summary> Номер акта выполненных работ в рамках организации. </summary>
    public int? WorkActNumber { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; set; }

    /// <summary> Исполнитель обслуживания. </summary>
    public Guid? EmployeeId { get; set; }
    /// <summary> Исполнитель обслуживания. </summary>
    public string? Performer { get; set; }

    /// <summary> Пробег на момент обслуживания. </summary>
    public int? Mileage { get; set; }

    /// <summary>Зафиксированное при приёмке состояние автомобиля и замечания.</summary>
    public string? IntakeNotes { get; set; }

    /// <summary>Пробег, зафиксированный при приёмке автомобиля.</summary>
    public int? IntakeMileage { get; set; }

    /// <summary>Комплектность автомобиля при приёмке.</summary>
    public string? IntakeCompleteness { get; set; }

    /// <summary>Заметные повреждения при приёмке.</summary>
    public string? IntakeVisibleDamage { get; set; }

    /// <summary>Время первой фиксации приёмки в UTC.</summary>
    public DateTime? IntakeRecordedAt { get; set; }

    /// <summary> Версия схемы, неизменяемая после первой фиксации осмотра. </summary>
    public string? IntakeDiagramVersion { get; set; }

    /// <summary> Комментарий пользователя. </summary>
    public string? Comment { get; set; }

    /// <summary> Скидка по записи/заказу. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Оплаченная сумма по записи/заказу. </summary>
    public decimal PaidAmount { get; set; }

    /// <summary> Учетная себестоимость списанных материалов. </summary>
    public decimal MaterialCostAmount { get; set; }

    /// <summary> Себестоимость труда или выплат исполнителям, если ведется управленческий учет. </summary>
    public decimal LaborCostAmount { get; set; }

    /// <summary> Дата создания записи. </summary>
    public DateTime DateTimeCreation { get; set; }

    /// <summary> Идентификатор владельца записи. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Владелец записи. </summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Техника, к которой относится обслуживание. </summary>
    public Vehicle Vehicle { get; set; } = default!;

    /// <summary> Идентификатор клиента-покупателя. </summary>
    public Guid? BuyerClientId { get; set; }

    /// <summary> Наименование покупателя. </summary>
    public string? BuyerName { get; set; }
    /// <summary> Код типа покупателя. </summary>
    public string? BuyerPartyTypeCode { get; set; }
    /// <summary> ИНН покупателя. </summary>
    public string? BuyerInn { get; set; }
    /// <summary> КПП покупателя. </summary>
    public string? BuyerKpp { get; set; }
    /// <summary> Юридический адрес покупателя. </summary>
    public string? BuyerLegalAddress { get; set; }

    /// <summary> Список строк работ записи об обслуживании. </summary>
    private List<MaintenanceDetail> _details = [];

    /// <summary> Список работ и услуг в сохраненном порядке. </summary>
    public List<MaintenanceDetail> Details
    {
        get
        {
            for (var index = 1; index < this._details.Count; index++)
            {
                if (this._details[index - 1].Position <= this._details[index].Position)
                {
                    continue;
                }

                // Восстанавливаем сохранённый порядок в той же коллекции, сохраняя экземпляры строк.
                var orderedDetails = this._details.OrderBy(detail => detail.Position).ToArray();
                for (var orderedIndex = 0; orderedIndex < orderedDetails.Length; orderedIndex++)
                {
                    this._details[orderedIndex] = orderedDetails[orderedIndex];
                }

                break;
            }

            return this._details;
        }
        set
        {
            this._details = value ?? [];
            Maintenance.SetDetailPositions(this._details);
        }
    }

    /// <summary> Заменяет строки работ, сохраняя существующие по DetailId и перенумеровывая позиции. </summary>
    public void ReplaceDetails(IEnumerable<MaintenanceDetail> details)
    {
        var existingDetails = this._details.ToDictionary(detail => detail.DetailId);
        var replacement = new List<MaintenanceDetail>();
        var position = 0;
        foreach (var detail in details)
        {
            if (existingDetails.TryGetValue(detail.DetailId, out var existingDetail))
            {
                // Обновляем существующую строку, чтобы сохранить её идентификатор и отслеживаемый экземпляр.
                Maintenance.CopyDetailValues(detail, existingDetail);
                existingDetail.Position = position++;
                replacement.Add(existingDetail);
                continue;
            }

            detail.Position = position++;
            replacement.Add(detail);
        }

        this._details.Clear();
        this._details.AddRange(replacement);
    }

    /// <summary> Присваивает строкам последовательные порядковые номера. </summary>
    private static void SetDetailPositions(IEnumerable<MaintenanceDetail> details)
    {
        var position = 0;
        foreach (var detail in details)
        {
            detail.Position = position++;
        }
    }

    /// <summary> Копирует значения строки-источника в существующую строку-приёмник. </summary>
    private static void CopyDetailValues(MaintenanceDetail source, MaintenanceDetail target)
    {
        target.Name = source.Name;
        target.Description = source.Description;
        target.Price = source.Price;
        target.VatRateCode = source.VatRateCode;
        target.EmployeeId = source.EmployeeId;
        target.EmployeeName = source.EmployeeName;
        target.CatalogItemId = source.CatalogItemId;
        target.Quantity = source.Quantity;
        target.MaterialCatalogItemId = source.MaterialCatalogItemId;
        target.MaterialName = source.MaterialName;
        target.MaterialQuantity = source.MaterialQuantity;
        target.MaterialUnit = source.MaterialUnit;
        target.MaterialUnitCost = source.MaterialUnitCost;
        target.MaterialTotalCost = source.MaterialTotalCost;
    }

    /// <summary> Записи счетчика, созданные из обслуживания. </summary>
    public virtual ICollection<VehicleCounterEntry> CounterEntries { get; set; } = new HashSet<VehicleCounterEntry>();

    /// <summary> Связанные неисправности, которые устраняет обслуживание. </summary>
    public virtual ICollection<MaintenanceDefect> LinkedDefects { get; set; } = new HashSet<MaintenanceDefect>();

    /// <summary> История событий обслуживания. </summary>
    public virtual ICollection<MaintenanceHistoryEvent> HistoryEvents { get; set; } = new HashSet<MaintenanceHistoryEvent>();

    /// <summary> Сформированные документы обслуживания. </summary>
    public virtual ICollection<MaintenanceDocument> Documents { get; set; } = new HashSet<MaintenanceDocument>();

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }
}
