namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Строка UTM-статистики с визитами, посетителями и глубиной просмотра. </summary>
public sealed record AdminAnalyticsUtmRowDto(
    string Source,
    string? Medium,
    string? Campaign,
    double Visits,
    double Visitors,
    double PageDepth);
