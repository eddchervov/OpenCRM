using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class SmsHistoryRow(AdminSmsHistoryItem item) : ObservableObject
{
    public int Id { get; } = item.Id;
    public string Phone { get; } = item.Phone;
    public string Text { get; } = item.Text;
    public string Code { get; } = item.SmsCode;
    public string Date { get; } = item.DateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string Response { get; } = string.IsNullOrWhiteSpace(item.Response) ? "—" : item.Response;
    public string Type { get; } = item.Type switch
    {
        10 => AdminLocalization.Instance["native.sms.podtverzhdenie.telefona"],
        20 => AdminLocalization.Instance["native.sms.vkhod.po.telefonu"],
        _ => AdminLocalization.Instance.Format("native.sms.tip.item.type", item.Type)
    };

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
