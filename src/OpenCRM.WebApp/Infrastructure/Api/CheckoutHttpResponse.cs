using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Payments.Contracts;

namespace OpenCRM.WebApp.Infrastructure.Api;

/// <summary> Формирование HTTP-ответов для сценариев оплаты. </summary>
internal static class CheckoutHttpResponse
{
    /// <summary> Пытается получить идентификатор операции оплаты из контекста запроса. </summary>
    public static bool TryGetOperationId(HttpContext context, out Guid operationId)
    {
        var header = context.Request.Headers["Idempotency-Key"];
        if (header.Count == 0)
        {
            operationId = Guid.NewGuid();
        }
        else if (header.Count != 1 ||
            !(Guid.TryParseExact(header[0], "N", out operationId) || Guid.TryParseExact(header[0], "D", out operationId)) ||
            operationId == Guid.Empty)
        {
            operationId = Guid.Empty;

            return false;
        }

        context.Response.Headers["Idempotency-Key"] = operationId.ToString("D");

        return true;
    }

    /// <summary> Создаёт ObjectResult по результату создания платежа. </summary>
    public static ObjectResult Create(PaymentCreationResult result, Guid operationId)
    {
        var isPendingPurchase = result.Status == PaymentCreationStatus.InProgress && result.PurchaseId.HasValue;
        var success = isPendingPurchase || result.Status is PaymentCreationStatus.PaymentRequired or PaymentCreationStatus.Completed;
        var message = result.Status == PaymentCreationStatus.ManualReview
            ? "Оплата требует проверки. Не оплачивайте повторно, обратитесь в поддержку."
            : isPendingPurchase
            ? result.Message ?? "Готовим оплату."
            : result.Message ?? (result.Status == PaymentCreationStatus.PaymentRequired
            ? "Переходим к оплате."
            : result.Status == PaymentCreationStatus.Completed ? "Оплата обработана." : "Не удалось подготовить оплату.");
        var statusCode = result.Status switch
        {
            PaymentCreationStatus.PaymentRequired or PaymentCreationStatus.Completed => StatusCodes.Status200OK,
            PaymentCreationStatus.InProgress when isPendingPurchase => StatusCodes.Status202Accepted,
            PaymentCreationStatus.InProgress => StatusCodes.Status503ServiceUnavailable,
            PaymentCreationStatus.Conflict or PaymentCreationStatus.ManualReview => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return new ObjectResult(new
        {
            Success = success,
            Error = success ? null : message,
            Message = message,
            result.PaymentId,
            result.PaymentUrl,
            result.PurchaseId,
            RequiresPayment = result.Status == PaymentCreationStatus.PaymentRequired,
            Code = result.Status.ToString(),
            OperationId = operationId
        }) { StatusCode = statusCode };
    }

    /// <summary> Создаёт ответ с состоянием точно указанной покупки подписки. </summary>
    public static ObjectResult CreatePurchaseState(PaymentPurchaseState state, Guid purchaseId)
    {
        var result = state.Result;
        var isSuccess = result.Status is PaymentCreationStatus.PaymentRequired or PaymentCreationStatus.Completed;
        var message = result.Message ?? (state.IsPaid && result.Status != PaymentCreationStatus.Completed
            ? "Оплата получена, активируем подписку."
            : result.Status == PaymentCreationStatus.InProgress
                ? "Готовим оплату."
                : result.Status == PaymentCreationStatus.PaymentRequired
                    ? "Переходим к оплате."
                    : result.Status == PaymentCreationStatus.Completed ? "Оплата обработана." : "Не удалось подготовить оплату.");
        var statusCode = result.Status == PaymentCreationStatus.InProgress
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status200OK;

        return new ObjectResult(new
        {
            Success = isSuccess,
            Error = result.Status == PaymentCreationStatus.ManualReview
                ? "Оплата требует проверки. Обратитесь в поддержку."
                : isSuccess ? null : message,
            Message = message,
            result.PaymentId,
            result.PaymentUrl,
            PurchaseId = purchaseId,
            RequiresPayment = result.Status == PaymentCreationStatus.PaymentRequired,
            Code = result.Status.ToString(),
            OperationId = (Guid?)null,
            IsPaid = state.IsPaid,
            state.PrimaryActionUrl
        }) { StatusCode = statusCode };
    }
}
