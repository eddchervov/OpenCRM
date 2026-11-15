namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Метрика аналитики с подписью, значением и форматом. </summary>
public sealed record AdminAnalyticsMetricDto(string Label, double Value, string Format);
