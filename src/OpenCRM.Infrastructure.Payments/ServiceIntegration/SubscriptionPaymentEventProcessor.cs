using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Применяет только проверенные денежные снимки и хранит долговечный inbox. </summary>
internal sealed class SubscriptionPaymentEventProcessor(
    OpenCRMContext context,
    IOptions<PaymentsServiceOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionPaymentEventProcessor> logger)
{
    public Task ReceiveAsync(PaymentStateChanged message, CancellationToken cancellationToken = default) =>
        this.ReceiveAsync(message.EventId, message.PurchaseId, "state", JsonSerializer.Serialize(message), cancellationToken);

    public Task ReceiveAsync(PaymentCommandRejected message, CancellationToken cancellationToken = default) =>
        this.ReceiveAsync(message.EventId, message.PurchaseId, "rejected", JsonSerializer.Serialize(message), cancellationToken);

    public Task ReceiveAsync(PaymentOperationStateChanged message, CancellationToken cancellationToken = default) =>
        this.ReceiveOperationAsync(message.EventId, message.PurchaseId, JsonSerializer.Serialize(message), cancellationToken);

    public Task ReceiveOperationAsync(Guid eventId, Guid purchaseId, string payload, CancellationToken cancellationToken = default) =>
        this.ReceiveAsync(eventId, purchaseId, "operation", payload, cancellationToken);

    public async Task ReplayPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await context.SubscriptionPaymentEvents.AsNoTracking()
            .Where(item => item.MessageId != null && item.ProcessedAt == null && item.NextAttemptAt <= now)
            .OrderBy(item => item.NextAttemptAt).Select(item => item.Id).Take(25).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({id.ToString("N")}))", cancellationToken);
                    var receipt = await context.SubscriptionPaymentEvents.SingleAsync(item => item.Id == id, cancellationToken);
                    if (!receipt.ProcessedAt.HasValue)
                    {
                        using var document = JsonDocument.Parse(receipt.Payload!);
                        var purchaseId = document.RootElement.GetProperty("PurchaseId").GetGuid();
                        await this.ApplyAsync(receipt, purchaseId, cancellationToken);
                        await context.SaveChangesAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SubscriptionPurchaseDiagnostics.Failure(logger, "inbox_replay", null, exception);
            }
        }
    }

    private async Task ReceiveAsync(Guid messageId, Guid purchaseId, string kind, string payload, CancellationToken cancellationToken)
    {
        // Время повторного отклонения команды не меняет его финансовое содержание.
        var fingerprint = kind == "rejected"
            ? JsonSerializer.Serialize(JsonSerializer.Deserialize<PaymentCommandRejected>(payload)! with { OccurredAt = DateTimeOffset.UnixEpoch })
            : payload;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({messageId.ToString("N")}))", cancellationToken);
                var previous = await context.SubscriptionPaymentEvents.SingleOrDefaultAsync(item => item.MessageId == messageId, cancellationToken);
                if (previous != null)
                {
                    if (previous.MessageHash != hash)
                    {
                        logger.LogCritical("Повтор финансового EventId с другим содержимым. EventId={EventId}, PurchaseId={PurchaseId}", messageId, purchaseId);
                        var originalId = previous.PurchaseId;
                        if (originalId == null && previous.Payload != null)
                        {
                            using var original = JsonDocument.Parse(previous.Payload);
                            if (original.RootElement.TryGetProperty("PurchaseId", out var originalProperty)
                                && originalProperty.TryGetGuid(out var parsedId))
                            {
                                originalId = parsedId;
                            }
                        }

                        var conflictAt = clock.GetUtcNow().UtcDateTime;
                        SubscriptionPurchase? affected = null;
                        if (originalId.HasValue)
                        {
                            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + originalId.Value.ToString("N")}))", cancellationToken);
                            affected = await context.SubscriptionPurchases.Include(item => item.CreationOperation)
                                .SingleOrDefaultAsync(item => item.Id == originalId, cancellationToken);
                            if (affected != null)
                            {
                                this.Attention(affected, "event_payload_conflict", conflictAt);
                            }
                        }

                        context.SubscriptionPaymentEvents.Add(new SubscriptionPaymentEvent
                        {
                            PurchaseId = affected?.Id, CommandId = previous.CommandId, ProviderOrderId = affected?.ProviderOrderId,
                            Kind = "inbox_conflict", Code = "event_payload_conflict", CreatedAt = conflictAt,
                            ProcessedAt = conflictAt, NextAttemptAt = conflictAt
                        });
                        await context.SaveChangesAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                var now = clock.GetUtcNow().UtcDateTime;
                var receipt = new SubscriptionPaymentEvent
                {
                    MessageId = messageId, Kind = kind, Code = "received", Payload = payload,
                    MessageHash = hash, CreatedAt = now, NextAttemptAt = now
                };
                context.SubscriptionPaymentEvents.Add(receipt);
                await this.ApplyAsync(receipt, purchaseId, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception exception)
        {
            SubscriptionPurchaseDiagnostics.Failure(logger, "event_commit", purchaseId, exception);
            throw;
        }
    }

    private async Task ApplyAsync(SubscriptionPaymentEvent receipt, Guid purchaseId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + purchaseId.ToString("N")}))", cancellationToken);
        var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).Include(item => item.CreationOperation)
            .SingleOrDefaultAsync(item => item.Id == purchaseId, cancellationToken);
        receipt.Attempts++;
        if (purchase == null)
        {
            receipt.NextAttemptAt = now.AddMinutes(Math.Min(60, receipt.Attempts));
            logger.LogWarning("Финансовое событие ожидает восстановления покупки. EventId={EventId}, PurchaseId={PurchaseId}, Attempt={Attempt}", receipt.MessageId, purchaseId, receipt.Attempts);
            return;
        }

        receipt.PurchaseId = purchase.Id;
        receipt.ProcessedAt = now;
        if (receipt.Kind == "state")
        {
            var message = JsonSerializer.Deserialize<PaymentStateChanged>(receipt.Payload!)!;
            await this.ApplyStateAsync(purchase, message, now, cancellationToken);
        }
        else if (receipt.Kind == "rejected")
        {
            var message = JsonSerializer.Deserialize<PaymentCommandRejected>(receipt.Payload!)!;
            await this.ApplyRejectionAsync(purchase, message, now, cancellationToken);
        }
        else
        {
            var message = JsonSerializer.Deserialize<PaymentOperationStateChanged>(receipt.Payload!)!;
            await this.ApplyOperationAsync(purchase, receipt, message, now, cancellationToken);
        }
    }

    private async Task ApplyStateAsync(SubscriptionPurchase purchase, PaymentStateChanged message, DateTime now, CancellationToken cancellationToken)
    {
        if (message.SchemaVersion != 1 || message.ApplicationId != options.Value.ApplicationId || message.EventId == Guid.Empty
            || message.PaymentOrderId == Guid.Empty || message.AggregateVersion <= 0
            || message.CreationStatus is not ("NotCreated" or "Prepared" or "Sending" or "OutcomeUnknown" or "AwaitingProvider" or "Resolved" or "Rejected" or "ManualReview")
            || message.AmountMinor != purchase.AmountMinor || message.Currency != purchase.Currency
            || message.IsTest != purchase.IsTest || message.IsTest != options.Value.ExpectedIsTest
            || purchase.ProviderOrderId.HasValue && purchase.ProviderOrderId != message.PaymentOrderId)
        {
            this.Attention(purchase, "financial_snapshot_mismatch", now);
            return;
        }

        if (message.AggregateVersion == purchase.ProviderVersion
            && (message.PaymentStatus != purchase.ProviderStatus || message.RefundedAmountMinor != purchase.RefundedAmountMinor
                || message.ProviderPaymentId != purchase.Payment.ExternalPaymentId
                || message.PaymentStatus == "Pending" && message.ConfirmationUrl != purchase.Payment.PaymentLink))
        {
            this.Attention(purchase, "financial_version_conflict", now);
            return;
        }

        if (message.AggregateVersion <= purchase.ProviderVersion)
        {
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "state", "stale_event_ignored", now);
            return;
        }

        if (message.RefundedAmountMinor < purchase.RefundedAmountMinor || message.RefundedAmountMinor > purchase.AmountMinor
            || message.RefundedAmountMinor < 0 || message.PaymentStatus is not ("NotCreated" or "Pending" or "WaitingForCapture" or "Succeeded" or "Canceled")
            || message.RefundedAmountMinor > 0 && message.PaymentStatus != "Succeeded"
            || purchase.ProviderStatus == "Succeeded" && message.PaymentStatus != "Succeeded"
            || purchase.ProviderStatus == "Canceled" && message.PaymentStatus != "Canceled"
            || !string.IsNullOrWhiteSpace(purchase.Payment.ExternalPaymentId) && purchase.Payment.ExternalPaymentId != message.ProviderPaymentId)
        {
            this.Attention(purchase, "financial_state_regression", now);
            return;
        }

        if ((message.PaymentStatus == "Succeeded" || message.ConfirmationUrl != null) && string.IsNullOrWhiteSpace(message.ProviderPaymentId)
            || message.ConfirmationUrl != null && !this.IsAllowedConfirmation(message.ConfirmationUrl))
        {
            this.Attention(purchase, "provider_result_invalid", now);
            return;
        }

        if (message.ProviderPaymentId != null && await context.SubscriptionPayments.AnyAsync(item =>
            item.Id != purchase.SubscriptionPaymentId && item.ExternalPaymentId == message.ProviderPaymentId, cancellationToken))
        {
            this.Attention(purchase, "provider_payment_already_assigned", now);
            return;
        }

        purchase.ProviderOrderId = message.PaymentOrderId;
        purchase.ProviderVersion = message.AggregateVersion;
        purchase.ProviderStatus = message.PaymentStatus;
        purchase.CreationStatus = message.CreationStatus;
        purchase.RefundedAmountMinor = message.RefundedAmountMinor;
        purchase.UpdatedAt = now;
        purchase.NextReconcileAt = now.AddMinutes(purchase.AppliedAt.HasValue ? 15 : 1);
        var payment = purchase.Payment;
        payment.ExternalPaymentId = message.ProviderPaymentId;
        payment.RefundedAmount = message.RefundedAmountMinor / 100m;
        payment.PaymentLink = message.PaymentStatus == "Pending" ? message.ConfirmationUrl : null;
        payment.StatusUpdatedAt = now;
        purchase.CreationOperation.ExternalPaymentId = message.ProviderPaymentId;
        purchase.CreationOperation.PaymentLink = payment.PaymentLink;
        purchase.CreationOperation.Status = message.ProviderPaymentId != null
            ? PaymentCreationOperationStatuses.Created : PaymentCreationOperationStatuses.Prepared;
        if (message.PaymentStatus == "Succeeded")
        {
            payment.PaidAt ??= message.OccurredAt.UtcDateTime;
            if (message.CreationStatus is "ManualReview" or "Rejected")
            {
                payment.PaymentStatus = purchase.AppliedAt.HasValue ? "completed" : "paid_waiting_confirmation";
                this.Attention(purchase, "provider_operation_requires_review", now);
            }
            else if (message.RefundedAmountMinor == message.AmountMinor)
            {
                payment.PaymentStatus = "refunded";
                purchase.ApplicationStatus = purchase.AppliedAt.HasValue ? SubscriptionPurchaseStatuses.NeedsAttention : SubscriptionPurchaseStatuses.Refunded;
                purchase.IsOpen = false;
                purchase.LastErrorCode = purchase.AppliedAt.HasValue ? "refund_requires_access_review" : null;
            }
            else if (message.RefundedAmountMinor > 0)
            {
                payment.PaymentStatus = purchase.AppliedAt.HasValue ? "completed" : "paid_waiting_confirmation";
                this.Attention(purchase, "refund_requires_access_review", now, updateCreation: false);
            }
            else if (!purchase.AppliedAt.HasValue)
            {
                payment.PaymentStatus = "paid_waiting_confirmation";
                purchase.ApplicationStatus = SubscriptionPurchaseStatuses.ActivationPending;
                purchase.NextApplicationAttemptAt = now;
                purchase.LastErrorCode = null;
            }
            else
            {
                payment.PaymentStatus = "completed";
                if (message.RefundedAmountMinor > 0)
                {
                    this.Attention(purchase, "refund_requires_access_review", now);
                }
            }
        }
        else if (message.PaymentStatus == "Canceled")
        {
            payment.PaymentStatus = "canceled";
            purchase.ApplicationStatus = SubscriptionPurchaseStatuses.Closed;
            purchase.IsOpen = false;
        }
        else if (message.CreationStatus == "Rejected" && message.ProviderPaymentId == null && !payment.PaidAt.HasValue)
        {
            purchase.ApplicationStatus = SubscriptionPurchaseStatuses.Closed;
            purchase.IsOpen = false;
            payment.PaymentStatus = "failed";
            purchase.CreationOperation.Status = PaymentCreationOperationStatuses.Rejected;
        }
        else if (message.CreationStatus is "ManualReview" or "Rejected")
        {
            this.Attention(purchase, "provider_operation_requires_review", now);
        }
        else
        {
            payment.PaymentStatus = message.PaymentStatus == "NotCreated" ? "processing" : "pending";
            purchase.ApplicationStatus = SubscriptionPurchaseStatuses.AwaitingPayment;
            purchase.LastErrorCode = null;
        }

        SubscriptionPurchaseDiagnostics.Add(context, purchase, "state", "verified_snapshot_applied", now);
        logger.LogInformation("Денежный снимок принят. PurchaseId={PurchaseId}, EventId={EventId}, Version={Version}, Status={PaymentStatus}, RefundedAmountMinor={RefundedAmountMinor}",
            purchase.Id, message.EventId, message.AggregateVersion, message.PaymentStatus, message.RefundedAmountMinor);
    }

    private async Task ApplyRejectionAsync(SubscriptionPurchase purchase, PaymentCommandRejected message, DateTime now, CancellationToken cancellationToken)
    {
        var command = await context.SubscriptionPaymentCommands.SingleOrDefaultAsync(item => item.Id == message.CommandId && item.PurchaseId == purchase.Id, cancellationToken);
        if (message.SchemaVersion != 1 || message.ApplicationId != options.Value.ApplicationId || command == null)
        {
            this.Attention(purchase, "command_rejection_mismatch", now);
            return;
        }

        var code = Regex.IsMatch(message.Reason, "^[a-zA-Z0-9_.:-]{1,100}$") ? message.Reason : "service_rejected_command";
        command.LastErrorCode = code;
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "command_rejected", code, now, message.CommandId);
        logger.LogWarning("Платёжный сервис отклонил команду. PurchaseId={PurchaseId}, CommandId={CommandId}, EventId={EventId}, Code={Code}", purchase.Id, message.CommandId, message.EventId, code);
        if (command.Kind == "refund")
        {
            this.Attention(purchase, code, now, updateCreation: false);
            return;
        }

        if (command.Kind != "create" || purchase.Payment.PaidAt.HasValue || purchase.ProviderVersion > 0 || purchase.ProviderOrderId.HasValue)
        {
            return;
        }

        if (code != "payments_disabled")
        {
            this.Attention(purchase, code, now);
            return;
        }

        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.Closed;
        purchase.IsOpen = false;
        purchase.LastErrorCode = code;
        purchase.Payment.PaymentStatus = "failed";
        purchase.Payment.StatusUpdatedAt = now;
        purchase.CreationOperation.Status = PaymentCreationOperationStatuses.Rejected;
    }

    private async Task ApplyOperationAsync(SubscriptionPurchase purchase, SubscriptionPaymentEvent receipt,
        PaymentOperationStateChanged message, DateTime now, CancellationToken cancellationToken)
    {
        var command = await context.SubscriptionPaymentCommands.SingleOrDefaultAsync(item =>
            item.Id == message.CommandId && item.PurchaseId == purchase.Id, cancellationToken);
        if (message.SchemaVersion != 1 || message.ApplicationId != options.Value.ApplicationId || message.EventId == Guid.Empty
            || message.OperationId == Guid.Empty || message.PaymentOrderId == Guid.Empty
            || command == null || message.Kind != "Refund" || command.Kind != "refund"
            || message.Status is not ("Prepared" or "Sending" or "OutcomeUnknown" or "AwaitingProvider" or "Resolved" or "Rejected" or "ManualReview"))
        {
            this.Attention(purchase, "operation_event_mismatch", now, updateCreation: false);
            return;
        }

        if (purchase.ProviderOrderId == null)
        {
            receipt.ProcessedAt = null;
            receipt.NextAttemptAt = now.AddMinutes(1);
            logger.LogWarning("Результат возврата ожидает денежный снимок. PurchaseId={PurchaseId}, EventId={EventId}", purchase.Id, message.EventId);
            return;
        }

        if (message.PaymentOrderId != purchase.ProviderOrderId)
        {
            this.Attention(purchase, "operation_event_mismatch", now, updateCreation: false);
            return;
        }

        var code = message.ErrorCode != null && Regex.IsMatch(message.ErrorCode, "^[a-zA-Z0-9_.:-]{1,100}$")
            ? message.ErrorCode : "refund_" + message.Status.ToLowerInvariant();
        receipt.CommandId = message.CommandId;
        receipt.ProviderOrderId = message.PaymentOrderId;
        receipt.Code = code;
        command.LastErrorCode = message.Status is "Rejected" or "ManualReview" ? code : null;
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "refund_operation_" + message.Status.ToLowerInvariant(), code, now, message.CommandId);
        if (message.Status is "Rejected" or "ManualReview" || message.ErrorCode != null)
        {
            this.Attention(purchase, code, now, updateCreation: false);
        }

        logger.LogInformation("Результат возврата сохранён. PurchaseId={PurchaseId}, CommandId={CommandId}, OperationId={OperationId}, Status={Status}, Code={Code}",
            purchase.Id, message.CommandId, message.OperationId, message.Status, code);
    }

    private bool IsAllowedConfirmation(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && options.Value.AllowedConfirmationHosts.Any(host => uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    private void Attention(SubscriptionPurchase purchase, string code, DateTime now, bool updateCreation = true)
    {
        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        purchase.LastErrorCode = code;
        purchase.UpdatedAt = now;
        if (updateCreation)
        {
            purchase.CreationOperation.Status = PaymentCreationOperationStatuses.ManualReview;
            purchase.CreationOperation.LastErrorCode = code;
        }
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "attention", code, now);
        logger.LogError("Финансовая покупка требует разбора. PurchaseId={PurchaseId}, Code={Code}, ProviderVersion={ProviderVersion}", purchase.Id, code, purchase.ProviderVersion);
    }
}
