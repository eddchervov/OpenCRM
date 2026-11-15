namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Результат проверки и принятия административного поручения. </summary>
public sealed record AdminPaymentServiceActionResult(
    AdminPaymentServiceAction? Operation,
    int StatusCode,
    string? Message = null,
    string? Code = null);
