using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление пользователями в административной панели. </summary>
public interface IAdminUsersService
{
    /// <summary> Возвращает страницу пользователей с фильтрами и сортировкой. </summary>
    Task<GetUserResponse> GetAsync(
        string? search,
        UsersSort sort,
        bool includeDeleted,
        int page,
        int pageSize,
        string? utmSource);

    /// <summary> Возвращает содержимое организации пользователя. </summary>
    Task<AdminUserOrganizationContentsDto?> GetOrganizationContentsAsync(string userId, Guid organizationId);

    /// <summary> Возвращает обзор пользователя. </summary>
    Task<AdminUserOverviewDto?> GetOverviewAsync(string userId);

    /// <summary> Обновляет разрешение пользователю на тестовые подписки. </summary>
    Task<bool> UpdateTestSubscriptionsAccessAsync(string userId, bool canUseTestSubscriptions);

    /// <summary> Возвращает длительность тарифного плана в днях. </summary>
    Task<int?> GetSubscriptionPlanDurationDaysAsync(int planId);

    /// <summary> Возвращает активную подписку пользователя, если есть. </summary>
    Task<AdminActiveUserSubscriptionDto?> GetActiveUserSubscriptionAsync(string userId);
}
