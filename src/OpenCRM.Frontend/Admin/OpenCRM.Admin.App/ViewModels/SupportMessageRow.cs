using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка обращений. </summary>
public sealed class SupportMessageRow(AdminSupportMessage message)
{
    public int Id => message.Id;
    public string Topic => message.Topic;
    public string Sender => message.UserEmail ?? message.Email ?? message.Phone ?? message.Name ?? "Гость";
    public string Organization => message.OrganizationName ?? "—";
    public string Status => message.StatusName;
    public string Type => message.TypeName;
    public string Date => UserDisplayFormatting.DateTime(message.CreatedAt);
}
