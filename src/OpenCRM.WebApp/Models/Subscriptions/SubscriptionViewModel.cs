using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.WebApp.Models.Subscriptions;

/// <summary> Модель страницы подписок пользователя. </summary>
public class SubscriptionViewModel
{
    /// <summary> Доступные тарифные планы. </summary>
    public IEnumerable<SubscriptionPlanDto> AvailablePlans { get; set; } = new List<SubscriptionPlanDto>();

    /// <summary> Тарифные планы, сгруппированные по группам. </summary>
    public IEnumerable<SubscriptionPlanGroupDto> GroupedPlans { get; set; } = new List<SubscriptionPlanGroupDto>();

    /// <summary> Группы бизнес-тарифных планов. </summary>
    public IEnumerable<SubscriptionPlanGroupDto> BusinessPlanGroups { get; set; } = new List<SubscriptionPlanGroupDto>();

    /// <summary> Текущая подписка пользователя. </summary>
    public UserSubscriptionDto? CurrentSubscription { get; set; }

    /// <summary> Тарифный план по умолчанию для выбора. </summary>
    public SubscriptionPlanDto? DefaultPlan { get; set; }

    /// <summary> Следующий тарифный план для перехода. </summary>
    public SubscriptionPlanDto? NextPlan { get; set; }

    /// <summary> Признак того, что выбран максимальный тариф. </summary>
    public bool IsMaxPlan { get; set; }

    /// <summary> Признак наличия активной подписки. </summary>
    public bool HasActiveSubscription { get; set; }

    /// <summary> Признак возможности продления подписки. </summary>
    public bool CanExtendSubscription { get; set; }

    /// <summary> Количество дней до окончания подписки. </summary>
    public int DaysUntilExpiry { get; set; }

    /// <summary> Сообщение о продлении подписки. </summary>
    public string? ExtensionMessage { get; set; }

    /// <summary> Сообщение об успешной операции. </summary>
    public string? SuccessMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }

    /// <summary> Информационное сообщение. </summary>
    public string? InfoMessage { get; set; }

    /// <summary> Предупреждающее сообщение. </summary>
    public string? WarningMessage { get; set; }

    /// <summary> Признак того, что текущий тариф демонстрационный. </summary>
    public bool IsDemoPlan { get; set; }

    /// <summary> Количество оставшихся дней демонстрационного тарифа. </summary>
    public int DemoDaysLeft { get; set; }

    /// <summary> Признак доступности демонстрационной подписки. </summary>
    public bool DemoSubscriptionAvailable { get; set; }

    /// <summary> Сообщение о доступности демонстрационной подписки. </summary>
    public string? DemoAvailabilityMessage { get; set; }

    /// <summary> Признак доступности тестовых подписок для пользователя. </summary>
    public bool CanUseTestSubscriptions { get; set; }

    /// <summary> Признак наличия бизнес-тарифов. </summary>
    public bool HasBusinessPlans { get; set; }

    /// <summary> Снимок доступа к возможностям текущего тарифа. </summary>
    public FeatureAccessSnapshotDto? CurrentFeatureSnapshot { get; set; }

    /// <summary> Текущий лимит автомобилей по тарифу. </summary>
    public int CurrentVehicleLimit { get; set; }

    /// <summary> Адрес страницы с бизнес-тарифами. </summary>
    public string BusinessPlansTargetUrl { get; set; } = "/organizations";

    /// <summary> Текст ссылки на страницу с бизнес-тарифами. </summary>
    public string BusinessPlansTargetText { get; set; } = "Открыть организации";
}

