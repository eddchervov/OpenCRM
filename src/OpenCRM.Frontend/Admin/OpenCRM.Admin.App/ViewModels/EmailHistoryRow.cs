using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка истории email с датами и состоянием доставки. </summary>
public sealed class EmailHistoryRow(EmailMessageListItem message)
{
    public int Id => message.Id;
    public string Recipient => message.ToEmail;
    public string Subject => message.Subject;
    public string CreatedAt => UserDisplayFormatting.DateTime(message.CreatedAt);
    public string SentAt => UserDisplayFormatting.DateTime(message.SentAt);
    public string Status => EmailHistoryRow.StatusName(message.StatusCode);
    public string Activity => $"Открытия: {message.OpenCount} · Переходы: {message.ClickCount} · События: {message.EventCount}";
    public string Error => message.ErrorMessage ?? string.Empty;

    public static string StatusName(string? status) => status switch
    {
        "pending" => "Ожидает отправки", "queued" => "В очереди", "retrying" => "Повторная отправка",
        "sent" or "accepted" => "Отправлено", "delivered" => "Доставлено",
        "opened" => "Открыто", "clicked" => "Переход по ссылке", "failed" => "Ошибка",
        "hard_bounced" => "Адрес недоступен", "soft_bounced" => "Временная ошибка",
        "spam" => "Спам", "spam_block" => "Блокировка спама", "unsubscribed" => "Отписка",
        "simulated" => "Отправка отключена", _ => status ?? "—"
    };
}
