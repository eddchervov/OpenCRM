using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Событие доставки письма. </summary>
public sealed class EmailHistoryEventRow(EmailMessageEventItem item) : ObservableObject
{
    public string Date => UserDisplayFormatting.DateTime(item.EventAt);
    public string ReceivedAt => UserDisplayFormatting.DateTime(item.ReceivedAt);
    public string Event => $"{item.Provider} · {item.EventName} · {EmailHistoryRow.StatusName(item.Status)}";
    public string Info => string.Join(" · ", new[] { item.DeliveryInfo, item.Url, item.ProviderMessageId }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
