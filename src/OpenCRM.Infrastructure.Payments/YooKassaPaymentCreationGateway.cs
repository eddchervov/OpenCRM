using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Шлюз создания платежей через YooKassa. </summary>
internal sealed class YooKassaPaymentCreationGateway(
    OpenCRMContext context,
    PaymentCreationProcessor processor,
    IOptions<YooKassaConfig> options) : IPaymentCreationGateway
{
    /// <summary> Префикс типа операции покупки рабочего пространства. </summary>
    private const string WorkspacePurchasePrefix = "yookassa_workspace_purchase:";

    /// <summary> Префикс типа операции продления рабочего пространства. </summary>
    private const string WorkspaceExtensionPrefix = "yookassa_workspace_extension:";

    /// <summary> Сообщение о выполняющейся операции. </summary>
    private const string InProgressMessage = "Оплата ещё обрабатывается. Повторите попытку позже.";

    /// <summary> Сообщение о конфликте операций. </summary>
    private const string ConflictMessage = "Попытка оплаты уже используется с другими параметрами.";

    /// <summary> Сообщение о требовании ручного разбора. </summary>
    private const string ManualReviewMessage = "Оплата требует ручной проверки.";

    /// <summary> URL возврата по умолчанию. </summary>
    private readonly string _defaultReturnUrl = $"{options.Value.ReturnUrl}/payment/complete";

    /// <summary> Возобновляет операцию создания платежа по намерению. </summary>
    public async Task<PaymentCreationResult?> ResumeAsync(PaymentCreationIntent intent)
    {
        if (!this.TryNormalizeIntent(intent, out var normalizedIntent, out var buyer, out var returnUrl))
        {
            return YooKassaPaymentCreationGateway.InvalidResult();
        }

        if (!this.HasCleanUnitOfWork())
        {
            return YooKassaPaymentCreationGateway.InvalidResult();
        }

        var operation = await this.FindOperationAsync(normalizedIntent.OperationId);
        if (operation == null)
        {
            return null;
        }

        if (!this.MatchesIntent(operation, normalizedIntent, buyer, returnUrl))
        {
            return YooKassaPaymentCreationGateway.ConflictResult();
        }

        if (operation.Status is PaymentCreationOperationStatuses.Prepared or PaymentCreationOperationStatuses.Sending)
        {
            await processor.ResumeOperationAsync(operation.Id);
            operation = await this.FindOperationAsync(normalizedIntent.OperationId);
            if (operation == null || !this.MatchesIntent(operation, normalizedIntent, buyer, returnUrl))
            {
                return YooKassaPaymentCreationGateway.ConflictResult();
            }
        }

        return this.MapOperation(operation);
    }

    /// <summary> Создаёт платёж через YooKassa по черновику. </summary>
    public async Task<PaymentCreationResult> CreateAsync(PaymentCreationDraft draft)
    {
        if (draft == null || !this.TryNormalizeIntent(draft.Intent, out var intent, out var buyer, out var returnUrl)
            || intent.SubscriptionPlanId.HasValue && intent.SubscriptionPlanId.Value != draft.SubscriptionPlanId
            || draft.SubscriptionPlanId <= 0 || draft.Amount <= 0 || string.IsNullOrWhiteSpace(draft.Description))
        {
            return YooKassaPaymentCreationGateway.InvalidResult();
        }

        if (!this.HasCleanUnitOfWork())
        {
            return YooKassaPaymentCreationGateway.InvalidResult();
        }

        var existing = await this.FindOperationAsync(intent.OperationId);
        if (existing != null)
        {
            return await this.ResumeAsync(intent) ?? YooKassaPaymentCreationGateway.ConflictResult();
        }

        await processor.CreatePaymentAsync(
            intent.UserId,
            draft.SubscriptionPlanId,
            draft.Amount,
            draft.Description,
            intent.OperationType,
            buyer,
            returnUrl,
            draft.Promotion,
            intent.OperationId.ToString("N"),
            requestTimestamp: null);

        var createdOperation = await this.FindOperationAsync(intent.OperationId);
        if (createdOperation == null || !this.MatchesIntent(createdOperation, intent, buyer, returnUrl))
        {
            return YooKassaPaymentCreationGateway.ConflictResult();
        }

        return this.MapOperation(createdOperation);
    }

    /// <summary> Ищет операцию создания платежа по идентификатору. </summary>
    private async Task<PaymentCreationOperation?> FindOperationAsync(Guid operationId) =>
        await context.PaymentCreationOperations
            .AsNoTracking()
            .Include(operation => operation.SubscriptionPayment)
            .SingleOrDefaultAsync(operation => operation.IdempotenceKey == operationId.ToString("N"));

    /// <summary> Проверяет соответствие операции намерению создания платежа. </summary>
    private bool MatchesIntent(
        PaymentCreationOperation operation,
        PaymentCreationIntent intent,
        PaymentBuyerDto buyer,
        string? returnUrl) =>
        operation.UserId == intent.UserId
        && operation.OperationType == intent.OperationType
        && (!intent.SubscriptionPlanId.HasValue || operation.SubscriptionPlanId == intent.SubscriptionPlanId.Value)
        && operation.BuyerTypeCode == buyer.TypeCode
        && operation.BuyerName == buyer.Name
        && operation.BuyerInn == buyer.Inn
        && (intent.ReturnUrl == null || operation.ReturnUrl == returnUrl);

    /// <summary> Преобразует операцию в результат создания платежа. </summary>
    private PaymentCreationResult MapOperation(PaymentCreationOperation operation)
    {
        if (operation.Status == PaymentCreationOperationStatuses.Prepared
            || operation.Status == PaymentCreationOperationStatuses.Sending)
        {
            return new PaymentCreationResult(PaymentCreationStatus.InProgress, YooKassaPaymentCreationGateway.InProgressMessage);
        }

        if (operation.Status == PaymentCreationOperationStatuses.Rejected)
        {
            return new PaymentCreationResult(PaymentCreationStatus.Rejected, "Платеж отклонён платёжным сервисом.");
        }

        if (operation.Status == PaymentCreationOperationStatuses.ManualReview)
        {
            return new PaymentCreationResult(PaymentCreationStatus.ManualReview, YooKassaPaymentCreationGateway.ManualReviewMessage);
        }

        var payment = operation.SubscriptionPayment;
        if (operation.Status != PaymentCreationOperationStatuses.Created || payment == null)
        {
            return new PaymentCreationResult(PaymentCreationStatus.ManualReview, YooKassaPaymentCreationGateway.ManualReviewMessage);
        }

        if (payment.PaymentStatus == "refunded")
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.Refunded,
                "Оплата возвращена. Можно оформить новую покупку.",
                PaymentId: payment.ExternalPaymentId);
        }

        if (payment.PaymentStatus == "completed")
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.Completed,
                PaymentId: payment.ExternalPaymentId);
        }

        if (payment.PaymentStatus == "pending")
        {
            if (payment.PaidAt.HasValue || string.IsNullOrWhiteSpace(payment.PaymentLink))
            {
                return new PaymentCreationResult(
                    PaymentCreationStatus.InProgress,
                    YooKassaPaymentCreationGateway.InProgressMessage,
                    PaymentId: payment.ExternalPaymentId);
            }

            return new PaymentCreationResult(
                PaymentCreationStatus.PaymentRequired,
                PaymentId: payment.ExternalPaymentId,
                PaymentUrl: payment.PaymentLink);
        }

        if (payment.PaymentStatus is "waiting_for_capture" or "paid_waiting_confirmation" or "processing")
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.InProgress,
                YooKassaPaymentCreationGateway.InProgressMessage,
                PaymentId: payment.ExternalPaymentId);
        }

        if (payment.PaymentStatus == "canceled" && !payment.PaidAt.HasValue)
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.Canceled,
                "Оплата отменена.",
                PaymentId: payment.ExternalPaymentId);
        }

        if (payment.PaymentStatus == "failed" && payment.PaidAt.HasValue)
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.ManualReview,
                YooKassaPaymentCreationGateway.ManualReviewMessage,
                PaymentId: payment.ExternalPaymentId);
        }

        if (payment.PaymentStatus == "failed")
        {
            return new PaymentCreationResult(
                PaymentCreationStatus.Rejected,
                "Платеж отклонён платёжным сервисом.",
                PaymentId: payment.ExternalPaymentId);
        }

        return new PaymentCreationResult(
            PaymentCreationStatus.ManualReview,
            YooKassaPaymentCreationGateway.ManualReviewMessage,
            PaymentId: payment.ExternalPaymentId);
    }

    /// <summary> Нормализует намерение создания платежа. </summary>
    private bool TryNormalizeIntent(
        PaymentCreationIntent? intent,
        out PaymentCreationIntent normalizedIntent,
        out PaymentBuyerDto buyer,
        out string? returnUrl)
    {
        normalizedIntent = null!;
        buyer = null!;
        returnUrl = null;

        if (intent == null || intent.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(intent.UserId)
            || !YooKassaPaymentCreationGateway.IsSupportedOperationType(intent.OperationType)
            || YooKassaService.ValidateBuyer(intent.Buyer) != null)
        {
            return false;
        }

        buyer = YooKassaService.NormalizeBuyer(intent.Buyer!);
        returnUrl = this.NormalizeReturnUrl(intent.ReturnUrl);
        normalizedIntent = intent with
        {
            Buyer = buyer,
            ReturnUrl = string.IsNullOrWhiteSpace(intent.ReturnUrl) ? null : returnUrl
        };

        return true;
    }

    /// <summary> Нормализует URL возврата. </summary>
    private string NormalizeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) ? this._defaultReturnUrl : returnUrl;

    /// <summary> Проверяет отсутствие несохранённых изменений в контексте. </summary>
    private bool HasCleanUnitOfWork() =>
        System.Transactions.Transaction.Current == null
        && context.Database.CurrentTransaction == null
        && !context.ChangeTracker.HasChanges();

    /// <summary> Проверяет поддержку типа операции. </summary>
    private static bool IsSupportedOperationType(string? operationType)
    {
        if (string.IsNullOrWhiteSpace(operationType))
        {
            return false;
        }

        if (operationType is "yookassa_purchase" or "yookassa_extension")
        {
            return true;
        }

        return YooKassaPaymentCreationGateway.HasOrganizationScope(operationType, WorkspacePurchasePrefix)
            || YooKassaPaymentCreationGateway.HasOrganizationScope(operationType, WorkspaceExtensionPrefix);
    }

    /// <summary> Проверяет, относится ли тип операции к организации по префиксу. </summary>
    private static bool HasOrganizationScope(string operationType, string prefix) =>
        operationType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(operationType[prefix.Length..], out var organizationId)
        && organizationId != Guid.Empty;

    /// <summary> Формирует результат с ошибкой валидации. </summary>
    private static PaymentCreationResult InvalidResult() =>
        new(PaymentCreationStatus.Invalid, "Параметры оплаты некорректны.");

    /// <summary> Формирует результат конфликта. </summary>
    private static PaymentCreationResult ConflictResult() =>
        new(PaymentCreationStatus.Conflict, YooKassaPaymentCreationGateway.ConflictMessage);
}
