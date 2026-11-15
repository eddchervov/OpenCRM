using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Ограниченная пачка событий одного сеанса браузера. </summary>
public sealed class UserActivityBatch
{
    public Guid SessionId { get; set; }

    public Guid? OrganizationId { get; set; }

    [Required, MinLength(1), MaxLength(50)]
    public List<UserActivityInput> Events { get; set; } = [];
}
