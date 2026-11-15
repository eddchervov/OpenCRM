namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для обновления списка работ и услуг обслуживания. </summary>
public sealed class UpdateMaintenanceServicesDto
{
    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; set; }

    /// <summary> Разрешает уточнить цену позже для ещё не завершённых работ. </summary>
    public bool AllowUnspecifiedPrices { get; set; }

    /// <summary> Новый список строк работ и услуг. </summary>
    public List<CreateMaintenanceDetailDto> Details { get; set; } = [];
}
