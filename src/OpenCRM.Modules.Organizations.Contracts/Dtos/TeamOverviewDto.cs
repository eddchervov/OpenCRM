namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Обзор организаций и их архива для пользователя. </summary>
public class TeamOverviewDto
{
    /// <summary> Активные организации пользователя. </summary>
    public IReadOnlyList<OrganizationDto> Organizations { get; set; } = [];

    /// <summary> Архивные организации пользователя. </summary>
    public IReadOnlyList<OrganizationDto> ArchivedOrganizations { get; set; } = [];

    /// <summary> Есть ли у пользователя активные организации. </summary>
    public bool HasOrganizations => Organizations.Count > 0;

    /// <summary> Есть ли у пользователя архивные организации. </summary>
    public bool HasArchivedOrganizations => ArchivedOrganizations.Count > 0;
}
