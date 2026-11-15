namespace OpenCRM.Admin.Api.Models;

/// <summary> Строка статистики посещений по UTM-меткам. </summary>
public sealed class AdminAnalyticsUtmRow
{
    /// <summary> UTM-источник посещений. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> UTM-тип трафика. </summary>
    public string? Medium { get; init; }

    /// <summary> UTM-название кампании. </summary>
    public string? Campaign { get; init; }

    /// <summary> Количество посещений. </summary>
    public double Visits { get; init; }

    /// <summary> Количество посетителей. </summary>
    public double Visitors { get; init; }

    /// <summary> Глубина просмотра страниц. </summary>
    public double PageDepth { get; init; }
}
