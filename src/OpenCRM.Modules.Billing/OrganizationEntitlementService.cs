using Helpers;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис проверки прав организации на подписки и возможности. </summary>
internal sealed class OrganizationEntitlementService(
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    OrganizationEntitlementReader reader,
    IPlatformCommercialPolicy? platformCommercialPolicy = null) : IOrganizationEntitlementService
{
    /// <summary> Возвращает текущую подписку организации. </summary>
    public async Task<OrganizationSubscriptionDto?> GetCurrentAsync(string userId, Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await reader.GetCurrentAsync(organizationId);
    }

    /// <summary> Возвращает действующий тариф организации. </summary>
    public async Task<SubscriptionPlanDto?> GetEffectivePlanAsync(string userId, Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await reader.GetEffectivePlanAsync(organizationId);
    }

    /// <summary> Проверяет наличие активной подписки организации. </summary>
    public async Task<bool> HasActiveSubscriptionAsync(string userId, Guid organizationId)
    {
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await reader.HasActiveSubscriptionAsync(organizationId);
    }

    /// <summary> Возвращает лимит техники организации. </summary>
    public async Task<int> GetVehicleLimitAsync(string userId, Guid organizationId, int fallbackLimit)
    {
        await reader.EnsureCanReadAsync(userId, organizationId);

        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return 0;
        }

        var snapshot = await this.GetFeatureSnapshotCoreAsync(organizationId);
        return snapshot.Features
            .FirstOrDefault(feature => feature.Code == FeatureCodes.WorkspaceVehiclesLimit)
            ?.IntValue ?? fallbackLimit;
    }

    /// <summary> Возвращает лимит участников организации. </summary>
    public async Task<int> GetMembersLimitAsync(string userId, Guid organizationId, int fallbackLimit)
    {
        await reader.EnsureCanReadAsync(userId, organizationId);

        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return 0;
        }

        var snapshot = await this.GetFeatureSnapshotCoreAsync(organizationId);
        return snapshot.Features
            .FirstOrDefault(feature => feature.Code == FeatureCodes.WorkspaceMembersLimit)
            ?.IntValue ?? fallbackLimit;
    }

    /// <summary> Проверяет наличие возможности у организации. </summary>
    public async Task<bool> HasFeatureAsync(string userId, Guid organizationId, string featureCode)
    {
        await reader.EnsureCanReadAsync(userId, organizationId);

        var snapshot = await this.GetFeatureSnapshotCoreAsync(organizationId);
        return snapshot.Features.FirstOrDefault(feature => feature.Code == featureCode)?.BoolValue ?? false;
    }

    /// <summary> Возвращает снимок возможностей организации. </summary>
    public async Task<FeatureAccessSnapshotDto> GetFeatureSnapshotAsync(string userId, Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await this.GetFeatureSnapshotCoreAsync(organizationId);
    }

    /// <summary> Проверяет доступность демо-подписки организации. </summary>
    public async Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(string userId, Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await reader.CheckDemoAvailabilityAsync(organizationId);
    }

    /// <summary> Возвращает права рабочего пространства организации. </summary>
    public async Task<Contracts.Dtos.OrganizationWorkspaceEntitlementDto> GetWorkspaceEntitlementAsync(
        string userId,
        Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        return await reader.GetWorkspaceEntitlementAsync(organizationId);
    }

    /// <summary> Возвращает снимок возможностей организации без проверки прав. </summary>
    private async Task<FeatureAccessSnapshotDto> GetFeatureSnapshotCoreAsync(Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        return await reader.GetFeatureSnapshotAsync(organizationId);
    }
}
