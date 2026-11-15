namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO ожидающего приглашения пользователя в организацию. </summary>
public class PendingOrganizationInvitationDto
{
    /// <summary> Идентификатор приглашения. </summary>
    public Guid InvitationId { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary> Код сценария организации. </summary>
    public string OrganizationScenarioCode { get; set; } = string.Empty;

    /// <summary> Название сценария организации. </summary>
    public string OrganizationScenarioName { get; set; } = string.Empty;

    /// <summary> Имя связанного сотрудника, если есть. </summary>
    public string? EmployeeName { get; set; }

    /// <summary> Код роли в приглашении. </summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Название роли в приглашении. </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary> Код статуса приглашения. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Название статуса приглашения. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Дата создания приглашения. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Может ли пользователь принять приглашение. </summary>
    public bool CanAccept { get; set; }
}
