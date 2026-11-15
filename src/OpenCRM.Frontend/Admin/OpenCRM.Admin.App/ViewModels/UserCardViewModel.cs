using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api.Models;

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
        var profile = overview.Profile;

        this.Title = UserDisplayFormatting.Text(profile.Email ?? profile.Phone ?? "Пользователь");
        this.Subtitle = $"ID: {profile.Id}";
        this.StatusLabel = profile.IsDeleted
            ? "Удалён"
            : profile.LockoutEnabled && profile.LockoutEnd > DateTimeOffset.UtcNow
                ? "Временно заблокирован"
                : "Активен";
        this.HeaderMetrics = this.CreateHeaderMetrics();
        this.Timeline = this.CreateTimeline();
        this.Tabs = this.CreateTabs();
        this.SelectedTab = this.Tabs[0];
    }

    /// <summary> Заголовок карточки. </summary>
    public string Title { get; }

    /// <summary> Административные действия над открытой карточкой. </summary>
    public UserManagementViewModel? Management { get; }

    /// <summary> Открывает журнал действий этого пользователя. </summary>
    [RelayCommand]
    private void OpenActivity() => this._activityRequested?.Invoke(this._overview.Profile.Id);

    /// <summary> Подзаголовок карточки. </summary>
    public string Subtitle { get; }

    /// <summary> Подпись статуса пользователя. </summary>
    public string StatusLabel { get; }

    /// <summary> Признак предупреждения о статусе. </summary>
    public bool HasStatusWarning => this.StatusLabel != "Активен";

    /// <summary> Вкладки карточки пользователя. </summary>
    public IReadOnlyList<UserCardTab> Tabs { get; }

    /// <summary> Метрики в шапке карточки. </summary>
    public IReadOnlyList<DashboardMetric> HeaderMetrics { get; }

    /// <summary> Хронология событий пользователя. </summary>
    public IReadOnlyList<UserCardItem> Timeline { get; }

    /// <summary> Признак выбранной вкладки обзора. </summary>
    public bool IsOverviewSelected => ReferenceEquals(this.SelectedTab, this.Tabs[0]);

    /// <summary> Признак наличия хронологии. </summary>
    public bool HasTimeline => this.IsOverviewSelected && this.Timeline.Count > 0;

    /// <summary> Выбранная вкладка карточки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(HasTimeline))]
    private UserCardTab _selectedTab;

    /// <summary> Доступные статусы организаций. </summary>
    public IReadOnlyList<string> OrganizationStatuses { get; } = ["Активные", "Архивированные", "Удалённые"];

    /// <summary> Выбранный фильтр статуса организаций. </summary>
    [ObservableProperty]
    private string _organizationStatus = "Активные";

    /// <summary> Реагирует на смену фильтра статуса организаций. </summary>
    partial void OnOrganizationStatusChanged(string value)
    {
        var tab = this.Tabs.First(tab => tab.IsOrganizations);
        tab.Items = this.CreateOrganizationItems();
    }

    /// <summary> Доступные статусы подписок. </summary>
    public IReadOnlyList<string> SubscriptionStatuses { get; } = ["Активные", "Завершённые", "Все"];

    /// <summary> Выбранный фильтр статуса подписок. </summary>
    [ObservableProperty]
    private string _subscriptionStatus = "Активные";

    /// <summary> Реагирует на смену фильтра статуса подписок. </summary>
    partial void OnSubscriptionStatusChanged(string value)
    {
        var tab = this.Tabs.FirstOrDefault(tab => tab.IsSubscriptions);
        if (tab != null)
        {
            tab.Items = this.CreateSubscriptionItems();
        }
    }

    /// <summary> Формирует элементы вкладки подписок. </summary>
    private IReadOnlyList<UserCardItem> CreateSubscriptionItems() => this._overview.Subscriptions.Organizations
        .Where(subscription => this.SubscriptionStatus switch
        {
            "Завершённые" => !subscription.IsActive,
            "Все" => true,
            _ => subscription.IsActive
        })
        .Select(subscription => new UserCardItem(
            $"{UserDisplayFormatting.Text(subscription.OrganizationName)} · {UserDisplayFormatting.Text(subscription.PlanName)}",
            subscription.IsActive ? "Активна" : "Завершена",
            [
                new("Начало", UserDisplayFormatting.Date(subscription.StartDate)),
                new("Окончание", UserDisplayFormatting.Date(subscription.EndDate)),
                new("Осталось дней", Math.Max(0, subscription.DaysLeft).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))),
                new("Автопродление", subscription.AutoRenewal ? "Да" : "Нет")
            ]))
        .ToArray();

    /// <summary> Формирует элементы вкладки организаций. </summary>
    private IReadOnlyList<UserCardItem> CreateOrganizationItems() => this._overview.Organizations
        .Where(organization => this.OrganizationStatus switch
        {
            "Архивированные" => organization.IsArchived && !organization.IsDeleted,
            "Удалённые" => organization.IsDeleted,
            _ => organization.IsActive && !organization.IsArchived && !organization.IsDeleted
        })
        .Select(organization => new UserCardItem(
            UserDisplayFormatting.Text(organization.OrganizationName),
            $"{UserDisplayFormatting.Text(organization.ScenarioName)} · {UserDisplayFormatting.Text(organization.RoleName)}",
            this.CreateOrganizationFields(organization)))
        .ToArray();

    /// <summary> Формирует метрики шапки карточки. </summary>
    private IReadOnlyList<DashboardMetric> CreateHeaderMetrics()
    {
        var profile = this._overview.Profile;
        var metrics = new List<DashboardMetric>
        {
            new("Регистрация", UserDisplayFormatting.Date(profile.DatetimeCreation)),
            new("Организации", this._overview.Kpis.OrganizationsTotal.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")))
        };

        return metrics;
    }

    /// <summary> Формирует вкладки карточки пользователя. </summary>
    private IReadOnlyList<UserCardTab> CreateTabs()
    {
        var profile = this._overview.Profile;
        var overviewItems = new List<UserCardItem>
        {
            new("Контакты", string.Empty,
            [
                new("Email", UserDisplayFormatting.Text(profile.Email), profile.EmailConfirmed ? "Подтверждён" : "Не подтверждён"),
                new("Телефон", UserDisplayFormatting.Text(profile.Phone), profile.PhoneNumberConfirmed ? "Подтверждён" : "Не подтверждён"),
                new("Роли", UserDisplayFormatting.Text(string.Join(", ", profile.Roles.Where(role => !string.IsNullOrWhiteSpace(role)))))
            ]),
            new("Доступ", string.Empty,
            [
                new("Статус", this.StatusLabel),
                new("Пароль", profile.HasPassword ? "Установлен" : "Не установлен"),
                new("Неудачные входы", profile.AccessFailedCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")))
            ]),
            new("Согласия", string.Empty,
            [
                new("Обработка данных", profile.PersonalDataProcessingConsentAccepted ? "Принято" : "Не принято", UserDisplayFormatting.DateTime(profile.PersonalDataProcessingConsentAcceptedAt))
            ])
        };

        var organizationItems = this.CreateOrganizationItems();

        var tabs = new List<UserCardTab>
        {
            new UserCardTab("Обзор", overviewItems, "Данных пользователя нет.",
                this._overview.Timeline.Count >= 40 ? "Показаны последние 40 событий." : null),
            CreateTab("Организации", organizationItems, "Организаций с выбранным статусом нет.")
        };

        if (this._canViewBilling)
        {
            tabs.Add(CreateTab("Подписки", this.CreateSubscriptionItems(), "Подписок с выбранным статусом нет."));

            var paymentItems = this._overview.Payments.Select(payment => new UserCardItem(
                UserDisplayFormatting.Text(payment.PlanName),
                $"{UserDisplayFormatting.DateTime(payment.PaymentDate)} · {UserDisplayFormatting.Text(payment.PaymentStatusName)}",
                [
                    new("Сумма", UserDisplayFormatting.Money(payment.Amount)),
                    new("Способ", UserDisplayFormatting.Text(payment.PaymentMethodName)),
                    new("Транзакция", UserDisplayFormatting.Text(payment.TransactionId ?? payment.ExternalPaymentId))
                ])).ToList();
            tabs.Add(CreateTab("Платежи", paymentItems, "Платежей нет.",
                this._overview.Payments.Count >= 40 ? "Показаны последние 40 платежей." : null));
        }

        var personalVehicles = this._overview.Vehicles.Where(vehicle => vehicle.OrganizationId == null).ToArray();
        var personalVehicleIds = personalVehicles.Where(vehicle => vehicle.Id != Guid.Empty).Select(vehicle => vehicle.Id).ToHashSet();
        var vehicleItems = personalVehicles.Select(vehicle => new UserCardItem(
            UserDisplayFormatting.Text(vehicle.Title),
            string.Join(" · ", new[] { vehicle.KindName, vehicle.OrganizationName, vehicle.IsDeleted ? "Удалена" : vehicle.IsArchived ? "В архиве" : null }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            [
                new("Госномер", UserDisplayFormatting.Text(vehicle.RegistrationPlate)),
                new("VIN", UserDisplayFormatting.Text(vehicle.Vin)),
                new("Пробег", vehicle.CurrentMileage is null ? "—" : $"{vehicle.CurrentMileage:N0} {vehicle.MileageUnitName}"),
                new("Обслуживания", vehicle.MaintenancesCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))),
                new("Открытые дефекты", vehicle.OpenDefectsCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))),
                new("Последнее ТО", UserDisplayFormatting.Date(vehicle.LastMaintenanceAt))
            ])).ToList();
        vehicleItems.AddRange(this._overview.Maintenances.Where(maintenance => personalVehicleIds.Contains(maintenance.VehicleId))
            .Select(maintenance => new UserCardItem(
            $"ТО · {UserDisplayFormatting.Text(maintenance.VehicleTitle)}",
            $"{UserDisplayFormatting.Date(maintenance.Date)} · {UserDisplayFormatting.Text(maintenance.StatusName)} · {UserDisplayFormatting.Text(maintenance.TypeName)}",
            [
                new("Место", UserDisplayFormatting.Text(maintenance.Location)),
                new("Исполнитель", UserDisplayFormatting.Text(maintenance.Performer)),
                new("Оплачено", UserDisplayFormatting.Money(maintenance.PaidAmount)),
                new("Комментарий", UserDisplayFormatting.Text(maintenance.Comment))
            ])));
        var vehicleLimitNote = new List<string>();
        if (this._overview.Vehicles.Count >= 30)
        {
            vehicleLimitNote.Add("Сводка ограничена 30 единицами техники; показана только личная.");
        }

        if (this._overview.Maintenances.Count >= 20)
        {
            vehicleLimitNote.Add("Сводка ограничена 20 обслуживаниями.");
        }

        tabs.Add(CreateTab("Личная техника и ТО", vehicleItems, "Личной техники и её обслуживаний нет.",
            vehicleLimitNote.Count > 0 ? string.Join(" ", vehicleLimitNote) : null));

        var supportItems = this._overview.SupportMessages.Select(message => new UserCardItem(
            UserDisplayFormatting.Text(message.Topic),
            $"{UserDisplayFormatting.Text(message.TypeName)} · {UserDisplayFormatting.Text(message.StatusName)} · {UserDisplayFormatting.DateTime(message.CreatedAt)}",
            [
                new("Текст", UserDisplayFormatting.Text(message.Text)),
                new("Страница", UserDisplayFormatting.Text(message.SourcePage)),
                new("Комментарий", UserDisplayFormatting.Text(message.AdminComment))
            ])).ToList();
        tabs.Add(CreateTab("Обращения", supportItems, "Обращений нет.",
            this._overview.SupportMessages.Count >= 20 ? "Показаны последние 20 обращений." : null));

        if (this._canViewBilling)
        {
            var featureItems = this._overview.FeatureGrants.Select(grant => new UserCardItem(
                UserDisplayFormatting.Text(grant.FeatureName),
                $"{UserDisplayFormatting.Text(grant.FeatureCode)} · {(grant.IsActive ? "Активна" : "Неактивна")}",
                [
                    new("Значение", FormatFeatureValue(grant)),
                    new("Источник", UserDisplayFormatting.Text(grant.Source)),
                    new("Начало", UserDisplayFormatting.Date(grant.StartDate)),
                    new("Окончание", UserDisplayFormatting.Date(grant.EndDate)),
                    new("Комментарий", UserDisplayFormatting.Text(grant.Comment))
                ])).ToList();
            tabs.Add(CreateTab("Возможности", featureItems, "Индивидуальных возможностей нет.",
                this._overview.FeatureGrants.Count >= 40 ? "Показаны последние 40 назначений." : null));
        }

        if (this.Management?.IsVisible == true)
        {
            tabs.Add(new UserCardTab("Управление", [], string.Empty));
        }

        return tabs;
    }

    /// <summary> Формирует поля карточки организации. </summary>
    private IReadOnlyList<DashboardMetric> CreateOrganizationFields(AdminUserOverviewOrganization organization)
    {
        var fields = new List<DashboardMetric>
        {
            new("Статус", organization.IsDeleted ? "Удалена" : organization.IsArchived ? "В архиве" : organization.IsActive ? "Активна" : "Неактивна"),
            new("Роль", UserDisplayFormatting.Text(organization.RoleName)),
            new("В организации с", UserDisplayFormatting.DateTime(organization.JoinedAt)),
            new("Участники", organization.MembersCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))),
            new("Техника", organization.VehiclesCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))),
            new("Клиенты", organization.ClientsCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")))
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
            fields.Add(new DashboardMetric("Приглашён", UserDisplayFormatting.DateTime(invitation.CreatedAt)));
            if (!string.IsNullOrWhiteSpace(invitation.InvitedBy))
            {
                fields.Add(new DashboardMetric("Кто пригласил", invitation.InvitedBy));
            }
        }

        if (this._canViewBilling)
        {
            var hasFreeWorkspace = organization.IsActive && !organization.IsArchived && !organization.IsDeleted
                && !organization.HasActiveSubscription
                && (string.Equals(organization.PrimaryScenarioCode, "detailing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(organization.PrimaryScenarioCode, "service-station", StringComparison.OrdinalIgnoreCase));
            fields.Add(new DashboardMetric(
                "Тариф",
                hasFreeWorkspace ? "Free" : UserDisplayFormatting.Text(organization.CurrentPlanName),
                organization.HasActiveSubscription
                    ? $"До {UserDisplayFormatting.Date(organization.CurrentPlanEndDate)}{(organization.CurrentPlanIsDemo ? " · демо" : string.Empty)}"
                    : hasFreeWorkspace ? "Без ограничения срока · базовая работа продолжается" : "Нет активной подписки"));
        }

        return fields;
    }

    /// <summary> Формирует хронологию событий пользователя. </summary>
    private IReadOnlyList<UserCardItem> CreateTimeline() => this._overview.Timeline
        .Where(item => this._canViewBilling || !IsBillingTimelineItem(item.Type))
        .Select(item => new UserCardItem(
            UserDisplayFormatting.Text(item.Title),
            $"{UserDisplayFormatting.Text(item.Label)} · {UserDisplayFormatting.DateTime(item.Date)}",
            []))
        .ToList();

    /// <summary> Определяет, относится ли запись хронологии к биллингу. </summary>
    private static bool IsBillingTimelineItem(string type) =>
        string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "subscription", StringComparison.OrdinalIgnoreCase);

    /// <summary> Форматирует значение предоставленной функции. </summary>
    private static string FormatFeatureValue(AdminUserOverviewFeatureGrant grant)
    {
        if (grant.ValueType.Contains("bool", StringComparison.OrdinalIgnoreCase))
        {
            return grant.BoolValue == true ? "Разрешено" : "Запрещено";
        }

        return grant.IntValue?.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) ?? "Без значения";
    }

    /// <summary> Создаёт вкладку карточки с элементами. </summary>
    private static UserCardTab CreateTab(
        string title,
        IReadOnlyList<UserCardItem> items,
        string emptyMessage,
        string? limitNote = null) =>
        new($"{title} · {items.Count}", items, emptyMessage, limitNote);
}
