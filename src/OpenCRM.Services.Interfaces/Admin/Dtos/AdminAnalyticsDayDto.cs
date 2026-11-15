namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary>Дневные значения в порядке показателей отчёта; null означает отсутствие значения.</summary>
public sealed record AdminAnalyticsDayDto(string Date, IReadOnlyList<double?> Values);
