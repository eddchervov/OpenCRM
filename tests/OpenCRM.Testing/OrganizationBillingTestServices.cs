using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Testing;

public static class OrganizationBillingTestServices
{
    public static IOrganizationEntitlementService CreateEntitlement(OpenCRMContext context) =>
        OrganizationBillingTestServices.CreateService<IOrganizationEntitlementService>(context);

    public static IOrganizationSubscriptionCatalogQueryService CreateCatalog(OpenCRMContext context, IPromoCodeService promotions) =>
        OrganizationBillingTestServices.CreateService<IOrganizationSubscriptionCatalogQueryService>(context, promotions);

    public static IOrganizationSubscriptionPurchasePolicyService CreatePurchasePolicy(
        OpenCRMContext context,
        IOrganizationPermissionService permissions) =>
        OrganizationBillingTestServices.CreateService<IOrganizationSubscriptionPurchasePolicyService>(context, permissions: permissions);

    public static IOrganizationSubscriptionExtensionQueryService CreateExtensionQuery(
        OpenCRMContext context,
        IOrganizationPermissionService permissions) =>
        OrganizationBillingTestServices.CreateService<IOrganizationSubscriptionExtensionQueryService>(context, permissions: permissions);

    public static IOrganizationSubscriptionCommandService CreateCommands(
        OpenCRMContext context,
        IOrganizationPermissionService permissions,
        IOrganizationAuditService? audit = null) =>
        OrganizationBillingTestServices.CreateService<IOrganizationSubscriptionCommandService>(context, permissions: permissions, audit: audit);

    private static TService CreateService<TService>(
        OpenCRMContext context,
        IPromoCodeService? promotions = null,
        IOrganizationPermissionService? permissions = null,
        IOrganizationAuditService? audit = null)
        where TService : notnull
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenCRMBilling();
        // Тесты биллинга проверяют коммерческий режим: он включён явно, как и тарифы в DefaultsInitializer.
        services.AddSingleton<IPlatformCommercialPolicy>(new CommercialEnabledPolicy());
        services.AddSingleton(context);
        services.AddSingleton<IFeatureDefaultsInitializer>(new DefaultsInitializer(context));
        services.AddSingleton(promotions ?? Mock.Of<IPromoCodeService>());
        services.AddSingleton(permissions ?? Mock.Of<IOrganizationPermissionService>());
        services.AddSingleton(audit ?? Mock.Of<IOrganizationAuditService>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TService>();
    }

    public static IWorkspaceAccessService CreateAccess(OpenCRMContext context, IOrganizationPermissionService permissions) =>
        new WorkspaceAccessService(context, new DefaultsInitializer(context),
            OrganizationBillingTestServices.CreateEntitlement(context), permissions, new CommercialEnabledPolicy());

    /// <summary> Политика коммерческого режима для тестов механизма тарифов: коммерция включена явно. </summary>
    private sealed class CommercialEnabledPolicy : IPlatformCommercialPolicy
    {
        /// <summary> Коммерческие тарифы включены. </summary>
        public bool CommercialEnabled => true;
    }

    public sealed class DefaultsInitializer(OpenCRMContext context) : IFeatureDefaultsInitializer
    {
        // Тесты механизма коммерческих тарифов включают его явно: базовая поставка
        // тарифы и квоты не создаёт.
        public async Task EnsureDefaultsAsync()
        {
            await FeatureBootstrapper.EnsureDefaultsAsync(context);
            await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        }
    }
}
