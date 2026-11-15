namespace OpenCRM.Domain;

/// <summary> Активация промокода. </summary>
public class PromoCodeRedemption
{
    /// <summary> Идентификатор активации промокода. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Активированный промокод. </summary>
    public PromoCode PromoCode { get; set; } = null!;

    /// <summary> Идентификатор пользователя, активировавшего промокод. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Организация, связанная с активацией промокода. </summary>
    public Organization? Organization { get; set; }

    /// <summary> Дата и время активации промокода. </summary>
    public DateTime RedeemedAt { get; set; }

    /// <summary> Дата и время назначения промокода. </summary>
    public DateTime? AssignedAt { get; set; }

    /// <summary> Дата и времени применения демо-периода по промокоду. </summary>
    public DateTime? DemoAppliedAt { get; set; }
}

