namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные одного дня аналитики для дашборда. </summary>
public sealed class AdminAnalyticsDay
{
    /// <summary> Дата дня аналитики. </summary>
    public string Date { get; init; } = string.Empty;

    /// <summary> Значения метрик за день, допускающие пропуск. </summary>
    public IReadOnlyList<double?> Values { get; init; } = [];
}
