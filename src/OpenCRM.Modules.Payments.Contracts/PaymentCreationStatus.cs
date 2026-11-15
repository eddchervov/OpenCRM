namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Статус создания платежа. </summary>
public enum PaymentCreationStatus
{
    /// <summary> Требуется оплата. </summary>
    PaymentRequired,

    /// <summary> Оплата завершена. </summary>
    Completed,

    /// <summary> Платёж выполняется. </summary>
    InProgress,

    /// <summary> Конфликт при создании платежа. </summary>
    Conflict,

    /// <summary> Платёж отклонён. </summary>
    Rejected,

    /// <summary> Платёж отменён. </summary>
    Canceled,

    /// <summary> Требуется ручная проверка. </summary>
    ManualReview,

    /// <summary> Некорректные данные платежа. </summary>
    Invalid,

    /// <summary> Платёж возвращён. </summary>
    Refunded
}
