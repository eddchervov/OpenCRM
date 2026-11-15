namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO техники организации пользователя. </summary>
public sealed class AdminUserOrganizationVehicleDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Название техники. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Название клиента техники. </summary>
    public string? ClientName { get; init; }
}
