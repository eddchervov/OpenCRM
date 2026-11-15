namespace OpenCRM.Admin.Api.Models;

/// <summary> Доступные действия и журнал ручной обработки платежа сервиса. </summary>
public sealed class AdminBillingPaymentServiceActions
{
    public string StateToken { get; init; } = string.Empty;
    public bool CanCheck { get; init; }
    public bool CanRefund { get; init; }
    public bool CanRetryAccess { get; init; }
    public bool CanResolveReview { get; init; }
    public decimal RefundableAmount { get; init; }
    public string CheckHint { get; init; } = string.Empty;
    public string RefundHint { get; init; } = string.Empty;
    public string RetryAccessHint { get; init; } = string.Empty;
    public string ResolveReviewHint { get; init; } = string.Empty;
    public bool ReviewResolved { get; init; }
    public IReadOnlyList<AdminBillingPaymentServiceActionOperation> Operations { get; init; } = [];
}

/// <summary> Запись команды администратора по обслуживанию платежа сервиса. </summary>
public sealed class AdminBillingPaymentServiceActionOperation
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary> Время действия в часовом поясе устройства администратора. </summary>
    public DateTime LocalCreatedAt => this.CreatedAt.ToLocalTime();
    public DateTime? CompletedAt { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public string ActionName => this.Action switch
    {
        "check" => "Проверка платежа",
        "refund" => "Возврат денег",
        "retry-access" => "Повтор выдачи доступа",
        "resolve-review" => "Решение по разбору",
        _ => "Действие по платежу"
    };

    public string StatusName => this.Status switch
    {
        "queued" or "requested" => "В очереди",
        "needs_attention" => "Требует внимания",
        "succeeded" => "Выполнено",
        "failed" => "Ошибка",
        _ => this.Status
    };
}

/// <summary> Команда административного действия с проверкой актуального состояния покупки. </summary>
public sealed record AdminBillingPaymentServiceActionRequest(
    Guid OperationId,
    string Action,
    string Reason,
    decimal? Amount,
    string StateToken);
