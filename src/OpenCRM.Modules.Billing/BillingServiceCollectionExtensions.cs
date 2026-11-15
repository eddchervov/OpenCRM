using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions;

namespace OpenCRM.Modules.Billing;

/// <summary> Методы расширения регистрации модуля биллинга. </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля биллинга в контейнере. </summary>
    public static IServiceCollection AddOpenCRMBilling(this IServiceCollection services)
    {
        services.TryAddScoped<IFeatureDefaultsInitializer, FeatureDefaultsInitializer>();
        services.TryAddSingleton<IPlatformCommercialPolicy, PlatformCommercialPolicy>();
        services.AddScoped<IFeatureAdminService, FeatureAdminService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionPlanAdminService, SubscriptionPlanAdminService>();
        services.AddScoped<ISubscriptionExpiryReminderService, SubscriptionExpiryReminderService>();
        services.AddScoped<IOrganizationSubscriptionService, OrganizationSubscriptionService>();
        services.AddScoped<OrganizationEntitlementReader>();
        services.AddScoped<IOrganizationEntitlementService, OrganizationEntitlementService>();
        services.AddScoped<IOrganizationSubscriptionCatalogQueryService, OrganizationSubscriptionCatalogQueryService>();
        services.AddScoped<IOrganizationSubscriptionExtensionQueryService, OrganizationSubscriptionExtensionQueryService>();
        services.AddScoped<PersonalSubscriptionReader>();
        services.AddScoped<IPersonalSubscriptionQueryService, PersonalSubscriptionQueryService>();
        services.AddScoped<IPersonalSubscriptionCatalogQueryService, PersonalSubscriptionCatalogQueryService>();
        services.AddScoped<IPersonalSubscriptionPurchasePolicyService, PersonalSubscriptionPurchasePolicyService>();
        services.AddScoped<IPersonalSubscriptionActivationService, PersonalSubscriptionActivationService>();
        services.AddScoped<IOrganizationSubscriptionPurchasePolicyService, OrganizationSubscriptionPurchasePolicyService>();
        services.AddScoped<IPersonalSubscriptionCheckoutService, PersonalSubscriptionCheckoutService>();
        services.AddScoped<IOrganizationSubscriptionCheckoutService, OrganizationSubscriptionCheckoutService>();
        services.AddScoped<IOrganizationSubscriptionActivationService, OrganizationSubscriptionActivationService>();
        services.AddScoped<IPersonalSubscriptionCommandService, PersonalSubscriptionCommandService>();
        services.AddScoped<IOrganizationSubscriptionCommandService, OrganizationSubscriptionCommandService>();
        services.AddScoped<IAdminBillingQueryService, AdminBillingQueryService>();
        services.AddScoped<IAdminNpdReceiptService, AdminNpdReceiptService>();
        services.AddScoped<IAdminPaymentCommandService, AdminPaymentCommandService>();
        services.AddScoped<IAdminSubscriptionCommandService, AdminSubscriptionCommandService>();
        services.AddScoped<AdminPersonalSubscriptionCommandService>();
        services.AddScoped<AdminOrganizationSubscriptionCommandService>();
        services.AddScoped<AdminSubscriptionAdjustmentService>();
        services.AddScoped<AdminSubscriptionMutation>();
        services.AddScoped<BillingPaymentTransactionRunner>();
        services.AddScoped<IFeatureAccessService, FeatureAccessService>();
        services.AddScoped<IOrganizationPromoAccessGrantPort, OrganizationPromoAccessGrantService>();

        return services;
    }
}
