namespace OpenCRM.Admin.Api.Models;

/// <summary> Дневные счётчики событий и полная очередь модерации. </summary>
public sealed class AdminNavigationCounts
{
    public DateTime? AsOfUtc { get; init; }

    public int Users { get; init; }

    public int Payments { get; init; }

    public int SupportMessages { get; init; }

    public int Emails { get; init; }

    /// <summary> Все предложения на модерации; null означает старую версию сервера. </summary>
    public int? PendingCatalogSuggestions { get; init; }
}
