namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Блок последних записей дашборда. </summary>
public sealed class AdminDashboardRecentDto
{
    /// <summary> Последние пользователи. </summary>
    public IReadOnlyList<AdminDashboardUserDto> Users { get; init; } = [];

    /// <summary> Последние организации. </summary>
    public IReadOnlyList<AdminDashboardOrganizationDto> Organizations { get; init; } = [];

    /// <summary> Последние платежи. </summary>
    public IReadOnlyList<AdminDashboardPaymentDto> Payments { get; init; } = [];

    /// <summary> Последние письма. </summary>
    public IReadOnlyList<AdminDashboardEmailDto> Emails { get; init; } = [];

    /// <summary> Последние сообщения поддержки. </summary>
    public IReadOnlyList<AdminDashboardSupportMessageDto> SupportMessages { get; init; } = [];

    /// <summary> Последние предложения справочника. </summary>
    public IReadOnlyList<AdminDashboardCatalogSuggestionDto> CatalogSuggestions { get; init; } = [];
}
