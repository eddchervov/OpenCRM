namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Организация для фильтра обращений поддержки. </summary>
public class SupportOrganizationOptionDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; set; }

    /// <summary> Наименование организации. </summary>
    public string Name { get; set; } = string.Empty;
}
