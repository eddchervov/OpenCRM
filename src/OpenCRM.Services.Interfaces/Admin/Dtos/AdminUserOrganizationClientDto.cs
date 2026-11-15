namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO клиента организации пользователя. </summary>
public sealed class AdminUserOrganizationClientDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid Id { get; init; }

    /// <summary> Название клиента. </summary>
    public string? Name { get; init; }

    /// <summary> Количество техники клиента. </summary>
    public int VehiclesCount { get; init; }
}
