using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantUsageService(IServiceScopeFactory scopes, AssistantOptions options)
{
    public async Task<Guid> ReserveAsync(
        AssistantActor actor,
        Guid? conversationId,
        string kind,
        string model,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var strategy = context.Database.CreateExecutionStrategy();
        var usageId = Guid.NewGuid();

        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            if (await context.Set<AssistantUsage>().AnyAsync(item => item.Id == usageId, cancellationToken))
            {
                return usageId;
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var settings = await context.Set<AssistantSettings>()
                .FromSqlRaw("SELECT * FROM \"AssistantSettings\" WHERE \"Id\" = 1 FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Помощник ещё не настроен.");
            var access = await context.Set<AssistantOrganizationAccess>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.OrganizationId == actor.OrganizationId, cancellationToken)
                ?? throw new InvalidOperationException("Доступ компании к помощнику не включён.");
            if (!settings.IsEnabled || !access.IsEnabled || access.EnabledUntil.HasValue && access.EnabledUntil <= DateTime.UtcNow ||
                actor.Channel == "web" && (!settings.WebEnabled || !access.WebEnabled) ||
                actor.Channel == "telegram" && (!settings.TelegramEnabled || !access.TelegramEnabled))
            {
                throw new InvalidOperationException("Помощник отключён.");
            }

            var reservation = kind == "text" ? settings.TextReservationRub : settings.AudioReservationRub;
            if (reservation <= 0)
            {
                throw new InvalidOperationException("Для запросов помощника не настроен резерв расходов.");
            }

            var now = DateTime.UtcNow;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(options.BudgetTimeZone);
            var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
            var monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, 1), zone);
            var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), zone);
            var charges = context.Set<AssistantUsage>().AsNoTracking().Where(item => item.CreatedAt >= monthStart);
            var globalMonth = await charges.SumAsync(item => item.CostRub ?? item.ReservedCostRub, cancellationToken);
            var globalDay = await charges.Where(item => item.CreatedAt >= dayStart)
                .SumAsync(item => item.CostRub ?? item.ReservedCostRub, cancellationToken);
            var organizationMonth = await charges.Where(item => item.OrganizationId == actor.OrganizationId)
                .SumAsync(item => item.CostRub ?? item.ReservedCostRub, cancellationToken);
            var organizationDay = await charges.Where(item => item.OrganizationId == actor.OrganizationId && item.CreatedAt >= dayStart)
                .SumAsync(item => item.CostRub ?? item.ReservedCostRub, cancellationToken);
            if (globalMonth + reservation > settings.MonthlyBudgetRub || globalDay + reservation > settings.DailyBudgetRub ||
                organizationMonth + reservation > access.MonthlyBudgetRub || organizationDay + reservation > access.DailyBudgetRub)
            {
                throw new InvalidOperationException("Достигнут бюджет помощника. Новые обращения к ИИ остановлены.");
            }

            var usage = new AssistantUsage
            {
                Id = usageId,
                OrganizationId = actor.OrganizationId,
                UserId = actor.UserId,
                Channel = actor.Channel,
                ConversationId = conversationId,
                Kind = kind,
                Model = model,
                ReservedCostRub = reservation
            };
            context.Add(usage);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return usage.Id;
        });
    }

    public async Task CompleteAsync(Guid id, JsonElement? response, bool succeeded, string? requestId, string? errorCode)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var record = await context.Set<AssistantUsage>().SingleAsync(item => item.Id == id);
        if (record.CompletedAt.HasValue)
        {
            return;
        }

        if (response.HasValue && response.Value.ValueKind == JsonValueKind.Object)
        {
            var body = response.Value;
            if (body.TryGetProperty("id", out var identity) && identity.ValueKind == JsonValueKind.String)
            {
                record.ProviderRequestId = Limit(identity.GetString(), 200);
            }

            if (body.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                record.InputTokens = ReadInteger(usage, "prompt_tokens") ?? ReadInteger(usage, "input_tokens");
                record.OutputTokens = ReadInteger(usage, "completion_tokens") ?? ReadInteger(usage, "output_tokens");
                record.CostRub = ReadDecimal(usage, "cost");
                record.AudioSeconds = ReadDecimal(usage, "seconds") ?? ReadDecimal(usage, "duration");
                if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
                {
                    record.CachedTokens = ReadInteger(details, "cached_tokens");
                }
            }
        }

        record.ProviderRequestId ??= Limit(requestId, 200);
        record.Status = record.CostRub.HasValue ? succeeded ? "succeeded" : "failed" : "unknown";
        record.ErrorCode = Limit(errorCode, 100);
        record.CompletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task CancelUnsentAsync(Guid id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        await context.Set<AssistantUsage>().Where(item => item.Id == id && item.CompletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "cancelled")
                .SetProperty(item => item.CostRub, 0m).SetProperty(item => item.CompletedAt, DateTime.UtcNow));
    }

    private static long? ReadInteger(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number) && number >= 0
            ? number : null;

    private static decimal? ReadDecimal(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item))
        {
            return null;
        }

        decimal number;
        var valid = item.ValueKind == JsonValueKind.Number ? item.TryGetDecimal(out number)
            : decimal.TryParse(item.ValueKind == JsonValueKind.String ? item.GetString() : null, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        return valid && number >= 0 ? number : null;
    }

    private static string? Limit(string? value, int maximum) => value?[..Math.Min(value.Length, maximum)];
}
