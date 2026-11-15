using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

[ApiController]
[Route("api/v1/admin/assistant")]
[Authorize(Policy = AdminAuthorizationPolicies.Access)]
public sealed class AssistantAdminApiController(
    OpenCRMContext context,
    AssistantOptions options,
    AssistantSettingsStore settingsStore,
    IConfiguration configuration,
    Microsoft.Extensions.Options.IOptions<IdentityOptions> identityOptions) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
    public async Task<IActionResult> Get(string? search, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, options.LookupPageSize);
        var query = context.Organizations.AsNoTracking().Where(item => options.AllowedScenarioCodes.Contains(item.PrimaryScenarioCode));
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item => item.Name.Contains(search.Trim()));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var organizations = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new { organizationId = item.Id, item.Name }).ToListAsync(cancellationToken);
        var ids = organizations.Select(item => item.organizationId).ToArray();
        var grants = await context.Set<AssistantOrganizationAccess>().AsNoTracking()
            .Where(item => ids.Contains(item.OrganizationId)).ToDictionaryAsync(item => item.OrganizationId, cancellationToken);

        return this.Ok(new
        {
            settings = await settingsStore.GetAsync(cancellationToken),
            limits = new { options.MaximumOutputTokensAllowed, options.MaximumBudgetRub, options.MaximumReservationRub, options.BudgetTimeZone },
            provider = new
            {
                routerConfigured = AssistantConfiguration.IsRouterConfigured(configuration),
                telegramConfigured = AssistantConfiguration.IsTelegramConfigured(configuration),
                telegramBotUsername = configuration["AssistantTelegram:BotUsername"]
            },
            organizations = organizations.Select(item => new
            {
                item.organizationId, item.Name,
                isEnabled = grants.GetValueOrDefault(item.organizationId)?.IsEnabled ?? false,
                enabledUntil = grants.GetValueOrDefault(item.organizationId)?.EnabledUntil,
                webEnabled = grants.GetValueOrDefault(item.organizationId)?.WebEnabled ?? false,
                telegramEnabled = grants.GetValueOrDefault(item.organizationId)?.TelegramEnabled ?? false,
                dailyBudgetRub = grants.GetValueOrDefault(item.organizationId)?.DailyBudgetRub ?? 0,
                monthlyBudgetRub = grants.GetValueOrDefault(item.organizationId)?.MonthlyBudgetRub ?? 0
            }),
            totalCount, page, pageSize,
            audits = await context.Set<AssistantAudit>().AsNoTracking().OrderByDescending(item => item.CreatedAt)
                .Take(options.LookupPageSize).ToListAsync(cancellationToken)
        });
    }

    [HttpPut("settings")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
    public async Task<IActionResult> Settings(AssistantSettingsRequest request, CancellationToken cancellationToken)
    {
        this.ValidateBudget(request.DailyBudgetRub, request.MonthlyBudgetRub);
        if (!IsIdentifier(request.TextModel) || !IsIdentifier(request.SpeechModel) || !IsIdentifier(request.VoiceModel) ||
            !IsIdentifier(request.VoiceName) || request.MaximumOutputTokens <= 0 || request.MaximumOutputTokens > options.MaximumOutputTokensAllowed ||
            request.TextReservationRub <= 0 || request.AudioReservationRub <= 0 ||
            request.TextReservationRub > options.MaximumReservationRub || request.AudioReservationRub > options.MaximumReservationRub)
        {
            return this.BadRequest(new { message = "Проверьте модели, голос, лимит токенов и резервы расходов." });
        }

        await settingsStore.GetAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var changed = await context.Set<AssistantSettings>().Where(item => item.Id == 1 && item.Revision == request.Revision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsEnabled, request.IsEnabled)
                    .SetProperty(item => item.WebEnabled, request.WebEnabled)
                    .SetProperty(item => item.TelegramEnabled, request.TelegramEnabled)
                    .SetProperty(item => item.SpeechRepliesEnabled, request.SpeechRepliesEnabled)
                    .SetProperty(item => item.TextModel, request.TextModel.Trim())
                    .SetProperty(item => item.SpeechModel, request.SpeechModel.Trim())
                    .SetProperty(item => item.VoiceModel, request.VoiceModel.Trim())
                    .SetProperty(item => item.VoiceName, request.VoiceName.Trim())
                    .SetProperty(item => item.DailyBudgetRub, request.DailyBudgetRub)
                    .SetProperty(item => item.MonthlyBudgetRub, request.MonthlyBudgetRub)
                    .SetProperty(item => item.TextReservationRub, request.TextReservationRub)
                    .SetProperty(item => item.AudioReservationRub, request.AudioReservationRub)
                    .SetProperty(item => item.MaximumOutputTokens, request.MaximumOutputTokens)
                    .SetProperty(item => item.Revision, item => item.Revision + 1)
                    .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
            if (changed == 0)
            {
                return this.Conflict(new { message = "Настройки уже изменились. Обновите страницу." });
            }

            this.Audit("settings", $"Режим: {request.IsEnabled}; сайт: {request.WebEnabled}; Telegram: {request.TelegramEnabled}; бюджеты: {request.DailyBudgetRub}/{request.MonthlyBudgetRub}.");
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return this.Ok(await context.Set<AssistantSettings>().AsNoTracking().SingleAsync(cancellationToken));
        });
    }

    [HttpPut("organizations/{organizationId:guid}/access")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
    public async Task<IActionResult> OrganizationAccess(Guid organizationId, AssistantOrganizationAccessRequest request, CancellationToken cancellationToken)
    {
        this.ValidateBudget(request.DailyBudgetRub, request.MonthlyBudgetRub);
        if (!await context.Organizations.AnyAsync(item => item.Id == organizationId && options.AllowedScenarioCodes.Contains(item.PrimaryScenarioCode), cancellationToken))
        {
            return this.NotFound();
        }

        var grant = await context.Set<AssistantOrganizationAccess>().SingleOrDefaultAsync(item => item.OrganizationId == organizationId, cancellationToken);
        if (grant is null)
        {
            grant = new AssistantOrganizationAccess { OrganizationId = organizationId };
            context.Add(grant);
        }

        grant.IsEnabled = request.IsEnabled;
        grant.WebEnabled = request.WebEnabled;
        grant.TelegramEnabled = request.TelegramEnabled;
        grant.EnabledUntil = request.EnabledUntil?.ToUniversalTime();
        grant.DailyBudgetRub = request.DailyBudgetRub;
        grant.MonthlyBudgetRub = request.MonthlyBudgetRub;
        grant.UpdatedByUserId = this.AdminId();
        grant.UpdatedAt = DateTime.UtcNow;
        this.Audit("organization_access", $"Доступ: {request.IsEnabled}; сайт: {request.WebEnabled}; Telegram: {request.TelegramEnabled}; до: {grant.EnabledUntil:O}; бюджеты: {request.DailyBudgetRub}/{request.MonthlyBudgetRub}.", organizationId);
        await context.SaveChangesAsync(cancellationToken);

        return this.NoContent();
    }

    [HttpGet("usage")]
    [Authorize(Policy = AdminAuthorizationPolicies.ViewBilling)]
    public async Task<IActionResult> Usage(DateOnly from, DateOnly to, Guid? organizationId, string? channel, string? model, string? status,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return this.BadRequest(new { message = "Укажите период до одного года." });
        }

        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = context.Set<AssistantUsage>().AsNoTracking().Where(item => item.CreatedAt >= start && item.CreatedAt < end);
        if (organizationId.HasValue) query = query.Where(item => item.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(channel)) query = query.Where(item => item.Channel == channel);
        if (!string.IsNullOrWhiteSpace(model)) query = query.Where(item => item.Model == model);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, options.LookupPageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var totals = await query.GroupBy(_ => 1).Select(group => new
        {
            costRub = group.Sum(item => item.CostRub),
            reservedRub = group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0),
            inputTokens = group.Sum(item => item.InputTokens ?? 0), outputTokens = group.Sum(item => item.OutputTokens ?? 0),
            unknownCount = group.Count(item => item.Status == "unknown"), requestCount = group.Count()
        }).SingleOrDefaultAsync(cancellationToken);
        var daily = await query.GroupBy(item => item.CreatedAt.Date).Select(group => new
        {
            date = group.Key, costRub = group.Sum(item => item.CostRub),
            reservedRub = group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0)
        }).OrderBy(item => item.date).ToListAsync(cancellationToken);
        var byOrganization = await query.GroupBy(item => item.OrganizationId).Select(group => new
        {
            organizationId = group.Key,
            name = context.Organizations.Where(item => item.Id == group.Key).Select(item => item.Name).First(),
            costRub = group.Sum(item => item.CostRub),
            reservedRub = group.Sum(item => item.CostRub == null ? item.ReservedCostRub : 0), requestCount = group.Count()
        }).ToListAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new
            {
                item.Id, item.OrganizationId,
                organizationName = context.Organizations.Where(org => org.Id == item.OrganizationId).Select(org => org.Name).First(),
                item.UserId, userEmail = context.Users.Where(user => user.Id == item.UserId).Select(user => user.Email).First(),
                item.Channel, item.Kind, item.Model, item.Status, item.ProviderRequestId, item.InputTokens, item.OutputTokens,
                item.CachedTokens, item.ReservedCostRub, item.CostRub, item.CreatedAt, item.ErrorCode
            }).ToListAsync(cancellationToken);

        return this.Ok(new
        {
            totals = totals ?? new { costRub = (decimal?)null, reservedRub = 0m, inputTokens = 0L, outputTokens = 0L, unknownCount = 0, requestCount = 0 },
            daily, byOrganization, items, totalCount, page, pageSize
        });
    }

    [HttpPost("usage/{id:guid}/settle")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
    public async Task<IActionResult> Settle(Guid id, AssistantUsageSettlement request, CancellationToken cancellationToken)
    {
        if (request.CostRub < 0 || request.CostRub > options.MaximumBudgetRub || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > options.MaximumMessageCharacters)
        {
            return this.BadRequest(new { message = "Укажите стоимость из кабинета провайдера и основание сверки." });
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var changed = await context.Set<AssistantUsage>().Where(item => item.Id == id && item.Status == "unknown" && item.CompletedAt != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CostRub, request.CostRub).SetProperty(item => item.Status, "settled"), cancellationToken);
            if (changed == 0)
            {
                return this.Conflict(new { message = "Сверка доступна только для завершённых запросов с неизвестной стоимостью." });
            }

            this.Audit("usage_settle", $"Запрос {id:D}; стоимость {request.CostRub}; основание: {request.Reason.Trim()}");
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return this.NoContent();
        });
    }

    private void ValidateBudget(decimal daily, decimal monthly)
    {
        if (daily < 0 || monthly < 0 || daily > options.MaximumBudgetRub || monthly > options.MaximumBudgetRub)
        {
            throw new InvalidOperationException("Бюджет выходит за пределы конфигурации.");
        }
    }

    private string AdminId() => this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType)
        ?? throw new UnauthorizedAccessException("Сессия администратора завершилась.");

    private void Audit(string action, string description, Guid? organizationId = null) =>
        context.Add(new AssistantAudit { AdminUserId = this.AdminId(), OrganizationId = organizationId, Action = action, Description = description });

    private static bool IsIdentifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 120 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.' or ':');
}

public sealed record AssistantSettingsRequest(bool IsEnabled, bool WebEnabled, bool TelegramEnabled, bool SpeechRepliesEnabled,
    string TextModel, string SpeechModel, string VoiceModel, string VoiceName, decimal DailyBudgetRub, decimal MonthlyBudgetRub,
    decimal TextReservationRub, decimal AudioReservationRub, int MaximumOutputTokens, long Revision);

public sealed record AssistantOrganizationAccessRequest(bool IsEnabled, DateTime? EnabledUntil, bool WebEnabled, bool TelegramEnabled,
    decimal DailyBudgetRub, decimal MonthlyBudgetRub);

public sealed record AssistantUsageSettlement(decimal CostRub, string Reason);
