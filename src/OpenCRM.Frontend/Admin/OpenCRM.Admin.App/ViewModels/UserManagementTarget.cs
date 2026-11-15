using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Цель управления подписками пользователя. </summary>
public sealed record UserManagementTarget(
    string Name,
    string Scope,
    string TargetId,
    bool IsAvailable,
    int ActiveSubscriptionCount,
    int? SubscriptionId,
    DateTime? EndDate,
    bool IsDemo)
{
    /// <summary> Признак активной подписки. </summary>
    public bool HasActiveSubscription => this.ActiveSubscriptionCount > 0;

    /// <summary> Краткое описание тарифа цели. </summary>
    public string CurrentPlanLabel { get; init; } = string.Empty;

    /// <summary> Признак допустимости выдачи демо для исторического сценария. </summary>
    public bool CanGrantDemo { get; init; }

    /// <summary> Признак перехода организации на Free после окончания PRO. </summary>
    public bool UsesFreeAfterSubscription { get; init; }

    /// <summary> Признак наличия ровно одной подписки с известными параметрами. </summary>
    public bool CanExtendSubscription { get; init; }

    internal static UserManagementTarget Organization(
        AdminUserOverview overview,
        AdminUserOverviewOrganization organization,
        bool canViewBilling)
    {
        var subscriptions = overview.Subscriptions.Organizations
            .Where(subscription => subscription.OrganizationId == organization.OrganizationId && subscription.IsActive)
            .ToArray();
        var activeSubscription = subscriptions.OrderByDescending(subscription => subscription.EndDate).FirstOrDefault();
        var isCurrentFreeScenario = string.Equals(organization.PrimaryScenarioCode, "detailing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(organization.PrimaryScenarioCode, "service-station", StringComparison.OrdinalIgnoreCase);
        var planLabel = !canViewBilling
            ? string.Empty
            : activeSubscription == null
                ? organization.HasActiveSubscription
                    ? $"{organization.CurrentPlanName} · до {organization.CurrentPlanEndDate:d}"
                    : isCurrentFreeScenario && organization.IsActive && !organization.IsArchived && !organization.IsDeleted
                        ? "Free · без ограничения срока"
                        : "Нет активной подписки"
                : $"{activeSubscription.PlanName} · до {activeSubscription.EndDate:d}";

        return new UserManagementTarget(organization.OrganizationName, "organizations",
            organization.OrganizationId.ToString(), organization.IsActive && !organization.IsArchived && !organization.IsDeleted,
            Math.Max(subscriptions.Length, organization.HasActiveSubscription ? 1 : 0),
            activeSubscription?.Id ?? organization.CurrentSubscriptionId,
            activeSubscription?.EndDate ?? organization.CurrentPlanEndDate,
            subscriptions.Any(subscription => subscription.IsDemoPlan) || organization.CurrentPlanIsDemo)
        {
            CurrentPlanLabel = planLabel,
            CanGrantDemo = !isCurrentFreeScenario,
            UsesFreeAfterSubscription = isCurrentFreeScenario,
            CanExtendSubscription = subscriptions.Length == 1 && activeSubscription?.Id > 0
                && activeSubscription.EndDate > DateTime.MinValue
        };
    }

    internal static UserManagementTarget User(AdminUserOverview overview, bool canViewBilling)
    {
        var subscriptions = overview.Subscriptions.Users
            .Where(subscription => subscription.IsActive)
            .ToArray();
        var activeSubscription = subscriptions.OrderByDescending(subscription => subscription.EndDate).FirstOrDefault();
        var planLabel = !canViewBilling
            ? string.Empty
            : activeSubscription == null
                ? "Нет активного тарифа"
                : $"{activeSubscription.PlanName} · до {activeSubscription.EndDate:d}";

        return new UserManagementTarget("Личный доступ пользователя", "users", overview.Profile.Id,
            !overview.Profile.IsDeleted, subscriptions.Length, activeSubscription?.Id, activeSubscription?.EndDate,
            subscriptions.Any(subscription => subscription.IsDemoPlan))
        {
            CurrentPlanLabel = planLabel,
            CanGrantDemo = true,
            CanExtendSubscription = subscriptions.Length == 1 && activeSubscription?.Id > 0
                && activeSubscription.EndDate > DateTime.MinValue
        };
    }
}
