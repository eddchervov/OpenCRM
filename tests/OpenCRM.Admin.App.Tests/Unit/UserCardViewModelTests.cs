using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class UserCardViewModelTests : IDisposable
{
    private readonly string _preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-card-language-{Guid.NewGuid():N}.txt");
    private readonly IDisposable _preferenceScope;
    private readonly string _previousLanguage;

    public UserCardViewModelTests()
    {
        this._preferenceScope = AdminLocalization.Instance.UsePreferencePath(this._preferencePath);
        this._previousLanguage = AdminLocalization.Instance.Language;
        AdminLocalization.Instance.Language = "ru";
    }

    public void Dispose()
    {
        AdminLocalization.Instance.Language = this._previousLanguage;
        this._preferenceScope.Dispose();
        if (File.Exists(this._preferencePath))
        {
            File.Delete(this._preferencePath);
        }
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void FreeWorkspaceLabelRequiresAnAvailableOrganization(bool isActive, bool isArchived, bool isDeleted, bool expectedFree)
    {
        var organization = new AdminUserOverviewOrganization
        {
            PrimaryScenarioCode = "detailing",
            IsActive = isActive,
            IsArchived = isArchived,
            IsDeleted = isDeleted
        };

        var model = new UserCardViewModel(CreateOverview(new AdminUserOverviewProfile(), organization), true);
        model.OrganizationStatus = isDeleted ? "Удалённые" : isArchived ? "Архивированные" : "Активные";
        var items = model.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Организации").Items;
        var hasFreePlan = items.SelectMany(item => item.Fields).Any(field => field.Title == "Тариф" && field.Value == "Free");
        var row = new AdminOrganizationRow(new AdminOrganization
        {
            PrimaryScenarioCode = "detailing",
            IsActive = isActive,
            IsArchived = isArchived,
            IsDeleted = isDeleted
        }, isCommercialVisible: true);

        Assert.Equal(expectedFree, hasFreePlan);
        Assert.Equal(expectedFree, row.Plan == "Free");
        Assert.Equal(expectedFree ? "Бессрочно" : "—", row.PlanEnd);
    }

    [Fact]
    public void PersonalPlanKeepsItsOriginalKind()
    {
        var row = new SubscriptionPlanListItem(new AdminSubscriptionPlan
        {
            PlanTypeCode = "personal",
            PlanTypeName = "Личный"
        }, false);

        Assert.Equal("Личный", row.KindLabel);
    }

    [Theory]
    [InlineData(false, true, "Активен")]
    [InlineData(true, true, "Временно заблокирован")]
    [InlineData(true, false, "Активен")]
    public void StatusRequiresEnabledLockoutWithFutureEnd(bool lockoutEnabled, bool hasFutureLockout, string expectedStatus)
    {
        var model = new UserCardViewModel(CreateOverview(new AdminUserOverviewProfile
        {
            LockoutEnabled = lockoutEnabled,
            LockoutEnd = hasFutureLockout ? DateTimeOffset.UtcNow.AddMinutes(5) : DateTimeOffset.UtcNow.AddMinutes(-5)
        }), false);

        Assert.Equal(expectedStatus, model.StatusLabel);
    }

    [Fact]
    public void ActivityCommandRequestsJournalForCardUser()
    {
        string? requestedUserId = null;
        var model = new UserCardViewModel(CreateOverview(new AdminUserOverviewProfile { Id = "user-activity-1" }), false,
            activityRequested: userId => requestedUserId = userId);

        model.OpenActivityCommand.Execute(null);

        Assert.Equal("user-activity-1", requestedUserId);
    }

    [Fact]
    public void CardHidesLoginTimestampAndPaymentsPreserveCents()
    {
        var loginDate = new DateTime(2026, 10, 13, 23, 30, 0, DateTimeKind.Utc);
        var card = new UserCardViewModel(CreateOverview(new AdminUserOverviewProfile { LastLoginDate = loginDate },
            payments: [new() { Amount = 1000.25m }]), true);

        Assert.DoesNotContain(card.HeaderMetrics, metric => metric.Title == "Последний вход");
        var payment = Assert.Single(card.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Платежи").Items);
        Assert.Contains(1000.25m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture),
            payment.Fields.Single(field => field.Title == "Сумма").Value);
    }

    [Fact]
    public void LoginVisitAndAdvertisingConsentValuesAreNotShownInCard()
    {
        var profile = new AdminUserOverviewProfile
        {
            LastLoginDate = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc),
            LastDateOfEntry = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc),
            CountLogins = 12,
            CountVisits = 25,
            FirstUtmSource = "sample-campaign",
            AdvertisingConsentAccepted = true,
            AdvertisingConsentAcceptedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            PersonalDataProcessingConsentAccepted = true
        };
        var model = new UserCardViewModel(CreateOverview(profile), false);
        var displayedValues = model.HeaderMetrics
            .SelectMany(metric => new[] { metric.Value, metric.Detail })
            .Concat(model.Tabs.SelectMany(tab => tab.Items)
                .SelectMany(item => new[] { item.Title, item.Subtitle }
                    .Concat(item.Fields.SelectMany(metric => new[] { metric.Value, metric.Detail }))))
            .ToArray();

        Assert.DoesNotContain(displayedValues, value => value.Contains("01.01.0001", StringComparison.Ordinal));
        Assert.DoesNotContain(displayedValues, value => value.Contains("01.11.2026", StringComparison.Ordinal));
        Assert.DoesNotContain(displayedValues, value => value.Contains("sample-campaign", StringComparison.Ordinal));
        Assert.DoesNotContain(displayedValues, value => value.Contains("Реклама", StringComparison.Ordinal));
        Assert.DoesNotContain(model.HeaderMetrics, metric => metric.Title is "Последний вход" or "Входы" or "Посещений");
        var consentFields = model.Tabs[0].Items.Single(item => item.Title == "Согласия").Fields;
        Assert.Contains(consentFields,
            field => field.Title == "Обработка данных");
        Assert.DoesNotContain(consentFields, field => field.Title == "Реклама");
        Assert.DoesNotContain(model.Tabs[0].Items, item => item.Title == "Источники");
    }

    [Fact]
    public void BillingDataIsHiddenWithoutBillingPermission()
    {
        var model = new UserCardViewModel(CreateOverview(
            new AdminUserOverviewProfile(),
            new AdminUserOverviewOrganization { CurrentPlanName = "Бизнес", HasActiveSubscription = true, IsActive = true },
            [new() { Type = "payment", Label = "Платёж", Title = "Платёж", Date = DateTime.UtcNow },
                new() { Type = "subscription", Label = "Подписка", Title = "Тариф", Date = DateTime.UtcNow },
                new() { Type = "login", Label = "Вход", Title = "Вход", Date = DateTime.UtcNow }],
            [new() { Amount = 1000, PlanName = "Личный", PaymentStatusName = "Оплачен" }]), false);

        Assert.DoesNotContain(model.Tabs, tab => GetBaseTitle(tab.Title) is "Подписки" or "Платежи");
        var organization = Assert.Single(model.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Организации").Items);
        Assert.DoesNotContain(organization.Fields, field => field.Title == "Тариф");
        Assert.Single(model.Timeline);
        Assert.Equal("Последний вход пользователя", model.Timeline[0].Title);
        Assert.DoesNotContain(model.HeaderMetrics, metric => metric.Title is "Платежи" or "Активные подписки");
    }

    [Fact]
    public void NeutralModeHidesCommercialUserDataAndKeepsOrganizationOperations()
    {
        var vehicleId = Guid.NewGuid();
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Organizations = [new() { OrganizationName = "Студия", CurrentPlanName = "PRO", HasActiveSubscription = true, IsActive = true }],
            Subscriptions = new() { Organizations = [new() { PlanName = "PRO", IsActive = true }] },
            Payments = [new() { PlanName = "PRO", Amount = 1000 }],
            FeatureGrants = [new() { FeatureName = "Платная функция" }],
            Vehicles = [new() { Id = vehicleId, Title = "Личная машина" }],
            Maintenances = [new() { VehicleId = vehicleId, VehicleTitle = "Личная машина", PaidAmount = 1250.50m }]
        }, false);

        Assert.DoesNotContain(card.Tabs, tab => tab.DisplayTitle is "Подписки" or "Платежи" or "Возможности");
        var organization = Assert.Single(card.Tabs.Single(tab => tab.IsOrganizations).Items);
        Assert.DoesNotContain(organization.Fields, field => field.Title == "Тариф");
        var paidAmount = Assert.Single(card.Tabs.Single(tab => tab.DisplayTitle == "Личная техника и ТО").Items.Last().Fields,
            field => field.Title == "Оплачено");
        Assert.Equal($"{1250.50m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture)} RUB", paidAmount.Value);
    }

    [Fact]
    public void SelectingAnotherTabHidesTimelineAndNotifiesOverviewState()
    {
        var model = new UserCardViewModel(CreateOverview(
            new AdminUserOverviewProfile(),
            timeline: [new() { Type = "login", Label = "Вход", Title = "Вход пользователя", Date = DateTime.UtcNow }]), false);
        var changedProperties = new List<string?>();
        model.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        Assert.True(model.IsOverviewSelected);
        Assert.True(model.HasTimeline);
        model.SelectedTab = model.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Организации");

        Assert.False(model.IsOverviewSelected);
        Assert.False(model.HasTimeline);
        Assert.Contains(nameof(model.IsOverviewSelected), changedProperties);
        Assert.Contains(nameof(model.HasTimeline), changedProperties);
    }

    [Fact]
    public void AccountHasNoModeAndInvitationHistoryIsNotAnOrganization()
    {
        var model = new UserCardViewModel(CreateOverview(
            new AdminUserOverviewProfile(),
            invitations: [new() { StatusCode = "pending", Email = "contact@example.invalid" }]), false);
        var contact = model.Tabs[0].Items.Single(item => item.Title == "Контакты");
        Assert.Empty(model.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Организации").Items);

        Assert.DoesNotContain(contact.Fields, field => field.Title == "Режим аккаунта");
    }

    [Fact]
    public void LimitNoteIsShownOnlyWhenTheCollectionReachesTheServerLimit()
    {
        var belowLimit = new UserCardViewModel(CreateOverview(
            new AdminUserOverviewProfile(),
            payments: [new() { Amount = 500, PlanName = "Личный" }]), true);
        var atLimit = new UserCardViewModel(CreateOverview(
            new AdminUserOverviewProfile(),
            payments: Enumerable.Range(0, 40)
                .Select(index => new AdminUserOverviewPayment { Id = index, Amount = 500, PlanName = "Личный" })
                .ToArray()), true);

        Assert.Null(belowLimit.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Платежи").LimitNote);
        Assert.Equal("Показаны последние 40 платежей.",
            atLimit.Tabs.Single(tab => GetBaseTitle(tab.Title) == "Платежи").LimitNote);
    }

    [Fact]
    public void OrganizationsShowCurrentMembershipAndMatchingInvitationDetailsWithoutHistoryCards()
    {
        var organizationId = Guid.NewGuid();
        var joined = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var overview = new AdminUserOverview
        {
            Profile = new() { Email = "contact@example.invalid" },
            Organizations = [new() { OrganizationId = organizationId, OrganizationName = "Текущая", IsActive = true, JoinedAt = joined }],
            Invitations = [
                new() { OrganizationId = organizationId, StatusCode = "accepted", Email = "contact@example.invalid", CreatedAt = joined.AddDays(-1), AcceptedAt = joined, InvitedBy = "contact@example.invalid" },
                new() { OrganizationId = organizationId, StatusCode = "accepted", Email = "contact@example.invalid", CreatedAt = joined.AddMonths(-1), AcceptedAt = joined.AddMonths(-1), InvitedBy = "contact@example.invalid" },
                new() { OrganizationId = organizationId, StatusCode = "accepted", Email = "contact@example.invalid", AcceptedAt = joined, InvitedBy = "contact@example.invalid" },
                new() { OrganizationId = Guid.NewGuid(), OrganizationName = "Бывшая", StatusCode = "accepted", Email = "contact@example.invalid", AcceptedAt = joined },
                new() { OrganizationId = Guid.NewGuid(), OrganizationName = "Отклонённая", StatusCode = "declined", Email = "contact@example.invalid" }
            ]
        };

        var card = new UserCardViewModel(overview, true);
        var tab = card.Tabs.Single(tab => tab.IsOrganizations);
        var organization = Assert.Single(tab.Items);
        Assert.Equal("Текущая", organization.Title);
        Assert.Equal("contact@example.invalid", organization.Fields.Single(field => field.Title == "Кто пригласил").Value);
        Assert.Contains(organization.Fields, field => field.Title == "В организации с");
        Assert.Contains(organization.Fields, field => field.Title == "Приглашён");
        Assert.Null(tab.LimitNote);
    }

    [Fact]
    public void OrganizationStatusSwitchesCurrentOrganizationsAndNotifiesEmptyState()
    {
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Organizations = [
                new() { OrganizationName = "Активная", IsActive = true },
                new() { OrganizationName = "Архив", IsActive = true, IsArchived = true },
                new() { OrganizationName = "Удалена", IsDeleted = true, IsArchived = true },
                new() { OrganizationName = "Неактивная", IsActive = false }
            ]
        }, false);
        var tab = card.Tabs.Single(tab => tab.IsOrganizations);
        var notifications = new List<string?>();
        tab.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Equal("Активные", card.OrganizationStatus);
        Assert.Equal("Активная", Assert.Single(tab.Items).Title);
        card.OrganizationStatus = "Архивированные";
        Assert.Equal("Архив", Assert.Single(tab.Items).Title);
        card.OrganizationStatus = "Удалённые";
        Assert.Equal("Удалена", Assert.Single(tab.Items).Title);
        Assert.Contains(nameof(tab.HasItems), notifications);
        Assert.Contains(nameof(tab.Items), notifications);
    }

    [Fact]
    public void CurrentOrganizationWithoutMatchingInvitationDoesNotInventAnInviter()
    {
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Profile = new() { Email = "contact@example.invalid" },
            Organizations = [new() { OrganizationName = "Созданная", IsOwner = true, IsActive = true }],
            Invitations = [new() { StatusCode = "pending", Email = "contact@example.invalid", InvitedBy = "contact@example.invalid" }]
        }, false);
        var organization = Assert.Single(card.Tabs.Single(tab => tab.IsOrganizations).Items);
        Assert.DoesNotContain(organization.Fields, field => field.Title == "Кто пригласил" || field.Title == "Приглашён");
    }

    [Fact]
    public void SubscriptionStatusFiltersOrganizationSubscriptionsAndDoesNotMixPersonalHistory()
    {
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Subscriptions = new()
            {
                Organizations = [new() { OrganizationName = "Студия", PlanName = "Бизнес", IsActive = true },
                    new() { OrganizationName = "СТО", PlanName = "Базовый", IsActive = false }],
                Users = [new() { PlanName = "Личный", IsActive = true }],
                History = [new() { PlanName = "Старый личный", Status = "created" }]
            }
        }, true);
        var tab = card.Tabs.Single(tab => tab.IsSubscriptions);
        Assert.Equal("Активные", card.SubscriptionStatus);
        Assert.Equal("Студия · Бизнес", Assert.Single(tab.Items).Title);
        card.SubscriptionStatus = "Завершённые";
        Assert.Equal("СТО · Базовый", Assert.Single(tab.Items).Title);
        card.SubscriptionStatus = "Все";
        Assert.Equal(2, tab.Items.Count);
        Assert.DoesNotContain(tab.Items, item => item.Title.Contains("личный", StringComparison.OrdinalIgnoreCase));
        var withoutBilling = new UserCardViewModel(new AdminUserOverview(), false);
        withoutBilling.SubscriptionStatus = "Все";
        Assert.DoesNotContain(withoutBilling.Tabs, tab => tab.IsSubscriptions);
    }

    [Fact]
    public void VehicleTabShowsOnlyPersonalVehiclesAndTheirMaintenances()
    {
        var personalId = Guid.NewGuid();
        var organizationVehicleId = Guid.NewGuid();
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Vehicles = [
                new() { Id = personalId, Title = "Личная машина" },
                new() { Id = organizationVehicleId, Title = "Машина организации", OrganizationId = Guid.NewGuid(), OrganizationName = "СТО" }
            ],
            Maintenances = [
                new() { VehicleId = personalId, VehicleTitle = "Личная машина" },
                new() { VehicleId = organizationVehicleId, VehicleTitle = "Машина организации" },
                new() { VehicleId = Guid.NewGuid(), VehicleTitle = "Неизвестная машина" }
            ]
        }, false);
        var tab = card.Tabs.Single(tab => tab.DisplayTitle == "Личная техника и ТО");
        Assert.Equal(new[] { "Личная машина", "ТО · Личная машина" }, tab.Items.Select(item => item.Title));
    }

    [Fact]
    public void LocaleSwitchPreservesCardTabAndFiltersWhileRebuildingDisplayProjection()
    {
        var overview = new AdminUserOverview
        {
            Profile = new() { Email = "kept@example.invalid" },
            Organizations = [
                new() { OrganizationName = "Active workspace", IsActive = true },
                new() { OrganizationName = "Archived workspace", IsActive = true, IsArchived = true }
            ],
            Subscriptions = new()
            {
                Organizations = [
                    new() { OrganizationName = "Active workspace", PlanName = "Stored plan", IsActive = true },
                    new() { OrganizationName = "Archived workspace", PlanName = "Stored plan", IsActive = false }
                ]
            }
        };
        var card = new UserCardViewModel(overview, true);
        card.OrganizationStatus = "Архивированные";
        card.SelectedTab = card.Tabs.Single(tab => tab.Id == "organizations");

        AdminLocalization.Instance.Language = "en";
        card.RefreshLocalizedValues();

        Assert.Equal("organizations", card.SelectedTab.Id);
        Assert.Equal("Archived workspace", Assert.Single(card.SelectedTab.Items).Title);
        Assert.Equal("Archived", card.OrganizationStatus);
        Assert.Equal("Organizations", card.SelectedTab.DisplayTitle);
        Assert.Equal("kept@example.invalid", card.Title);

        AdminLocalization.Instance.Language = "zh-Hans";
        card.RefreshLocalizedValues();

        Assert.Equal("organizations", card.SelectedTab.Id);
        Assert.Equal("Archived workspace", Assert.Single(card.SelectedTab.Items).Title);
        Assert.Equal("已归档", card.OrganizationStatus);
        Assert.Equal("组织", card.SelectedTab.DisplayTitle);
        Assert.Equal("kept@example.invalid", card.Title);
    }

    [Fact]
    public void TimelineAndCodedLabelsLocalizeWhileUserTitlesAndUnknownValuesStayRaw()
    {
        var vehicleId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow;
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Timeline = [
                new() { Type = "registration", Label = "Регистрация", Title = "Пользователь зарегистрировался" },
                new() { Type = "login", Label = "Вход", Title = "Последний вход пользователя" },
                new() { Type = "vehicle", Label = "Техника", Title = "My vehicle" },
                new() { Type = "future_event", Label = "Stored label", Title = "Stored title" }
            ],
            Profile = new() { Email = "coded@example.invalid" },
            Organizations = [new() { OrganizationId = organizationId, OrganizationName = "Stored organization", PrimaryScenarioCode = "detailing", ScenarioName = "Детейлинг", RoleCode = "admin", RoleName = "Администратор", JoinedAt = joinedAt, IsActive = true }],
            Invitations = [new() { OrganizationId = organizationId, Email = "coded@example.invalid", StatusCode = "accepted", RoleCode = "admin", RoleName = "Администратор", AcceptedAt = joinedAt, InvitedBy = "inviter@example.invalid" }],
            Vehicles = [
                new() { Id = vehicleId, Kind = 1, KindName = "PassengerCar", MileageUnit = 1, MileageUnitName = "Kilometers", CurrentMileage = 1200, Title = "My vehicle" },
                new() { Id = Guid.NewGuid(), Kind = 500, KindName = "Vendor kind", MileageUnit = 99, MileageUnitName = "Vendor unit", CurrentMileage = 8, Title = "Unknown vehicle" }
            ],
            Maintenances = [new() { VehicleId = vehicleId, VehicleTitle = "My vehicle", Status = 3, StatusName = "Completed", Type = 2, TypeName = "Repair" }],
            SupportMessages = [new() { TypeCode = "question", TypeName = "Вопрос", StatusCode = "in_progress", StatusName = "В работе" }],
            Payments = [new() { PaymentStatus = "completed", PaymentStatusName = "Оплачено", PaymentMethod = "workspace_purchase:code", PaymentMethodName = "Покупка организации" }]
        }, true);
        var timeline = card.Timeline.ToDictionary(item => item.Title);
        var vehicle = card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title == "My vehicle");
        var maintenance = card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title.StartsWith("ТО", StringComparison.Ordinal));

        Assert.Equal("Пользователь зарегистрировался", timeline["Пользователь зарегистрировался"].Title);
        Assert.Equal("My vehicle", timeline["My vehicle"].Title);
        Assert.Equal("Stored label", timeline["Stored title"].Subtitle.Split(" · ")[0]);
        Assert.Contains("Легковой автомобиль", vehicle.Subtitle);
        Assert.Contains("Завершено", maintenance.Subtitle);
        Assert.Equal("Вопрос", card.Tabs.Single(tab => tab.Id == "support").Items[0].Subtitle.Split(" · ")[0]);
        Assert.Equal("Оплачено", card.Tabs.Single(tab => tab.Id == "payments").Items[0].Subtitle.Split(" · ")[1]);
        var organizationItem = Assert.Single(card.Tabs.Single(tab => tab.Id == "organizations").Items);
        Assert.Equal("Детейлинг · Администратор", organizationItem.Subtitle);
        Assert.Contains(organizationItem.Fields, field => field.Title == "Роль приглашения" && field.Value == "Администратор");

        AdminLocalization.Instance.Language = "en";
        card.RefreshLocalizedValues();

        var englishTimeline = card.Timeline.ToDictionary(item => item.Title);
        var englishVehicle = card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title == "My vehicle");
        var englishMaintenance = card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title.StartsWith("Maintenance", StringComparison.Ordinal));
        Assert.Equal("Registered user", englishTimeline["Registered user"].Title);
        Assert.Equal("Last user sign-in", englishTimeline["Last user sign-in"].Title);
        Assert.Equal("My vehicle", englishTimeline["My vehicle"].Title);
        Assert.Equal("Stored label", englishTimeline["Stored title"].Subtitle.Split(" · ")[0]);
        Assert.Contains("Passenger car", englishVehicle.Subtitle);
        Assert.Contains("Vendor kind", card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title == "Unknown vehicle").Subtitle);
        Assert.Contains("Vendor unit", card.Tabs.Single(tab => tab.Id == "vehicles").Items.Single(item => item.Title == "Unknown vehicle").Fields.Single(field => field.Title == "Mileage").Value);
        Assert.Contains("Completed", englishMaintenance.Subtitle);
        Assert.Equal("Question", card.Tabs.Single(tab => tab.Id == "support").Items[0].Subtitle.Split(" · ")[0]);
        Assert.Equal("Paid", card.Tabs.Single(tab => tab.Id == "payments").Items[0].Subtitle.Split(" · ")[1]);
        var englishOrganization = Assert.Single(card.Tabs.Single(tab => tab.Id == "organizations").Items);
        Assert.Equal("Detailing · Administrator", englishOrganization.Subtitle);
        Assert.Contains(englishOrganization.Fields, field => field.Title == "Invitation role" && field.Value == "Administrator");

        AdminLocalization.Instance.Language = "zh-Hans";
        card.RefreshLocalizedValues();
        Assert.Contains("乘用车", card.Tabs.Single(tab => tab.Id == "vehicles").Items[0].Subtitle);
    }

    [Fact]
    public void OnlyOrganizationalVehiclesResultInEmptyPersonalVehicleTab()
    {
        var vehicleId = Guid.NewGuid();
        var card = new UserCardViewModel(new AdminUserOverview
        {
            Vehicles = [new() { Id = vehicleId, Title = "Рабочая", OrganizationId = Guid.NewGuid() }],
            Maintenances = [new() { VehicleId = vehicleId, VehicleTitle = "Рабочая" }]
        }, false);
        Assert.False(card.Tabs.Single(tab => tab.DisplayTitle == "Личная техника и ТО").HasItems);
    }

    private static AdminUserOverview CreateOverview(
        AdminUserOverviewProfile profile,
        AdminUserOverviewOrganization? organization = null,
        IReadOnlyList<AdminUserTimelineItem>? timeline = null,
        IReadOnlyList<AdminUserOverviewPayment>? payments = null,
        IReadOnlyList<AdminUserOverviewInvitation>? invitations = null) => new()
    {
        Profile = profile,
        Organizations = organization is null ? [] : [organization],
        Timeline = timeline ?? [],
        Payments = payments ?? [],
        Invitations = invitations ?? []
    };

    private static string GetBaseTitle(string title) => title.Split('·')[0].Trim();
}
