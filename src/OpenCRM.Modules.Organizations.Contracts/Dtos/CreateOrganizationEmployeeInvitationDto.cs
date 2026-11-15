using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для приглашения сотрудника в организацию. </summary>
public class CreateOrganizationEmployeeInvitationDto
{
    /// <summary> Имя приглашаемого сотрудника. </summary>
    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Телефон приглашаемого сотрудника, необязательный. </summary>
    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary> Email приглашаемого сотрудника. </summary>
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary> Код роли, назначаемой сотруднику. </summary>
    [Required]
    [MaxLength(40)]
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Признак доступности сотрудника для назначения на работы. </summary>
    public bool IsActive { get; set; } = true;
}
