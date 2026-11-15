namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Подробные сведения о клиенте организации. </summary>
public class OrganizationClientDetailsDto
{
    /// <summary> Клиент организации с подробными сведениями. </summary>
    public required OrganizationClientDto Client { get; set; }

    /// <summary> Последние обслуживания клиента. </summary>
    public IReadOnlyList<OrganizationClientMaintenanceItemDto> RecentMaintenances { get; set; } = [];

    /// <summary> Доступно ли пользователю просматривать обслуживание организации. </summary>
    public bool CanViewMaintenance { get; set; }

    /// <summary> Есть ли у пользователя право редактировать клиента организации. </summary>
    public bool CanEdit { get; set; }

    /// <summary> Сумма действующих оплат по всем обслуживаниям клиента. </summary>
    public decimal? TotalPaidAmount { get; set; }

    /// <summary> Ближайшее будущее обслуживание клиента со статусом «Запланировано». </summary>
    public OrganizationClientMaintenanceItemDto? UpcomingMaintenance { get; set; }

    /// <summary> Дефекты клиента. </summary>
    public IReadOnlyList<OrganizationClientDefectItemDto> Defects { get; set; } = [];

    /// <summary> События истории клиента. </summary>
    public IReadOnlyList<OrganizationClientHistoryEventDto> HistoryItems { get; set; } = [];

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; set; }

    /// <summary> Количество закрытых дефектов. </summary>
    public int ClosedDefectsCount { get; set; }
}
