namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO участника организации. </summary>
public class OrganizationMemberDto
{
    /// <summary> Идентификатор участника. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Email участника. </summary>
    public string? Email { get; set; }

    /// <summary> Имя пользователя участника. </summary>
    public string? UserName { get; set; }

    /// <summary> Имя связанного сотрудника в этой организации. </summary>
    public string? EmployeeName { get; set; }

    /// <summary> Код роли участника. </summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Название роли участника. </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary> Дата присоединения к организации. </summary>
    public DateTime JoinedAt { get; set; }

    /// <summary> Признак владельца организации. </summary>
    public bool IsOwner { get; set; }
}
