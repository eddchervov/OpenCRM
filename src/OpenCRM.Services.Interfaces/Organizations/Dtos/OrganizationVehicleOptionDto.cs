namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Вариант выбора транспортного средства организации. </summary>
public sealed class OrganizationVehicleOptionDto
{
    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор клиента организации, если есть. </summary>
    public Guid? OrganizationClientId { get; init; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительная подпись транспортного средства. </summary>
    public string? Subtitle { get; init; }
}
