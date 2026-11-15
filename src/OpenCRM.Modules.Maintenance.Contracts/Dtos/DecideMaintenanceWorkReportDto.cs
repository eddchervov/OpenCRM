namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для принятия решения по отчёту о работе. </summary>
public sealed class DecideMaintenanceWorkReportDto
{
    /// <summary> Принятое решение по отчёту. </summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary> Причина решения. </summary>
    public string? Reason { get; set; }

    /// <summary> Согласованная цена работы. </summary>
    public decimal? Price { get; set; }

    /// <summary> Код ставки НДС. </summary>
    public string? VatRateCode { get; set; }
}
