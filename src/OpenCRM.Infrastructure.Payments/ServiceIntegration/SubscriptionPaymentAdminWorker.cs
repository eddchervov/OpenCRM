using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Восстанавливает административные действия после сетевых ошибок и перезапуска процесса. </summary>
internal sealed class SubscriptionPaymentAdminWorker(
    OpenCRMContext context,
    IHttpClientFactory clients,
    SubscriptionPaymentReconciler reconciler,
    SubscriptionPurchaseApplicator applicator,
    IOptions<PaymentsServiceOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionPaymentAdminWorker> logger)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await context.SubscriptionPaymentAdminActions.AsNoTracking()
            .Where(item => item.CompletedAt == null && item.NextAttemptAt <= now)
            .OrderBy(item => item.NextAttemptAt).Select(item => item.Id).Take(25).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                var action = await this.ClaimAsync(id, cancellationToken);
                if (action == null)
                {
                    continue;
                }

                if (action.Action == "check")
                {
                    await this.CheckAsync(action, cancellationToken);
                }
                else if (action.Action == "retry-access")
                {
                    await this.RetryAccessAsync(action, cancellationToken);
                }
                else if (action.Action == "refund")
                {
                    await this.UpdateRefundAsync(action, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SubscriptionPurchaseDiagnostics.Failure(logger, "admin_action", null, exception);
                await this.RecordAsync(id, "needs_attention", "service_unavailable", false, cancellationToken);
            }
        }
    }

    private async Task<SubscriptionPaymentAdminAction?> ClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var rows = await context.SubscriptionPaymentAdminActions.FromSqlInterpolated(
                $"SELECT * FROM \"SubscriptionPaymentAdminActions\" WHERE \"Id\" = {id} FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken);
            var action = rows.SingleOrDefault();
            var now = clock.GetUtcNow().UtcDateTime;
            if (action == null || action.CompletedAt != null || action.NextAttemptAt > now)
            {
                return null;
            }

            action.NextAttemptAt = now.AddMinutes(2);
            action.Attempts++;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return action;
        });
    }

    private async Task CheckAsync(SubscriptionPaymentAdminAction action, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            await this.RecordAsync(action.Id, "failed", "payments_disabled", true, cancellationToken);
            return;
        }

        using var client = clients.CreateClient("OpenCRM.PaymentsService");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        if (action.ProviderCheckRequestedAt == null)
        {
            using var post = new HttpRequestMessage(HttpMethod.Post, $"internal/payments/{options.Value.ApplicationId}/{action.PurchaseId:D}/reconcile")
            {
                Content = JsonContent.Create(new { OperationId = action.Id })
            };
            using var response = await client.SendAsync(post, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                var terminal = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict;
                await this.RecordAsync(action.Id, terminal ? "failed" : "needs_attention", "service_http_" + (int)response.StatusCode, terminal, cancellationToken);
                return;
            }

            var scheduled = JsonSerializer.Deserialize<CheckScheduled>(await ReadBoundedAsync(response.Content, deadline.Token), s_json);
            if (scheduled == null || scheduled.PurchaseId != action.PurchaseId || scheduled.RequestedAt == default)
            {
                throw new JsonException("Некорректное подтверждение запроса проверки.");
            }

            await context.SubscriptionPaymentAdminActions.Where(item => item.Id == action.Id && item.CompletedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.ProviderCheckRequestedAt, scheduled.RequestedAt.UtcDateTime)
                    .SetProperty(item => item.Status, "requested").SetProperty(item => item.Code, "provider_check_requested"), cancellationToken);
            action.ProviderCheckRequestedAt = scheduled.RequestedAt.UtcDateTime;
        }

        using var snapshotResponse = await client.GetAsync($"internal/payments/{options.Value.ApplicationId}/{action.PurchaseId:D}",
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshot = JsonSerializer.Deserialize<CheckSnapshot>(await ReadBoundedAsync(snapshotResponse.Content, deadline.Token), s_json)
            ?? throw new JsonException("Пустой снимок проверки.");
        if (snapshot.PurchaseId != action.PurchaseId || snapshot.ApplicationId != options.Value.ApplicationId)
        {
            await this.RecordAsync(action.Id, "failed", "service_identity_mismatch", true, cancellationToken);
            return;
        }

        if (snapshot.LastReconciledAt?.UtcDateTime < action.ProviderCheckRequestedAt || snapshot.LastReconciledAt == null)
        {
            await this.RecordAsync(action.Id, snapshot.LastError != null ? "needs_attention" : "requested",
                snapshot.LastError != null ? "provider_check_unavailable" : "provider_check_pending", false, cancellationToken);
            return;
        }

        await reconciler.ReconcileAsync(action.PurchaseId, cancellationToken);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + action.PurchaseId.ToString("N")}))", cancellationToken);
            var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).SingleAsync(item => item.Id == action.PurchaseId, cancellationToken);
            var currentRows = await context.SubscriptionPaymentAdminActions.FromSqlInterpolated(
                $"SELECT * FROM \"SubscriptionPaymentAdminActions\" WHERE \"Id\" = {action.Id} FOR UPDATE").ToListAsync(cancellationToken);
            var current = currentRows.Single();
            if (current.CompletedAt != null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (purchase.ProviderOrderId != snapshot.Id || purchase.ProviderVersion != snapshot.Version
                || purchase.AmountMinor != snapshot.AmountMinor || purchase.Currency != snapshot.Currency
                || purchase.RefundedAmountMinor != snapshot.RefundedAmountMinor || purchase.ProviderStatus != snapshot.PaymentStatus
                || purchase.IsTest != snapshot.IsTest || snapshot.IsTest != options.Value.ExpectedIsTest)
            {
                current.Status = "failed";
                current.Code = "verified_snapshot_mismatch";
            }
            else
            {
                current.Status = "succeeded";
                current.Code = "provider_check_confirmed";
                // Результат проверки хранится отдельно от неизменяемой подписи исходного поручения.
                current.VerifiedStateToken = await SubscriptionPaymentAdminService.StateTokenAsync(context, purchase, cancellationToken);
            }

            current.CompletedAt = clock.GetUtcNow().UtcDateTime;
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "admin_action", current.Code, current.CompletedAt.Value, current.Id);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task RetryAccessAsync(SubscriptionPaymentAdminAction action, CancellationToken cancellationToken)
    {
        var accepted = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + action.PurchaseId.ToString("N")}))", cancellationToken);
            var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).SingleAsync(item => item.Id == action.PurchaseId, cancellationToken);
            if (purchase.AppliedAt != null)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            var token = await SubscriptionPaymentAdminService.StateTokenAsync(context, purchase, cancellationToken);
            var verifiedRecently = await context.SubscriptionPaymentAdminActions.AnyAsync(item => item.PurchaseId == purchase.Id
                && item.Action == "check" && item.Status == "succeeded" && item.CompletedAt >= clock.GetUtcNow().UtcDateTime.AddMinutes(-5)
                && item.VerifiedStateToken == token, cancellationToken);
            if (!verifiedRecently || token != action.StateToken || purchase.ProviderStatus != "Succeeded" || purchase.Payment.PaidAt == null || purchase.RefundedAmountMinor != 0
                || purchase.ApplicationStatus != SubscriptionPurchaseStatuses.ActivationPending
                    && !(purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention && purchase.LastErrorCode == "access_transaction_failed")
                || await context.SubscriptionPaymentAdminActions.AnyAsync(item => item.PurchaseId == purchase.Id
                    && item.Action == "refund" && item.Status != "succeeded" && item.Status != "failed", cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            purchase.ApplicationStatus = SubscriptionPurchaseStatuses.ActivationPending;
            purchase.LastErrorCode = null;
            purchase.NextApplicationAttemptAt = clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        });
        if (!accepted)
        {
            await this.RecordAsync(action.Id, "failed", "access_retry_conflict", true, cancellationToken);
            return;
        }

        await applicator.ApplyAsync(action.PurchaseId, cancellationToken);
        var result = await context.SubscriptionPurchases.AsNoTracking().SingleAsync(item => item.Id == action.PurchaseId, cancellationToken);
        await this.RecordAsync(action.Id, result.AppliedAt != null ? "succeeded" : "failed",
            result.AppliedAt != null ? "access_applied" : "access_retry_requires_review", true, cancellationToken);
    }

    private async Task UpdateRefundAsync(SubscriptionPaymentAdminAction action, CancellationToken cancellationToken)
    {
        var events = await context.SubscriptionPaymentEvents.AsNoTracking()
            .Where(item => item.PurchaseId == action.PurchaseId && item.CommandId == action.Id && item.ProcessedAt != null)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Select(item => new { item.Kind, item.Code }).ToListAsync(cancellationToken);
        var refunded = await context.SubscriptionPurchases.AsNoTracking().Where(item => item.Id == action.PurchaseId)
            .Select(item => item.RefundedAmountMinor).SingleAsync(cancellationToken);
        if (events.Any(item => item.Kind == "refund_operation_resolved") && refunded >= action.RefundedBeforeMinor + action.AmountMinor)
        {
            await this.RecordAsync(action.Id, "succeeded", "refund_confirmed", true, cancellationToken);
        }
        else if (events.Any(item => item.Kind == "refund_operation_rejected" || item.Kind == "command_rejected"))
        {
            await this.RecordAsync(action.Id, "failed", "refund_rejected", true, cancellationToken);
        }
        else if (events.FirstOrDefault(item => item.Kind.StartsWith("refund_operation_"))?.Kind == "refund_operation_outcomeunknown")
        {
            await this.RecordAsync(action.Id, "needs_attention", "refund_outcome_unknown", false, cancellationToken);
        }
        else if (events.Any(item => item.Kind == "refund_operation_manualreview"))
        {
            await this.RecordAsync(action.Id, "needs_attention", "refund_requires_review", false, cancellationToken);
        }
        else
        {
            var command = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync(item => item.Id == action.Id, cancellationToken);
            await this.RecordAsync(action.Id, command.LastErrorCode != null ? "needs_attention" : "requested",
                command.LastErrorCode != null ? "refund_delivery_unconfirmed" : "refund_pending", false, cancellationToken);
        }
    }

    private async Task RecordAsync(Guid id, string status, string code, bool completed, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var currentRows = await context.SubscriptionPaymentAdminActions.FromSqlInterpolated(
                $"SELECT * FROM \"SubscriptionPaymentAdminActions\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken);
            var current = currentRows.Single();
            if (current.CompletedAt != null)
            {
                return;
            }

            current.Status = status;
            current.Code = code;
            var now = clock.GetUtcNow().UtcDateTime;
            current.CompletedAt = completed ? now : null;
            current.NextAttemptAt = now.AddSeconds(status == "needs_attention" ? 60 : 10);
            var purchase = await context.SubscriptionPurchases.SingleAsync(item => item.Id == current.PurchaseId, cancellationToken);
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "admin_action", code, now, current.Id);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Состояние действия администратора. PurchaseId={PurchaseId}, OperationId={OperationId}, Action={Action}, Status={Status}, Code={Code}",
                current.PurchaseId, id, current.Action, status, code);
        });
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > 262144)
            {
                throw new JsonException("Ответ сервиса превышает допустимый размер.");
            }

            buffer.Write(chunk, 0, count);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed record CheckScheduled(Guid PurchaseId, DateTimeOffset RequestedAt);
    private sealed record CheckSnapshot(Guid Id, string ApplicationId, Guid PurchaseId, long Version, long AmountMinor,
        string Currency, string PaymentStatus, long RefundedAmountMinor, bool IsTest, DateTimeOffset? LastReconciledAt, string? LastError);
}
