namespace OpenCRM.Admin.Api.Models;

/// <summary> Организация и её доступ к ИИ-помощнику. </summary>
public sealed class AdminAssistantOrganization
{
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public DateTimeOffset? EnabledUntil { get; init; }
    public bool WebEnabled { get; init; }
    public bool TelegramEnabled { get; init; }
    public decimal DailyBudgetRub { get; init; }
    public decimal MonthlyBudgetRub { get; init; }
}
