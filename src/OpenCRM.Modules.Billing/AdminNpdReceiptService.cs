using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using System.Data;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис администрирования чеков НПД. </summary>
internal sealed class AdminNpdReceiptService(
    OpenCRMContext opencrmContext,
    IFileStorageService? fileStorageService = null) : IAdminNpdReceiptService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость файлового хранилища. </summary>
    private readonly IFileStorageService? _fileStorageService = fileStorageService;

    /// <summary> Отмечает выдачу чека НПД для платежа. </summary>
    public async Task<AdminBillingPaymentOperationDto> MarkNpdReceiptAsync(
        int paymentId,
        bool issued,
        bool expectedIssued,
        string? reason,
        string adminUserId)
    {
        var normalizedReason = reason?.Trim();
        if (normalizedReason?.Length > 500)
        {
            return AdminBillingOperationResults.PaymentInvalid("Причина не должна превышать 500 символов.");
        }

        if (!issued && string.IsNullOrWhiteSpace(normalizedReason))
        {
            return AdminBillingOperationResults.PaymentInvalid("Укажите причину снятия отметки о чеке.");
        }

        var executionStrategy = this._opencrmContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            this._opencrmContext.ChangeTracker.Clear();
            AdminBillingPaymentOperationDto result;
            await using (var transaction = await this._opencrmContext.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                result = await this.MarkNpdReceiptCoreAsync(
                    paymentId, issued, expectedIssued, normalizedReason, adminUserId, transaction);
            }

            return result;
        });
    }

    /// <summary> Основная логика отметки чека НПД в транзакции. </summary>
    private async Task<AdminBillingPaymentOperationDto> MarkNpdReceiptCoreAsync(
        int paymentId,
        bool issued,
        bool expectedIssued,
        string? normalizedReason,
        string adminUserId,
        IDbContextTransaction transaction)
    {
        var payment = await this._opencrmContext.SubscriptionPayments
            .FirstOrDefaultAsync(item => item.Id == paymentId);

        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        if (payment.PaidAt == null || payment.PaymentStatus is not ("completed" or "refunded" or "failed"))
        {
            return AdminBillingOperationResults.PaymentInvalid("Чек можно отметить только у подтвержденной оплаты.");
        }

        if ((payment.NpdReceiptIssuedAt != null) != expectedIssued)
        {
            return AdminBillingOperationResults.PaymentInvalid("Статус чека уже изменился. Обновите список.");
        }

        if (issued == expectedIssued)
        {
            return new AdminBillingPaymentOperationDto { Succeeded = true, Message = "Статус чека не изменился." };
        }

        var now = DateTime.UtcNow;
        payment.NpdReceiptIssuedAt = issued ? now : null;
        payment.NpdReceiptMarkedByUserId = adminUserId;
        payment.NpdReceiptReason = normalizedReason ?? "Чек НПД выдан и передан покупателю.";
        this._opencrmContext.AdminNpdReceiptAudits.Add(new AdminNpdReceiptAudit
        {
            SubscriptionPaymentId = paymentId,
            Issued = issued,
            ChangedAt = now,
            ChangedByUserId = adminUserId,
            Reason = payment.NpdReceiptReason
        });
        await this._opencrmContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return new AdminBillingPaymentOperationDto
        {
            Succeeded = true,
            Message = issued ? "Чек отмечен как выданный и переданный." : "Отметка о чеке снята."
        };
    }

    /// <summary> Обновляет детали чека НПД. </summary>
    public async Task<AdminBillingPaymentOperationDto> UpdateNpdReceiptDetailsAsync(
        int paymentId,
        string? receiptUrl,
        string? comment)
    {
        var normalizedUrl = receiptUrl?.Trim();
        var normalizedComment = comment?.Trim();
        if (normalizedUrl?.Length > 2048 || normalizedComment?.Length > 1000)
        {
            return AdminBillingOperationResults.PaymentInvalid("Ссылка или комментарий слишком длинные.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedUrl) &&
            (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme != Uri.UriSchemeHttps ||
             !(string.Equals(uri.Host, "nalog.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".nalog.ru", StringComparison.OrdinalIgnoreCase))))
        {
            return AdminBillingOperationResults.PaymentInvalid("Укажите HTTPS-ссылку на чек с сайта ФНС.");
        }

        var payment = await this._opencrmContext.SubscriptionPayments.FirstOrDefaultAsync(item => item.Id == paymentId);
        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        if (payment.PaidAt == null || payment.PaymentStatus is not ("completed" or "refunded" or "failed"))
        {
            return AdminBillingOperationResults.PaymentInvalid("Чек можно прикрепить только к подтвержденной оплате.");
        }

        payment.NpdReceiptUrl = string.IsNullOrWhiteSpace(normalizedUrl) ? null : normalizedUrl;
        payment.NpdReceiptComment = string.IsNullOrWhiteSpace(normalizedComment) ? null : normalizedComment;
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingPaymentOperationDto { Succeeded = true, Message = "Данные чека сохранены." };
    }

    /// <summary> Загружает файл чека НПД в хранилище. </summary>
    public async Task<AdminBillingPaymentOperationDto> UploadNpdReceiptFileAsync(
        int paymentId,
        Stream content,
        string fileName,
        string contentType)
    {
        if (this._fileStorageService == null)
        {
            return AdminBillingOperationResults.PaymentInvalid("Хранилище файлов недоступно.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var expectedContentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => null
        };
        if (expectedContentType == null || !string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return AdminBillingOperationResults.PaymentInvalid("Разрешены файлы PDF, PNG и JPG.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        if (buffer.Length is 0 or > 5_242_880)
        {
            return AdminBillingOperationResults.PaymentInvalid("Размер чека должен быть от 1 байта до 5 МБ.");
        }

        var bytes = buffer.GetBuffer();
        var validSignature = extension switch
        {
            ".pdf" => buffer.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".png" => buffer.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            _ => buffer.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
        };
        if (!validSignature)
        {
            return AdminBillingOperationResults.PaymentInvalid("Содержимое файла не соответствует его формату.");
        }

        var payment = await this._opencrmContext.SubscriptionPayments.FirstOrDefaultAsync(item => item.Id == paymentId);
        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        if (payment.PaidAt == null || payment.PaymentStatus is not ("completed" or "refunded" or "failed"))
        {
            return AdminBillingOperationResults.PaymentInvalid("Чек можно прикрепить только к подтвержденной оплате.");
        }

        var path = $"admin/npd-receipts/{paymentId}/{Guid.NewGuid():N}{extension}";
        buffer.Position = 0;
        var savedPath = await this._fileStorageService.SaveFileAsync(path, buffer);
        payment.NpdReceiptFilePath = savedPath;
        payment.NpdReceiptFileName = Path.GetFileName(fileName)[..Math.Min(Path.GetFileName(fileName).Length, 200)];
        payment.NpdReceiptFileContentType = expectedContentType;
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingPaymentOperationDto { Succeeded = true, Message = "Файл чека прикреплен." };
    }

    /// <summary> Возвращает файл чека НПД. </summary>
    public async Task<AdminNpdReceiptFileDto?> GetNpdReceiptFileAsync(int paymentId)
    {
        var payment = await this._opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == paymentId);
        if (payment?.NpdReceiptFilePath == null || this._fileStorageService == null)
        {
            return null;
        }

        return new AdminNpdReceiptFileDto
        {
            Content = await this._fileStorageService.GetFileAsync(payment.NpdReceiptFilePath),
            FileName = payment.NpdReceiptFileName ?? "receipt",
            ContentType = payment.NpdReceiptFileContentType ?? "application/octet-stream"
        };
    }

    /// <summary> Удаляет файл чека НПД. </summary>
    public async Task<AdminBillingPaymentOperationDto> DeleteNpdReceiptFileAsync(int paymentId)
    {
        var payment = await this._opencrmContext.SubscriptionPayments.FirstOrDefaultAsync(item => item.Id == paymentId);
        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        payment.NpdReceiptFilePath = null;
        payment.NpdReceiptFileName = null;
        payment.NpdReceiptFileContentType = null;
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingPaymentOperationDto { Succeeded = true, Message = "Файл чека откреплен." };
    }
}
