namespace OpenCRM.Admin.Api.Models;

/// <summary> Параметры подключения помощника для организации. </summary>
public sealed class AdminAssistantOrganizationAccessRequest
{
    public bool IsEnabled { get; init; }
    public DateTimeOffset? EnabledUntil { get; init; }
    public bool WebEnabled { get; init; }
    public bool TelegramEnabled { get; init; }
    public decimal DailyBudgetRub { get; init; }
    public decimal MonthlyBudgetRub { get; init; }
}
