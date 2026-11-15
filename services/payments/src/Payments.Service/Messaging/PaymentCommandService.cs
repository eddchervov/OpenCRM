using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Payments.Contracts;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Persistence;
using Payments.Service.Processing;
using Payments.Service.Providers;

namespace Payments.Service.Messaging;

/// <summary> Принимает поручения в локальной транзакции, не выполняя денежных запросов. </summary>
public sealed class PaymentCommandService(
    PaymentDbContext database,
    IPaymentProvider provider,
    PaymentEventPublisher events,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentCommandService> logger)
{
    public async Task AcceptAsync(CreatePayment command, CancellationToken cancellationToken)
    {
        this.Validate(command);
        var hash = PaymentCommandService.Hash(command with { CommandId = Guid.Empty });
        if (this.IsDisabled())
        {
            await using var disabledTransaction = await this.BeginTransactionAsync(cancellationToken);
            await this.LockAsync(command.ApplicationId, command.CommandId, command.PurchaseId, cancellationToken);
            if (await this.IsReplayAsync(command.ApplicationId, command.CommandId, hash, cancellationToken))
            {
                return;
            }

            this.ValidateForExecution(command);
            this.Record(command.ApplicationId, command.CommandId, hash);
            await events.PublishCommandRejectedAsync(command.ApplicationId, command.PurchaseId, command.CommandId,
                "payments_disabled", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (disabledTransaction != null)
            {
                await disabledTransaction.CommitAsync(cancellationToken);
            }

            logger.LogWarning("Платёжная команда отклонена {PurchaseId} {CommandId} {ReasonCode}.",
                command.PurchaseId, command.CommandId, "payments_disabled");

            return;
        }

        await using var transaction = await this.BeginTransactionAsync(cancellationToken);
        await this.LockAsync(command.ApplicationId, command.CommandId, command.PurchaseId, cancellationToken);
        if (await this.IsReplayAsync(command.ApplicationId, command.CommandId, hash, cancellationToken))
        {
            return;
        }

        this.ValidateForExecution(command);
        if (command.NotAfter <= clock.GetUtcNow())
        {
            this.Record(command.ApplicationId, command.CommandId, hash);
            await events.PublishCommandRejectedAsync(command.ApplicationId, command.PurchaseId, command.CommandId,
                "command_expired", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogWarning("Платёжная команда просрочена {PurchaseId} {CommandId}.",
                command.PurchaseId, command.CommandId);

            return;
        }

        var order = await database.PaymentOrders.Include(item => item.Operations)
            .SingleOrDefaultAsync(item => item.ApplicationId == command.ApplicationId && item.PurchaseId == command.PurchaseId,
                cancellationToken);

        if (order != null && order.RequestHash != hash)
        {
            throw new PaymentCommandException("purchase_conflict");
        }

        if (order == null)
        {
            var now = clock.GetUtcNow();
            order = new PaymentOrder
            {
                Id = PaymentCommandService.StableId("payment-order", command.ApplicationId,
                    command.MerchantAccountId, command.PurchaseId.ToString("D")),
                ApplicationId = command.ApplicationId,
                PurchaseId = command.PurchaseId,
                RequestHash = hash,
                AmountMinor = command.AmountMinor,
                Currency = command.Currency,
                Description = command.Description,
                ReturnUrl = command.ReturnUrl,
                BuyerId = command.BuyerId,
                BeneficiaryId = command.BeneficiaryId,
                MerchantAccountId = command.MerchantAccountId,
                IsTest = options.Value.IsTest,
                CreatedAt = now,
                NextCheckAt = now
            };

            order.Operations.Add(new ProviderOperation
            {
                Id = PaymentCommandService.StableId("create-payment", command.ApplicationId,
                    command.MerchantAccountId, command.PurchaseId.ToString("D")),
                CommandId = command.CommandId,
                Kind = OperationKind.CreatePayment,
                ExecutionMode = options.Value.Mode,
                AmountMinor = command.AmountMinor,
                IdempotenceKey = PaymentCommandService.StableId("create-payment-key", command.ApplicationId,
                    command.MerchantAccountId, command.PurchaseId.ToString("D")).ToString("N"),
                RequestBody = provider.BuildPaymentRequest(order),
                PostBefore = command.NotAfter,
                CreatedAt = now,
                UpdatedAt = now,
                NextAttemptAt = now
            });

            database.PaymentOrders.Add(order);
            await events.PublishStateAsync(order, cancellationToken);
            var creationOperation = order.Operations.Single(item => item.Kind == OperationKind.CreatePayment);
            database.PaymentAuditEntries.Add(new PaymentAuditEntry
            {
                Id = Guid.NewGuid(),
                PaymentOrderId = order.Id,
                ProviderOperationId = creationOperation.Id,
                Stage = "payment_command",
                Code = "payment_prepared",
                AggregateVersion = order.Version,
                AmountMinor = order.AmountMinor,
                OccurredAt = now
            });
        }

        this.Record(command.ApplicationId, command.CommandId, hash);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task AcceptAsync(RequestRefund command, CancellationToken cancellationToken)
    {
        this.Validate(command);

        if (this.IsDisabled())
        {
            var disabledHash = PaymentCommandService.Hash(command);
            await using var disabledTransaction = await this.BeginTransactionAsync(cancellationToken);
            await this.LockAsync(command.ApplicationId, command.CommandId, command.PurchaseId, cancellationToken);
            if (await this.IsReplayAsync(command.ApplicationId, command.CommandId, disabledHash, cancellationToken))
            {
                return;
            }

            this.ValidateForExecution(command);
            this.Record(command.ApplicationId, command.CommandId, disabledHash);
            await events.PublishCommandRejectedAsync(command.ApplicationId, command.PurchaseId, command.CommandId,
                "payments_disabled", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (disabledTransaction != null)
            {
                await disabledTransaction.CommitAsync(cancellationToken);
            }

            logger.LogWarning("Команда возврата отклонена {PurchaseId} {CommandId} {ReasonCode}.",
                command.PurchaseId, command.CommandId, "payments_disabled");

            return;
        }

        var hash = PaymentCommandService.Hash(command);
        await using var transaction = await this.BeginTransactionAsync(cancellationToken);
        await this.LockAsync(command.ApplicationId, command.CommandId, command.PurchaseId, cancellationToken);
        if (await this.IsReplayAsync(command.ApplicationId, command.CommandId, hash, cancellationToken))
        {
            return;
        }

        this.ValidateForExecution(command);
        if (command.NotAfter <= clock.GetUtcNow())
        {
            this.Record(command.ApplicationId, command.CommandId, hash);
            await events.PublishCommandRejectedAsync(command.ApplicationId, command.PurchaseId, command.CommandId,
                "command_expired", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogWarning("Команда возврата просрочена {PurchaseId} {CommandId}.",
                command.PurchaseId, command.CommandId);

            return;
        }

        var order = await database.PaymentOrders
            .FromSqlInterpolated($"SELECT * FROM payments.\"PaymentOrders\" WHERE \"ApplicationId\" = {command.ApplicationId} AND \"PurchaseId\" = {command.PurchaseId} FOR UPDATE")
            .Include(item => item.Operations).SingleOrDefaultAsync(cancellationToken)
            ?? throw new PaymentCommandException("purchase_not_found");

        if (!string.Equals(order.MerchantAccountId, command.MerchantAccountId, StringComparison.Ordinal))
        {
            throw new PaymentCommandException("merchant_configuration_mismatch");
        }

        if (order.IsTest != options.Value.IsTest)
        {
            throw new PaymentCommandException("merchant_configuration_mismatch");
        }

        if (order.Status != PaymentStatus.Succeeded || order.ProviderPaymentId == null)
        {
            throw new PaymentCommandException("payment_not_succeeded");
        }

        var reserved = order.Operations
            .Where(operation => operation.Kind == OperationKind.Refund
                && operation.ProviderStatus != "succeeded"
                && operation.Status is not (OperationStatus.Rejected or OperationStatus.Resolved))
            .Sum(operation => operation.AmountMinor);

        var succeeded = order.Operations
            .Where(operation => operation.Kind == OperationKind.Refund && operation.ProviderStatus == "succeeded")
            .Sum(operation => operation.AmountMinor);

        var alreadyRefunded = Math.Max(order.RefundedAmountMinor, succeeded);
        if (command.AmountMinor > order.AmountMinor - alreadyRefunded - reserved)
        {
            throw new PaymentCommandException("refund_exceeds_available_amount");
        }

        var now = clock.GetUtcNow();
        var refundOperation = new ProviderOperation
        {
            Id = PaymentCommandService.StableId("refund-operation", command.ApplicationId,
                command.MerchantAccountId, command.PurchaseId.ToString("D"), command.CommandId.ToString("D")),
            PaymentOrder = order,
            PaymentOrderId = order.Id,
            CommandId = command.CommandId,
            Kind = OperationKind.Refund,
            ExecutionMode = options.Value.Mode,
            AmountMinor = command.AmountMinor,
            IdempotenceKey = PaymentCommandService.StableId("refund-operation-key", command.ApplicationId,
                command.MerchantAccountId, command.PurchaseId.ToString("D"), command.CommandId.ToString("D")).ToString("N"),
            RequestBody = provider.BuildRefundRequest(order, command.AmountMinor, command.Reason),
            PostBefore = command.NotAfter,
            Reason = command.Reason,
            RequestedBy = command.RequestedBy,
            CreatedAt = now,
            UpdatedAt = now,
            NextAttemptAt = now
        };
        database.ProviderOperations.Add(refundOperation);

        this.Record(command.ApplicationId, command.CommandId, hash);
        await events.PublishStateAsync(order, cancellationToken);
        database.PaymentAuditEntries.Add(new PaymentAuditEntry
        {
            Id = Guid.NewGuid(),
            PaymentOrderId = order.Id,
            ProviderOperationId = refundOperation.Id,
            Stage = "refund_command",
            Code = "refund_prepared",
            AggregateVersion = order.Version,
            AmountMinor = refundOperation.AmountMinor,
            RefundedAmountMinor = order.RefundedAmountMinor,
            OccurredAt = now
        });
        await database.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private void Validate(CreatePayment command)
    {
        if (string.IsNullOrWhiteSpace(command.ApplicationId) || command.ApplicationId.Length > 64
            || command.SchemaVersion != 2 || command.Purpose != "subscription" || command.InitiationMode != "customer"
            || command.CommandId == Guid.Empty || command.PurchaseId == Guid.Empty
            || command.AmountMinor is <= 0 or > 100_000_000 || command.Currency != "RUB"
            || string.IsNullOrWhiteSpace(command.Description) || command.Description.Length > 128
            || string.IsNullOrWhiteSpace(command.BuyerId) || command.BuyerId.Length > 128
            || string.IsNullOrWhiteSpace(command.BeneficiaryId) || command.BeneficiaryId.Length > 128
            || string.IsNullOrWhiteSpace(command.MerchantAccountId) || command.MerchantAccountId.Length > 128
            || !this.IsValidCommandWindow(command.IssuedAt, command.NotAfter))
        {
            throw new PaymentCommandException("invalid_payment");
        }
    }

    private void Validate(RequestRefund command)
    {
        if (string.IsNullOrWhiteSpace(command.ApplicationId) || command.ApplicationId.Length > 64
            || command.SchemaVersion != 2 || command.CommandId == Guid.Empty || command.PurchaseId == Guid.Empty
            || command.AmountMinor <= 0
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 128
            || string.IsNullOrWhiteSpace(command.RequestedBy) || command.RequestedBy.Length > 128
            || string.IsNullOrWhiteSpace(command.MerchantAccountId) || command.MerchantAccountId.Length > 128
            || !this.IsValidCommandWindow(command.IssuedAt, command.NotAfter))
        {
            throw new PaymentCommandException("invalid_refund");
        }
    }

    private void ValidateForExecution(CreatePayment command)
    {
        if (command.ApplicationId != options.Value.ApplicationId || !this.IsAllowedReturnUrl(command))
        {
            throw new PaymentCommandException("invalid_payment");
        }

        this.EnsureMerchantMatches(command.MerchantAccountId);
    }

    private void ValidateForExecution(RequestRefund command)
    {
        if (command.ApplicationId != options.Value.ApplicationId)
        {
            throw new PaymentCommandException("invalid_refund");
        }

        this.EnsureMerchantMatches(command.MerchantAccountId);
    }

    private void EnsureMerchantMatches(string merchantAccountId)
    {
        if (!string.Equals(merchantAccountId, options.Value.MerchantAccountId, StringComparison.Ordinal))
        {
            throw new PaymentCommandException("merchant_configuration_mismatch");
        }
    }

    private bool IsValidCommandWindow(DateTimeOffset issuedAt, DateTimeOffset notAfter)
    {
        var now = clock.GetUtcNow();

        return issuedAt != default && notAfter != default
            && issuedAt <= now.AddMinutes(5)
            && notAfter > issuedAt
            && notAfter <= issuedAt.AddHours(23);
    }

    private bool IsAllowedReturnUrl(CreatePayment command)
    {
        var expectedQuery = $"?purchaseId={command.PurchaseId:D}";

        return options.Value.AllowedReturnUrls.Any(baseUrl =>
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                && string.IsNullOrEmpty(baseUri.Query)
                && string.IsNullOrEmpty(baseUri.Fragment)
                && string.IsNullOrEmpty(baseUri.UserInfo)
                && baseUri.Scheme == Uri.UriSchemeHttps
                && string.Equals(command.ReturnUrl, baseUrl + expectedQuery, StringComparison.Ordinal));
    }

    private bool IsDisabled() => string.Equals(options.Value.Mode, "Disabled", StringComparison.OrdinalIgnoreCase);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await database.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task LockAsync(string applicationId, Guid commandId, Guid purchaseId, CancellationToken cancellationToken)
    {
        var commandKey = PaymentCommandService.LockKey($"command:{applicationId}:{commandId}");
        var purchaseKey = PaymentCommandService.LockKey($"purchase:{applicationId}:{purchaseId}");
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({commandKey})", cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({purchaseKey})", cancellationToken);
    }

    private async Task<bool> IsReplayAsync(string applicationId, Guid commandId, string hash, CancellationToken cancellationToken)
    {
        var receipt = await database.CommandReceipts.FindAsync([applicationId, commandId], cancellationToken);
        if (receipt != null && receipt.RequestHash != hash)
        {
            throw new PaymentCommandException("command_conflict");
        }

        return receipt != null;
    }

    private void Record(string applicationId, Guid commandId, string hash)
    {
        database.CommandReceipts.Add(new CommandReceipt
        {
            ApplicationId = applicationId,
            CommandId = commandId,
            RequestHash = hash,
            ReceivedAt = clock.GetUtcNow()
        });
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static Guid StableId(string purpose, params string[] values)
    {
        var value = string.Join(":", new[] { "payments-v2", purpose }.Concat(values));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return new Guid(hash.AsSpan(0, 16));
    }

    private static long LockKey(string value) => BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
