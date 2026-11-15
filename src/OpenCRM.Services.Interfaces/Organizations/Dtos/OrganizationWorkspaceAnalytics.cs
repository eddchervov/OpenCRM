namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Аналитические показатели рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceAnalytics
{
    /// <summary> Ключевые показатели эффективности. </summary>
    public OrganizationWorkspaceAnalyticsKpis Kpis { get; init; } = new();

    /// <summary> Ряд данных о созданных обслуживаниях. </summary>
    public IReadOnlyList<OrganizationWorkspaceAnalyticsSeriesPoint> MaintenanceCreatedSeries { get; init; } = [];

    /// <summary> Ряд данных о завершённых обслуживаниях. </summary>
    public IReadOnlyList<OrganizationWorkspaceAnalyticsSeriesPoint> MaintenanceCompletedSeries { get; init; } = [];

    /// <summary> Ряд данных о выручке. </summary>
    public IReadOnlyList<OrganizationWorkspaceAnalyticsMoneySeriesPoint> RevenueSeries { get; init; } = [];

    /// <summary> Распределение по статусам. </summary>
    public IReadOnlyList<OrganizationWorkspaceAnalyticsBreakdownItem> StatusBreakdown { get; init; } = [];

    /// <summary> Распределение по рискам материалов. </summary>
    public IReadOnlyList<OrganizationWorkspaceAnalyticsBreakdownItem> MaterialRiskBreakdown { get; init; } = [];
}

/// <summary> Ключевые показатели эффективности рабочего пространства. </summary>
public sealed class OrganizationWorkspaceAnalyticsKpis
{
    /// <summary> Количество активных транспортных средств. </summary>
    public int ActiveVehicles { get; init; }

    /// <summary> Количество клиентов. </summary>
    public int Clients { get; init; }

    /// <summary> Количество запланированных работ. </summary>
    public int PlannedWorks { get; init; }

    /// <summary> Количество работ в процессе выполнения. </summary>
    public int InProgressWorks { get; init; }

    /// <summary> Количество работ, завершённых за 14 дней. </summary>
    public int CompletedWorks14Days { get; init; }

    /// <summary> Выручка за 14 дней. </summary>
    public decimal Revenue14Days { get; init; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefects { get; init; }

    /// <summary> Количество записей счётчиков за 14 дней. </summary>
    public int CounterEntries14Days { get; init; }

    /// <summary> Количество материалов с низким остатком. </summary>
    public int MaterialsLowStock { get; init; }

    /// <summary> Количество материалов без остатка. </summary>
    public int MaterialsOutOfStock { get; init; }

    /// <summary> Количество документов за 14 дней. </summary>
    public int Documents14Days { get; init; }
}

/// <summary> Точка числового ряда аналитики. </summary>
public sealed class OrganizationWorkspaceAnalyticsSeriesPoint
{
    /// <summary> Дата точки. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение точки. </summary>
    public int Value { get; init; }
}

/// <summary> Точка денежного ряда аналитики. </summary>
public sealed class OrganizationWorkspaceAnalyticsMoneySeriesPoint
{
    /// <summary> Дата точки. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Денежное значение точки. </summary>
    public decimal Value { get; init; }
}

/// <summary> Элемент распределения аналитики. </summary>
public sealed class OrganizationWorkspaceAnalyticsBreakdownItem
{
    /// <summary> Код элемента. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Подпись элемента. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение элемента. </summary>
    public int Value { get; init; }

    /// <summary> Процент элемента. </summary>
    public int Percent { get; init; }
}
