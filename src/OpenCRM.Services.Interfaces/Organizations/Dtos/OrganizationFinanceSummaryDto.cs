namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Сводка финансовых показателей организации за период. </summary>
public sealed class OrganizationFinanceSummaryDto
{
    public string CurrencyCode { get; init; } = "RUB";

    public IReadOnlyList<OrganizationFinanceCurrencyTotalsDto> OtherCurrencyTotals { get; init; } = [];

    /// <summary> Начало периода. </summary>
    public DateTime DateFrom { get; init; }

    /// <summary> Конец периода. </summary>
    public DateTime DateTo { get; init; }

    /// <summary> Ключевые финансовые показатели. </summary>
    public OrganizationFinanceKpisDto Kpis { get; init; } = new();

    /// <summary> Ряд выручки по периодам. </summary>
    public IReadOnlyList<OrganizationFinanceMoneySeriesPointDto> RevenueSeries { get; init; } = [];

    /// <summary> Ряд начислений по периодам. </summary>
    public IReadOnlyList<OrganizationFinanceMoneySeriesPointDto> AccruedSeries { get; init; } = [];

    /// <summary> Распределение затрат по категориям. </summary>
    public IReadOnlyList<OrganizationFinanceBreakdownItemDto> CostBreakdown { get; init; } = [];

    /// <summary> Распределение платежей по способам оплаты. </summary>
    public IReadOnlyList<OrganizationFinanceBreakdownItemDto> PaymentBreakdown { get; init; } = [];

    /// <summary> Топ услуг по сумме. </summary>
    public IReadOnlyList<OrganizationFinanceServiceItemDto> TopServices { get; init; } = [];

    /// <summary> Топ должников среди клиентов. </summary>
    public IReadOnlyList<OrganizationFinanceClientDebtItemDto> TopDebtors { get; init; } = [];

    /// <summary> Заказы за период. </summary>
    public IReadOnlyList<OrganizationFinanceOrderItemDto> Orders { get; init; } = [];
}

public sealed class OrganizationFinanceCurrencyTotalsDto
{
    public string CurrencyCode { get; init; } = "RUB";
    public decimal GrossAmount { get; init; }
    public decimal NetAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal DebtAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal CostAmount { get; init; }
    public decimal ProfitAmount { get; init; }
}

/// <summary> Ключевые финансовые показатели организации. </summary>
public sealed class OrganizationFinanceKpisDto
{
    /// <summary> Количество заказов. </summary>
    public int OrdersCount { get; init; }

    /// <summary> Общая сумма заказов. </summary>
    public decimal GrossAmount { get; init; }

    /// <summary> Сумма заказов с учётом скидок. </summary>
    public decimal NetAmount { get; init; }

    /// <summary> Оплаченная сумма. </summary>
    public decimal PaidAmount { get; init; }

    /// <summary> Сумма задолженности. </summary>
    public decimal DebtAmount { get; init; }

    /// <summary> Сумма скидок. </summary>
    public decimal DiscountAmount { get; init; }

    /// <summary> Сумма затрат на материалы. </summary>
    public decimal MaterialCostAmount { get; init; }

    /// <summary> Сумма затрат на работы. </summary>
    public decimal LaborCostAmount { get; init; }

    /// <summary> Сумма затрат. </summary>
    public decimal CostAmount { get; init; }

    /// <summary> Сумма прибыли. </summary>
    public decimal ProfitAmount { get; init; }

    /// <summary> Средний чек. </summary>
    public decimal AverageCheckAmount { get; init; }
}

/// <summary> Точка денежного ряда по периодам. </summary>
public sealed class OrganizationFinanceMoneySeriesPointDto
{
    /// <summary> Дата точки. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение точки. </summary>
    public decimal Value { get; init; }
}

/// <summary> Элемент распределения по категориям. </summary>
public sealed class OrganizationFinanceBreakdownItemDto
{
    /// <summary> Код категории. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Наименование категории. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение категории. </summary>
    public decimal Value { get; init; }

    /// <summary> Доля категории в процентах. </summary>
    public int Percent { get; init; }
}

/// <summary> Услуга в финансовой сводке организации. </summary>
public sealed class OrganizationFinanceServiceItemDto
{
    /// <summary> Наименование услуги. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Количество оказаний услуги. </summary>
    public int Count { get; init; }

    /// <summary> Сумма по услуге. </summary>
    public decimal Amount { get; init; }
}

/// <summary> Клиент с задолженностью в финансовой сводке. </summary>
public sealed class OrganizationFinanceClientDebtItemDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid? ClientId { get; init; }

    /// <summary> Имя клиента. </summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary> Количество заказов клиента. </summary>
    public int OrdersCount { get; init; }

    /// <summary> Сумма задолженности клиента. </summary>
    public decimal DebtAmount { get; init; }
}

/// <summary> Заказ в финансовой сводке организации. </summary>
public sealed class OrganizationFinanceOrderItemDto
{
    public string CurrencyCode { get; init; } = "RUB";
    /// <summary> Идентификатор заказа. </summary>
    public Guid Id { get; init; }

    /// <summary> Признак отменённого обслуживания. </summary>
    public bool IsCancelled { get; init; }

    /// <summary> Дата заказа. </summary>
    public DateTime Date { get; init; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; init; }

    /// <summary> Отображаемое название техники. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительное описание техники. </summary>
    public string? VehicleSubtitle { get; init; }

    /// <summary> Идентификатор клиента. </summary>
    public Guid? ClientId { get; init; }

    /// <summary> Имя клиента. </summary>
    public string? ClientName { get; init; }

    /// <summary> Общая сумма заказа. </summary>
    public decimal GrossAmount { get; init; }

    /// <summary> Сумма заказа с учётом скидок. </summary>
    public decimal NetAmount { get; init; }

    /// <summary> Оплаченная сумма по заказу. </summary>
    public decimal PaidAmount { get; init; }

    /// <summary> Сумма задолженности по заказу. </summary>
    public decimal DebtAmount { get; init; }

    /// <summary> Сумма скидки по заказу. </summary>
    public decimal DiscountAmount { get; init; }

    /// <summary> Сумма затрат на материалы по заказу. </summary>
    public decimal MaterialCostAmount { get; init; }

    /// <summary> Сумма затрат на работы по заказу. </summary>
    public decimal LaborCostAmount { get; init; }

    /// <summary> Сумма прибыли по заказу. </summary>
    public decimal ProfitAmount { get; init; }

    /// <summary> Код статуса оплаты заказа. </summary>
    public string PaymentStatus { get; init; } = string.Empty;

    /// <summary> Отображаемое название статуса оплаты. </summary>
    public string PaymentStatusName { get; init; } = string.Empty;

    /// <summary> URL страницы заказа. </summary>
    public string DetailUrl { get; init; } = string.Empty;
}
