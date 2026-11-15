namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Ставка НДС для строки работы обслуживания. </summary>
public sealed class MaintenanceVatRateDto
{
    /// <summary> Индекс строки в заказе. </summary>
    public int LineIndex { get; set; }

    /// <summary> Код ставки НДС. </summary>
    public string VatRateCode { get; set; } = string.Empty;
}
