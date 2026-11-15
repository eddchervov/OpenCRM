namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Данные внешнего провайдера аналитики. </summary>
public sealed record AdminAnalyticsProviderDto
{
    /// <summary> Код провайдера. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название провайдера. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Статус провайдера. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Сообщение провайдера. </summary>
    public string? Message { get; init; }

    /// <summary> Начало периода данных. </summary>
    public string? DateFrom { get; init; }

    /// <summary> Конец периода данных. </summary>
    public string? DateTo { get; init; }

    /// <summary> Время последнего получения данных. </summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary> Признак устаревших данных. </summary>
    public bool IsStale { get; init; }

    /// <summary> Метрики провайдера. </summary>
    public IReadOnlyList<AdminAnalyticsMetricDto> Metrics { get; init; } = [];

    /// <summary> Дневные данные провайдера. </summary>
    public IReadOnlyList<AdminAnalyticsDayDto> Days { get; init; } = [];

    /// <summary> Строки UTM-статистики провайдера. </summary>
    public IReadOnlyList<AdminAnalyticsUtmRowDto> UtmRows { get; init; } = [];
}
