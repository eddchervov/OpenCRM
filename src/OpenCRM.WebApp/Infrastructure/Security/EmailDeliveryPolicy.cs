using Microsoft.Extensions.Configuration;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary>Определяет доступность функций, требующих отправки email.</summary>
public static class EmailDeliveryPolicy
{
    /// <summary>Возвращает <see langword="false"/>, если доставка отключена или работает в памяти.</summary>
    public static bool IsAvailable(IConfiguration configuration)
    {
        var transport = configuration["Notifications:Transport"];

        return !string.Equals(transport, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase);
    }
}