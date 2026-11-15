using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Черновик данных для создания платежа по подписке. </summary>
public sealed record PaymentCreationDraft(
    PaymentCreationIntent Intent,
    int SubscriptionPlanId,
    decimal Amount,
    string Description,
    PaymentPromotionDto? Promotion = null);
