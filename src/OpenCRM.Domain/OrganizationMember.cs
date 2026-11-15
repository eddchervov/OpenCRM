using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Участник организации. </summary>
public class OrganizationMember
{
    /// <summary> Идентификатор участника. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, в которой состоит участник. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор пользователя-участника. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь, связанный с участником организации. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Код роли участника в организации. </summary>
    public string RoleCode { get; set; } = OrganizationRoleCodes.Employee;

    /// <summary> Идентификатор пользователя, пригласившего участника. </summary>
    public string? InvitedByUserId { get; set; }

    /// <summary> Пользователь, пригласивший участника. </summary>
    public ApplicationUser? InvitedByUser { get; set; }

    /// <summary> Дата и время создания записи участника. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время присоединения к организации. </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
