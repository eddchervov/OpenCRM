namespace Notifications.Infrastructure;

/// <summary>
/// Управляет общим режимом работы адаптеров доставки.
/// </summary>
public sealed class DeliveryOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "Delivery";

    /// <summary>Включает безопасную имитацию отправки без обращения к внешним провайдерам.</summary>
    public bool LogOnly { get; init; }
}
