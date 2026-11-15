namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> Результат проверки промокода. </summary>
public class PromoCodeValidationDto
{
    /// <summary> Признак того, что промокод валиден. </summary>
    public bool IsValid { get; set; }

    /// <summary> Название промокода, если доступно. </summary>
    public string? Name { get; set; }

    /// <summary> Сообщение об ошибке проверки. </summary>
    public string? ErrorMessage { get; set; }
}
