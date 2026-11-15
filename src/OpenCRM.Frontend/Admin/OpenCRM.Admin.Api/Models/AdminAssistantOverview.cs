namespace OpenCRM.Admin.Api.Models;

/// <summary> Состояние подключений поставщиков ИИ-помощника. </summary>
public sealed class AdminAssistantProviderStatus
{
    public bool RouterConfigured { get; init; }
    public bool TelegramConfigured { get; init; }
    public string? TelegramBotUsername { get; init; }
}

/// <summary> Настройки и доступ организаций к ИИ-помощнику. </summary>
public sealed class AdminAssistantOverview
{
    public AdminAssistantSettings Settings { get; init; } = new();
    public AdminAssistantLimits Limits { get; init; } = new();
    public AdminAssistantProviderStatus Provider { get; init; } = new();
    public IReadOnlyList<AdminAssistantOrganization> Organizations { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IReadOnlyList<AdminAssistantAudit> Audits { get; init; } = [];
}

/// <summary> Верхние границы настроек ИИ-помощника из конфигурации сервера. </summary>
public sealed class AdminAssistantLimits
{
    public int MaximumOutputTokensAllowed { get; init; }
    public decimal MaximumBudgetRub { get; init; }
    public decimal MaximumReservationRub { get; init; }
    public string BudgetTimeZone { get; init; } = string.Empty;
}
