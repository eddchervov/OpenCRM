using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Фасадный сервис подписок организаций. </summary>
public class OrganizationSubscriptionService(
    IOrganizationSubscriptionCatalogQueryService organizationSubscriptionCatalogQueryService,
    IOrganizationEntitlementService organizationEntitlementService,
    IWorkspaceAccessService workspaceAccessService,
    IOrganizationSubscriptionCheckoutService subscriptionCheckoutService,
    IOrganizationSubscriptionExtensionQueryService subscriptionExtensionQueryService,
    IOrganizationSubscriptionCommandService subscriptionCommandService,
    IOrganizationSubscriptionActivationService subscriptionActivationService,
    IOrganizationSubscriptionPurchasePolicyService subscriptionPurchasePolicyService) : IOrganizationSubscriptionService
{
    /// <summary> Зависимость сервиса оформления покупки подписки. </summary>
    private readonly IOrganizationSubscriptionCheckoutService _subscriptionCheckoutService = subscriptionCheckoutService;

    /// <summary> Зависимость сервиса запросов о продлении подписки. </summary>
    private readonly IOrganizationSubscriptionExtensionQueryService _subscriptionExtensionQueryService = subscriptionExtensionQueryService;

    /// <summary> Зависимость сервиса команд по подпискам организаций. </summary>
    private readonly IOrganizationSubscriptionCommandService _subscriptionCommandService = subscriptionCommandService;

    /// <summary> Зависимость сервиса активации подписок организаций. </summary>
    private readonly IOrganizationSubscriptionActivationService _subscriptionActivationService = subscriptionActivationService;

    /// <summary> Зависимость сервиса правил покупки тарифов. </summary>
    private readonly IOrganizationSubscriptionPurchasePolicyService _subscriptionPurchasePolicyService = subscriptionPurchasePolicyService;

    /// <summary> Возвращает текущую подписку организации. </summary>
    public Task<OrganizationSubscriptionDto?> GetCurrentAsync(string userId, Guid organizationId) =>
        organizationEntitlementService.GetCurrentAsync(userId, organizationId);

    /// <summary> Возвращает фактически действующий тариф организации. </summary>
    public Task<SubscriptionPlanDto?> GetEffectivePlanAsync(string userId, Guid organizationId) =>
        organizationEntitlementService.GetEffectivePlanAsync(userId, organizationId);

    /// <summary> Возвращает доступные организации тарифы. </summary>
    public Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(
        string userId,
        Guid organizationId,
        bool includeUnavailableDemoForTesting = false) =>
        organizationSubscriptionCatalogQueryService.GetAvailablePlansAsync(userId, organizationId, includeUnavailableDemoForTesting);

    /// <summary> Проверяет возможность покупки выбранного тарифа. </summary>
    public Task<SubscriptionResult> CanPurchasePlanAsync(string userId, Guid organizationId, int planId) =>
        this._subscriptionPurchasePolicyService.CanPurchasePlanAsync(userId, organizationId, planId);

    /// <summary> Оформляет покупку подписки организации. </summary>
    public async Task<SubscriptionResult> PurchaseSubscriptionAsync(string userId, Guid organizationId, int planId, string? paymentMethodId = null, PaymentBuyerDto? buyer = null)
    {
        var result = await this._subscriptionCheckoutService.PurchaseAsync(userId, organizationId, planId, buyer, Guid.NewGuid());

        if (result.Status == OpenCRM.Modules.Payments.Contracts.PaymentCreationStatus.Completed)
        {
            return SubscriptionResult.CreateSuccess();
        }

        return OrganizationSubscriptionService.MapCheckoutResult(result);
    }

    /// <summary> Проверяет возможность продления подписки организации. </summary>
    public Task<bool> CanExtendSubscriptionAsync(string userId, Guid organizationId) =>
        this._subscriptionExtensionQueryService.CanExtendSubscriptionAsync(userId, organizationId);

    /// <summary> Возвращает информацию о продлении подписки организации. </summary>
    public Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(
        string userId,
        Guid organizationId) =>
        this._subscriptionExtensionQueryService.GetSubscriptionExtensionInfoAsync(userId, organizationId);

    /// <summary> Оформляет продление подписки организации. </summary>
    public async Task<SubscriptionResult> ExtendSubscriptionAsync(string userId, Guid organizationId, PaymentBuyerDto? buyer = null)
    {
        var result = await this._subscriptionCheckoutService.ExtendAsync(userId, organizationId, buyer, Guid.NewGuid());

        if (result.Status == OpenCRM.Modules.Payments.Contracts.PaymentCreationStatus.Completed)
        {
            return SubscriptionResult.CreateSuccess();
        }

        return OrganizationSubscriptionService.MapCheckoutResult(result);
    }

    /// <summary> Возвращает доступ пользователя к рабочему пространству организации. </summary>
    public Task<OrganizationWorkspaceAccessDto> GetWorkspaceAccessAsync(string userId, Guid organizationId) =>
        workspaceAccessService.GetWorkspaceAccessAsync(userId, organizationId);

    /// <summary> Проверяет право чтения с указанным кодом разрешения. </summary>
    public Task EnsureWorkspaceCanReadAsync(string userId, Guid organizationId, string permissionCode) =>
        workspaceAccessService.EnsureWorkspaceCanReadAsync(userId, organizationId, permissionCode);

    /// <summary> Проверяет право записи в рабочем пространстве организации. </summary>
    public Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId) =>
        workspaceAccessService.EnsureWorkspaceCanWriteAsync(userId, organizationId);

    /// <summary> Проверяет право записи с указанным кодом разрешения. </summary>
    public Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId, string permissionCode) =>
        workspaceAccessService.EnsureWorkspaceCanWriteAsync(userId, organizationId, permissionCode);

    /// <summary> Проверяет наличие активной подписки у организации. </summary>
    public Task<bool> HasActiveSubscriptionAsync(string userId, Guid organizationId) =>
        organizationEntitlementService.HasActiveSubscriptionAsync(userId, organizationId);

    /// <summary> Возвращает лимит техники для организации с запасным значением. </summary>
    public Task<int> GetVehicleLimitAsync(string userId, Guid organizationId, int fallbackLimit) =>
        organizationEntitlementService.GetVehicleLimitAsync(userId, organizationId, fallbackLimit);

    /// <summary> Возвращает лимит участников организации с запасным значением. </summary>
    public Task<int> GetMembersLimitAsync(string userId, Guid organizationId, int fallbackLimit) =>
        organizationEntitlementService.GetMembersLimitAsync(userId, organizationId, fallbackLimit);

    /// <summary> Проверяет доступность функции по её коду для организации. </summary>
    public Task<bool> HasFeatureAsync(string userId, Guid organizationId, string featureCode) =>
        organizationEntitlementService.HasFeatureAsync(userId, organizationId, featureCode);

    /// <summary> Возвращает снимок доступности функций организации. </summary>
    public Task<FeatureAccessSnapshotDto> GetFeatureSnapshotAsync(string userId, Guid organizationId) =>
        organizationEntitlementService.GetFeatureSnapshotAsync(userId, organizationId);

    /// <summary> Проверяет доступность демо-режима для организации. </summary>
    public Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(string userId, Guid organizationId) =>
        organizationEntitlementService.CheckDemoAvailabilityAsync(userId, organizationId);

    /// <summary> Активирует демо-подписку организации. </summary>
    public Task<SubscriptionResult> ActivateDemoAsync(string userId, Guid organizationId, int planId, int? durationDays = null) =>
        this._subscriptionCommandService.ActivateDemoAsync(userId, organizationId, planId, durationDays);

    /// <summary> Активирует тариф организации для тестирования. </summary>
    public Task<SubscriptionResult> ActivateForTestingAsync(string userId, Guid organizationId, int planId) =>
        this._subscriptionCommandService.ActivateForTestingAsync(userId, organizationId, planId);

    /// <summary> Продлевает подписку организации для тестирования. </summary>
    public Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId, Guid organizationId) =>
        this._subscriptionCommandService.ExtendSubscriptionForTestingAsync(userId, organizationId);

    /// <summary> Активирует новую подписку организации после оплаты. </summary>
    public Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId) =>
        this._subscriptionActivationService.ActivateNewSubscriptionAsync(userId, organizationId, planId, paymentId);

    /// <summary> Активирует продление подписки организации после оплаты. </summary>
    public Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId) =>
        this._subscriptionActivationService.ActivateExtendedSubscriptionAsync(userId, organizationId, planId, paymentId);

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
}
