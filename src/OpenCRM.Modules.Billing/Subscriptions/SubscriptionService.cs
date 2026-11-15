using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Subscriptions;

/// <summary> Фасадный сервис личных подписок. </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary> Зависимость сервиса запросов техники. </summary>
    private readonly IVehicleQueryService _vehicles;

    /// <summary> Зависимость сервиса команд по личным подпискам. </summary>
    private readonly IPersonalSubscriptionCommandService _personalSubscriptionCommandService;

    /// <summary> Зависимость сервиса оформления покупки подписок. </summary>
    private readonly IPersonalSubscriptionCheckoutService _personalSubscriptionCheckoutService;

    /// <summary> Зависимость сервиса запросов о личных подписках. </summary>
    private readonly IPersonalSubscriptionQueryService _personalSubscriptionQueryService;

    /// <summary> Зависимость сервиса запросов каталога тарифов. </summary>
    private readonly IPersonalSubscriptionCatalogQueryService _personalSubscriptionCatalogQueryService;

    /// <summary> Зависимость сервиса активации личных подписок. </summary>
    private readonly IPersonalSubscriptionActivationService _personalSubscriptionActivationService;

    /// <summary> Зависимость сервиса правил покупки подписок. </summary>
    private readonly IPersonalSubscriptionPurchasePolicyService _personalSubscriptionPurchasePolicyService;

    /// <summary> Создаёт сервис с его зависимостями. </summary>
    public SubscriptionService(
        IVehicleQueryService vehicles,
        IPersonalSubscriptionCommandService personalSubscriptionCommandService,
        IPersonalSubscriptionCheckoutService personalSubscriptionCheckoutService,
        IPersonalSubscriptionQueryService personalSubscriptionQueryService,
        IPersonalSubscriptionCatalogQueryService personalSubscriptionCatalogQueryService,
        IPersonalSubscriptionActivationService personalSubscriptionActivationService,
        IPersonalSubscriptionPurchasePolicyService personalSubscriptionPurchasePolicyService)
    {
        this._vehicles = vehicles;
        this._personalSubscriptionCommandService = personalSubscriptionCommandService;
        this._personalSubscriptionCheckoutService = personalSubscriptionCheckoutService;
        this._personalSubscriptionQueryService = personalSubscriptionQueryService;
        this._personalSubscriptionCatalogQueryService = personalSubscriptionCatalogQueryService;
        this._personalSubscriptionActivationService = personalSubscriptionActivationService;
        this._personalSubscriptionPurchasePolicyService = personalSubscriptionPurchasePolicyService;
    }

    /// <summary> Возвращает доступные пользователю тарифы. </summary>
    public Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync(string? userId = null) =>
        this._personalSubscriptionCatalogQueryService.GetAvailablePlansAsync(userId);

    /// <summary> Возвращает тарифы каталога личных подписок. </summary>
    public Task<IEnumerable<SubscriptionPlanDto>> GetCatalogPlansAsync(string? userId = null) =>
        this._personalSubscriptionCatalogQueryService.GetCatalogPlansAsync(userId);

    /// <summary> Возвращает тариф по умолчанию. </summary>
    public Task<SubscriptionPlanDto?> GetDefaultPlanAsync() =>
        this._personalSubscriptionCatalogQueryService.GetDefaultPlanAsync();

    /// <summary> Обеспечивает пользователю доступ к тарифу по умолчанию. </summary>
    public Task EnsureDefaultPlanAccessAsync(string userId) =>
        this._personalSubscriptionQueryService.EnsureDefaultPlanAccessAsync(userId);

    /// <summary> Возвращает тарифы, сгруппированные по группам. </summary>
    public Task<IEnumerable<SubscriptionPlanGroupDto>> GetGroupedPlansAsync(string? userId = null) =>
        this._personalSubscriptionCatalogQueryService.GetGroupedPlansAsync(userId);

    /// <summary> Проверяет возможность продления подписки пользователя. </summary>
    public Task<bool> CanExtendSubscriptionAsync(string userId) =>
        this._personalSubscriptionQueryService.CanExtendSubscriptionAsync(userId);

    /// <summary> Возвращает следующий тариф пользователя. </summary>
    public Task<SubscriptionPlanDto?> GetNextPlanAsync(string userId) =>
        this._personalSubscriptionQueryService.GetNextPlanAsync(userId);

    /// <summary> Проверяет, находится ли пользователь на максимальном тарифе. </summary>
    public Task<bool> IsMaxPlanAsync(string userId) =>
        this._personalSubscriptionQueryService.IsMaxPlanAsync(userId);

    /// <summary> Возвращает текущую подписку пользователя. </summary>
    public Task<UserSubscriptionDto?> GetUserSubscriptionAsync(string userId) =>
        this._personalSubscriptionQueryService.GetUserSubscriptionAsync(userId);

    /// <summary> Проверяет наличие активной подписки у пользователя. </summary>
    public Task<bool> HasActiveSubscriptionAsync(string userId) =>
        this._personalSubscriptionQueryService.HasActiveSubscriptionAsync(userId);

    /// <summary> Оформляет покупку личной подписки. </summary>
    public async Task<SubscriptionResult> PurchaseSubscriptionAsync(string userId, int planId, string? paymentMethodId = null, string? returnUrl = null, PaymentBuyerDto? buyer = null)
    {
        var result = await this._personalSubscriptionCheckoutService.PurchaseAsync(userId, planId, buyer, Guid.NewGuid(), returnUrl);

        if (result.Status == OpenCRM.Modules.Payments.Contracts.PaymentCreationStatus.Completed)
        {
            return SubscriptionResult.CreateSuccess();
        }

        return SubscriptionService.MapCheckoutResult(result);
    }

    /// <summary> Проверяет возможность покупки выбранного тарифа. </summary>
    public Task<SubscriptionResult> CanPurchasePlanAsync(string userId, int planId) =>
        this._personalSubscriptionPurchasePolicyService.CanPurchasePlanAsync(userId, planId);

    /// <summary> Оформляет продление личной подписки. </summary>
    public async Task<SubscriptionResult> ExtendSubscriptionAsync(string userId, string? returnUrl = null, PaymentBuyerDto? buyer = null)
    {
        var result = await this._personalSubscriptionCheckoutService.ExtendAsync(userId, buyer, Guid.NewGuid(), returnUrl);

        if (result.Status == OpenCRM.Modules.Payments.Contracts.PaymentCreationStatus.Completed)
        {
            return SubscriptionResult.CreateSuccess();
        }

        return SubscriptionService.MapCheckoutResult(result);
    }

    /// <summary> Преобразует результат создания платежа в результат подписки. </summary>
    private static SubscriptionResult MapCheckoutResult(OpenCRM.Modules.Payments.Contracts.PaymentCreationResult result)
    {
        if (result.Status == OpenCRM.Modules.Payments.Contracts.PaymentCreationStatus.PaymentRequired
            && result.PaymentUrl is not null
            && result.PaymentId is not null)
        {
            return SubscriptionResult.CreatePaymentRequired(result.PaymentUrl, result.PaymentId);
        }

        return SubscriptionResult.CreateFailed(result.Message ?? "Платеж не удалось создать.");
    }

    /// <summary> Активирует тариф для тестирования. </summary>
    public Task<SubscriptionResult> ActivateSubscriptionForTestingAsync(string userId, int planId) =>
        this._personalSubscriptionCommandService.ActivateSubscriptionForTestingAsync(userId, planId);

    /// <summary> Продлевает подписку для тестирования. </summary>
    public Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId) =>
        this._personalSubscriptionCommandService.ExtendSubscriptionForTestingAsync(userId);

    /// <summary> Отменяет подписку для тестирования. </summary>
    public Task<SubscriptionResult> CancelSubscriptionForTestingAsync(string userId) =>
        this._personalSubscriptionCommandService.CancelSubscriptionForTestingAsync(userId);

    /// <summary> Активирует новую подписку после оплаты. </summary>
    public Task<SubscriptionResult> ActivateNewSubscription(string userId, int planId, string paymentId) =>
        this._personalSubscriptionActivationService.ActivateNewSubscriptionAsync(userId, planId, paymentId);

    /// <summary> Активирует продление подписки после оплаты. </summary>
    public Task<SubscriptionResult> ActivateExtendedSubscription(string userId, int planId, string paymentId) =>
        this._personalSubscriptionActivationService.ActivateExtendedSubscriptionAsync(userId, planId, paymentId);

    /// <summary> Возвращает информацию о продлении подписки пользователя. </summary>
    public Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(string userId) =>
        this._personalSubscriptionQueryService.GetSubscriptionExtensionInfoAsync(userId);

    /// <summary> Проверяет, может ли пользователь добавить технику. </summary>
    public async Task<bool> CanUserAddVehicleAsync(string userId)
    {
        var vehicleCount = await this._vehicles.GetVehiclesCountAsync(userId);
        var vehicleLimit = await this.GetUserVehicleLimitAsync(userId);

        return vehicleCount < vehicleLimit;
    }

    /// <summary> Возвращает лимит техники пользователя. </summary>
    public Task<int> GetUserVehicleLimitAsync(string userId) =>
        this._personalSubscriptionQueryService.GetUserVehicleLimitAsync(userId);

    /// <summary> Деактивирует истёкшие подписки. </summary>
    public Task CheckAndDeactivateExpiredSubscriptionsAsync() =>
        this._personalSubscriptionCommandService.CheckAndDeactivateExpiredSubscriptionsAsync();

    /// <summary> Возвращает объединённую историю подписок пользователя. </summary>
    public Task<IEnumerable<SubscriptionHistoryDto>> GetCombinedSubscriptionHistoryAsync(string userId) =>
        this._personalSubscriptionQueryService.GetCombinedSubscriptionHistoryAsync(userId);

    /// <summary> Возвращает демо-тариф. </summary>
    public Task<SubscriptionPlanDto?> GetDemoPlanAsync() =>
        this._personalSubscriptionCatalogQueryService.GetDemoPlanAsync();

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    public Task<SubscriptionResult> GrantDemoSubscriptionAsync(string userId) =>
        this._personalSubscriptionCommandService.GrantDemoSubscriptionAsync(userId);

    /// <summary> Выдаёт пользователю демо-подписку от имени администратора. </summary>
    public Task<SubscriptionResult> GrantDemoSubscriptionByAdminAsync(string userId) =>
        this._personalSubscriptionCommandService.GrantDemoSubscriptionByAdminAsync(userId);

    /// <summary> Назначает пользователю тариф от имени администратора. </summary>
    public Task<SubscriptionResult> AssignSubscriptionByAdminAsync(string userId, int planId, string adminName) =>
        this._personalSubscriptionCommandService.AssignSubscriptionByAdminAsync(userId, planId, adminName);

    /// <summary> Возвращает пользователя на тариф по умолчанию от имени администратора. </summary>
    public Task<SubscriptionResult> ReturnUserToDefaultPlanByAdminAsync(string userId, string adminName) =>
        this._personalSubscriptionCommandService.ReturnUserToDefaultPlanByAdminAsync(userId, adminName);

    /// <summary> Проверяет доступность демо-подписки для пользователя. </summary>
    public Task<(bool Available, string Message)> CheckDemoSubscriptionAvailabilityAsync(string userId) =>
        this._personalSubscriptionQueryService.CheckDemoSubscriptionAvailabilityAsync(userId);
}
