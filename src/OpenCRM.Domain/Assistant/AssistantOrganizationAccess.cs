namespace OpenCRM.Domain.Assistant;

public sealed class AssistantOrganizationAccess
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public DateTime? EnabledUntil { get; set; }
    public bool WebEnabled { get; set; }
    public bool TelegramEnabled { get; set; }
    public decimal DailyBudgetRub { get; set; }
    public decimal MonthlyBudgetRub { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedByUserId { get; set; } = string.Empty;
}
