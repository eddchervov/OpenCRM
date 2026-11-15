using Microsoft.Extensions.Configuration;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Определяет, требуется ли подтверждать email перед доступом к приложению. </summary>
public static class EmailConfirmationPolicy
{
    /// <summary> Ключ настройки обязательного подтверждения email. </summary>
    public const string ConfigurationKey = "EmailConfirmation:Required";

    /// <summary> Возвращает настройку обязательного подтверждения; по умолчанию она выключена. </summary>
    public static bool IsRequired(IConfiguration configuration) =>
        configuration.GetValue<bool>(EmailConfirmationPolicy.ConfigurationKey);
}
