namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Метка клиента организации. </summary>
public class OrganizationClientTagDto
{
    /// <summary> Идентификатор метки. </summary>
    public Guid Id { get; set; }

    /// <summary> Название метки. </summary>
    public string Name { get; set; } = string.Empty;
}
