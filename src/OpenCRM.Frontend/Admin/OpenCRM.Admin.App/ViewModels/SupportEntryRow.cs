using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Запись переписки с состоянием доставки ответа гостю. </summary>
public sealed class SupportEntryRow(AdminSupportMessageEntry entry) : ObservableObject
{
    public string Author => entry.Kind switch
    {
        "internal_comment" or "internal" => AdminLocalization.Instance["native.support.vnutrenniy.kommentariy"],
        "status" => AdminLocalization.Instance["native.support.izmenenie.statusa"], _ => entry.IsStaff ? AdminLocalization.Instance["native.support.administrator"] : AdminLocalization.Instance["native.support.otpravitel"]
    };

    public string Text => entry.Text;
    public string Date => UserDisplayFormatting.DateTime(entry.CreatedAt);
    public string Delivery => entry.Kind == "guest_email_reply"
        ? $"{EmailHistoryRow.StatusName(entry.DeliveryStatus ?? "pending")} {entry.DeliveryError}".Trim()
        : string.Empty;

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
