namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для обновления ставок НДС по строкам заказа. </summary>
public sealed class UpdateMaintenanceVatRatesDto
{
    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; set; }

    /// <summary> Ставки НДС по строкам заказа. </summary>
    public List<MaintenanceVatRateDto> Lines { get; set; } = [];
}
