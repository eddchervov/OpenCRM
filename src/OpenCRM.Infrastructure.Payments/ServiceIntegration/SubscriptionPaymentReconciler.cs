using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Восстанавливает отставшую локальную проекцию через приватное чтение платёжного сервиса. </summary>
internal sealed class SubscriptionPaymentReconciler(
    OpenCRMContext context,
    IHttpClientFactory clients,
    SubscriptionPaymentEventProcessor processor,
    IOptions<PaymentsServiceOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionPaymentReconciler> logger)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    public async Task ReconcileDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var purchases = await context.SubscriptionPurchases.AsNoTracking()
            .Where(item => item.NextReconcileAt <= now && (item.ProviderOrderId != null || item.IsOpen))
            .OrderBy(item => item.NextReconcileAt).Select(item => item.Id).Take(25).ToListAsync(cancellationToken);
        foreach (var id in purchases)
        {
            try
            {
                await this.ReconcileAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SubscriptionPurchaseDiagnostics.Failure(logger, "service_reconciliation", id, exception);
                await this.DeferAsync(id, "service_read_unavailable", cancellationToken);
            }
        }
    }

    internal async Task ReconcileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var client = clients.CreateClient("OpenCRM.PaymentsService");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = client.Timeout > TimeSpan.Zero && client.Timeout < TimeSpan.FromSeconds(15)
            ? client.Timeout : TimeSpan.FromSeconds(15);
        deadline.CancelAfter(timeout);
        using var response = await client.GetAsync($"internal/payments/opencrm/{id:D}", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await this.DeferAsync(id, "service_purchase_not_found", cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Чтение платёжного сервиса отклонено. PurchaseId={PurchaseId}, HttpStatus={HttpStatus}", id, (int)response.StatusCode);
            await this.DeferAsync(id, "service_http_" + (int)response.StatusCode, cancellationToken);
            return;
        }

        var json = await ReadBoundedAsync(response.Content, deadline.Token);
        var snapshot = JsonSerializer.Deserialize<RemotePaymentSnapshot>(json, s_json)
            ?? throw new JsonException("Пустой денежный снимок сервиса.");
        if (snapshot.PurchaseId != id || snapshot.ApplicationId != "opencrm")
        {
            await this.DeferAsync(id, "service_identity_mismatch", cancellationToken);
            return;
        }

        var creation = snapshot.Operations.SingleOrDefault(item => item.Kind == "CreatePayment");
        if (creation == null)
        {
            await this.DeferAsync(id, "service_creation_missing", cancellationToken);
            return;
        }

        await processor.ReceiveAsync(new PaymentStateChanged(Guid.NewGuid(), "opencrm", id, snapshot.Id, snapshot.Version,
            creation.Status, snapshot.PaymentStatus, snapshot.AmountMinor, snapshot.RefundedAmountMinor, snapshot.Currency,
            snapshot.ProviderPaymentId, snapshot.ConfirmationUrl, clock.GetUtcNow(), snapshot.IsTest), cancellationToken);
        foreach (var operation in snapshot.Operations.Where(item => item.Kind == "Refund"))
        {
            var fingerprint = JsonSerializer.Serialize(operation);
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint));
            await processor.ReceiveAsync(new PaymentOperationStateChanged(new Guid(hash.AsSpan(0, 16)), "opencrm", id, snapshot.Id,
                operation.Id, operation.CommandId, operation.Kind, operation.Status, operation.LastError, operation.UpdatedAt), cancellationToken);
        }

        await this.RestoreVerifiedProjectionAsync(id, snapshot, cancellationToken);
        await context.SubscriptionPurchases.Where(item => item.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.NextReconcileAt, item => item.AppliedAt == null
                ? clock.GetUtcNow().UtcDateTime.AddMinutes(1) : clock.GetUtcNow().UtcDateTime.AddMinutes(15)), cancellationToken);
        logger.LogInformation("Проекция сверена с платёжным сервисом. PurchaseId={PurchaseId}, ProviderVersion={ProviderVersion}", id, snapshot.Version);
    }

    private async Task DeferAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + id.ToString("N")}))", cancellationToken);
                var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).SingleAsync(item => item.Id == id, cancellationToken);
                var now = clock.GetUtcNow().UtcDateTime;
                var failureCode = code;
                var preservePurchaseState = false;
                purchase.NextReconcileAt = now.AddMinutes(5);
                if (code == "service_purchase_not_found")
                {
                    var commands = await context.SubscriptionPaymentCommands.FromSqlInterpolated(
                        $"SELECT * FROM \"SubscriptionPaymentCommands\" WHERE \"PurchaseId\" = {id} AND \"Kind\" = 'create' FOR UPDATE")
                        .ToListAsync(cancellationToken);
                    var command = commands.Single();
                    if (command.LeaseUntil > now)
                    {
                        preservePurchaseState = true;
                    }
                    else if (command.SentAt.HasValue || purchase.ProviderOrderId.HasValue)
                    {
                        failureCode = "service_purchase_missing_after_dispatch";
                        Quarantine(command, failureCode);
                    }
                    else if (command.Attempts > 0)
                    {
                        if (this.CanResumeUnacknowledgedCreate(command, purchase, now))
                        {
                            failureCode = "create_dispatch_resumed";
                            command.LastErrorCode = null;
                            command.NextAttemptAt = now;
                            command.LeaseToken = null;
                            command.LeaseUntil = null;
                            purchase.LastErrorCode = null;
                            if (purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention)
                            {
                                purchase.ApplicationStatus = SubscriptionPurchaseStatuses.AwaitingPayment;
                            }
                        }
                        else
                        {
                            failureCode = "service_purchase_missing_after_dispatch";
                            Quarantine(command, failureCode);
                        }
                    }
                }

                var hasCriticalAttention = purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
                    && !IsTransientServiceError(purchase.LastErrorCode);
                var hasVerifiedPaidAccess = purchase.ProviderStatus == "Succeeded" && purchase.Payment.PaidAt != null
                    && purchase.RefundedAmountMinor == 0
                    && (purchase.AppliedAt != null || purchase.ApplicationStatus == SubscriptionPurchaseStatuses.ActivationPending);
                if (!preservePurchaseState && failureCode != "create_dispatch_resumed"
                    && !hasCriticalAttention && !hasVerifiedPaidAccess)
                {
                    purchase.LastErrorCode = failureCode;
                    if (failureCode is "service_http_401" or "service_http_403" or "service_identity_mismatch" or "service_purchase_missing_after_dispatch")
                    {
                        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
                    }
                }

                SubscriptionPurchaseDiagnostics.Add(context, purchase, "service_reconciliation", failureCode, now);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogWarning("Сверка покупки отложена. PurchaseId={PurchaseId}, Code={Code}", id, failureCode);
            });
        }
        catch (Exception exception)
        {
            SubscriptionPurchaseDiagnostics.Failure(logger, "reconciliation_failure_journal", id, exception);
        }
    }

    private bool CanResumeUnacknowledgedCreate(SubscriptionPaymentCommand command, SubscriptionPurchase purchase, DateTime now)
    {
        if (command.Kind != "create" || command.Attempts <= 0 || command.SentAt.HasValue
            || command.LeaseUntil > now || purchase.ProviderOrderId.HasValue
            || purchase.ProviderStatus != "NotCreated" || purchase.CreationStatus != "Prepared"
            || purchase.Payment.PaidAt.HasValue || purchase.RefundedAmountMinor != 0 || purchase.AppliedAt.HasValue
            || purchase.IsTest != options.Value.ExpectedIsTest
            || purchase.ApplicationStatus != SubscriptionPurchaseStatuses.AwaitingPayment
                && !(purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
                    && purchase.LastErrorCode == "service_purchase_missing_after_dispatch"))
        {
            return false;
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Payload)));
        if (!string.Equals(payloadHash, command.PayloadHash, StringComparison.Ordinal))
        {
            return false;
        }

        CreatePayment? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CreatePayment>(command.Payload, s_json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload == null)
        {
            return false;
        }

        var nowUtc = new DateTimeOffset(now, TimeSpan.Zero);

        return command.Id == purchase.Id && command.PurchaseId == purchase.Id
            && payload.SchemaVersion == 2 && payload.CommandId == command.Id && payload.PurchaseId == purchase.Id
            && payload.ApplicationId == "opencrm" && payload.MerchantAccountId == options.Value.MerchantAccountId
            && payload.AmountMinor == purchase.AmountMinor && payload.Currency == purchase.Currency
            && payload.IssuedAt != default && payload.NotAfter != default
            && payload.IssuedAt <= nowUtc.AddMinutes(5) && payload.NotAfter > nowUtc
            && payload.NotAfter > payload.IssuedAt && payload.NotAfter <= payload.IssuedAt.AddHours(23);
    }

    private static void Quarantine(SubscriptionPaymentCommand command, string errorCode)
    {
        command.LastErrorCode = errorCode;
        command.NextAttemptAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        command.LeaseToken = null;
        command.LeaseUntil = null;
    }

    private async Task RestoreVerifiedProjectionAsync(Guid id, RemotePaymentSnapshot snapshot, CancellationToken cancellationToken)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + id.ToString("N")}))", cancellationToken);
            var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).SingleAsync(item => item.Id == id, cancellationToken);
            var creation = snapshot.Operations.Single(item => item.Kind == "CreatePayment");
            var sameProjection = purchase.ProviderOrderId == snapshot.Id && purchase.ProviderVersion == snapshot.Version
                && purchase.AmountMinor == snapshot.AmountMinor && purchase.Currency == snapshot.Currency && purchase.IsTest == snapshot.IsTest
                && purchase.ProviderStatus == snapshot.PaymentStatus && purchase.RefundedAmountMinor == snapshot.RefundedAmountMinor
                && purchase.Payment.ExternalPaymentId == snapshot.ProviderPaymentId && purchase.CreationStatus == creation.Status
                && (purchase.ProviderStatus != "Pending" || purchase.Payment.PaymentLink == snapshot.ConfirmationUrl);
            if (!IsTransientServiceError(purchase.LastErrorCode) || !sameProjection || purchase.RefundedAmountMinor != 0
                || creation.Status is "ManualReview" or "Rejected")
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            if (purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention)
            {
                if (purchase.ProviderStatus == "Succeeded" && purchase.Payment.PaidAt != null)
                {
                    purchase.ApplicationStatus = purchase.AppliedAt.HasValue
                        ? SubscriptionPurchaseStatuses.Applied : SubscriptionPurchaseStatuses.ActivationPending;
                    purchase.NextApplicationAttemptAt = now;
                }
                else if (purchase.ProviderStatus is "Pending" or "NotCreated" or "WaitingForCapture")
                {
                    purchase.ApplicationStatus = SubscriptionPurchaseStatuses.AwaitingPayment;
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            purchase.LastErrorCode = null;
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "service_reconciliation", "verified_projection_restored", now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static bool IsTransientServiceError(string? code) => code?.StartsWith("service_http_", StringComparison.Ordinal) == true
        || code is "service_read_unavailable" or "service_identity_mismatch" or "service_purchase_not_found"
            or "service_purchase_missing_after_dispatch" or "service_creation_missing";

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int length;
        while ((length = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + length > 262_144)
            {
                throw new JsonException("Денежный снимок превышает допустимый размер.");
            }

            buffer.Write(chunk, 0, length);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed record RemotePaymentSnapshot(Guid Id, string ApplicationId, Guid PurchaseId, long Version,
        long AmountMinor, string Currency, string PaymentStatus, long RefundedAmountMinor, string? ProviderPaymentId,
        string? ConfirmationUrl, bool IsTest, List<RemotePaymentOperation> Operations);
    private sealed record RemotePaymentOperation(Guid Id, Guid CommandId, DateTimeOffset UpdatedAt, int AttemptCount,
        string Kind, string Status, string? LastError);
}
