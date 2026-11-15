using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис работы с пользователями в админке. </summary>
public sealed class AdminUsersService : IAdminUsersService
{
    /// <summary> Зависимость сервиса списка пользователей. </summary>
    private readonly AdminUserListService _listService;

    /// <summary> Зависимость сервиса содержимого организаций. </summary>
    private readonly AdminUserOrganizationContentsService _organizationContentsService;

    /// <summary> Зависимость сервиса сводки по пользователю. </summary>
    private readonly AdminUserOverviewService _overviewService;

    /// <summary> Зависимость запросов по подпискам. </summary>
    private readonly AdminUserSubscriptionQueries _subscriptionQueries;

    /// <summary> Создаёт сервис и инициализирует внутренние сервисы. </summary>
    public AdminUsersService(OpenCRMContext opencrmContext, IConfiguration? configuration = null)
    {
        var commercialEnabled = configuration?.GetValue("PlatformPolicy:Commercial:Enabled", false) ?? false;
        this._listService = new AdminUserListService(opencrmContext, commercialEnabled);
        this._organizationContentsService = new AdminUserOrganizationContentsService(opencrmContext);
        this._overviewService = new AdminUserOverviewService(opencrmContext, commercialEnabled);
        this._subscriptionQueries = new AdminUserSubscriptionQueries(opencrmContext);
    }

    /// <summary> Возвращает страницу списка пользователей с фильтрами и сортировкой. </summary>
    public Task<GetUserResponse> GetAsync(
        string? search,
        UsersSort sort,
        bool includeDeleted,
        int page,
        int pageSize,
        string? utmSource) =>
        this._listService.GetAsync(search, sort, includeDeleted, page, pageSize, utmSource);

    /// <summary> Возвращает содержимое организации пользователя. </summary>
    public Task<AdminUserOrganizationContentsDto?> GetOrganizationContentsAsync(
        string userId,
        Guid organizationId) =>
        this._organizationContentsService.GetOrganizationContentsAsync(userId, organizationId);

    /// <summary> Возвращает сводку по пользователю. </summary>
    public Task<AdminUserOverviewDto?> GetOverviewAsync(string userId) =>
        this._overviewService.GetOverviewAsync(userId);

    /// <summary> Обновляет доступ пользователя к тестовым подпискам. </summary>
    public Task<bool> UpdateTestSubscriptionsAccessAsync(
        string userId,
        bool canUseTestSubscriptions) =>
        this._subscriptionQueries.UpdateTestSubscriptionsAccessAsync(userId, canUseTestSubscriptions);

    /// <summary> Возвращает длительность тарифного плана в днях. </summary>
    public Task<int?> GetSubscriptionPlanDurationDaysAsync(int planId) =>
        this._subscriptionQueries.GetSubscriptionPlanDurationDaysAsync(planId);

    /// <summary> Возвращает активную подписку пользователя. </summary>
    public Task<AdminActiveUserSubscriptionDto?> GetActiveUserSubscriptionAsync(string userId) =>
        this._subscriptionQueries.GetActiveUserSubscriptionAsync(userId);
}
