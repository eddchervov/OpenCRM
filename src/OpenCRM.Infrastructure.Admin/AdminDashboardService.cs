using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис данных главной страницы администрирования. </summary>
public sealed class AdminDashboardService(
    OpenCRMContext opencrmContext,
    AdminDashboardCache dashboardCache,
    TimeProvider timeProvider) : IAdminDashboardService
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает данные дашборда за выбранный период. </summary>
    public Task<AdminDashboardDto> GetAsync(int periodDays, bool canViewBilling)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return dashboardCache.GetOrCreateAsync(
            periodDays,
            canViewBilling,
            now.Date,
            () => this.BuildAsync(periodDays, canViewBilling, now));
    }

    private async Task<AdminDashboardDto> BuildAsync(int periodDays, bool canViewBilling, DateTime now)
    {
        var today = now.Date;
        var sevenDaysAgo = today.AddDays(-6);
        var thirtyDaysAgo = today.AddDays(-29);
        var periodStart = today.AddDays(-(periodDays - 1));
        var periodEnd = today.AddDays(1);
        var emptyLoginCutoff = DateTime.MinValue.AddYears(1);

        IReadOnlyList<AdminDashboardCountPointDto> registrationSeries;
        IReadOnlyList<AdminDashboardCountPointDto> loginSeries;
        IReadOnlyList<AdminDashboardCountPointDto> maintenanceSeries;
        IReadOnlyList<AdminDashboardCountPointDto> emailSeries;
        IReadOnlyList<AdminDashboardMoneyPointDto> paymentSeries;

        if (periodDays == 1)
        {
            registrationSeries = await BuildHourlySeriesAsync(
                today,
                now.Hour,
                this._opencrmContext.ApplicationUsers
                    .AsNoTracking()
                    .Where(user => !user.IsDeleted && user.DatetimeCreation >= today)
                    .GroupBy(user => user.DatetimeCreation.Hour)
                    .Select(group => new HourlyPoint(group.Key, group.Count())));

            loginSeries = await BuildHourlySeriesAsync(
                today,
                now.Hour,
                this._opencrmContext.ApplicationUsers
                    .AsNoTracking()
                    .Where(user => !user.IsDeleted && user.LastLoginDate >= today)
                    .GroupBy(user => user.LastLoginDate.Hour)
                    .Select(group => new HourlyPoint(group.Key, group.Count())));

            maintenanceSeries = await BuildHourlySeriesAsync(
                today,
                now.Hour,
                this._opencrmContext.Maintenance
                    .AsNoTracking()
                    .Where(maintenance => !maintenance.IsDeleted && maintenance.DateTimeCreation >= today)
                    .GroupBy(maintenance => maintenance.DateTimeCreation.Hour)
                    .Select(group => new HourlyPoint(group.Key, group.Count())));

            emailSeries = await BuildHourlySeriesAsync(
                today,
                now.Hour,
                this._opencrmContext.EmailMessages
                    .AsNoTracking()
                    .Where(message => message.CreatedAt >= today)
                    .GroupBy(message => message.CreatedAt.Hour)
                    .Select(group => new HourlyPoint(group.Key, group.Count())));

            paymentSeries = canViewBilling
                ? await BuildHourlyMoneySeriesAsync(
                    today,
                    now.Hour,
                    this._opencrmContext.SubscriptionPayments
                        .AsNoTracking()
                        .Where(payment =>
                            payment.PaidAt >= today &&
                            (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                                || payment.PaymentStatus == "failed"))
                        .GroupBy(payment => payment.PaidAt!.Value.Hour)
                        .Select(group => new HourlyMoneyPoint(group.Key, group.Sum(payment => payment.Amount -
                            (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                                ? payment.Amount : payment.RefundedAmount)))))
                : [];
        }
        else
        {
            registrationSeries = await BuildDailySeriesAsync(
                periodStart,
                today,
                this._opencrmContext.ApplicationUsers
                    .AsNoTracking()
                    .Where(user => !user.IsDeleted && user.DatetimeCreation >= periodStart)
                    .GroupBy(user => user.DatetimeCreation.Date)
                    .Select(group => new DailyPoint(group.Key, group.Count())));

            loginSeries = await BuildDailySeriesAsync(
                periodStart,
                today,
                this._opencrmContext.ApplicationUsers
                    .AsNoTracking()
                    .Where(user => !user.IsDeleted && user.LastLoginDate >= periodStart)
                    .GroupBy(user => user.LastLoginDate.Date)
                    .Select(group => new DailyPoint(group.Key, group.Count())));

            maintenanceSeries = await BuildDailySeriesAsync(
                periodStart,
                today,
                this._opencrmContext.Maintenance
                    .AsNoTracking()
                    .Where(maintenance => !maintenance.IsDeleted && maintenance.DateTimeCreation >= periodStart)
                    .GroupBy(maintenance => maintenance.DateTimeCreation.Date)
                    .Select(group => new DailyPoint(group.Key, group.Count())));

            emailSeries = await BuildDailySeriesAsync(
                periodStart,
                today,
                this._opencrmContext.EmailMessages
                    .AsNoTracking()
                    .Where(message => message.CreatedAt >= periodStart)
                    .GroupBy(message => message.CreatedAt.Date)
                    .Select(group => new DailyPoint(group.Key, group.Count())));

            paymentSeries = canViewBilling
                ? await BuildMoneySeriesAsync(
                    periodStart,
                    today,
                    this._opencrmContext.SubscriptionPayments
                        .AsNoTracking()
                        .Where(payment =>
                            payment.PaidAt >= periodStart &&
                            (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                                || payment.PaymentStatus == "failed"))
                        .GroupBy(payment => payment.PaidAt!.Value.Date)
                        .Select(group => new DailyMoneyPoint(group.Key, group.Sum(payment => payment.Amount -
                            (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                                ? payment.Amount : payment.RefundedAmount)))))
                : [];
        }

        var usersTotal = await this._opencrmContext.ApplicationUsers.AsNoTracking().CountAsync(user => !user.IsDeleted);
        var newUsersToday = await this._opencrmContext.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.DatetimeCreation >= today);
        var newUsers7Days = await this._opencrmContext.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.DatetimeCreation >= sevenDaysAgo);
        var loginsToday = await this._opencrmContext.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.LastLoginDate >= today);
        var activeUsers7Days = await this._opencrmContext.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.LastLoginDate >= sevenDaysAgo);
        var usersNeverLoggedIn = await this._opencrmContext.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.LastLoginDate <= emptyLoginCutoff);

        var organizationsTotal = await this._opencrmContext.Organizations.AsNoTracking()
            .CountAsync(organization => !organization.IsDeleted);
        var activeOrganizations = await this._opencrmContext.Organizations.AsNoTracking()
            .CountAsync(organization => !organization.IsDeleted && organization.IsActive);
        var newOrganizations7Days = await this._opencrmContext.Organizations.AsNoTracking()
            .CountAsync(organization => !organization.IsDeleted && organization.CreatedAt >= sevenDaysAgo);
        var organizationMembers = await this._opencrmContext.OrganizationMembers.AsNoTracking()
            .CountAsync(member => !member.Organization.IsDeleted);

        var vehiclesTotal = await this._opencrmContext.Vehicles.AsNoTracking().CountAsync(vehicle => !vehicle.IsDeleted);
        var maintenancesTotal = await this._opencrmContext.Maintenance.AsNoTracking()
            .CountAsync(maintenance => !maintenance.IsDeleted);
        var plannedMaintenances = await this._opencrmContext.Maintenance.AsNoTracking()
            .CountAsync(maintenance => !maintenance.IsDeleted && maintenance.Status == MaintenanceStatus.Planned);
        var documentsTotal = await this._opencrmContext.MaintenanceDocuments.AsNoTracking().CountAsync();
        var openDefects = await this._opencrmContext.VehicleDefects.AsNoTracking()
            .CountAsync(defect => !defect.IsDeleted && defect.StatusCode != VehicleDefectStatusCodes.Resolved);

        var payments30DaysAmount = 0m;
        var payments30DaysCount = 0;
        var activeOrganizationSubscriptions = 0;
        if (canViewBilling)
        {
            payments30DaysAmount = await this._opencrmContext.SubscriptionPayments.AsNoTracking()
                .Where(payment =>
                    payment.PaidAt >= thirtyDaysAgo &&
                    (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                        || payment.PaymentStatus == "failed"))
                .SumAsync(payment => (decimal?)(payment.Amount -
                    (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                        ? payment.Amount : payment.RefundedAmount))) ?? 0m;
            payments30DaysCount = await this._opencrmContext.SubscriptionPayments.AsNoTracking()
                .CountAsync(payment =>
                    payment.PaidAt >= thirtyDaysAgo &&
                    (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                        || payment.PaymentStatus == "failed"));
            activeOrganizationSubscriptions = await this._opencrmContext.OrganizationSubscriptions.AsNoTracking()
                .CountAsync(subscription => subscription.EndDate >= now);
        }

        var emailsToday = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.CreatedAt >= today);
        var emails7Days = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.CreatedAt >= sevenDaysAgo);
        var sentEmails = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.SentAt >= periodStart && message.SentAt < periodEnd
                && message.StatusCode != EmailMessageStatusCodes.Simulated);
        var failedEmails = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.CreatedAt >= periodStart && message.CreatedAt < periodEnd
                && message.StatusCode == EmailMessageStatusCodes.Failed);
        var sentEmails7Days = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.SentAt >= sevenDaysAgo && message.SentAt < periodEnd
                && message.StatusCode != EmailMessageStatusCodes.Simulated);
        var failedEmails7Days = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.CreatedAt >= sevenDaysAgo && message.StatusCode == EmailMessageStatusCodes.Failed);
        var pendingEmails = await this._opencrmContext.EmailMessages.AsNoTracking()
            .CountAsync(message => message.StatusCode == EmailMessageStatusCodes.Pending);

        var newSupportMessages = await this._opencrmContext.SupportMessages.AsNoTracking()
            .CountAsync(message => message.StatusCode == SupportMessageStatusCodes.New);
        var pendingCatalogSuggestions = await this._opencrmContext.VehicleCatalogSuggestions.AsNoTracking()
            .CountAsync(suggestion => suggestion.Status == VehicleCatalogSuggestionStatus.Pending);
        var pendingInvitations = await this._opencrmContext.OrganizationInvitations.AsNoTracking()
            .CountAsync(invitation => invitation.StatusCode == OrganizationInvitationStatusCodes.Pending);
        var overdueTasks = await this._opencrmContext.OrganizationSchedulerTasks.AsNoTracking()
            .CountAsync(task => !task.IsDeleted && task.StartAt.HasValue && task.StartAt.Value < now && task.ClosedAt == null);
        var lowStockItems = await this._opencrmContext.OrganizationMaterialStocks.AsNoTracking()
            .CountAsync(stock => stock.QuantityOnHand <= stock.MinQuantity);
        var failedPayments = canViewBilling
            ? await this._opencrmContext.SubscriptionPayments.AsNoTracking()
                .CountAsync(payment =>
                    payment.CreatedAt >= thirtyDaysAgo &&
                    payment.PaymentStatus != null &&
                    EF.Functions.ILike(payment.PaymentStatus, "%fail%"))
            : 0;

        var recentUsers = await this._opencrmContext.ApplicationUsers
            .AsNoTracking()
            .Where(user => !user.IsDeleted && user.DatetimeCreation >= periodStart)
            .OrderByDescending(user => user.DatetimeCreation)
            .Select(user => new AdminDashboardUserDto
            {
                Id = user.Id,
                Title = user.Email ?? user.PhoneNumber ?? "Пользователь",
                Email = user.Email,
                Phone = user.PhoneNumber,
                CreatedAt = user.DatetimeCreation,
                LastLoginDate = user.LastLoginDate
            })
            .ToListAsync();

        var recentOrganizations = await this._opencrmContext.Organizations
            .AsNoTracking()
            .Where(organization => !organization.IsDeleted && organization.CreatedAt >= periodStart)
            .OrderByDescending(organization => organization.CreatedAt)
            .Select(organization => new AdminDashboardOrganizationDto
            {
                Id = organization.Id,
                Name = organization.Name,
                PrimaryScenarioCode = organization.PrimaryScenarioCode,
                CreatedAt = organization.CreatedAt,
                Owner = organization.OwnerUser.Email ?? organization.OwnerUser.PhoneNumber ?? organization.OwnerUserId,
                MembersCount = organization.Members.Count
            })
            .ToListAsync();

        IReadOnlyList<AdminDashboardPaymentDto> recentPayments = canViewBilling
            ? await this._opencrmContext.SubscriptionPayments
                .AsNoTracking()
                .Where(payment =>
                    payment.PaidAt >= periodStart &&
                    (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                        || payment.PaymentStatus == "failed"))
                .OrderByDescending(payment => payment.PaidAt)
                .Select(payment => new AdminDashboardPaymentDto
                {
                    Id = payment.Id,
                    Amount = payment.Amount - (payment.PaymentStatus == "refunded" && payment.RefundedAmount == 0
                        ? payment.Amount : payment.RefundedAmount),
                    PaymentStatus = payment.PaymentStatus,
                    PaymentDate = payment.PaidAt!.Value,
                    Plan = payment.SubscriptionPlan.Name,
                    User = payment.User.Email ?? payment.User.PhoneNumber ?? payment.UserId
                })
                .ToListAsync()
            : [];

        var recentEmails = await this._opencrmContext.EmailMessages
            .AsNoTracking()
            .Where(message => message.CreatedAt >= periodStart && message.CreatedAt < periodEnd)
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => new AdminDashboardEmailDto
            {
                Id = message.Id,
                ToEmail = message.ToEmail,
                Subject = message.Subject,
                StatusCode = message.StatusCode,
                CreatedAt = message.CreatedAt,
                SentAt = message.SentAt,
                User = message.User == null ? null : message.User.Email ?? message.User.PhoneNumber ?? message.UserId
            })
            .ToListAsync();

        var recentSupportMessages = await this._opencrmContext.SupportMessages
            .AsNoTracking()
            .Where(message => message.CreatedAt >= periodStart && message.CreatedAt < periodEnd)
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => new AdminDashboardSupportMessageDto
            {
                Id = message.Id,
                Topic = message.Topic,
                StatusCode = message.StatusCode,
                CreatedAt = message.CreatedAt
            })
            .ToListAsync();

        var recentCatalogSuggestions = await this._opencrmContext.VehicleCatalogSuggestions
            .AsNoTracking()
            .Where(suggestion => suggestion.CreatedAt >= periodStart && suggestion.CreatedAt < periodEnd)
            .OrderByDescending(suggestion => suggestion.CreatedAt)
            .Select(suggestion => new AdminDashboardCatalogSuggestionDto
            {
                Id = suggestion.Id,
                BrandName = suggestion.BrandName,
                ModelName = suggestion.ModelName,
                Status = suggestion.Status,
                CreatedAt = suggestion.CreatedAt
            })
            .ToListAsync();

        return new AdminDashboardDto
        {
            GeneratedAt = now,
            Period = new AdminDashboardPeriodDto
            {
                Days = periodDays,
                NewUsers = recentUsers.Count,
                NewOrganizations = recentOrganizations.Count,
                PaymentsCount = recentPayments.Count,
                PaymentsAmount = recentPayments.Sum(payment => payment.Amount),
                Emails = recentEmails.Count,
                SentEmails = sentEmails,
                FailedEmails = failedEmails,
                SupportMessages = recentSupportMessages.Count,
                CatalogSuggestions = recentCatalogSuggestions.Count
            },
            Kpis = new AdminDashboardKpisDto
            {
                UsersTotal = usersTotal,
                NewUsersToday = newUsersToday,
                NewUsers7Days = newUsers7Days,
                LoginsToday = loginsToday,
                ActiveUsers7Days = activeUsers7Days,
                UsersNeverLoggedIn = usersNeverLoggedIn,
                OrganizationsTotal = organizationsTotal,
                ActiveOrganizations = activeOrganizations,
                NewOrganizations7Days = newOrganizations7Days,
                OrganizationMembers = organizationMembers,
                VehiclesTotal = vehiclesTotal,
                MaintenancesTotal = maintenancesTotal,
                PlannedMaintenances = plannedMaintenances,
                DocumentsTotal = documentsTotal,
                OpenDefects = openDefects,
                Payments30DaysAmount = payments30DaysAmount,
                Payments30DaysCount = payments30DaysCount,
                ActiveOrganizationSubscriptions = activeOrganizationSubscriptions,
                EmailsToday = emailsToday,
                Emails7Days = emails7Days,
                SentEmails7Days = sentEmails7Days,
                FailedEmails7Days = failedEmails7Days,
                PendingEmails = pendingEmails
            },
            Attention = new AdminDashboardAttentionDto
            {
                NewSupportMessages = newSupportMessages,
                PendingCatalogSuggestions = pendingCatalogSuggestions,
                PendingInvitations = pendingInvitations,
                OverdueTasks = overdueTasks,
                LowStockItems = lowStockItems,
                FailedPayments = failedPayments,
                FailedEmails7Days = failedEmails7Days,
                PendingEmails = pendingEmails
            },
            Charts = new AdminDashboardChartsDto
            {
                Registrations = registrationSeries,
                Logins = loginSeries,
                Maintenances = maintenanceSeries,
                Emails = emailSeries,
                Payments = paymentSeries
            },
            Recent = new AdminDashboardRecentDto
            {
                Users = recentUsers,
                Organizations = recentOrganizations,
                Payments = recentPayments,
                Emails = recentEmails,
                SupportMessages = recentSupportMessages,
                CatalogSuggestions = recentCatalogSuggestions
            }
        };
    }

    /// <summary> Формирует ежедневный ряд счётчиков. </summary>
    private static async Task<IReadOnlyList<AdminDashboardCountPointDto>> BuildDailySeriesAsync(
        DateTime from,
        DateTime to,
        IQueryable<DailyPoint> query)
    {
        var points = await query.ToDictionaryAsync(point => point.Date, point => point.Count);

        return EachDay(from, to)
            .Select(date => new AdminDashboardCountPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = points.TryGetValue(date, out var count) ? count : 0
            })
            .ToList();
    }

    /// <summary> Формирует почасовой ряд счётчиков. </summary>
    private static async Task<IReadOnlyList<AdminDashboardCountPointDto>> BuildHourlySeriesAsync(
        DateTime date,
        int lastHour,
        IQueryable<HourlyPoint> query)
    {
        var points = await query.ToDictionaryAsync(point => point.Hour, point => point.Count);

        return Enumerable.Range(0, lastHour + 1)
            .Select(hour => new AdminDashboardCountPointDto
            {
                Date = date.AddHours(hour),
                Label = $"{hour:00}:00",
                Value = points.TryGetValue(hour, out var count) ? count : 0
            })
            .ToList();
    }

    /// <summary> Формирует ежедневный ряд сумм. </summary>
    private static async Task<IReadOnlyList<AdminDashboardMoneyPointDto>> BuildMoneySeriesAsync(
        DateTime from,
        DateTime to,
        IQueryable<DailyMoneyPoint> query)
    {
        var points = await query.ToDictionaryAsync(point => point.Date, point => point.Amount);

        return EachDay(from, to)
            .Select(date => new AdminDashboardMoneyPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = points.TryGetValue(date, out var amount) ? amount : 0m
            })
            .ToList();
    }

    /// <summary> Формирует почасовой ряд сумм. </summary>
    private static async Task<IReadOnlyList<AdminDashboardMoneyPointDto>> BuildHourlyMoneySeriesAsync(
        DateTime date,
        int lastHour,
        IQueryable<HourlyMoneyPoint> query)
    {
        var points = await query.ToDictionaryAsync(point => point.Hour, point => point.Amount);

        return Enumerable.Range(0, lastHour + 1)
            .Select(hour => new AdminDashboardMoneyPointDto
            {
                Date = date.AddHours(hour),
                Label = $"{hour:00}:00",
                Value = points.TryGetValue(hour, out var amount) ? amount : 0m
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

    /// <summary> Точка ежедневного счётчика. </summary>
    private sealed record DailyPoint(DateTime Date, int Count);

    /// <summary> Точка ежедневной суммы. </summary>
    private sealed record DailyMoneyPoint(DateTime Date, decimal Amount);

    /// <summary> Точка почасового счётчика. </summary>
    private sealed record HourlyPoint(int Hour, int Count);

    /// <summary> Точка почасовой суммы. </summary>
    private sealed record HourlyMoneyPoint(int Hour, decimal Amount);
}
