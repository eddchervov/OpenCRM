namespace OpenCRM.Modules.Billing.Contracts;

/// <summary> Признак коммерческого режима платформы. </summary>
/// <remarks>
/// Базовая поставка работает без обязательной подписки: при выключенном
/// коммерческом режиме ограничения по тарифу не применяются.
/// </remarks>
public interface IPlatformCommercialPolicy
{
    /// <summary> Коммерческие тарифы, квоты и промокоды включены. </summary>
    bool CommercialEnabled { get; }
}
