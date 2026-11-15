namespace OpenCRM.Modules.Support.Personal;

/// <summary> Правила нормализации контактных данных поддержки. </summary>
internal static class SupportContactRules
{
    /// <summary> Нормализует строку телефонного номера. </summary>
    internal static string? NormalizePhone(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized == null)
        {
            return null;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length is < 7 or > 15 || normalized.Any(SupportContactRules.IsInvalidPhoneCharacter))
        {
            throw new InvalidOperationException("Укажите корректный номер телефона или оставьте поле пустым.");
        }

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary> Проверяет недопустимый символ в номере телефона. </summary>
    private static bool IsInvalidPhoneCharacter(char value) =>
        !char.IsDigit(value) && value is not '+' and not '(' and not ')' and not '-' and not ' ' and not '.';
}
