using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка организации в управлении доступом помощника. </summary>
public sealed class AssistantOrganizationRow(AdminAssistantOrganization organization) : ObservableObject
{
    public AdminAssistantOrganization Organization { get; } = organization;
    public Guid OrganizationId => this.Organization.OrganizationId;
    public string Name => this.Organization.Name;
    public string Access => this.Organization.IsEnabled ? AdminLocalization.Instance["native.assistant.vklyuchyon"] : AdminLocalization.Instance["native.assistant.vyklyuchen"];
    public string Channels => string.Join(" · ", new[]
    {
        this.Organization.WebEnabled ? AdminLocalization.Instance["native.assistant.sayt"] : null,
        this.Organization.TelegramEnabled ? "Telegram" : null
    }.OfType<string>());

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
