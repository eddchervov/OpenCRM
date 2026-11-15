namespace OpenCRM.Admin.Api.Models;

/// <summary> Блоки недавних событий административной панели. </summary>
public sealed class AdminDashboardRecent
{
    /// <summary> Недавно зарегистрированные пользователи. </summary>
    public IReadOnlyList<AdminDashboardUser> Users { get; init; } = [];

    /// <summary> Недавно созданные организации. </summary>
    public IReadOnlyList<AdminDashboardOrganization> Organizations { get; init; } = [];

    /// <summary> Недавние платежи. </summary>
    public IReadOnlyList<AdminDashboardPayment> Payments { get; init; } = [];

    /// <summary> Недавние письма. </summary>
    public IReadOnlyList<AdminDashboardEmail> Emails { get; init; } = [];

    /// <summary> Недавние обращения в поддержку. </summary>
    public IReadOnlyList<AdminDashboardSupportMessage> SupportMessages { get; init; } = [];

    /// <summary> Недавние предложения по каталогу. </summary>
    public IReadOnlyList<AdminDashboardCatalogSuggestion> CatalogSuggestions { get; init; } = [];
}
