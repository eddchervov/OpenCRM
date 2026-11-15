using OpenCRM.Services.Interfaces.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions;

namespace OpenCRM.Modules.Billing.Subscriptions;

/// <summary> Сервис напоминаний об окончании подписок. </summary>
public sealed class SubscriptionExpiryReminderService(
    OpenCRMContext context,
    IEmailNotificationService emailSender,
    ISiteNotificationPublisher siteNotificationPublisher,
    IConfiguration configuration,
    ILogger<SubscriptionExpiryReminderService> logger)
    : ISubscriptionExpiryReminderService
{
    /// <summary> Код области напоминаний для подписок организаций. </summary>
    private const string OrganizationScope = "organization";

    /// <summary> Код области напоминаний для личных подписок. </summary>
    private const string PersonalScope = "personal";

    /// <summary> Код события напоминания за семь дней до окончания. </summary>
    private const string UpcomingSevenDaysEvent = "expiry-upcoming-7";

    /// <summary> Код события напоминания за три дня до окончания. </summary>
    private const string UpcomingThreeDaysEvent = "expiry-upcoming-3";

    /// <summary> Код события напоминания за один день до окончания. </summary>
    private const string UpcomingOneDayEvent = "expiry-upcoming-1";

    /// <summary> Код события истечения платной подписки. </summary>
    private const string PaidExpiredEvent = "paid-expired";

    /// <summary> Код события истечения демо-подписки. </summary>
    private const string DemoExpiredEvent = "demo-expired";

    /// <summary> Код события доступности демо-подписки. </summary>
    private const string DemoAvailableEvent = "demo-available";

    /// <summary> Задержка повторной попытки после неудачной отправки. </summary>
    private static readonly TimeSpan FailedAttemptRetryDelay = TimeSpan.FromHours(12);

    /// <summary> Период поиска недавно истёкших подписок. </summary>
    private static readonly TimeSpan ExpiredReminderLookback = TimeSpan.FromDays(7);

    /// <summary> Задержка перед напоминанием о доступности демо-режима. </summary>
    private static readonly TimeSpan DemoAvailableReminderDelay = TimeSpan.FromDays(1);

    /// <summary> Отправляет наступившие напоминания об окончании подписок. </summary>
    public async Task SendDueRemindersAsync()
    {
        if (!configuration.GetValue("PlatformPolicy:Commercial:Enabled", false))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var candidates = await LoadCandidatesAsync(now);

        foreach (var candidate in candidates)
        {
            await SendCandidateAsync(candidate, now);
        }
    }

    /// <summary> Загружает кандидатов на отправку напоминаний. </summary>
    private async Task<IReadOnlyList<ReminderCandidate>> LoadCandidatesAsync(DateTime now)
    {
        var candidates = new List<ReminderCandidate>();
        var horizon = now.AddDays(7);
        var expiredSince = now.Subtract(ExpiredReminderLookback);

        var organizationUpcoming = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Where(x =>
                x.EndDate > now
                && (x.SourceCode != "promo"
                    || x.Organization.PrimaryScenarioCode == OrganizationTypeCodes.Detailing
                    || x.Organization.PrimaryScenarioCode == OrganizationTypeCodes.ServiceStation)
                && x.EndDate <= horizon
                && !x.SubscriptionPlan.IsDefaultPlan
                && !x.SubscriptionPlan.IsDemoPlan
                && x.Organization.IsActive
                && !x.Organization.IsArchived
                && !x.Organization.IsDeleted
                && !x.Organization.OwnerUser.IsDeleted
                && x.Organization.OwnerUser.Email != null)
            .Select(x => new ReminderCandidate(
                ReminderKind.Upcoming,
                OrganizationScope,
                x.Id,
                x.Organization.OwnerUserId,
                x.OrganizationId,
                x.Organization.Name,
                x.Organization.PrimaryScenarioCode,
                x.SubscriptionPlan.Name,
                0,
                x.EndDate,
                x.Organization.OwnerUser.Email!))
            .ToListAsync();
        candidates.AddRange(organizationUpcoming);

        var personalUpcoming = await context.UserSubscriptions
            .AsNoTracking()
            .Where(x =>
                x.EndDate > now
                && x.EndDate <= horizon
                && !x.SubscriptionPlan.IsDefaultPlan
                && !x.SubscriptionPlan.IsDemoPlan
                && !x.User.IsDeleted
                && x.User.Email != null)
            .Select(x => new ReminderCandidate(
                ReminderKind.Upcoming,
                PersonalScope,
                x.Id,
                x.UserId,
                null,
                null,
                null,
                x.SubscriptionPlan.Name,
                0,
                x.EndDate,
                x.User.Email!))
            .ToListAsync();
        candidates.AddRange(personalUpcoming);

        var paidExpired = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Where(x =>
                x.EndDate <= now
                && (x.SourceCode != "promo"
                    || x.Organization.PrimaryScenarioCode == OrganizationTypeCodes.Detailing
                    || x.Organization.PrimaryScenarioCode == OrganizationTypeCodes.ServiceStation)
                && x.EndDate >= expiredSince
                && !x.SubscriptionPlan.IsDefaultPlan
                && !x.SubscriptionPlan.IsDemoPlan
                && x.Organization.IsActive
                && !x.Organization.IsArchived
                && !x.Organization.IsDeleted
                && !x.Organization.OwnerUser.IsDeleted
                && x.Organization.OwnerUser.Email != null
                && !context.OrganizationSubscriptions.Any(active =>
                    active.OrganizationId == x.OrganizationId
                    && active.EndDate > now
                    && !active.SubscriptionPlan.IsDefaultPlan))
            .Select(x => new ReminderCandidate(
                ReminderKind.PaidExpired,
                OrganizationScope,
                x.Id,
                x.Organization.OwnerUserId,
                x.OrganizationId,
                x.Organization.Name,
                x.Organization.PrimaryScenarioCode,
                x.SubscriptionPlan.Name,
                0,
                x.EndDate,
                x.Organization.OwnerUser.Email!))
            .ToListAsync();
        candidates.AddRange(SelectLatestPerOrganization(paidExpired));

        var demoExpired = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Where(x =>
                x.EndDate <= now
                && x.EndDate >= expiredSince
                && x.SubscriptionPlan.IsDemoPlan
                && !x.SubscriptionPlan.IsDefaultPlan
                && x.Organization.IsActive
                && !x.Organization.IsArchived
                && !x.Organization.IsDeleted
                && !x.Organization.OwnerUser.IsDeleted
                && x.Organization.OwnerUser.Email != null
                && !context.OrganizationSubscriptions.Any(active =>
                    active.OrganizationId == x.OrganizationId
                    && active.EndDate > now
                    && !active.SubscriptionPlan.IsDefaultPlan))
            .Select(x => new ReminderCandidate(
                ReminderKind.DemoExpired,
                OrganizationScope,
                x.Id,
                x.Organization.OwnerUserId,
                x.OrganizationId,
                x.Organization.Name,
                x.Organization.PrimaryScenarioCode,
                x.SubscriptionPlan.Name,
                x.SubscriptionPlan.DurationDays,
                x.EndDate,
                x.Organization.OwnerUser.Email!))
            .ToListAsync();
        candidates.AddRange(SelectLatestPerOrganization(demoExpired));

        var demoPlan = await context.SubscriptionPlans
            .AsNoTracking()
            .Where(x =>
                x.IsActive
                && x.IsDemoPlan
                && !x.IsDefaultPlan
                && x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business)
            .OrderBy(x => x.Order)
            .Select(x => new { x.Name, x.DurationDays })
            .FirstOrDefaultAsync();

        if (demoPlan != null)
        {
            var demoReminderDate = now.Subtract(DemoAvailableReminderDelay);
            var demoAvailable = await context.Organizations
                .AsNoTracking()
                .Where(x =>
                    x.CreatedAt <= demoReminderDate
                    && x.PrimaryScenarioCode != OrganizationTypeCodes.Detailing
                    && x.PrimaryScenarioCode != OrganizationTypeCodes.ServiceStation
                    && x.IsActive
                    && !x.IsArchived
                    && !x.IsDeleted
                    && !x.OwnerUser.IsDeleted
                    && x.OwnerUser.Email != null
                    && !context.OrganizationSubscriptions.Any(subscription => subscription.OrganizationId == x.Id
                        && (subscription.SourceCode != "promo" || subscription.EndDate > now))
                    && !context.Organizations.Any(other =>
                        other.Id != x.Id
                        && other.OwnerUserId == x.OwnerUserId
                        && other.PrimaryScenarioCode == x.PrimaryScenarioCode
                        && !other.IsDeleted
                        && other.IsActive))
                .Select(x => new ReminderCandidate(
                    ReminderKind.DemoAvailable,
                    OrganizationScope,
                    0,
                    x.OwnerUserId,
                    x.Id,
                    x.Name,
                    x.PrimaryScenarioCode,
                    demoPlan.Name,
                    demoPlan.DurationDays,
                    x.CreatedAt,
                    x.OwnerUser.Email!))
                .ToListAsync();
            candidates.AddRange(demoAvailable);
        }

        return candidates;
    }

    /// <summary> Отбирает последнее напоминание по каждой организации. </summary>
    private static IEnumerable<ReminderCandidate> SelectLatestPerOrganization(
        IEnumerable<ReminderCandidate> candidates)
    {
        return candidates
            .GroupBy(x => x.OrganizationId)
            .Select(group => group.OrderByDescending(x => x.EndDate).First());
    }

    /// <summary> Отправляет напоминание для одного кандидата. </summary>
    private async Task SendCandidateAsync(ReminderCandidate candidate, DateTime now)
    {
        var recipientIsActive = await context.Users
            .AsNoTracking()
            .AnyAsync(user =>
                user.Id == candidate.UserId
                && !user.IsDeleted
                && user.Email == candidate.Email);
        if (!recipientIsActive)
        {
            return;
        }
        var daysRemaining = candidate.Kind == ReminderKind.Upcoming
            ? Math.Max(1, (int)Math.Ceiling((candidate.EndDate - now).TotalDays))
            : 0;
        var eventCode = ResolveEventCode(candidate.Kind, daysRemaining);
        var reminder = await context.SubscriptionExpiryReminders
            .FirstOrDefaultAsync(x =>
                x.ScopeCode == candidate.ScopeCode
                && x.SubscriptionId == candidate.SubscriptionId
                && x.SubscriptionEndDate == candidate.EndDate
                && x.EventCode == eventCode);

        if (reminder?.SentAt != null || reminder?.NotificationQueuedAt != null)
        {
            return;
        }

        if (reminder?.LastAttemptAt > now.Subtract(FailedAttemptRetryDelay))
        {
            return;
        }

        if (reminder == null)
        {
            reminder = new SubscriptionExpiryReminder
            {
                ScopeCode = candidate.ScopeCode,
                SubscriptionId = candidate.SubscriptionId,
                UserId = candidate.UserId,
                OrganizationId = candidate.OrganizationId,
                SubscriptionEndDate = candidate.EndDate,
                EventCode = eventCode,
                RecipientEmail = candidate.Email,
                CreatedAt = now
            };
            context.SubscriptionExpiryReminders.Add(reminder);
        }

        reminder.LastAttemptAt = now;
        reminder.AttemptCount++;
        reminder.ErrorMessage = null;
        if (candidate.ScopeCode == OrganizationScope &&
            (candidate.Kind == ReminderKind.Upcoming ||
                candidate.Kind == ReminderKind.PaidExpired ||
                candidate.Kind == ReminderKind.DemoExpired))
        {
            var typeCode = candidate.Kind == ReminderKind.Upcoming
                ? SiteNotificationTypes.OrganizationSubscriptionExpiring
                : SiteNotificationTypes.OrganizationSubscriptionExpired;

            await siteNotificationPublisher.AddAsync(
                typeCode,
                candidate.UserId,
                SiteNotificationAudiences.Admins | SiteNotificationAudiences.OrganizationOwners,
                $"subscription:{candidate.SubscriptionId}:{eventCode}:{candidate.EndDate.Ticks}",
                new Dictionary<string, string>
                {
                    ["organization"] = candidate.OrganizationName ?? string.Empty,
                    ["plan"] = candidate.PlanName,
                    ["days"] = daysRemaining.ToString()
                });
        }

        await context.SaveChangesAsync();

        try
        {
            var subject = BuildSubject(candidate);
            reminder.NotificationQueuedAt = DateTime.UtcNow;
            reminder.ErrorMessage = null;
            await emailSender.QueueTemplateAsync(
                candidate.Email, subject, "system/subscription-notice",
                new
                {
                    kind = candidate.Kind switch
                    {
                        ReminderKind.PaidExpired => "paid-expired",
                        ReminderKind.DemoExpired => "demo-expired",
                        ReminderKind.DemoAvailable => "demo-available",
                        _ => "upcoming"
                    },
                    organizationName = candidate.OrganizationName,
                    planName = candidate.PlanName,
                    endDate = DateOnly.FromDateTime(candidate.EndDate),
                    daysRemaining,
                    durationDays = candidate.DurationDays,
                    hasFreeWorkspace = HasFreeWorkspace(candidate),
                    actionUrl = BuildActionUrl(candidate)
                },
                $"subscription-reminder:{reminder.Id}", subscriptionReminderId: reminder.Id);
        }
        catch (ArgumentException exception)
        {
            reminder.NotificationQueuedAt = null;
            reminder.ErrorMessage = Truncate(exception.Message, 2000);
            await context.SaveChangesAsync();

            logger.LogError(
                exception,
                "Unable to send tariff reminder {EventCode} for {ScopeCode} subscription {SubscriptionId}",
                eventCode,
                candidate.ScopeCode,
                candidate.SubscriptionId);
        }
    }

    /// <summary> Строит ссылку действия для напоминания. </summary>
    private string BuildActionUrl(ReminderCandidate candidate)
    {
        var baseUrl = (configuration["AppSettings:BaseUrl"] ?? "https://opencrm.example.invalid").TrimEnd('/');
        if (candidate.ScopeCode == PersonalScope)
        {
            return $"{baseUrl}/personal/subscription";
        }

        var scenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(candidate.ScenarioCode);
        var relativeUrl = scenarioCode switch
        {
            OrganizationTypeCodes.ServiceStation => $"/autoservice/{candidate.OrganizationId:D}#billing",
            OrganizationTypeCodes.Detailing => $"/detailing/{candidate.OrganizationId:D}#billing",
            OrganizationTypeCodes.Fleet => $"/fleet/{candidate.OrganizationId:D}#billing",
            _ => "/organizations"
        };

        return $"{baseUrl}{relativeUrl}";
    }

    /// <summary> Определяет доступность бесплатного рабочего пространства после окончания тарифа. </summary>
    private static bool HasFreeWorkspace(ReminderCandidate candidate)
    {
        return candidate.ScopeCode == OrganizationScope
            && candidate.ScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation;
    }

    /// <summary> Формирует тему письма напоминания. </summary>
    private static string BuildSubject(ReminderCandidate candidate)
    {
        return candidate.Kind switch
        {
            ReminderKind.PaidExpired when HasFreeWorkspace(candidate) => "Коммерческий тариф завершён — продолжайте работу без подписки",
            ReminderKind.DemoExpired when HasFreeWorkspace(candidate) => "Коммерческий тариф завершён — продолжайте работу без подписки",
            ReminderKind.PaidExpired => "Платный тариф организации закончился",
            ReminderKind.DemoExpired => "Демо-период организации закончился",
            ReminderKind.DemoAvailable => "Для организации доступен демо-тариф",
            _ when HasFreeWorkspace(candidate) => "Срок коммерческого тарифа скоро закончится — работа без подписки останется доступной",
            _ when candidate.ScopeCode == OrganizationScope => "Срок тарифа организации скоро закончится",
            _ => "Срок тарифа скоро закончится"
        };
    }

    /// <summary> Определяет код события по типу напоминания и оставшимся дням. </summary>
    private static string ResolveEventCode(ReminderKind kind, int daysRemaining)
    {
        return kind switch
        {
            ReminderKind.PaidExpired => PaidExpiredEvent,
            ReminderKind.DemoExpired => DemoExpiredEvent,
            ReminderKind.DemoAvailable => DemoAvailableEvent,
            _ when daysRemaining <= 1 => UpcomingOneDayEvent,
            _ when daysRemaining <= 3 => UpcomingThreeDaysEvent,
            _ => UpcomingSevenDaysEvent
        };
    }

    /// <summary> Обрезает строку до максимальной длины. </summary>
    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary> Тип напоминания об окончании подписки. </summary>
    private enum ReminderKind
    {
        /// <summary> Приближающееся окончание подписки. </summary>
        Upcoming,

        /// <summary> Истечение платной подписки. </summary>
        PaidExpired,

        /// <summary> Истечение демо-подписки. </summary>
        DemoExpired,

        /// <summary> Доступность демо-подписки. </summary>
        DemoAvailable
    }

    /// <summary> Данные кандидата на отправку напоминания. </summary>
    private sealed record ReminderCandidate(
        ReminderKind Kind,
        string ScopeCode,
        int SubscriptionId,
        string UserId,
        Guid? OrganizationId,
        string? OrganizationName,
        string? ScenarioCode,
        string PlanName,
        int DurationDays,
        DateTime EndDate,
        string Email);
}
