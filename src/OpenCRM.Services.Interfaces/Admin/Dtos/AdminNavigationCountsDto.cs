namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Количество новых записей за день и предложений в очереди модерации. </summary>
public sealed class AdminNavigationCountsDto
{
    public DateTime? AsOfUtc { get; init; }

    public int Users { get; init; }

    public int Payments { get; init; }

    public int SupportMessages { get; init; }

    public int Emails { get; init; }

    /// <summary> Все предложения, ожидающие модерации, независимо от даты создания. </summary>
    public int PendingCatalogSuggestions { get; init; }
}
