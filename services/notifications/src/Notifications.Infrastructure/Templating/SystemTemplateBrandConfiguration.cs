using System.Text;
using Microsoft.Extensions.Configuration;

namespace Notifications.Infrastructure.Templating;

/// <summary>Переносит общее название проекта в настройки писем сервиса уведомлений.</summary>
public static class SystemTemplateBrandConfiguration
{
    /// <summary>
    /// Задаёт имя бренда профилям системных шаблонов и подписи отправителя по общему названию проекта.
    /// Значения записываются как значения по умолчанию: явная настройка окружения, командной строки
    /// или приложения имеет приоритет, поэтому бренд не затирает переопределения.
    /// </summary>
    public static void ApplyProjectBrand(IConfigurationRoot configuration, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var name = projectName.Trim();
        var mark = CreateMark(name);
        foreach (var application in configuration.GetSection($"{SystemTemplateOptions.SectionName}:Applications").GetChildren())
        {
            ApplyDefault(configuration, $"{application.Path}:BrandName", name);
            ApplyDefault(configuration, $"{application.Path}:BrandMark", mark);
        }

        ApplyDefault(configuration, "Email:FromName", name);
        ApplyDefault(configuration, "Smtp:FromName", name);
    }

    /// <summary>
    /// Записывает значение только тогда, когда настройка не задана или пуста: пустая подпись отправителя
    /// или эмблема недопустимы. Пустая метка не записывается вовсе.
    /// </summary>
    private static void ApplyDefault(IConfigurationRoot configuration, string key, string value)
    {
        if (value.Length != 0 && string.IsNullOrWhiteSpace(configuration[key]))
        {
            configuration[key] = value;
        }
    }

    /// <summary> Метка письма — первая буква названия; если буквы нет, метка не выводится. </summary>
    private static string CreateMark(string projectName)
    {
        // Перебор по символам целиком: пара суррогатов не считается буквой при посимвольной проверке.
        var letter = projectName.EnumerateRunes().FirstOrDefault(Rune.IsLetter);

        return letter.Value == 0 ? string.Empty : Rune.ToUpperInvariant(letter).ToString();
    }
}
