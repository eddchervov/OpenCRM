namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO приглашения в организацию. </summary>
public class OrganizationInvitationDto
{
    /// <summary> Идентификатор приглашения. </summary>
    public Guid Id { get; set; }

    /// <summary> Email приглашённого участника. </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary> Идентификатор связанного сотрудника, если есть. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Имя связанного сотрудника. </summary>
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
}
