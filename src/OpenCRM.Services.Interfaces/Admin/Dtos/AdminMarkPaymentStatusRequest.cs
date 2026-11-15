namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на изменение статуса платежа с ожидаемым текущим статусом. </summary>
public sealed record AdminMarkPaymentStatusRequest(
    string? Status,
    string? ExpectedCurrentStatus,
    string? Reason);
