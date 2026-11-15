namespace Notifications.Infrastructure.Templating;

/// <summary>Настройки доступа приложений к общим системным шаблонам.</summary>
public sealed class SystemTemplateOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "SystemTemplates";

    /// <summary>Профили приложений; отсутствие профиля запрещает системные письма.</summary>
    public Dictionary<string, SystemTemplateApplicationOptions> Applications { get; init; } = new(StringComparer.Ordinal);
}
