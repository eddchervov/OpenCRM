namespace OpenCRM.Domain;

/// <summary>
/// Хранит готовые финансовые показатели одного обслуживания для быстрого построения отчётов.
/// Контекст C# обновляет эту восстанавливаемую проекцию в транзакции исходных заказов и работ.
/// Источником данных остаются заказ, оплаты и строки работ; триггеры для обновления не используются.
/// </summary>
public sealed class MaintenanceFinancialTotal
{
    /// <summary>Код валюты финансового итога обслуживания.</summary>
    public string CurrencyCode { get; set; } = CurrencyCodes.Rub;
    public Guid MaintenanceId { get; set; }

    /// <summary> Сумма цен работ до скидки; отсутствующая цена считается нулевой. </summary>
    public decimal GrossAmount { get; set; }

    public Guid? OrganizationId { get; set; }

    public DateTime EffectiveDate { get; set; }

    /// <summary> Номер дня UTC от 01.01.0001 для устойчивой сортировки списка заказов. </summary>
    public int UtcDay { get; set; }

    /// <summary> Заказ и автомобиль не удалены, автомобиль относится к организации. </summary>
    public bool IsVisible { get; set; }

    /// <summary> Видимый заказ, который участвует в начислении с учётом статуса. </summary>
    public bool IsAccruable { get; set; }

    public Guid? ClientId { get; set; }

    public string? ClientName { get; set; }

    public decimal NetAmount { get; set; }

    public decimal PaidAmount { get; set; }

    public decimal DebtAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal MaterialCostAmount { get; set; }

    public decimal LaborCostAmount { get; set; }

    public decimal ProfitAmount { get; set; }

    /// <summary> Дата по убыванию и идентификатор заказа сохраняют порядок первого появления в группах. </summary>
    public string FirstKey { get; set; } = string.Empty;
}
