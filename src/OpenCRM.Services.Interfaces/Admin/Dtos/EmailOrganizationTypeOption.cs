namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Вариант типа организации для рассылки. </summary>
public sealed class EmailOrganizationTypeOption
{
    /// <summary> Код типа организации. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название типа организации. </summary>
    public string Name { get; init; } = string.Empty;
}
