namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Безопасный результат административной операции без секретов провайдера. </summary>
public sealed record AdminPaymentServiceAction(
    Guid Id,
    string Action,
    string Status,
    string? Code,
    string Message,
    decimal? Amount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string Actor,
    string Reason);
