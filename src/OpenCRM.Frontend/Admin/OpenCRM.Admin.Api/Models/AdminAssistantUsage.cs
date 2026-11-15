namespace OpenCRM.Admin.Api.Models;

/// <summary> Итоговые расходы на ИИ-помощника за период. </summary>
public sealed class AdminAssistantUsageTotals
{
    public decimal? CostRub { get; init; }
    public decimal ReservedRub { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public int UnknownCount { get; init; }
    public int RequestCount { get; init; }
}

/// <summary> Расходы помощника за один день. </summary>
public sealed class AdminAssistantUsageDay
{
    public DateOnly Date { get; init; }
    public decimal? CostRub { get; init; }
    public decimal ReservedRub { get; init; }
}

/// <summary> Расходы и число запросов помощника по организации. </summary>
public sealed class AdminAssistantUsageOrganization
{
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal? CostRub { get; init; }
    public decimal ReservedRub { get; init; }
    public int RequestCount { get; init; }
}

/// <summary> Данные об одном запросе к ИИ-помощнику. </summary>
public sealed class AdminAssistantUsageItem
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProviderRequestId { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? CachedTokens { get; init; }
    public decimal ReservedCostRub { get; init; }
    public decimal? CostRub { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary> Расходы помощника с разбивкой по дням, организациям и запросам. </summary>
public sealed class AdminAssistantUsageResponse
{
    public AdminAssistantUsageTotals Totals { get; init; } = new();
    public IReadOnlyList<AdminAssistantUsageDay> Daily { get; init; } = [];
    public IReadOnlyList<AdminAssistantUsageOrganization> ByOrganization { get; init; } = [];
    public IReadOnlyList<AdminAssistantUsageItem> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary> Параметры сверки неизвестной стоимости запроса. </summary>
public sealed class AdminAssistantUsageSettlementRequest
{
    public decimal CostRub { get; init; }
    public string Reason { get; init; } = string.Empty;
}
