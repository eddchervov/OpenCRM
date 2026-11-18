using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка обращений. </summary>
public sealed class SupportMessageRow(AdminSupportMessage message) : ObservableObject
{
    public int Id => message.Id;
    public string Topic => message.Topic;
    public string Sender => message.UserEmail ?? message.Email ?? message.Phone ?? message.Name ?? AdminLocalization.Instance["native.support.gost"];
    public string Organization => message.OrganizationName ?? "—";
    public string Status => AdminDisplayLabels.SupportStatus(message.StatusCode, message.StatusName);
    public string Type => AdminDisplayLabels.SupportType(message.TypeCode, message.TypeName);
    public string Date => UserDisplayFormatting.DateTime(message.CreatedAt);

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
