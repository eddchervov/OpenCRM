namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> Данные выгоды, полученной при погашении промокода. </summary>
public class PromoCodeBenefitDto
{
    /// <summary> Идентификатор погашения промокода. </summary>
    public long RedemptionId { get; set; }

    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Код промокода. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Длительность демо-периода в днях, если задана. </summary>
    public int? DemoDurationDays { get; set; }
}
