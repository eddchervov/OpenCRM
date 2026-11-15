using OpenCRM.WebApp.Models.Home;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Проверка формы обратной связи на спам. </summary>
public static class ContactSubmissionGuard
{
    /// <summary> Возвращает список ошибок валидации заявки в поддержку. </summary>
    public static IReadOnlyList<ContactSubmissionError> Validate(ContactSupportViewModel model, DateTime utcNow)
    {
        var errors = new List<ContactSubmissionError>();

        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            errors.Add(new("honeypot", string.Empty, "Сообщение не принято."));
        }

        if (model.FormRenderedAtTicks <= 0 || model.FormRenderedAtTicks > utcNow.Ticks
            || utcNow.Ticks - model.FormRenderedAtTicks < TimeSpan.FromSeconds(3).Ticks)
        {
            errors.Add(new("timing", string.Empty, "Отправьте сообщение еще раз через пару секунд."));
        }

        if (CountLinks(model.Text) > 3)
        {
            errors.Add(new("links", nameof(model.Text), "В сообщении слишком много ссылок."));
        }

        return errors;
    }

    /// <summary> Считает количество ссылок в строке. </summary>
    private static int CountLinks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return CountOccurrences(value, "http://")
            + CountOccurrences(value, "https://")
            + CountOccurrences(value, "www.");
    }

    /// <summary> Считает количество вхождений шаблона в строку. </summary>
    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}

/// <summary> Ошибка валидации формы обратной связи. </summary>
public sealed record ContactSubmissionError(string Code, string Field, string Message);
