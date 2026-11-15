using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для приглашения участника в организацию. </summary>
public class InviteOrganizationMemberDto
{
    /// <summary> Email приглашаемого участника. </summary>
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary> Код роли приглашаемого участника. </summary>
    [Required]
    [MaxLength(40)]
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Идентификатор связанного сотрудника, если есть. </summary>
    public Guid? EmployeeId { get; set; }
}
