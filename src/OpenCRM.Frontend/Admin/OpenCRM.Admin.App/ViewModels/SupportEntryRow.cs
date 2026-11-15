using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Запись переписки с состоянием доставки ответа гостю. </summary>
public sealed class SupportEntryRow(AdminSupportMessageEntry entry)
{
    public string Author => entry.Kind switch
    {
        "internal_comment" or "internal" => "Внутренний комментарий",
        "status" => "Изменение статуса", _ => entry.IsStaff ? "Администратор" : "Отправитель"
    };

    public string Text => entry.Text;
    public string Date => UserDisplayFormatting.DateTime(entry.CreatedAt);
    public string Delivery => entry.Kind == "guest_email_reply"
        ? $"{EmailHistoryRow.StatusName(entry.DeliveryStatus ?? "pending")} {entry.DeliveryError}".Trim()
        : string.Empty;
}
