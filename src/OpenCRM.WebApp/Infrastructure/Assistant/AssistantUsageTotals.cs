namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed record AssistantUsageTotals(
    decimal? CostRub,
    decimal ReservedRub,
    decimal ChargedRub,
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    int UnknownCount)
{
    public static AssistantUsageTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
