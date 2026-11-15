namespace OpenCRM.Admin.Api.Models;

/// <summary> Сведения о внешнем провайдере аналитики. </summary>
public sealed class AdminAnalyticsProvider
{
    /// <summary> Код провайдера аналитики. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Отображаемое название провайдера. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Статус получения данных провайдера. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Сообщение провайдера, если есть. </summary>
    public string? Message { get; init; }

    /// <summary> Начальная дата периода данных, если задана. </summary>
    public string? DateFrom { get; init; }

    /// <summary> Конечная дата периода данных, если задана. </summary>
    public string? DateTo { get; init; }

    /// <summary> Время последнего получения данных провайдера. </summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary> Признак устаревания данных провайдера аналитики. </summary>
    public bool IsStale { get; init; }

    /// <summary> Список метрик внешнего провайдера аналитики. </summary>
    public IReadOnlyList<AdminAnalyticsMetric> Metrics { get; init; } = [];

    /// <summary> Список дневных данных провайдера аналитики. </summary>
    public IReadOnlyList<AdminAnalyticsDay> Days { get; init; } = [];

    /// <summary> Список строк по UTM-меткам провайдера аналитики. </summary>
    public IReadOnlyList<AdminAnalyticsUtmRow> UtmRows { get; init; } = [];
}
