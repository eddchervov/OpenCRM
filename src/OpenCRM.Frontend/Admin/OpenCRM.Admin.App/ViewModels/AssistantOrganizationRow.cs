using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка организации в управлении доступом помощника. </summary>
public sealed class AssistantOrganizationRow(AdminAssistantOrganization organization)
{
    public AdminAssistantOrganization Organization { get; } = organization;
    public Guid OrganizationId => this.Organization.OrganizationId;
    public string Name => this.Organization.Name;
    public string Access => this.Organization.IsEnabled ? "Включён" : "Выключен";
    public string Channels => string.Join(" · ", new[]
    {
        this.Organization.WebEnabled ? "Сайт" : null,
        this.Organization.TelegramEnabled ? "Telegram" : null
    }.OfType<string>());
}
