using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис загрузки сводки по пользователю. </summary>
internal sealed class AdminUserOverviewService(OpenCRMContext opencrmContext, bool commercialEnabled)
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает сводку по пользователю для карточки. </summary>
    public async Task<AdminUserOverviewDto?> GetOverviewAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.Date.AddDays(-6);

        var scenarioNames = await this._opencrmContext.OrganizationTypeDefinitions
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Name);

        var user = await this._opencrmContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.EmailConfirmed,
                Phone = x.PhoneNumber,
                x.PhoneNumberConfirmed,
                x.PhoneFiltered,
                x.DatetimeCreation,
                x.LastDateOfEntry,
                x.LastLoginDate,
                x.CountLogins,
                x.CountVisits,
                x.LockoutEnabled,
                x.LockoutEnd,
                x.AccessFailedCount,
                HasPassword = !string.IsNullOrEmpty(x.PasswordHash),
                x.PersonalDataProcessingConsentAccepted,
                x.PersonalDataProcessingConsentAcceptedAt,
                x.AdvertisingConsentAccepted,
                x.AdvertisingConsentAcceptedAt,
                x.FirstUtmSource,
                x.FirstUtmMedium,
                x.FirstUtmCampaign,
                x.FirstUtmContent,
                x.FirstUtmTerm,
                x.FirstVisitAt,
                x.LastUtmSource,
                x.LastUtmMedium,
                x.LastUtmCampaign,
                x.LastUtmContent,
                x.LastUtmTerm,
                x.LastVisitAt,
                x.CanUseTestSubscriptions,
                x.IsDeleted,
                x.DeletedAt
            })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return null;
        }

        var roles = await this.LoadRolesAsync(userId);
        var vehicles = await this.LoadVehiclesAsync(userId);
        var maintenances = await this.LoadMaintenancesAsync(userId);
        var organizations = await this.LoadOrganizationsAsync(userId, scenarioNames, now);
        var invitations = await this.LoadInvitationsAsync(userId, user.Email);
        var userSubscriptions = commercialEnabled ? await this.LoadUserSubscriptionsAsync(userId, now) : [];
        var subscriptionHistory = commercialEnabled ? await this.LoadSubscriptionHistoryAsync(userId) : [];
        var organizationSubscriptions = commercialEnabled ? await this.LoadOrganizationSubscriptionsAsync(userId, now) : [];
        var payments = commercialEnabled ? await this.LoadPaymentsAsync(userId) : [];
        var paymentSummary = commercialEnabled ? await this.LoadPaymentSummaryAsync(userId) : new PaymentSummary(0m, 0);
        var featureGrants = commercialEnabled ? await this.LoadFeatureGrantsAsync(userId, now) : [];
        var supportMessages = await this.LoadSupportMessagesAsync(userId, user.Email, user.Phone);
        var charts = await this.LoadChartsAsync(userId, sevenDaysAgo, now.Date);

        var timeline = AdminUserOverviewService.BuildTimeline(
            user.DatetimeCreation,
            user.LastLoginDate,
            vehicles.Select(x => new TimelineItem("vehicle", "Техника", x.Title, x.DateTimeCreation, x.Id.ToString())),
            payments.Select(x => new TimelineItem(
                "payment",
                "Платеж",
                $"{AdminUserOverviewService.FormatMoney(x.Amount)} · {x.PlanName}",
                x.PaymentDate,
                x.ExternalPaymentId ?? x.Id.ToString())),
            userSubscriptions.Select(x => new TimelineItem("subscription", "Подписка", x.PlanName, x.StartDate, x.Id.ToString())),
            supportMessages.Select(x => new TimelineItem("support", "Обращение", x.Topic, x.CreatedAt, x.Id.ToString())),
            organizations.Select(x => new TimelineItem("organization", "Организация", x.OrganizationName, x.JoinedAt, x.OrganizationId.ToString())))
            .Take(40)
            .ToList();

        var maintenancesTotal = await this.LoadMaintenanceCountAsync(userId);

        return new AdminUserOverviewDto
        {
            GeneratedAt = now,
            Profile = new AdminUserOverviewProfileDto
            {
                Id = user.Id,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                Phone = user.Phone,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                PhoneFiltered = user.PhoneFiltered,
                DatetimeCreation = user.DatetimeCreation,
                LastDateOfEntry = user.LastDateOfEntry,
                LastLoginDate = user.LastLoginDate,
                CountLogins = user.CountLogins,
                CountVisits = user.CountVisits,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd,
                AccessFailedCount = user.AccessFailedCount,
                HasPassword = user.HasPassword,
                PersonalDataProcessingConsentAccepted = user.PersonalDataProcessingConsentAccepted,
                PersonalDataProcessingConsentAcceptedAt = user.PersonalDataProcessingConsentAcceptedAt,
                AdvertisingConsentAccepted = user.AdvertisingConsentAccepted,
                AdvertisingConsentAcceptedAt = user.AdvertisingConsentAcceptedAt,
                FirstUtmSource = user.FirstUtmSource,
                FirstUtmMedium = user.FirstUtmMedium,
                FirstUtmCampaign = user.FirstUtmCampaign,
                FirstUtmContent = user.FirstUtmContent,
                FirstUtmTerm = user.FirstUtmTerm,
                FirstVisitAt = user.FirstVisitAt,
                LastUtmSource = user.LastUtmSource,
                LastUtmMedium = user.LastUtmMedium,
                LastUtmCampaign = user.LastUtmCampaign,
                LastUtmContent = user.LastUtmContent,
                LastUtmTerm = user.LastUtmTerm,
                LastVisitAt = user.LastVisitAt,
                CanUseTestSubscriptions = commercialEnabled && user.CanUseTestSubscriptions,
                IsDeleted = user.IsDeleted,
                DeletedAt = user.DeletedAt,
                Roles = roles
            },
            Kpis = new AdminUserOverviewKpisDto
            {
                VehiclesTotal = vehicles.Count,
                VehiclesActive = vehicles.Count(x => !x.IsDeleted && !x.IsArchived),
                MaintenancesTotal = maintenancesTotal,
                OrganizationsTotal = organizations.Count,
                PaymentsTotal = paymentSummary.Count,
                PaymentsAmount = paymentSummary.Amount,
                ActiveUserSubscriptions = userSubscriptions.Count(x => x.IsActive),
                ActiveOrganizationSubscriptions = organizationSubscriptions.Count(x => x.IsActive),
                ActiveFeatureGrants = featureGrants.Count(x => x.IsActive),
                SupportOpen = supportMessages.Count(x => x.StatusCode is SupportMessageStatusCodes.New or SupportMessageStatusCodes.InProgress)
            },
            Charts = new AdminUserOverviewChartsDto
            {
                Registrations = charts.Registrations,
                Maintenances = charts.Maintenances,
                Payments = charts.Payments
            },
            Vehicles = vehicles,
            Maintenances = maintenances,
            Organizations = organizations,
            Invitations = invitations,
            Subscriptions = new AdminUserOverviewSubscriptionsDto
            {
                Users = userSubscriptions,
                Organizations = organizationSubscriptions,
                History = subscriptionHistory
            },
            Payments = payments,
            FeatureGrants = featureGrants,
            SupportMessages = supportMessages,
            Timeline = timeline
        };
    }

    /// <summary> Загружает роли пользователя. </summary>
    private Task<List<string?>> LoadRolesAsync(string userId) =>
        this._opencrmContext.UserRoles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Join(
                this._opencrmContext.Roles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => role.Name)
            .Where(x => x != null)
            .OrderBy(x => x)
            .ToListAsync();

    /// <summary> Загружает технику пользователя. </summary>
    private Task<List<AdminUserOverviewVehicleDto>> LoadVehiclesAsync(string userId) =>
        this._opencrmContext.Vehicles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.DateTimeCreation)
            .Select(x => new AdminUserOverviewVehicleDto
            {
                Id = x.Id,
                Kind = x.Kind,
                KindName = x.Kind.ToString(),
                Title = x.Name ?? x.RegistrationPlate ?? x.Vin ?? "Техника",
                Brand = x.Brand != null ? x.Brand.Name : x.ManualBrandName,
                Model = x.Model != null ? x.Model.Name : x.ManualModelName,
                YearIssue = x.YearIssue,
                RegistrationPlate = x.RegistrationPlate,
                Vin = x.Vin,
                CurrentMileage = x.CurrentMileage,
                MileageUnit = x.MileageUnit,
                MileageUnitName = x.MileageUnit.ToString(),
                DateTimeCreation = x.DateTimeCreation,
                IsArchived = x.IsArchived,
                IsDeleted = x.IsDeleted,
                OrganizationId = x.OrganizationId,
                OrganizationName = x.Organization != null ? x.Organization.Name : null,
                MaintenancesCount = x.Maintenance.Count(maintenance => !maintenance.IsDeleted),
                OpenDefectsCount = x.Defects.Count(defect => !defect.IsDeleted && defect.StatusCode != VehicleDefectStatusCodes.Resolved),
                LastMaintenanceAt = x.Maintenance
                    .Where(maintenance => !maintenance.IsDeleted)
                    .OrderByDescending(maintenance => maintenance.Date)
                    .Select(maintenance => (DateTime?)maintenance.Date)
                    .FirstOrDefault()
            })
            .Take(30)
            .ToListAsync();

    /// <summary> Загружает обслуживания пользователя. </summary>
    private Task<List<AdminUserOverviewMaintenanceDto>> LoadMaintenancesAsync(string userId) =>
        this._opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.Date)
            .Select(x => new AdminUserOverviewMaintenanceDto
            {
                Id = x.Id,
                Date = x.Date,
                DateTimeCreation = x.DateTimeCreation,
                Status = x.Status,
                StatusName = x.Status.ToString(),
                Type = x.Type,
                TypeName = x.Type.ToString(),
                VehicleTitle = x.Vehicle.Name ?? x.Vehicle.RegistrationPlate ?? x.Vehicle.Vin ?? "Техника",
                VehicleId = x.VehicleId,
                Location = x.Location,
                Performer = x.Performer,
                PaidAmount = x.PaidAmount,
                CurrencyCode = x.CurrencyCode,
                MaterialCostAmount = x.MaterialCostAmount,
                LaborCostAmount = x.LaborCostAmount,
                WorkActNumber = x.WorkActNumber,
                Comment = x.Comment
            })
            .Take(20)
            .ToListAsync();

    /// <summary> Загружает организации пользователя. </summary>
    private async Task<List<AdminUserOverviewOrganizationDto>> LoadOrganizationsAsync(string userId, IReadOnlyDictionary<string, string> scenarioNames, DateTime now)
    {
        var organizationRows = await this._opencrmContext.OrganizationMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.JoinedAt)
            .Select(x => new
            {
                x.OrganizationId,
                organizationName = x.Organization.Name,
                x.Organization.PrimaryScenarioCode,
                x.RoleCode,
                roleName = AdminUserRules.FormatOrganizationRole(x.RoleCode),
                x.JoinedAt,
                x.CreatedAt,
                x.Organization.OwnerUserId,
                isOwner = x.Organization.OwnerUserId == userId,
                x.Organization.IsActive,
                x.Organization.IsArchived,
                x.Organization.IsDeleted,
                membersCount = x.Organization.Members.Count,
                vehiclesCount = this._opencrmContext.Vehicles.Count(vehicle => vehicle.OrganizationId == x.OrganizationId && !vehicle.IsDeleted),
                clientsCount = x.Organization.Clients.Count(client => !client.IsDeleted)
            })
            .ToListAsync();

        var organizationIds = organizationRows.Select(x => x.OrganizationId).ToHashSet();
        var ownedOrganizationRows = await this._opencrmContext.Organizations
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId && !organizationIds.Contains(x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                OrganizationId = x.Id,
                organizationName = x.Name,
                x.PrimaryScenarioCode,
                RoleCode = OrganizationRoleCodes.Owner,
                roleName = AdminUserRules.FormatOrganizationRole(OrganizationRoleCodes.Owner),
                JoinedAt = x.CreatedAt,
                CreatedAt = x.CreatedAt,
                x.OwnerUserId,
                isOwner = true,
                x.IsActive,
                x.IsArchived,
                x.IsDeleted,
                membersCount = x.Members.Count,
                vehiclesCount = this._opencrmContext.Vehicles.Count(vehicle => vehicle.OrganizationId == x.Id && !vehicle.IsDeleted),
                clientsCount = x.Clients.Count(client => !client.IsDeleted)
            })
            .ToListAsync();

        var allOrganizationRows = organizationRows.Concat(ownedOrganizationRows).ToList();
        var allOrganizationIds = allOrganizationRows.Select(x => x.OrganizationId).ToArray();
        var defaultBusinessPlanName = commercialEnabled ? await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .Where(x => x.IsActive
                && x.IsDefaultPlan
                && x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() : null;
        var freePlanName = commercialEnabled ? await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .Where(x => x.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() : null;
        var activeOrganizationPlans = commercialEnabled ? await this._opencrmContext.OrganizationSubscriptions
            .AsNoTracking()
            .Where(x => allOrganizationIds.Contains(x.OrganizationId) && x.EndDate >= now)
            .Select(x => new
            {
                x.Id,
                x.OrganizationId,
                x.EndDate,
                planName = x.SubscriptionPlan.Name,
                x.SubscriptionPlan.IsDemoPlan
            })
            .ToListAsync() : [];
        var currentPlans = activeOrganizationPlans
            .GroupBy(x => x.OrganizationId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(x => x.EndDate)
                .First());

        var organizations = allOrganizationRows
            .OrderByDescending(x => x.JoinedAt)
            .Select(x =>
            {
                currentPlans.TryGetValue(x.OrganizationId, out var currentPlan);

                return new AdminUserOverviewOrganizationDto
                {
                    OrganizationId = x.OrganizationId,
                    OrganizationName = x.organizationName,
                    PrimaryScenarioCode = x.PrimaryScenarioCode,
                    ScenarioName = scenarioNames.TryGetValue(x.PrimaryScenarioCode, out var scenarioName)
                        ? scenarioName
                        : x.PrimaryScenarioCode,
                    RoleCode = x.RoleCode,
                    RoleName = x.roleName,
                    JoinedAt = x.JoinedAt,
                    CreatedAt = x.CreatedAt,
                    OwnerUserId = x.OwnerUserId,
                    IsOwner = x.isOwner,
                    IsActive = x.IsActive,
                    IsArchived = x.IsArchived,
                    IsDeleted = x.IsDeleted,
                    MembersCount = x.membersCount,
                    VehiclesCount = x.vehiclesCount,
                    ClientsCount = x.clientsCount,
                    CurrentPlanName = commercialEnabled ? currentPlan?.planName
                        ?? (x.IsActive && !x.IsArchived && !x.IsDeleted
                            ? x.PrimaryScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation
                                ? freePlanName ?? "Free не настроен"
                                : defaultBusinessPlanName ?? "Базовый тариф не настроен"
                            : "Нет активного тарифа") : string.Empty,
                    CurrentSubscriptionId = currentPlan?.Id,
                    CurrentPlanEndDate = currentPlan?.EndDate,
                    CurrentPlanIsDemo = currentPlan?.IsDemoPlan == true,
                    HasActiveSubscription = currentPlan != null
                };
            })
            .ToList();

        return organizations;
    }

    /// <summary> Загружает приглашения пользователя. </summary>
    private Task<List<AdminUserOverviewInvitationDto>> LoadInvitationsAsync(string userId, string? email) =>
        this._opencrmContext.OrganizationInvitations
            .AsNoTracking()
            .Where(x => x.InvitedByUserId == userId || x.AcceptedByUserId == userId || (email != null && x.Email == email))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminUserOverviewInvitationDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                OrganizationName = x.Organization.Name,
                Email = x.Email,
                RoleCode = x.RoleCode,
                RoleName = AdminUserRules.FormatOrganizationRole(x.RoleCode),
                StatusCode = x.StatusCode,
                CreatedAt = x.CreatedAt,
                AcceptedAt = x.AcceptedAt,
                RevokedAt = x.RevokedAt,
                InvitedBy = x.InvitedByUser.Email ?? x.InvitedByUser.PhoneNumber ?? x.InvitedByUserId
            })
            .Take(20)
            .ToListAsync();

    /// <summary> Загружает активные подписки пользователя. </summary>
    private Task<List<AdminUserSubscriptionDto>> LoadUserSubscriptionsAsync(string userId, DateTime now) =>
        this._opencrmContext.UserSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.EndDate)
            .Select(x => new AdminUserSubscriptionDto
            {
                Id = x.Id,
                SubscriptionPlanId = x.SubscriptionPlanId,
                PlanName = x.SubscriptionPlan.Name,
                PlanTypeCode = x.SubscriptionPlan.PlanTypeCode,
                IsDemoPlan = x.SubscriptionPlan.IsDemoPlan,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                AutoRenewal = x.AutoRenewal,
                IsActive = x.EndDate >= now,
                DaysLeft = (int)Math.Ceiling((x.EndDate - now).TotalDays)
            })
            .ToListAsync();

    /// <summary> Загружает историю подписок пользователя. </summary>
    private Task<List<AdminUserSubscriptionHistoryDto>> LoadSubscriptionHistoryAsync(string userId) =>
        this._opencrmContext.UserSubscriptionHistory
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminUserSubscriptionHistoryDto
            {
                Id = x.Id,
                SubscriptionPlanId = x.SubscriptionPlanId,
                PlanName = x.SubscriptionPlan.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                ActualEndDate = x.ActualEndDate,
                Status = x.Status,
                AutoRenewal = x.AutoRenewal,
                CreatedAt = x.CreatedAt,
                Comment = x.Comment
            })
            .Take(30)
            .ToListAsync();

    /// <summary> Загружает подписки организаций пользователя. </summary>
    private Task<List<AdminOrganizationSubscriptionDto>> LoadOrganizationSubscriptionsAsync(string userId, DateTime now) =>
        this._opencrmContext.OrganizationSubscriptions
            .AsNoTracking()
            .Where(x => x.Organization.OwnerUserId == userId || x.Organization.Members.Any(member => member.UserId == userId))
            .OrderByDescending(x => x.EndDate)
            .Select(x => new AdminOrganizationSubscriptionDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                OrganizationName = x.Organization.Name,
                SubscriptionPlanId = x.SubscriptionPlanId,
                PlanName = x.SubscriptionPlan.Name,
                PlanTypeCode = x.SubscriptionPlan.PlanTypeCode,
                IsDemoPlan = x.SubscriptionPlan.IsDemoPlan,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                AutoRenewal = x.AutoRenewal,
                IsActive = x.EndDate >= now,
                DaysLeft = (int)Math.Ceiling((x.EndDate - now).TotalDays)
            })
            .ToListAsync();

    /// <summary> Загружает платежи пользователя. </summary>
    private Task<List<AdminUserOverviewPaymentDto>> LoadPaymentsAsync(string userId) =>
        this._opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminUserOverviewPaymentDto
            {
                Id = x.Id,
                Amount = x.Amount,
                PaymentDate = x.CreatedAt,
                PaymentMethod = x.PaymentMethod,
                PaymentMethodName = AdminUserOverviewService.FormatPaymentMethod(x.PaymentMethod),
                PaymentStatus = x.PaymentStatus,
                PaymentStatusName = AdminUserOverviewService.FormatPaymentStatus(x.PaymentStatus),
                PaymentStatusGroup = AdminUserOverviewService.ResolvePaymentStatusGroup(x.PaymentStatus),
                TransactionId = x.TransactionId,
                ExternalPaymentId = x.ExternalPaymentId,
                PaymentLink = x.PaymentLink,
                SubscriptionPlanId = x.SubscriptionPlanId,
                PlanName = x.SubscriptionPlan.Name
            })
            .Take(40)
            .ToListAsync();

    /// <summary> Формирует итоговую сумму и количество платежей. </summary>
    private async Task<PaymentSummary> LoadPaymentSummaryAsync(string userId)
    {
        var paidPaymentsQuery = this.GetPaidPaymentsQuery(userId);
        var amount = await paidPaymentsQuery
            .SumAsync(payment => (decimal?)(payment.Amount -
                (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                    ? payment.Amount : payment.RefundedAmount))) ?? 0m;
        var count = await paidPaymentsQuery.CountAsync();

        return new PaymentSummary(amount, count);
    }

    /// <summary> Загружает предоставленные доступы к функциям пользователя. </summary>
    private Task<List<AdminUserOverviewFeatureGrantDto>> LoadFeatureGrantsAsync(string userId, DateTime now) =>
        this._opencrmContext.UserFeatureGrants
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminUserOverviewFeatureGrantDto
            {
                Id = x.Id,
                FeatureDefinitionId = x.FeatureDefinitionId,
                FeatureCode = x.FeatureDefinition.Code,
                FeatureName = x.FeatureDefinition.Name,
                ValueType = x.FeatureDefinition.ValueType.ToString(),
                BoolValue = x.BoolValue,
                IntValue = x.IntValue,
                IsEnabled = x.IsEnabled,
                CreatedAt = x.CreatedAt,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Source = x.Source,
                Comment = x.Comment,
                GrantedByUserId = x.GrantedByUserId,
                IsActive = x.IsEnabled &&
                    (!x.StartDate.HasValue || x.StartDate <= now) &&
                    (!x.EndDate.HasValue || x.EndDate >= now)
            })
            .Take(40)
            .ToListAsync();

    /// <summary> Загружает обращения пользователя в поддержку. </summary>
    private Task<List<AdminUserOverviewSupportMessageDto>> LoadSupportMessagesAsync(string userId, string? email, string? phone) =>
        this._opencrmContext.SupportMessages
            .AsNoTracking()
            .Where(x => x.UserId == userId ||
                (email != null && x.Email == email) ||
                (phone != null && x.Phone == phone))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminUserOverviewSupportMessageDto
            {
                Id = x.Id,
                TypeCode = x.TypeCode,
                TypeName = AdminUserOverviewService.FormatSupportType(x.TypeCode),
                StatusCode = x.StatusCode,
                StatusName = AdminUserOverviewService.FormatSupportStatus(x.StatusCode),
                Topic = x.Topic,
                Text = x.Text,
                SourcePage = x.SourcePage,
                AdminComment = x.AdminComment,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                ClosedAt = x.ClosedAt
            })
            .Take(20)
            .ToListAsync();

    /// <summary> Загружает данные диаграмм карточки пользователя. </summary>
    private async Task<AdminUserOverviewChartsDto> LoadChartsAsync(string userId, DateTime from, DateTime to)
    {
        var registrationSeries = await AdminUserOverviewService.BuildDailySeriesAsync(
            from,
            to,
            this._opencrmContext.ApplicationUsers
                .AsNoTracking()
                .Where(x => x.Id == userId && x.DatetimeCreation >= from)
                .GroupBy(x => x.DatetimeCreation.Date)
                .Select(x => new DailyPoint(x.Key, x.Count())));
        var maintenanceSeries = await AdminUserOverviewService.BuildDailySeriesAsync(
            from,
            to,
            this._opencrmContext.Maintenance
                .AsNoTracking()
                .Where(x => x.UserId == userId && !x.IsDeleted && x.DateTimeCreation >= from)
                .GroupBy(x => x.DateTimeCreation.Date)
                .Select(x => new DailyPoint(x.Key, x.Count())));
        var paidPaymentsQuery = this.GetPaidPaymentsQuery(userId);
        var paymentSeries = commercialEnabled ? await AdminUserOverviewService.BuildMoneySeriesAsync(
            from,
            to,
            paidPaymentsQuery
                .Where(payment => payment.PaidAt >= from)
                .GroupBy(payment => payment.PaidAt!.Value.Date)
                .Select(group => new DailyMoneyPoint(group.Key, group.Sum(payment => payment.Amount -
                    (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                        ? payment.Amount : payment.RefundedAmount))))) : [];

        return new AdminUserOverviewChartsDto
        {
            Registrations = registrationSeries,
            Maintenances = maintenanceSeries,
            Payments = paymentSeries
        };
    }

    /// <summary> Возвращает количество обслуживаний пользователя. </summary>
    private Task<int> LoadMaintenanceCountAsync(string userId) => this._opencrmContext.Maintenance
        .AsNoTracking()
        .CountAsync(x => x.UserId == userId && !x.IsDeleted);

    /// <summary> Возвращает запрос оплаченных платежей пользователя. </summary>
    private IQueryable<SubscriptionPayment> GetPaidPaymentsQuery(string userId) => this._opencrmContext.SubscriptionPayments
        .AsNoTracking()
        .Where(payment => payment.UserId == userId && payment.PaidAt != null
            && (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                || payment.PaymentStatus == "failed"));

    /// <summary> Итоговая сумма и количество платежей. </summary>
    private sealed record PaymentSummary(decimal Amount, int Count);

    /// <summary> Формирует ежедневный ряд счётчиков. </summary>
    private static async Task<IReadOnlyList<AdminUserOverviewDailyCountDto>> BuildDailySeriesAsync(
        DateTime from,
        DateTime to,
        IQueryable<DailyPoint> query)
    {
        var points = await query.ToDictionaryAsync(x => x.Date, x => x.Count);

        return AdminUserOverviewService.EachDay(from, to)
            .Select(date => new AdminUserOverviewDailyCountDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = points.TryGetValue(date, out var count) ? count : 0
            })
            .ToList();
    }

    /// <summary> Формирует ежедневный ряд сумм. </summary>
    private static async Task<IReadOnlyList<AdminUserOverviewDailyMoneyDto>> BuildMoneySeriesAsync(
        DateTime from,
        DateTime to,
        IQueryable<DailyMoneyPoint> query)
    {
        var points = await query.ToDictionaryAsync(x => x.Date, x => x.Amount);

        return AdminUserOverviewService.EachDay(from, to)
            .Select(date => new AdminUserOverviewDailyMoneyDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = points.TryGetValue(date, out var amount) ? amount : 0m
            })
            .ToList();
    }

    /// <summary> Перечисляет дни между двумя датами. </summary>
    private static IEnumerable<DateTime> EachDay(DateTime from, DateTime to)
    {
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    /// <summary> Строит объединённый таймлайн событий пользователя. </summary>
    private static IEnumerable<AdminUserTimelineItemDto> BuildTimeline(
        DateTime registeredAt,
        DateTime lastLoginAt,
        params IEnumerable<TimelineItem>[] groups)
    {
        var items = groups
            .SelectMany(x => x)
            .ToList();

        items.Add(new TimelineItem("registration", "Регистрация", "Пользователь зарегистрировался", registeredAt, "registration"));

        if (lastLoginAt > DateTime.MinValue.AddYears(1))
        {
            items.Add(new TimelineItem("login", "Вход", "Последний вход пользователя", lastLoginAt, "last-login"));
        }

        return items
            .OrderByDescending(x => x.Date)
            .Select(x => new AdminUserTimelineItemDto
            {
                Type = x.Type,
                Label = x.Label,
                Title = x.Title,
                Date = x.Date,
                Key = x.Key
            });
    }

    /// <summary> Формирует тип обращения в поддержку. </summary>
    private static string FormatSupportType(string? typeCode) => typeCode switch
    {
        SupportMessageTypeCodes.Question => "Вопрос",
        SupportMessageTypeCodes.Suggestion => "Предложение",
        SupportMessageTypeCodes.Bug => "Ошибка",
        SupportMessageTypeCodes.Billing => "Биллинг",
        SupportMessageTypeCodes.Cooperation => "Сотрудничество",
        _ => typeCode ?? "Тип не указан"
    };

    /// <summary> Формирует статус обращения в поддержку. </summary>
    private static string FormatSupportStatus(string? statusCode) => statusCode switch
    {
        SupportMessageStatusCodes.New => "Новое",
        SupportMessageStatusCodes.InProgress => "В работе",
        SupportMessageStatusCodes.Closed => "Закрыто",
        SupportMessageStatusCodes.Spam => "Спам",
        _ => statusCode ?? "Статус не указан"
    };

    /// <summary> Формирует статус платежа. </summary>
    private static string FormatPaymentStatus(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "completed" => "Оплачено",
        "succeeded" => "Успешно в YooKassa",
        "pending" => "Ожидание",
        "processing" => "Обработка",
        "waiting_for_capture" => "Ждет подтверждения",
        "paid_waiting_confirmation" => "Оплачен, ждет подтверждения",
        "canceled" => "Отменено",
        "failed" => "Ошибка",
        "refunded" => "Возврат",
        "" => "Без статуса",
        var value => value
    };

    /// <summary> Определяет группу статуса платежа. </summary>
    private static string ResolvePaymentStatusGroup(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized is "completed" or "succeeded" or "success" or "paid" or "done")
        {
            return "success";
        }

        if (normalized is "pending" or "processing" or "waiting_for_capture" or "paid_waiting_confirmation")
        {
            return "pending";
        }

        if (normalized.Contains("cancel") || normalized.Contains("fail") || normalized.Contains("refund") || normalized.Contains("error"))
        {
            return "problem";
        }

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : "other";
    }

    /// <summary> Формирует название способа оплаты. </summary>
    private static string FormatPaymentMethod(string? method) => (method ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "yookassa" => "YooKassa",
        "yookassa_extension" => "YooKassa: продление",
        var value when value.StartsWith("workspace_purchase:") => "Покупка организации",
        var value when value.StartsWith("workspace_extension:") => "Продление организации",
        "" => "Не указан",
        var value => value
    };

    /// <summary> Форматирует денежную сумму. </summary>
    private static string FormatMoney(decimal amount) => $"{amount:N0} ₽";

    /// <summary> Точка ежедневного счётчика. </summary>
    private sealed record DailyPoint(DateTime Date, int Count);

    /// <summary> Точка ежедневной суммы. </summary>
    private sealed record DailyMoneyPoint(DateTime Date, decimal Amount);

    /// <summary> Элемент таймлайна событий пользователя. </summary>
    private sealed record TimelineItem(string Type, string Label, string Title, DateTime Date, string Key);
}
