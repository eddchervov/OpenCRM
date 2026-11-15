namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Подписка организации в разделе биллинга админки. </summary>
public sealed class AdminBillingOrganizationSubscriptionDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Код сценария организации. </summary>
    public string PrimaryScenarioCode { get; init; } = string.Empty;

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary> Имя владельца организации. </summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary> Идентификатор пользователя — владельца организации. </summary>
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary> Идентификатор тарифного плана подписки. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Признак демонстрационного тарифного плана. </summary>
    public bool IsDemoPlan { get; init; }

    /// <summary> Источник оформления подписки. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Признак автоматического продления подписки. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive { get; init; }

    /// <summary> Признак скорого окончания подписки. </summary>
    public bool IsExpiringSoon { get; init; }

    /// <summary> Количество дней до окончания подписки. </summary>
    public int DaysLeft { get; init; }
}

