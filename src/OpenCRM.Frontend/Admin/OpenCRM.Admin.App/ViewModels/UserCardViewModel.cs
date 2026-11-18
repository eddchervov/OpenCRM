using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления карточки пользователя. </summary>
public partial class UserCardViewModel : ObservableObject
{
    /// <summary> Обзорные данные пользователя. </summary>
    private readonly AdminUserOverview _overview;

    /// <summary> Доступ к разделу биллинга. </summary>
    private readonly bool _canViewBilling;
    private readonly Action<string>? _activityRequested;

    /// <summary> Создаёт карточку пользователя на основе обзора. </summary>
    public UserCardViewModel(
        AdminUserOverview overview,
        bool canViewBilling,
        UserManagementViewModel? management = null,
        Action<string>? activityRequested = null)
    {
        this._overview = overview;
        this._canViewBilling = canViewBilling;
        this._activityRequested = activityRequested;
        this.Management = management;
        this.HeaderMetrics = this.CreateHeaderMetrics();
        this.Timeline = this.CreateTimeline();
        this.Tabs = this.CreateTabs();
        this.SelectedTab = this.Tabs[0];
    }

    /// <summary> Заголовок карточки. </summary>
    public string Title => UserDisplayFormatting.Text(this._overview.Profile.Email ?? this._overview.Profile.Phone
        ?? AdminLocalization.Instance["userCard.user"]);

    /// <summary> Административные действия над открытой карточкой. </summary>
    public UserManagementViewModel? Management { get; }

    /// <summary> Открывает журнал действий этого пользователя. </summary>
    [RelayCommand]
    private void OpenActivity() => this._activityRequested?.Invoke(this._overview.Profile.Id);

    /// <summary> Подзаголовок карточки. </summary>
    public string Subtitle => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        AdminLocalization.Instance["userCard.userId"], this._overview.Profile.Id);

    /// <summary> Подпись статуса пользователя. </summary>
    public string StatusLabel => this.GetStatusLabel();

    /// <summary> Признак предупреждения о статусе. </summary>
    public bool HasStatusWarning => this._overview.Profile.IsDeleted
        || this._overview.Profile.LockoutEnabled && this._overview.Profile.LockoutEnd > DateTimeOffset.UtcNow;

    /// <summary> Вкладки карточки пользователя. </summary>
    public IReadOnlyList<UserCardTab> Tabs { get; private set; }

    /// <summary> Метрики в шапке карточки. </summary>
    public IReadOnlyList<DashboardMetric> HeaderMetrics { get; private set; }

    /// <summary> Хронология событий пользователя. </summary>
    public IReadOnlyList<UserCardItem> Timeline { get; private set; }

    public void RefreshLocalizedValues()
    {
        var selectedTabId = this.SelectedTab?.Id;
        this.HeaderMetrics = this.CreateHeaderMetrics();
        this.Timeline = this.CreateTimeline();
        this.Tabs = this.CreateTabs();
        this.SelectedTab = this.Tabs.FirstOrDefault(tab => tab.Id == selectedTabId) ?? this.Tabs[0];
        this.OnPropertyChanged(nameof(this.StatusLabel));
        this.OnPropertyChanged(nameof(this.HasStatusWarning));
        this.OnPropertyChanged(nameof(this.Title));
        this.OnPropertyChanged(nameof(this.Subtitle));
        this.OnPropertyChanged(nameof(this.HeaderMetrics));
        this.OnPropertyChanged(nameof(this.Timeline));
        this.OnPropertyChanged(nameof(this.Tabs));
        this.OnPropertyChanged(nameof(this.IsOverviewSelected));
        this.OnPropertyChanged(nameof(this.HasTimeline));
        this.OnPropertyChanged(nameof(this.OrganizationStatuses));
        this.OnPropertyChanged(nameof(this.OrganizationStatus));
        this.OnPropertyChanged(nameof(this.SubscriptionStatuses));
        this.OnPropertyChanged(nameof(this.SubscriptionStatus));
        this.Management?.RefreshLocalizedValues();
    }

    /// <summary> Признак выбранной вкладки обзора. </summary>
    public bool IsOverviewSelected => this.SelectedTab.IsOverview;

    /// <summary> Признак наличия хронологии. </summary>
    public bool HasTimeline => this.IsOverviewSelected && this.Timeline.Count > 0;

    /// <summary> Выбранная вкладка карточки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(HasTimeline))]
    private UserCardTab _selectedTab;

    public IReadOnlyList<string> OrganizationStatuses => [
        AdminLocalization.Instance["userCard.organizationStatus.active"],
        AdminLocalization.Instance["userCard.organizationStatus.archived"],
        AdminLocalization.Instance["userCard.organizationStatus.deleted"]];

    public string OrganizationStatus
    {
        get => AdminLocalization.Instance[$"userCard.organizationStatus.{this._organizationStatusCode}"];
        set => this.SetFilterStatus(value, isOrganization: true);
    }

    public IReadOnlyList<string> SubscriptionStatuses => [
        AdminLocalization.Instance["userCard.subscriptionStatus.active"],
        AdminLocalization.Instance["userCard.subscriptionStatus.ended"],
        AdminLocalization.Instance["userCard.subscriptionStatus.all"]];

    public string SubscriptionStatus
    {
        get => AdminLocalization.Instance[$"userCard.subscriptionStatus.{this._subscriptionStatusCode}"];
        set => this.SetFilterStatus(value, isOrganization: false);
    }

    private string _organizationStatusCode = "active";
    private string _subscriptionStatusCode = "active";

    private void SetFilterStatus(string value, bool isOrganization)
    {
        var statuses = isOrganization ? this.OrganizationStatuses : this.SubscriptionStatuses;
        var code = isOrganization
            ? value == statuses[1] ? "archived" : value == statuses[2] ? "deleted" : "active"
            : value == statuses[1] ? "ended" : value == statuses[2] ? "all" : "active";
        if (isOrganization)
        {
            this._organizationStatusCode = code;
            this.OnPropertyChanged(nameof(this.OrganizationStatus));
            this.OnPropertyChanged(nameof(this.Tabs));
        }
        else
        {
            this._subscriptionStatusCode = code;
            this.OnPropertyChanged(nameof(this.SubscriptionStatus));
            this.OnPropertyChanged(nameof(this.Tabs));
        }

        var updatedTabs = this.CreateTabs();
        var targetId = isOrganization ? "organizations" : "subscriptions";
        var existingTab = this.Tabs.FirstOrDefault(tab => tab.Id == targetId);
        var updatedTab = updatedTabs.FirstOrDefault(tab => tab.Id == targetId);
        if (existingTab != null && updatedTab != null)
        {
            existingTab.Items = updatedTab.Items;
            existingTab.RefreshLocalizedValues();
        }
    }

    /// <summary> Формирует элементы вкладки подписок. </summary>
    private IReadOnlyList<UserCardItem> CreateSubscriptionItems() => this._overview.Subscriptions.Organizations
        .Where(subscription => this._subscriptionStatusCode switch
        {
            "ended" => !subscription.IsActive,
            "all" => true,
            _ => subscription.IsActive
        })
        .Select(subscription => new UserCardItem(
            $"{UserDisplayFormatting.Text(subscription.OrganizationName)} · {UserDisplayFormatting.Text(subscription.PlanName)}",
            AdminLocalization.Instance[subscription.IsActive
                ? "userCard.active"
                : "userCard.subscriptionEnded"],
            [
                new(AdminLocalization.Instance["userCard.start"], UserDisplayFormatting.Date(subscription.StartDate)),
                new(AdminLocalization.Instance["userCard.end"], UserDisplayFormatting.Date(subscription.EndDate)),
                new(AdminLocalization.Instance["userCard.daysLeft"], Math.Max(0, subscription.DaysLeft).ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
                new(AdminLocalization.Instance["userCard.autoRenewal"], AdminLocalization.Instance[subscription.AutoRenewal
                    ? "userCard.yes"
                    : "userCard.no"])
            ]))
        .ToArray();

    /// <summary> Формирует элементы вкладки организаций. </summary>
    private IReadOnlyList<UserCardItem> CreateOrganizationItems() => this._overview.Organizations
        .Where(organization => this._organizationStatusCode switch
        {
            "archived" => organization.IsArchived && !organization.IsDeleted,
            "deleted" => organization.IsDeleted,
            _ => organization.IsActive && !organization.IsArchived && !organization.IsDeleted
        })
        .Select(organization => new UserCardItem(
            UserDisplayFormatting.Text(organization.OrganizationName),
            $"{AdminDisplayLabels.OrganizationScenario(organization.PrimaryScenarioCode, organization.ScenarioName)} · {AdminDisplayLabels.OrganizationRole(organization.RoleCode, organization.RoleName)}",
            this.CreateOrganizationFields(organization)))
        .ToArray();

    /// <summary> Формирует метрики шапки карточки. </summary>
    private IReadOnlyList<DashboardMetric> CreateHeaderMetrics()
    {
        var profile = this._overview.Profile;
        var metrics = new List<DashboardMetric>
        {
            new(AdminLocalization.Instance["userCard.registration"], UserDisplayFormatting.Date(profile.DatetimeCreation)),
            new(AdminLocalization.Instance["userCard.organizations"], this._overview.Kpis.OrganizationsTotal.ToString("N0", System.Globalization.CultureInfo.CurrentCulture))
        };

        return metrics;
    }

    /// <summary> Формирует вкладки карточки пользователя. </summary>
    private IReadOnlyList<UserCardTab> CreateTabs()
    {
        var profile = this._overview.Profile;
        var overviewItems = new List<UserCardItem>
        {
            new(UserCardViewModel.Localize("userCard.section.contacts"), string.Empty,
            [
                new(UserCardViewModel.Localize("userCard.field.email"), UserDisplayFormatting.Text(profile.Email), UserCardViewModel.Localize(profile.EmailConfirmed ? "userCard.confirmed" : "userCard.notConfirmed")),
                new(UserCardViewModel.Localize("userCard.field.phone"), UserDisplayFormatting.Text(profile.Phone), UserCardViewModel.Localize(profile.PhoneNumberConfirmed ? "userCard.confirmed" : "userCard.notConfirmed")),
                new(UserCardViewModel.Localize("userCard.field.roles"), UserDisplayFormatting.Text(string.Join(", ", profile.Roles.Where(role => !string.IsNullOrWhiteSpace(role)))))
            ]),
            new(UserCardViewModel.Localize("userCard.section.access"), string.Empty,
            [
                new(UserCardViewModel.Localize("userCard.field.status"), this.StatusLabel),
                new(UserCardViewModel.Localize("userCard.field.password"), UserCardViewModel.Localize(profile.HasPassword ? "userCard.passwordSet" : "userCard.passwordNotSet")),
                new(UserCardViewModel.Localize("userCard.field.failedLogins"), profile.AccessFailedCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture))
            ]),
            new(UserCardViewModel.Localize("userCard.section.consents"), string.Empty,
            [
                new(UserCardViewModel.Localize("userCard.field.dataProcessing"), UserCardViewModel.Localize(profile.PersonalDataProcessingConsentAccepted ? "userCard.accepted" : "userCard.notAccepted"), UserDisplayFormatting.DateTime(profile.PersonalDataProcessingConsentAcceptedAt))
            ])
        };

        var organizationItems = this.CreateOrganizationItems();

        var tabs = new List<UserCardTab>
        {
            new UserCardTab("overview", "userCard.tab.overview", overviewItems, null, showItemCount: false),
            CreateTab("organizations", "userCard.tab.organizations", organizationItems, "userCard.empty.organizations")
        };

        if (this._canViewBilling)
        {
            tabs.Add(CreateTab("subscriptions", "userCard.tab.subscriptions", this.CreateSubscriptionItems(), "userCard.empty.subscriptions"));

            var paymentItems = this._overview.Payments.Select(payment => new UserCardItem(
                UserDisplayFormatting.Text(payment.PlanName),
                $"{UserDisplayFormatting.DateTime(payment.PaymentDate)} · {AdminDisplayLabels.PaymentStatus(payment.PaymentStatus, payment.PaymentStatusName)}",
                [
                    new(UserCardViewModel.Localize("userCard.field.amount"), UserDisplayFormatting.Money(payment.Amount)),
                    new(UserCardViewModel.Localize("userCard.field.method"), AdminDisplayLabels.PaymentMethod(payment.PaymentMethod, payment.PaymentMethodName)),
                    new(UserCardViewModel.Localize("userCard.field.transaction"), UserDisplayFormatting.Text(payment.TransactionId ?? payment.ExternalPaymentId))
                ])).ToList();
            tabs.Add(CreateTab("payments", "userCard.tab.payments", paymentItems, "userCard.empty.payments",
                this._overview.Payments.Count >= 40 ? ["userCard.limit.payments"] : null));
        }

        var personalVehicles = this._overview.Vehicles.Where(vehicle => vehicle.OrganizationId == null).ToArray();
        var personalVehicleIds = personalVehicles.Where(vehicle => vehicle.Id != Guid.Empty).Select(vehicle => vehicle.Id).ToHashSet();
        var vehicleItems = personalVehicles.Select(vehicle => new UserCardItem(
            UserDisplayFormatting.Text(vehicle.Title),
            string.Join(" · ", new[] { AdminDisplayLabels.VehicleKind(vehicle.Kind, vehicle.KindName), vehicle.OrganizationName, vehicle.IsDeleted ? UserCardViewModel.Localize("userCard.vehicle.deleted") : vehicle.IsArchived ? UserCardViewModel.Localize("userCard.vehicle.archived") : null }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            [
                new(UserCardViewModel.Localize("userCard.field.registrationPlate"), UserDisplayFormatting.Text(vehicle.RegistrationPlate)),
                new("VIN", UserDisplayFormatting.Text(vehicle.Vin)),
                new(UserCardViewModel.Localize("userCard.field.mileage"), vehicle.CurrentMileage is null ? "—" : $"{vehicle.CurrentMileage:N0} {AdminDisplayLabels.MileageUnit(vehicle.MileageUnit, vehicle.MileageUnitName)}"),
                new(UserCardViewModel.Localize("userCard.field.maintenances"), vehicle.MaintenancesCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
                new(UserCardViewModel.Localize("userCard.field.openDefects"), vehicle.OpenDefectsCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
                new(UserCardViewModel.Localize("userCard.field.lastMaintenance"), UserDisplayFormatting.CalendarDate(vehicle.LastMaintenanceAt))
            ])).ToList();
        vehicleItems.AddRange(this._overview.Maintenances.Where(maintenance => personalVehicleIds.Contains(maintenance.VehicleId))
            .Select(maintenance => new UserCardItem(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, UserCardViewModel.Localize("userCard.maintenanceTitle"),
                UserDisplayFormatting.Text(maintenance.VehicleTitle)),
            $"{UserDisplayFormatting.CalendarDate(maintenance.Date)} · {AdminDisplayLabels.MaintenanceStatus(maintenance.Status, maintenance.StatusName)} · {AdminDisplayLabels.MaintenanceType(maintenance.Type, maintenance.TypeName)}",
            [
                new(UserCardViewModel.Localize("userCard.field.location"), UserDisplayFormatting.Text(maintenance.Location)),
                new(UserCardViewModel.Localize("userCard.field.performer"), UserDisplayFormatting.Text(maintenance.Performer)),
                new(UserCardViewModel.Localize("userCard.field.paid"), UserDisplayFormatting.Money(maintenance.PaidAmount, maintenance.CurrencyCode)),
                new(UserCardViewModel.Localize("userCard.field.comment"), UserDisplayFormatting.Text(maintenance.Comment))
            ])));
        var vehicleLimitNote = new List<string>();
        if (this._overview.Vehicles.Count >= 30)
        {
            vehicleLimitNote.Add("userCard.limit.vehicles");
        }

        if (this._overview.Maintenances.Count >= 20)
        {
            vehicleLimitNote.Add("userCard.limit.maintenances");
        }

        tabs.Add(CreateTab("vehicles", "userCard.tab.vehicles", vehicleItems, "userCard.empty.vehicles",
            vehicleLimitNote.Count > 0 ? vehicleLimitNote : null));

        var supportItems = this._overview.SupportMessages.Select(message => new UserCardItem(
            UserDisplayFormatting.Text(message.Topic),
            $"{AdminDisplayLabels.SupportType(message.TypeCode, message.TypeName)} · {AdminDisplayLabels.SupportStatus(message.StatusCode, message.StatusName)} · {UserDisplayFormatting.DateTime(message.CreatedAt)}",
            [
                new(UserCardViewModel.Localize("userCard.field.text"), UserDisplayFormatting.Text(message.Text)),
                new(UserCardViewModel.Localize("userCard.field.page"), UserDisplayFormatting.Text(message.SourcePage)),
                new(UserCardViewModel.Localize("userCard.field.comment"), UserDisplayFormatting.Text(message.AdminComment))
            ])).ToList();
        tabs.Add(CreateTab("support", "userCard.tab.support", supportItems, "userCard.empty.support",
            this._overview.SupportMessages.Count >= 20 ? ["userCard.limit.support"] : null));

        if (this._canViewBilling)
        {
            var featureItems = this._overview.FeatureGrants.Select(grant => new UserCardItem(
                UserDisplayFormatting.Text(grant.FeatureName),
                $"{UserDisplayFormatting.Text(grant.FeatureCode)} · {UserCardViewModel.Localize(grant.IsActive ? "userCard.active" : "userCard.inactive")}",
                [
                    new(UserCardViewModel.Localize("userCard.field.value"), FormatFeatureValue(grant)),
                    new(UserCardViewModel.Localize("userCard.field.source"), UserDisplayFormatting.Text(grant.Source)),
                    new(UserCardViewModel.Localize("userCard.field.start"), UserDisplayFormatting.Date(grant.StartDate)),
                    new(UserCardViewModel.Localize("userCard.field.end"), UserDisplayFormatting.Date(grant.EndDate)),
                    new(UserCardViewModel.Localize("userCard.field.comment"), UserDisplayFormatting.Text(grant.Comment))
                ])).ToList();
            tabs.Add(CreateTab("features", "userCard.tab.features", featureItems, "userCard.empty.features",
                this._overview.FeatureGrants.Count >= 40 ? ["userCard.limit.features"] : null));
        }

        if (this.Management?.IsVisible == true)
        {
            tabs.Add(new UserCardTab("management", "userCard.tab.management", [], null, showItemCount: false));
        }

        return tabs;
    }

    /// <summary> Формирует поля карточки организации. </summary>
    private IReadOnlyList<DashboardMetric> CreateOrganizationFields(AdminUserOverviewOrganization organization)
    {
        var fields = new List<DashboardMetric>
        {
            new(UserCardViewModel.Localize("userCard.field.status"), UserCardViewModel.Localize(organization.IsDeleted ? "userCard.vehicle.deleted" : organization.IsArchived ? "userCard.vehicle.archived" : organization.IsActive ? "userCard.active" : "userCard.inactive")),
            new(UserCardViewModel.Localize("userCard.field.role"), UserDisplayFormatting.Text(organization.RoleName)),
            new(UserCardViewModel.Localize("userCard.field.memberSince"), UserDisplayFormatting.DateTime(organization.JoinedAt)),
            new(UserCardViewModel.Localize("userCard.field.members"), organization.MembersCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
            new(UserCardViewModel.Localize("userCard.field.vehicles"), organization.VehiclesCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
            new(UserCardViewModel.Localize("userCard.field.clients"), organization.ClientsCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture))
        };

        var invitation = this._overview.Invitations
            .Where(invitation => invitation.OrganizationId == organization.OrganizationId
                && string.Equals(invitation.StatusCode, "accepted", StringComparison.OrdinalIgnoreCase)
                && invitation.AcceptedAt == organization.JoinedAt
                && !string.IsNullOrWhiteSpace(this._overview.Profile.Email)
                && string.Equals(invitation.Email, this._overview.Profile.Email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(invitation => invitation.CreatedAt)
            .FirstOrDefault();

        if (invitation != null)
        {
            fields.Add(new DashboardMetric(UserCardViewModel.Localize("userCard.field.invitedAt"), UserDisplayFormatting.DateTime(invitation.CreatedAt)));
            if (!string.IsNullOrWhiteSpace(invitation.RoleCode))
            {
                fields.Add(new DashboardMetric(UserCardViewModel.Localize("userCard.field.invitationRole"),
                    AdminDisplayLabels.OrganizationRole(invitation.RoleCode, invitation.RoleName)));
            }

            if (!string.IsNullOrWhiteSpace(invitation.InvitedBy))
            {
                fields.Add(new DashboardMetric(UserCardViewModel.Localize("userCard.field.invitedBy"), invitation.InvitedBy));
            }
        }

        if (this._canViewBilling)
        {
            var hasFreeWorkspace = organization.IsActive && !organization.IsArchived && !organization.IsDeleted
                && !organization.HasActiveSubscription
                && (string.Equals(organization.PrimaryScenarioCode, "detailing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(organization.PrimaryScenarioCode, "service-station", StringComparison.OrdinalIgnoreCase));
            fields.Add(new DashboardMetric(
                UserCardViewModel.Localize("userCard.field.plan"),
                hasFreeWorkspace ? "Free" : UserDisplayFormatting.Text(organization.CurrentPlanName),
                organization.HasActiveSubscription
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UserCardViewModel.Localize("userCard.planUntil"), UserDisplayFormatting.Date(organization.CurrentPlanEndDate), organization.CurrentPlanIsDemo ? $" · {UserCardViewModel.Localize("userCard.demo")}" : string.Empty)
                    : hasFreeWorkspace ? UserCardViewModel.Localize("userCard.freePlan") : UserCardViewModel.Localize("userCard.noActiveSubscription")));
        }

        return fields;
    }

    /// <summary> Формирует хронологию событий пользователя. </summary>
    private IReadOnlyList<UserCardItem> CreateTimeline() => this._overview.Timeline
        .Where(item => this._canViewBilling || !IsBillingTimelineItem(item.Type))
        .Select(item => new UserCardItem(
            UserDisplayFormatting.Text(this.TimelineTitle(item)),
            $"{UserDisplayFormatting.Text(this.TimelineLabel(item))} · {UserDisplayFormatting.DateTime(item.Date)}",
            []))
        .ToList();

    private string TimelineLabel(AdminUserTimelineItem item) => item.Type switch
    {
        "registration" => UserCardViewModel.Localize("userCard.timeline.registration"),
        "login" => UserCardViewModel.Localize("userCard.timeline.login"),
        "vehicle" => UserCardViewModel.Localize("userCard.timeline.vehicle"),
        "payment" => UserCardViewModel.Localize("userCard.timeline.payment"),
        "subscription" => UserCardViewModel.Localize("userCard.timeline.subscription"),
        "support" => UserCardViewModel.Localize("userCard.timeline.support"),
        "organization" => UserCardViewModel.Localize("userCard.timeline.organization"),
        _ => item.Label
    };

    private string TimelineTitle(AdminUserTimelineItem item) => item.Type switch
    {
        "registration" => UserCardViewModel.Localize("userCard.timeline.registrationTitle"),
        "login" => UserCardViewModel.Localize("userCard.timeline.loginTitle"),
        _ => item.Title
    };

    /// <summary> Определяет, относится ли запись хронологии к биллингу. </summary>
    private static bool IsBillingTimelineItem(string type) =>
        string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "subscription", StringComparison.OrdinalIgnoreCase);

    private string GetStatusLabel() => this._overview.Profile.IsDeleted
        ? AdminLocalization.Instance["core.user.status.deleted"]
        : this._overview.Profile.LockoutEnabled && this._overview.Profile.LockoutEnd > DateTimeOffset.UtcNow
            ? AdminLocalization.Instance["core.user.status.locked"]
            : AdminLocalization.Instance["core.user.status.active"];

    /// <summary> Форматирует значение предоставленной функции. </summary>
    private static string FormatFeatureValue(AdminUserOverviewFeatureGrant grant)
    {
        if (grant.ValueType.Contains("bool", StringComparison.OrdinalIgnoreCase))
        {
            return UserCardViewModel.Localize(grant.BoolValue == true ? "userCard.allowed" : "userCard.denied");
        }

        return grant.IntValue?.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? UserCardViewModel.Localize("userCard.noValue");
    }

    /// <summary> Создаёт вкладку карточки с элементами. </summary>
    private static UserCardTab CreateTab(
        string id,
        string titleKey,
        IReadOnlyList<UserCardItem> items,
        string emptyMessageKey,
        IReadOnlyList<string>? limitNoteKeys = null) =>
        new(id, titleKey, items, emptyMessageKey, limitNoteKeys);

    private static string Localize(string key) => AdminLocalization.Instance[key];
}
