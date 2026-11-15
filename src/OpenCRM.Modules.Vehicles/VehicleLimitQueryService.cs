using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Сервис проверки лимитов количества техники. </summary>
public sealed class VehicleLimitQueryService : IVehicleLimitQueryService
{
    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Сервис запросов личной подписки. </summary>
    private readonly IPersonalSubscriptionQueryService _subscriptionService;

    /// <summary> Сервис подписки организации. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Сервис проверки доступа к функциям. </summary>
    private readonly IFeatureAccessService _featureAccessService;

    private readonly IPlatformCommercialPolicy? _platformCommercialPolicy;

    /// <summary> Создаёт сервис лимитов техники с зависимостями. </summary>
    public VehicleLimitQueryService(
        IVehicleRepository vehicleRepository,
        IPersonalSubscriptionQueryService subscriptionService,
        IOrganizationSubscriptionService organizationSubscriptionService,
        IFeatureAccessService featureAccessService,
        IPlatformCommercialPolicy? platformCommercialPolicy = null)
    {
        this._vehicleRepository = vehicleRepository;
        this._subscriptionService = subscriptionService;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._featureAccessService = featureAccessService;
        this._platformCommercialPolicy = platformCommercialPolicy;
    }

    /// <summary> Возвращает информацию о лимите техники пользователя. </summary>
    public async Task<VehicleLimitInfoDto> GetVehicleLimitInfoAsync(string userId)
    {
        return await this.GetVehicleLimitInfoAsync(userId, null);
    }

    /// <summary> Возвращает информацию о лимите техники с учётом организации. </summary>
    public async Task<VehicleLimitInfoDto> GetVehicleLimitInfoAsync(string userId, Guid? organizationId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        if (this._platformCommercialPolicy?.CommercialEnabled != true)
        {
            return new VehicleLimitInfoDto
            {
                CurrentVehicleCount = await this._vehicleRepository.CountVehicles(userId, organizationId, false),
                CanAddMoreVehicles = true,
                MaxVehiclesForUser = 0
            };
        }

        var currentCount = await this.GetWorkspaceVehicleCountAsync(userId, organizationId);
        var canAddMore = await this.CanUserAddVehicleAsync(userId, organizationId);
        var hasActiveSubscription = organizationId.HasValue
            ? await this._organizationSubscriptionService.HasActiveSubscriptionAsync(userId, organizationId.Value)
            : await this._subscriptionService.HasActiveSubscriptionAsync(userId);
        var workspaceVehicleLimit = await this.GetWorkspaceVehicleLimitAsync(userId, organizationId);
        var isMaxPlan = organizationId.HasValue ? false : await this._subscriptionService.IsMaxPlanAsync(userId);
        var currentPlanName = organizationId.HasValue
            ? (await this._organizationSubscriptionService.GetEffectivePlanAsync(userId, organizationId.Value))?.Name
            : (await this._subscriptionService.GetUserSubscriptionAsync(userId))?.SubscriptionPlan?.Name;

        return new VehicleLimitInfoDto
        {
            CurrentVehicleCount = currentCount,
            CanAddMoreVehicles = canAddMore,
            HasActiveSubscription = hasActiveSubscription,
            MaxVehiclesForUser = workspaceVehicleLimit,
            IsMaxPlan = isMaxPlan,
            CurrentPlanName = currentPlanName
        };
    }

    /// <summary> Проверяет, может ли пользователь добавить технику. </summary>
    internal async Task<bool> CanUserAddVehicleAsync(string userId, Guid? organizationId)
    {
        var currentCount = await this.GetWorkspaceVehicleCountAsync(userId, organizationId);
        var limit = await this.GetWorkspaceVehicleLimitAsync(userId, organizationId);

        // Нулевой лимит означает отсутствие ограничения: базовая поставка не задаёт квоты.
        return limit <= 0 || currentCount < limit;
    }

    internal async Task<int> GetWorkspaceVehicleCountAsync(string userId, Guid? organizationId)
    {
        if (this._platformCommercialPolicy?.CommercialEnabled == true && organizationId.HasValue
            && (await this._organizationSubscriptionService.GetWorkspaceAccessAsync(userId, organizationId.Value))?.IsFreePlan == true)
        {
            return await this._vehicleRepository.CountVehicles(userId, organizationId, null);
        }

        return await this._vehicleRepository.CountVehicles(userId, organizationId, false);
    }

    /// <summary> Проверяет право пользователя читать данные рабочего пространства. </summary>
    private async Task EnsureCanReadWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.VehiclesView)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId.Value, permissionCode);
    }

    /// <summary> Возвращает лимит техники для личного аккаунта пользователя. </summary>
    private Task<int> GetUserVehicleLimitAsync(string userId)
    {
        return this._featureAccessService.GetIntValueAsync(userId, Helpers.FeatureCodes.VehiclesLimit, Helpers.PlatformDefaults.VehicleLimit);
    }

    /// <summary> Возвращает лимит техники для рабочего пространства. </summary>
    internal async Task<int> GetWorkspaceVehicleLimitAsync(string userId, Guid? organizationId)
    {
        if (this._platformCommercialPolicy?.CommercialEnabled != true)
        {
            if (organizationId.HasValue)
            {
                await this._organizationSubscriptionService.GetVehicleLimitAsync(userId, organizationId.Value, 0);
            }

            return 0;
        }

        var personalLimit = await this.GetUserVehicleLimitAsync(userId);
        if (!organizationId.HasValue)
        {
            return personalLimit;
        }

        return await this._organizationSubscriptionService.GetVehicleLimitAsync(userId, organizationId.Value, personalLimit);
    }
}
