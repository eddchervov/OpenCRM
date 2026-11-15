using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Payments.Contracts;

/// <summary>Признаки намерения покупки, проверяемые при каждом повторе.</summary>
/// <remarks>Явный ReturnUrl проверяется на совпадение; null при повторе сохраняет прежний адрес.</remarks>
public sealed record PaymentCreationIntent(
    Guid OperationId,
    string UserId,
    string OperationType,
    int? SubscriptionPlanId,
    PaymentBuyerDto? Buyer,
    string? ReturnUrl = null);
