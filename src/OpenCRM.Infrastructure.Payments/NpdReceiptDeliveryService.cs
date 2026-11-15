using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Payments;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис постановки в очередь и доставки чеков НПД. </summary>
public sealed class NpdReceiptDeliveryService(
    OpenCRMContext context,
    IEmailNotificationService emailNotifications,
    IFileStorageService fileStorage,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    Helpers.ProjectBrand? brand = null) : INpdReceiptDeliveryService
{
    /// <summary> Протектор для защиты токенов скачивания. </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("npd-receipt-download-v1");

    /// <summary> Ставит в очередь доставку чека НПД для платежа. </summary>
    public Task<AdminBillingPaymentOperationDto> QueueAsync(int paymentId)
    {
        var executionStrategy = context.Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();

            return await this.QueueCoreAsync(paymentId);
        });
    }

    /// <summary> Основная логика постановки чека НПД в очередь. </summary>
    private async Task<AdminBillingPaymentOperationDto> QueueCoreAsync(int paymentId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var payment = await context.SubscriptionPayments
            .Include(item => item.User)
            .Include(item => item.SubscriptionPlan)
            .FirstOrDefaultAsync(item => item.Id == paymentId);

        if (payment == null)
        {
            return new AdminBillingPaymentOperationDto { NotFound = true, ErrorMessage = "Платеж не найден." };
        }

        if (payment.PaidAt == null || payment.PaymentStatus is not ("completed" or "refunded" or "failed"))
        {
            return Invalid("Отправить чек можно только после подтверждения оплаты.");
        }

        if (string.IsNullOrWhiteSpace(payment.User.Email))
        {
            return Invalid("У покупателя нет адреса электронной почты.");
        }

        var source = payment.NpdReceiptFilePath ?? payment.NpdReceiptUrl;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Invalid("Сначала приложите файл чека или ссылку ФНС.");
        }

        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        if (payment.NpdReceiptEmailMessageId.HasValue)
        {
            var previous = await context.EmailMessages.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == payment.NpdReceiptEmailMessageId.Value);
            var hasSameSource = payment.NpdReceiptDeliverySourceHash == sourceHash;
            var hasSameRecipient = string.Equals(previous?.ToEmail, payment.User.Email, StringComparison.OrdinalIgnoreCase);
            var hasFailed = previous?.StatusCode is EmailMessageStatusCodes.Failed
                or EmailMessageStatusCodes.HardBounced or EmailMessageStatusCodes.SoftBounced
                or EmailMessageStatusCodes.Spam or EmailMessageStatusCodes.SpamBlock
                or EmailMessageStatusCodes.Unsubscribed;
            var linkExpired = previous?.CreatedAt <= DateTime.UtcNow.AddDays(-29);
            if (hasSameSource && hasSameRecipient && !hasFailed && !linkExpired)
            {
                return new AdminBillingPaymentOperationDto
                {
                    Succeeded = true,
                    Message = "Эта версия чека уже отправлялась на этот адрес. Для повторной отправки замените файл или ссылку."
                };
            }
        }

        var receiptUrl = payment.NpdReceiptFilePath == null
            ? payment.NpdReceiptUrl!
            : CreateDownloadUrl(payment.Id, payment.NpdReceiptFilePath);
        var subject = $"Чек НПД за подписку {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}";
        var html = $"<p>Здравствуйте!</p><p>Чек за оплату тарифа «{HtmlEncoder.Default.Encode(payment.SubscriptionPlan.Name)}» "
            + $"на сумму {payment.Amount:0.00} ₽: <a href=\"{HtmlEncoder.Default.Encode(receiptUrl)}\">открыть чек</a>.</p>";
        var attempt = payment.NpdReceiptDeliveryAttempt + 1;
        var key = $"npd-receipt:{paymentId}:{attempt}";

        await emailNotifications.QueueAsync(payment.User.Email, subject, html, key);
        var message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        payment.NpdReceiptDeliveryAttempt = attempt;
        payment.NpdReceiptDeliverySourceHash = sourceHash;
        payment.NpdReceiptEmailMessageId = message.Id;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new AdminBillingPaymentOperationDto
        {
            Succeeded = true,
            Message = $"Чек поставлен в очередь отправки на {payment.User.Email}."
        };
    }

    /// <summary> Возвращает файл чека НПД по защищённому токену. </summary>
    public async Task<AdminNpdReceiptFileDto?> GetFileAsync(string token)
    {
        string payload;
        try
        {
            payload = this._protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('\n', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var paymentId)
            || !long.TryParse(parts[1], out var expiresAtTicks)
            || expiresAtTicks < DateTime.UtcNow.Ticks)
        {
            return null;
        }

        var payment = await context.SubscriptionPayments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == paymentId);
        if (payment?.NpdReceiptFilePath == null || payment.NpdReceiptFilePath != parts[2])
        {
            return null;
        }

        return new AdminNpdReceiptFileDto
        {
            Content = await fileStorage.GetFileAsync(payment.NpdReceiptFilePath),
            FileName = payment.NpdReceiptFileName ?? "receipt",
            ContentType = payment.NpdReceiptFileContentType ?? "application/octet-stream"
        };
    }

    /// <summary> Формирует URL скачивания файла чека. </summary>
    private string CreateDownloadUrl(int paymentId, string path)
    {
        var baseUrl = configuration["AppSettings:BaseUrl"]?.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Для отправки чека нужен корректный HTTPS AppSettings:BaseUrl.");
        }

        var expiresAt = DateTime.UtcNow.AddDays(30);
        var token = this._protector.Protect($"{paymentId}\n{expiresAt.Ticks}\n{path}");

        return $"{baseUrl}/api/npd-receipts/download?token={Uri.EscapeDataString(token)}";
    }

    /// <summary> Формирует результат операции с сообщением об ошибке. </summary>
    private static AdminBillingPaymentOperationDto Invalid(string message) => new()
    {
        ErrorMessage = message
    };
}
