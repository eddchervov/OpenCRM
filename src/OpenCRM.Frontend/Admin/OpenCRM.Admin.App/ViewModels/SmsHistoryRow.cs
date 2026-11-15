using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class SmsHistoryRow(AdminSmsHistoryItem item)
{
    public int Id { get; } = item.Id;
    public string Phone { get; } = item.Phone;
    public string Text { get; } = item.Text;
    public string Code { get; } = item.SmsCode;
    public string Date { get; } = item.DateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string Response { get; } = string.IsNullOrWhiteSpace(item.Response) ? "—" : item.Response;
    public string Type { get; } = item.Type switch
    {
        10 => "Подтверждение телефона",
        20 => "Вход по телефону",
        _ => $"Тип {item.Type}"
    };
}
