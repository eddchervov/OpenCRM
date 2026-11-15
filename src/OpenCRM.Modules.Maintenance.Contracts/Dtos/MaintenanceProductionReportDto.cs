namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Производственный отчёт по обслуживанию. </summary>
public sealed class MaintenanceProductionReportDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Общее количество операций. </summary>
    public int TotalOperations { get; set; }

    /// <summary> Количество завершённых операций. </summary>
    public int CompletedOperations { get; set; }

    /// <summary> Активное время работы. </summary>
    public TimeSpan ActiveTime { get; set; }

    /// <summary> Количество корректировок времени. </summary>
    public int TimeAdjustmentsCount { get; set; }

    /// <summary> Список израсходованных материалов. </summary>
    public IReadOnlyList<MaintenanceProductionMaterialDto> Materials { get; set; } = [];

    /// <summary> Сумма стоимости материалов. </summary>
    public decimal? MaterialCostAmount { get; set; }

    /// <summary> Сумма стоимости работ. </summary>
    public decimal? LaborCostAmount { get; set; }

    /// <summary> Сумма выручки. </summary>
    public decimal? RevenueAmount { get; set; }

    /// <summary> Сумма валовой маржи. </summary>
    public decimal? GrossMarginAmount { get; set; }
}

/// <summary> Данные материала в производственном отчёте. </summary>
public sealed class MaintenanceProductionMaterialDto
{
    /// <summary> Наименование материала. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Единица измерения материала. </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary> Количество материала. </summary>
    public decimal Quantity { get; set; }

    /// <summary> Стоимость материала. </summary>
    public decimal? Cost { get; set; }
}
