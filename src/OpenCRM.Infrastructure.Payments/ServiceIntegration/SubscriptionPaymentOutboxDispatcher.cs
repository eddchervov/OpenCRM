using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Повторяет доставку одной и той же команды, включая потерю подтверждения брокера. </summary>
internal sealed class SubscriptionPaymentOutboxDispatcher(
    OpenCRMContext context,
    ISubscriptionPaymentCommandTransport transport,
    IOptions<PaymentsServiceOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionPaymentOutboxDispatcher> logger)
{
    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        for (var index = 0; index < 25 && !cancellationToken.IsCancellationRequested; index++)
        {
            var command = await this.ClaimAsync(cancellationToken);
            if (command == null)
            {
                return;
            }

            try
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Payload)));
                if (hash != command.PayloadHash)
                {
                    throw new JsonException("Снимок финансового поручения повреждён.");
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SendTimeoutSeconds));
                await transport.SendAsync(command.Kind, command.Payload, command.Id, command.PurchaseId, timeout.Token);
                await this.RecordResultAsync(command, null, false, cancellationToken);
                logger.LogInformation("Поручение принято брокером. PurchaseId={PurchaseId}, CommandId={CommandId}, Kind={Kind}, Attempt={Attempt}",
                    command.PurchaseId, command.Id, command.Kind, command.Attempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SubscriptionPurchaseDiagnostics.Failure(logger, "command_delivery", command.PurchaseId, exception);
                try
                {
                    await this.RecordResultAsync(command, exception is JsonException ? "command_snapshot_invalid" : "broker_delivery_unknown",
                        exception is JsonException, cancellationToken);
                }
                catch (Exception journalException)
                {
                    SubscriptionPurchaseDiagnostics.Failure(logger, "command_failure_journal", command.PurchaseId, journalException);
                }

                if (exception is not JsonException)
                {
                    return;
                }
            }
        }
    }

    private async Task<SubscriptionPaymentCommand?> ClaimAsync(CancellationToken cancellationToken)
    {
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var candidates = await context.SubscriptionPaymentCommands.FromSqlInterpolated($"SELECT * FROM \"SubscriptionPaymentCommands\" WHERE \"SentAt\" IS NULL AND \"NextAttemptAt\" <= {now} AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" <= {now}) ORDER BY \"NextAttemptAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
            var command = candidates.SingleOrDefault();
            if (command != null)
            {
                command.LeaseToken = Guid.NewGuid();
                command.LeaseUntil = now.AddMinutes(1);
                command.Attempts++;
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return command;
        });
    }

    private async Task RecordResultAsync(SubscriptionPaymentCommand lease, string? code, bool quarantine, CancellationToken cancellationToken)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var commands = await context.SubscriptionPaymentCommands.FromSqlInterpolated(
                $"SELECT * FROM \"SubscriptionPaymentCommands\" WHERE \"Id\" = {lease.Id} FOR UPDATE").ToListAsync(cancellationToken);
            var command = commands.Single();
            if (command.LeaseToken != lease.LeaseToken || command.SentAt.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            command.LeaseToken = null;
            command.LeaseUntil = null;
            command.LastErrorCode = code;
            if (code == null)
            {
                command.SentAt = now;
            }
            else
            {
                command.NextAttemptAt = quarantine ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
                    : now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(command.Attempts, 6))));
            }

            var purchase = await context.SubscriptionPurchases.SingleAsync(item => item.Id == command.PurchaseId, cancellationToken);
            if (quarantine)
            {
                purchase.LastErrorCode = code;
                purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
            }

            SubscriptionPurchaseDiagnostics.Add(context, purchase, "command_delivery", code ?? "broker_acknowledged", now, command.Id);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
