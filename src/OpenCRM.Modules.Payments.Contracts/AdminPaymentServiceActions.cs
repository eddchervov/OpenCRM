namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Доступные действия и последние административные операции по платежу. </summary>
public sealed record AdminPaymentServiceActions(
    string StateToken,
    bool CanCheck,
    bool CanRefund,
    bool CanRetryAccess,
    bool CanResolveReview,
    decimal RefundableAmount,
    string CheckHint,
    string RefundHint,
    string RetryAccessHint,
    string ResolveReviewHint,
    bool ReviewResolved,
    IReadOnlyList<AdminPaymentServiceAction> Operations);
