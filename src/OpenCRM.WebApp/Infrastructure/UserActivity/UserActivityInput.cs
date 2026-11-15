using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Разрешённые технические сведения браузерного события. </summary>
public sealed class UserActivityInput
{
    public DateTimeOffset OccurredAt { get; set; }

    [Required, RegularExpression("^(page|click|goal)$")]
    public string Kind { get; set; } = string.Empty;

    [Required, StringLength(80), RegularExpression("^[a-zA-Z0-9_.:-]+$")]
    public string Action { get; set; } = string.Empty;

    [Required, StringLength(240), RegularExpression("^/[a-zA-Z0-9_/{}/.:-]*$")]
    public string Page { get; set; } = string.Empty;

    [Required, RegularExpression("^(personal|detailing|autoservice|fleet|account)$")]
    public string Scenario { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true), StringLength(64), RegularExpression("^[a-zA-Z0-9_-]*$")]
    public string Section { get; set; } = string.Empty;

    [StringLength(64), RegularExpression("^([a-fA-F0-9-]{36}|[0-9]{1,20})$")]
    public string? TargetId { get; set; }
}
