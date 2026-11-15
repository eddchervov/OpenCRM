using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Приглашение пользователя в организацию. </summary>
public class OrganizationInvitation
{
    /// <summary> Идентификатор приглашения. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, в которую приглашают пользователя. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор связанного сотрудника. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Связанный сотрудник организации. </summary>
    public OrganizationEmployee? Employee { get; set; }

    /// <summary> Email приглашаемого пользователя. </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary> Нормализованный email приглашаемого пользователя. </summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary> Код роли, предлагаемой приглашением. </summary>
    public string RoleCode { get; set; } = OrganizationRoleCodes.Employee;

    /// <summary> Код статуса приглашения. </summary>
    public string StatusCode { get; set; } = OrganizationInvitationStatusCodes.Pending;

    /// <summary> Токен подтверждения приглашения. </summary>
    public string Token { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary> Идентификатор пользователя, отправившего приглашение. </summary>
    public string InvitedByUserId { get; set; } = string.Empty;

    /// <summary> Пользователь, отправивший приглашение. </summary>
    public ApplicationUser InvitedByUser { get; set; } = null!;

    /// <summary> Идентификатор пользователя, принявшего приглашение. </summary>
    public string? AcceptedByUserId { get; set; }

    /// <summary> Пользователь, принявший приглашение. </summary>
    public ApplicationUser? AcceptedByUser { get; set; }

    /// <summary> Дата и время создания приглашения. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время принятия приглашения. </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary> Дата и время отзыва приглашения. </summary>
    public DateTime? RevokedAt { get; set; }
}
