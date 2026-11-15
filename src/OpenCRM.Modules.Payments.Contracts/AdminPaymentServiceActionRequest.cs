namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Поручение с ключом, который сохраняется при повторе после сетевого сбоя. </summary>
public sealed record AdminPaymentServiceActionRequest(
    Guid OperationId,
    string Action,
    string Reason,
    decimal? Amount,
    string StateToken);
