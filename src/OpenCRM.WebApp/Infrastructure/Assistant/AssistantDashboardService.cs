using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

/// <summary>Читает историю и использование без доступа к исполнению архивных команд.</summary>
public sealed class AssistantDashboardService(
    OpenCRMContext context,
    AssistantAccessService access,
    IOrganizationPermissionService permissions,
    IOrganizationSubscriptionService subscriptions,
    AssistantOptions options)
{
    public async Task<object> GetHistoryAsync(AssistantActor actor, string? channel, int page, CancellationToken cancellationToken)
    {
        await access.EnsureHistoryAvailableAsync(actor, cancellationToken);
        ValidateChannel(channel);
        var canResumeAssistant = await subscriptions.HasFeatureAsync(actor.UserId, actor.OrganizationId, Helpers.FeatureCodes.Assistant);
        var canViewOrganization = await this.CanViewOrganizationHistoryAsync(actor, cancellationToken);
        var query = this.Conversations(actor, canViewOrganization);
        if (!string.IsNullOrEmpty(channel))
        {
            query = query.Where(item => item.Channel == channel);
        }
        var pageSize = options.LookupPageSize;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var now = DateTime.UtcNow;
        var items = await query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new
            {
                item.Id,
                userName = context.Users.Where(user => user.Id == item.UserId).Select(user => user.UserName).FirstOrDefault() ?? item.UserId,
                item.Channel,
                item.CreatedAt,
                item.UpdatedAt,
                item.IsClosed,
                isExpired = item.ExpiresAt <= now,
                preview = context.Set<AssistantMessage>().Where(message => message.ConversationId == item.Id && message.Role == "user")
                    .OrderBy(message => message.Sequence).Select(message => message.Text).FirstOrDefault() ?? "Новый разговор",
                canResume = canResumeAssistant && item.UserId == actor.UserId && item.Channel == "web" && !item.IsClosed && item.ExpiresAt > now
            }).ToListAsync(cancellationToken);

        return new
        {
            scope = canViewOrganization ? "organization" : "self",
            items,
            totalCount = await query.CountAsync(cancellationToken),
            page,
            pageSize,
            retentionDays = options.ConversationHistoryRetentionDays
        };
    }

    public async Task<object?> GetConversationAsync(AssistantActor actor, Guid id, int page, CancellationToken cancellationToken)
    {
        await access.EnsureHistoryAvailableAsync(actor, cancellationToken);
        var canResumeAssistant = await subscriptions.HasFeatureAsync(actor.UserId, actor.OrganizationId, Helpers.FeatureCodes.Assistant);
        var canViewOrganization = await this.CanViewOrganizationHistoryAsync(actor, cancellationToken);
        var conversation = await this.Conversations(actor, canViewOrganization).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (conversation is null)
        {
            return null;
        }
        var pageSize = options.LookupPageSize;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var storedMessages = await context.Set<AssistantMessage>().AsNoTracking().Where(item => item.ConversationId == id)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var messages = VisibleMessages(storedMessages);
        var now = DateTime.UtcNow;

        return new
        {
            conversation.Id,
            userName = await context.Users.Where(user => user.Id == conversation.UserId)
                .Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken) ?? conversation.UserId,
            conversation.Channel,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.IsClosed,
            isExpired = conversation.ExpiresAt <= now,
            canResume = canResumeAssistant && conversation.UserId == actor.UserId && conversation.Channel == "web" && !conversation.IsClosed && conversation.ExpiresAt > now,
            messages = messages.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            totalCount = messages.Count,
            page,
            pageSize
        };
    }

    public async Task<object> GetUsageAsync(AssistantActor actor, DateOnly? from, DateOnly? to, string? channel, CancellationToken cancellationToken)
    {
        await access.EnsureHistoryAvailableAsync(actor, cancellationToken);
        ValidateChannel(channel);
        var canViewOrganization = await permissions.HasPermissionAsync(actor.UserId, actor.OrganizationId, OrganizationPermissionCodes.MembersView)
            && await permissions.HasPermissionAsync(actor.UserId, actor.OrganizationId, OrganizationPermissionCodes.FinanceView);
        var organizationTimeZone = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == actor.OrganizationId)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(organizationTimeZone)
            ? options.DefaultTimeZone
            : organizationTimeZone);
        var budgetZone = TimeZoneInfo.FindSystemTimeZoneById(options.BudgetTimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var month = new DateOnly(today.Year, today.Month, 1);
        from ??= month;
        to ??= today;
        if (to < from || to.Value.DayNumber - from.Value.DayNumber >= options.MaximumUsagePeriodDays || to == DateOnly.MaxValue)
        {
            throw new InvalidOperationException($"Укажите период до {options.MaximumUsagePeriodDays} дней.");
        }

        var grant = await context.Set<AssistantOrganizationAccess>().AsNoTracking()
            .SingleAsync(item => item.OrganizationId == actor.OrganizationId, cancellationToken);
        var organizationUsage = context.Set<AssistantUsage>().AsNoTracking().Where(item => item.OrganizationId == actor.OrganizationId);
        var budgetToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, budgetZone));
        var budgetMonth = new DateOnly(budgetToday.Year, budgetToday.Month, 1);
        var monthUsage = organizationUsage.Where(item => item.CreatedAt >= StartOfDay(budgetMonth, budgetZone)
            && item.CreatedAt < StartOfDay(budgetMonth.AddMonths(1), budgetZone));
        var dayUsage = organizationUsage.Where(item => item.CreatedAt >= StartOfDay(budgetToday, budgetZone)
            && item.CreatedAt < StartOfDay(budgetToday.AddDays(1), budgetZone));
        var periodStart = StartOfDay(from.Value, zone);
        var periodEnd = StartOfDay(to.Value.AddDays(1), zone);
        var query = organizationUsage.Where(item => item.CreatedAt >= periodStart && item.CreatedAt < periodEnd);
        if (!canViewOrganization)
        {
            query = query.Where(item => item.UserId == actor.UserId);
        }
        if (!string.IsNullOrEmpty(channel))
        {
            query = query.Where(item => item.Channel == channel);
        }

        var byUser = await query.GroupBy(item => item.UserId).Select(group => new
        {
            userId = group.Key,
            userName = context.Users.Where(user => user.Id == group.Key).Select(user => user.UserName).FirstOrDefault() ?? group.Key,
            costRub = group.Count(item => item.CostRub != null) == 0 ? (decimal?)null : group.Sum(item => item.CostRub),
            reservedRub = group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0),
            chargedRub = group.Sum(item => item.CostRub ?? item.ReservedCostRub),
            requestCount = group.Count(),
            inputTokens = group.Sum(item => item.InputTokens ?? 0),
            outputTokens = group.Sum(item => item.OutputTokens ?? 0),
            unknownCount = group.Count(item => item.Status == "unknown")
        }).OrderByDescending(item => item.chargedRub).ThenBy(item => item.userId).ToListAsync(cancellationToken);
        var byChannel = await query.GroupBy(item => item.Channel).Select(group => new
        {
            channel = group.Key,
            costRub = group.Count(item => item.CostRub != null) == 0 ? (decimal?)null : group.Sum(item => item.CostRub),
            reservedRub = group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0),
            chargedRub = group.Sum(item => item.CostRub ?? item.ReservedCostRub),
            requestCount = group.Count(),
            inputTokens = group.Sum(item => item.InputTokens ?? 0),
            outputTokens = group.Sum(item => item.OutputTokens ?? 0),
            unknownCount = group.Count(item => item.Status == "unknown")
        }).OrderBy(item => item.channel).ToListAsync(cancellationToken);

        return new
        {
            scope = canViewOrganization ? "organization" : "self",
            timeZone = zone.Id,
            budgetTimeZone = budgetZone.Id,
            from,
            to,
            limits = new { grant.DailyBudgetRub, grant.MonthlyBudgetRub, grant.EnabledUntil, grant.WebEnabled, grant.TelegramEnabled },
            day = Budget(await TotalsAsync(dayUsage, cancellationToken), grant.DailyBudgetRub),
            month = Budget(await TotalsAsync(monthUsage, cancellationToken), grant.MonthlyBudgetRub),
            totals = await TotalsAsync(query, cancellationToken),
            byUser,
            byChannel
        };
    }

    private IQueryable<AssistantConversation> Conversations(AssistantActor actor, bool canViewOrganization)
    {
        var query = context.Set<AssistantConversation>().AsNoTracking().Where(item => item.OrganizationId == actor.OrganizationId);

        return canViewOrganization ? query : query.Where(item => item.UserId == actor.UserId);
    }

    internal static List<AssistantTranscriptMessage> VisibleMessages(IReadOnlyList<AssistantMessage> messages)
    {
        var result = new List<AssistantTranscriptMessage>();
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (message.Role is not ("user" or "assistant"))
            {
                continue;
            }
            var text = message.Text;
            if (message.Role == "assistant")
            {
                if (text.StartsWith("Интерфейс отметил предыдущий черновик выполненным;", StringComparison.Ordinal))
                {
                    continue;
                }
                if (text.StartsWith("Результат интерфейса (", StringComparison.Ordinal))
                {
                    text = text.StartsWith("Результат интерфейса (completed):", StringComparison.Ordinal) ? "Действие выполнено."
                        : text.StartsWith("Результат интерфейса (failed):", StringComparison.Ordinal) ? "Действие не выполнено." : string.Empty;
                    if (text.Length == 0)
                    {
                        continue;
                    }
                }
                // Движок готовит черновик до серверной проверки; в истории оставляем итоговое уточнение.
                if (text.StartsWith("Подготовила черновик:", StringComparison.Ordinal) && index + 1 < messages.Count)
                {
                    var next = messages[index + 1];
                    if (next.Role == "assistant" && !next.Text.StartsWith("Результат интерфейса (", StringComparison.Ordinal)
                        && !next.Text.StartsWith("Интерфейс отметил предыдущий черновик выполненным;", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
            }
            result.Add(new AssistantTranscriptMessage(message.Id, message.Role, text, message.CreatedAt, message.Sequence));
        }

        return result;
    }

    private async Task<bool> CanViewOrganizationHistoryAsync(AssistantActor actor, CancellationToken cancellationToken)
    {
        if (await context.Organizations.AnyAsync(item => item.Id == actor.OrganizationId && item.OwnerUserId == actor.UserId, cancellationToken))
        {
            return true;
        }

        return await context.Set<OrganizationMember>().AnyAsync(item => item.OrganizationId == actor.OrganizationId
            && item.UserId == actor.UserId && item.RoleCode == OrganizationRoleCodes.Admin, cancellationToken);
    }

    private static async Task<AssistantUsageTotals> TotalsAsync(IQueryable<AssistantUsage> query, CancellationToken cancellationToken)
    {
        var totals = await query.GroupBy(_ => 1).Select(group => new AssistantUsageTotals(
            group.Count(item => item.CostRub != null) == 0 ? null : group.Sum(item => item.CostRub),
            group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0),
            group.Sum(item => item.CostRub ?? item.ReservedCostRub),
            group.Count(),
            group.Sum(item => item.InputTokens ?? 0),
            group.Sum(item => item.OutputTokens ?? 0),
            group.Count(item => item.Status == "unknown"))).SingleOrDefaultAsync(cancellationToken);

        return totals ?? AssistantUsageTotals.Empty;
    }

    private static object Budget(AssistantUsageTotals totals, decimal limit) => new
    {
        totals.CostRub, totals.ReservedRub, totals.ChargedRub, totals.RequestCount, totals.InputTokens, totals.OutputTokens, totals.UnknownCount,
        remainingRub = Math.Max(0, limit - totals.ChargedRub)
    };

    private static DateTime StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // При смене времени день начинается с первого существующего локального момента.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }
        if (zone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static void ValidateChannel(string? channel)
    {
        if (!string.IsNullOrEmpty(channel) && channel is not ("web" or "telegram"))
        {
            throw new InvalidOperationException("Выберите канал: сайт или Telegram.");
        }
    }
}
