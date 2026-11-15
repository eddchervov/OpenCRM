namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO содержимого организации пользователя. </summary>
public sealed class AdminUserOrganizationContentsDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; init; }

    /// <summary> Название организации. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Список участников организации. </summary>
    public IReadOnlyList<AdminUserOrganizationMemberDto> Members { get; init; } = [];

    /// <summary> Список техники организации. </summary>
    public IReadOnlyList<AdminUserOrganizationVehicleDto> Vehicles { get; init; } = [];

    /// <summary> Список клиентов организации. </summary>
    public IReadOnlyList<AdminUserOrganizationClientDto> Clients { get; init; } = [];

    /// <summary> Количество обслуживаний организации. </summary>
    public int MaintenancesCount { get; init; }

    /// <summary> Список последних обслуживаний организации. </summary>
    public IReadOnlyList<AdminUserOrganizationMaintenanceDto> RecentMaintenances { get; init; } = [];
}
