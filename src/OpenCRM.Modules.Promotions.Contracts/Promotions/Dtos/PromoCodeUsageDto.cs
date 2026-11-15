namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> Данные об использовании промокода. </summary>
public class PromoCodeUsageDto
{
    /// <summary> Идентификатор записи об использовании. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор пользователя, применившего промокод. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Электронная почта пользователя. </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary> Идентификатор организации, если промокод применён к ней. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Название организации, если применимо. </summary>
    public string? OrganizationName { get; set; }

    /// <summary> Время погашения промокода. </summary>
    public DateTime RedeemedAt { get; set; }

    /// <summary> Время назначения промокода. </summary>
    public DateTime? AssignedAt { get; set; }

    /// <summary> Время применения демо-периода. </summary>
    public DateTime? DemoAppliedAt { get; set; }

    /// <summary> Статус применения промокода. </summary>
    public string? ApplicationStatus { get; set; }

    /// <summary> Дата окончания действия скидки. </summary>
    public DateTime? DiscountValidUntil { get; set; }

    /// <summary> Идентификатор платежа по подписке, если связан. </summary>
    public int? SubscriptionPaymentId { get; set; }

    /// <summary> Исходный код промокода. </summary>
    public string SourceCode { get; set; } = "registration";
}
