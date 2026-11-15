namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Тарифные возможности рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceEntitlementDto
{
    /// <summary> Признак рабочего бесплатного тарифа детейлинга или СТО. </summary>
    public bool IsFreePlan { get; init; }

    /// <summary> Признак активной подписки организации. </summary>
    public bool HasActiveSubscription { get; init; }

    /// <summary> Признак наличия истории подписок. </summary>
    public bool HasSubscriptionHistory { get; init; }

    /// <summary> Признак базового рабочего пространства. </summary>
    public bool IsBaseWorkspace { get; init; }
}
