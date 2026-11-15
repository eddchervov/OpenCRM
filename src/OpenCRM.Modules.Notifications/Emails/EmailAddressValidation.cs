using MimeKit;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Утилиты строгой проверки адресов email. </summary>
public static class EmailAddressValidation
{
    /// <summary> Сообщение об ошибке по умолчанию при неверном адресе. </summary>
    public const string DefaultErrorMessage = "Email содержит недопустимые символы. Проверьте адрес и попробуйте ещё раз.";

    /// <summary> Проверяет адрес email и возвращает сообщение об ошибке. </summary>
    public static bool TryValidate(string? email, out string errorMessage)
    {
        errorMessage = DefaultErrorMessage;
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmedEmail = email.Trim();
        var firstAtIndex = trimmedEmail.IndexOf('@');
        var lastAtIndex = trimmedEmail.LastIndexOf('@');
        if (firstAtIndex != lastAtIndex)
        {
            errorMessage = "Email должен содержать только один символ @. Удалите лишний @.";
            return false;
        }

        if (firstAtIndex <= 0 || lastAtIndex == trimmedEmail.Length - 1)
        {
            return false;
        }

        var localPart = trimmedEmail[..lastAtIndex];
        var domainPart = trimmedEmail[(lastAtIndex + 1)..];

        if (localPart.EndsWith('.'))
        {
            var suggestedEmail = $"{localPart.TrimEnd('.')}@{domainPart}";
            errorMessage = $"Перед @ не должно быть точки. Возможно, вы имели в виду «{suggestedEmail}».";
            return false;
        }

        if (localPart.StartsWith('.'))
        {
            errorMessage = "Email не может начинаться с точки. Проверьте адрес и попробуйте ещё раз.";
            return false;
        }

        if (localPart.Contains("..", StringComparison.Ordinal))
        {
            errorMessage = "В email не должно быть двух точек подряд. Проверьте адрес и попробуйте ещё раз.";
            return false;
        }

        if (!MailboxAddress.TryParse(trimmedEmail, out var mailbox)
            || !string.IsNullOrEmpty(mailbox.Name)
            || !string.Equals(mailbox.Address, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
