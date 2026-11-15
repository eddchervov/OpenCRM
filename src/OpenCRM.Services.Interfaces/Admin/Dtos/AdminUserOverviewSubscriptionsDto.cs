namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO подписок в обзоре пользователя. </summary>
public sealed class AdminUserOverviewSubscriptionsDto
{
    /// <summary> Подписки пользователя. </summary>
    public IReadOnlyList<AdminUserSubscriptionDto> Users { get; init; } = [];

    /// <summary> Подписки организаций пользователя. </summary>
    public IReadOnlyList<AdminOrganizationSubscriptionDto> Organizations { get; init; } = [];

    /// <summary> История подписок пользователя. </summary>
    public IReadOnlyList<AdminUserSubscriptionHistoryDto> History { get; init; } = [];
}
