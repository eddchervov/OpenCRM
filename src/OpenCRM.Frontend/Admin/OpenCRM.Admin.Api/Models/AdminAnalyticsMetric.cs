namespace OpenCRM.Admin.Api.Models;

/// <summary> Метрика аналитики для дашборда. </summary>
public sealed class AdminAnalyticsMetric
{
    /// <summary> Отображаемое название метрики. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение метрики. </summary>
    public double Value { get; init; }

    /// <summary> Формат отображения значения метрики. </summary>
    public string Format { get; init; } = string.Empty;
}
