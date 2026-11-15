using System.Text.RegularExpressions;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила обработки и проверки текстов обслуживания. </summary>
internal static class MaintenanceTextRules
{
    /// <summary> Усекает текст до максимальной длины. </summary>
    internal static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    /// <summary> Нормализует необязательный текст, превращая пустой в null. </summary>
    internal static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            normalized = Regex.Replace(normalized, @"\s+", " ");
            MaintenanceTextRules.ValidateSafeText("Текстовое поле", normalized);
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary> Проверяет текст на недопустимое содержимое. </summary>
    internal static void ValidateSafeText(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Contains('<') || value.Contains('>') || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{fieldName}: HTML-теги и скрипты не допускаются.");
        }

        if (value.Length > 1000)
        {
            throw new InvalidOperationException($"{fieldName}: максимум 1000 символов.");
        }
    }
}
