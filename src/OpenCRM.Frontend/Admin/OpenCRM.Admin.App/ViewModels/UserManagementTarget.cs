using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

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
    public string CurrentPlanName { get; init; } = string.Empty;

    public DateTime? CurrentPlanEndDate { get; init; }

    public string? CurrentPlanLabelKey { get; init; }

    public bool HasCurrentPlan { get; init; }

    public string? NameKey { get; init; }

    public string DisplayName => this.NameKey == null ? this.Name : AdminLocalization.Instance[this.NameKey];

    public string CurrentPlanLabel => !this.HasCurrentPlan
        ? string.Empty
        : this.CurrentPlanLabelKey is "userManagement.freePlanTarget" or "userManagement.noActivePlanTarget"
            ? AdminLocalization.Instance[this.CurrentPlanLabelKey]
            : this.CurrentPlanLabelKey == null
                ? this.CurrentPlanName
                : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                AdminLocalization.Instance[this.CurrentPlanLabelKey], this.CurrentPlanName,
                this.CurrentPlanEndDate?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty);

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
        var planName = activeSubscription?.PlanName ?? organization.CurrentPlanName ?? string.Empty;
        var planEndDate = activeSubscription?.EndDate ?? organization.CurrentPlanEndDate;
        var isFree = activeSubscription == null && !organization.HasActiveSubscription
            && isCurrentFreeScenario && organization.IsActive && !organization.IsArchived && !organization.IsDeleted;
        var hasPlan = canViewBilling && (activeSubscription != null || organization.HasActiveSubscription);

        return new UserManagementTarget(organization.OrganizationName, "organizations",
            organization.OrganizationId.ToString(), organization.IsActive && !organization.IsArchived && !organization.IsDeleted,
            Math.Max(subscriptions.Length, organization.HasActiveSubscription ? 1 : 0),
            activeSubscription?.Id ?? organization.CurrentSubscriptionId,
            activeSubscription?.EndDate ?? organization.CurrentPlanEndDate,
            subscriptions.Any(subscription => subscription.IsDemoPlan) || organization.CurrentPlanIsDemo)
        {
            CurrentPlanName = planName,
            CurrentPlanEndDate = planEndDate,
            CurrentPlanLabelKey = isFree ? "userManagement.freePlanTarget"
                : hasPlan && planEndDate.HasValue ? "userManagement.planTargetUntil"
                : hasPlan ? null : "userManagement.noActivePlanTarget",
            HasCurrentPlan = canViewBilling,
            NameKey = null,
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
        var hasPlan = canViewBilling && activeSubscription != null;

        return new UserManagementTarget(AdminLocalization.Instance["userManagement.personalAccess"], "users", overview.Profile.Id,
            !overview.Profile.IsDeleted, subscriptions.Length, activeSubscription?.Id, activeSubscription?.EndDate,
            subscriptions.Any(subscription => subscription.IsDemoPlan))
        {
            CurrentPlanName = activeSubscription?.PlanName ?? string.Empty,
            CurrentPlanEndDate = activeSubscription?.EndDate,
            CurrentPlanLabelKey = hasPlan ? "userManagement.planTargetUntil" : "userManagement.noActivePlanTarget",
            HasCurrentPlan = canViewBilling,
            NameKey = "userManagement.personalAccess",
            CanGrantDemo = true,
            CanExtendSubscription = subscriptions.Length == 1 && activeSubscription?.Id > 0
                && activeSubscription.EndDate > DateTime.MinValue
        };
    }
}
