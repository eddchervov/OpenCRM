using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис подтверждения платежей. </summary>
public sealed class PaymentConfirmationService(
    IOrganizationSubscriptionActivationService organizationSubscriptions,
    IPersonalSubscriptionActivationService subscriptions,
    IYooKassaService yooKassa,
    OpenCRMContext? opencrmContext = null) : IPaymentConfirmationService
{
    /// <summary> Префикс типа операции покупки рабочего пространства. </summary>
    private const string WorkspacePurchasePrefix = "yookassa_workspace_purchase:";

    /// <summary> Префикс типа операции продления рабочего пространства. </summary>
    private const string WorkspaceExtensionPrefix = "yookassa_workspace_extension:";

    /// <summary> Подтверждает платёж и активирует соответствующую подписку. </summary>
    public async Task<bool> ConfirmAsync(string paymentId)
    {
        if (opencrmContext == null)
        {
            return await this.ConfirmCoreAsync(paymentId);
        }

        var lockKey = BinaryPrimitives.ReadInt64LittleEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes(paymentId)));
        await opencrmContext.Database.OpenConnectionAsync();
        var hasLock = false;
        try
        {
            await ExecuteAdvisoryLockAsync("pg_advisory_lock", lockKey);
            hasLock = true;

            return await this.ConfirmCoreAsync(paymentId);
        }
        finally
        {
            try
            {
                if (hasLock)
                {
                    await ExecuteAdvisoryLockAsync("pg_advisory_unlock", lockKey);
                }
            }
            finally
            {
                await opencrmContext.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary> Основная логика подтверждения платежа. </summary>
    private async Task<bool> ConfirmCoreAsync(string paymentId)
    {
        if (opencrmContext != null && await opencrmContext.SubscriptionPayments.AnyAsync(item =>
            item.ExternalPaymentId == paymentId && item.PaymentProviderCode == "payments-service"))
        {
            return false;
        }

        var localPayment = await yooKassa.GetLocalPaymentInfoAsync(paymentId)
            ?? throw new InvalidOperationException($"Локальный платеж {paymentId} не найден.");

        if (localPayment.PaymentStatus is "completed" or "refunded")
        {
            return true;
        }

        if (localPayment.PaymentStatus == "failed")
        {
            return false;
        }

        var remotePayment = await yooKassa.GetPaymentStatusAsync(paymentId);
        if (!remotePayment.IsSuccess || remotePayment.PaymentId != paymentId
            || remotePayment.Status != "succeeded" || !remotePayment.Paid
            || remotePayment.Currency != "RUB" || remotePayment.Amount != localPayment.Amount
            || remotePayment.RefundedAmount > 0 && remotePayment.RefundedCurrency != "RUB")
        {
            throw new InvalidOperationException($"Оплата {paymentId} не подтверждена YooKassa.");
        }

        if (!await yooKassa.MarkPaymentPaidAsync(paymentId))
        {
            throw new InvalidOperationException($"Не удалось зафиксировать оплату {paymentId}.");
        }

        if (remotePayment.RefundedAmount >= localPayment.Amount)
        {
            return true;
        }

        SubscriptionResult result;
        if (TryParseWorkspaceOperation(localPayment.PaymentMethod, WorkspaceExtensionPrefix, out var extensionOrganizationId))
        {
            result = await organizationSubscriptions.ActivateExtendedSubscriptionAsync(
                localPayment.UserId, extensionOrganizationId, localPayment.PlanId, paymentId);
        }
        else if (TryParseWorkspaceOperation(localPayment.PaymentMethod, WorkspacePurchasePrefix, out var purchaseOrganizationId))
        {
            result = await organizationSubscriptions.ActivateNewSubscriptionAsync(
                localPayment.UserId, purchaseOrganizationId, localPayment.PlanId, paymentId);
        }
        else if (localPayment.PaymentMethod == "yookassa_extension")
        {
            result = await subscriptions.ActivateExtendedSubscriptionAsync(localPayment.UserId, localPayment.PlanId, paymentId);
        }
        else
        {
            result = await subscriptions.ActivateNewSubscriptionAsync(localPayment.UserId, localPayment.PlanId, paymentId);
        }

        if (!result.Success)
        {
            var latestPayment = await yooKassa.GetLocalPaymentInfoAsync(paymentId);
            if (latestPayment?.PaymentStatus == "failed")
            {
                return false;
            }

            throw new InvalidOperationException($"Не удалось активировать подписку для платежа {paymentId}: {result.Error}");
        }

        return true;
    }

    /// <summary> Выполняет консультативную блокировку базы данных по ключу. </summary>
    private async Task ExecuteAdvisoryLockAsync(string functionName, long lockKey)
    {
        var connection = opencrmContext!.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {functionName}(@lock_key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary> Разбирает тип операции рабочего пространства с заданным префиксом. </summary>
    private static bool TryParseWorkspaceOperation(string paymentMethod, string prefix, out Guid organizationId)
    {
        organizationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(paymentMethod) || !paymentMethod.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Guid.TryParse(paymentMethod[prefix.Length..], out organizationId);
    }
}
