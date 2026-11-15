namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> Настройки подключения к ЮKassa. </summary>
public class YooKassaConfig
{
    /// <summary> Идентификатор магазина в ЮKassa. </summary>
    public string ShopId { get; set; } = string.Empty;

    /// <summary> Секретный ключ магазина. </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary> URL возврата после оплаты. </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary> Признак тестового режима. </summary>
    public bool TestMode { get; set; } = true;
}
