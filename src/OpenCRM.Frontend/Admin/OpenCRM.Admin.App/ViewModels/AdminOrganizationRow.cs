using System.Globalization;
using Avalonia.Controls;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка организаций. </summary>
public sealed class AdminOrganizationRow(AdminOrganization organization, bool isCommercialVisible = false)
{
    public AdminOrganization Organization { get; } = organization;
    public bool IsCommercialVisible { get; } = isCommercialVisible;
    public GridLength CommercialColumnWidth { get; } = isCommercialVisible ? new GridLength(170, GridUnitType.Star) : new GridLength(0);
    public Guid Id => this.Organization.Id;
    public string Name => this.Organization.Name;
    public string Scenario => AdminDisplayLabels.OrganizationScenario(this.Organization.PrimaryScenarioCode, this.Organization.ScenarioName);
    public string Owner => this.Organization.Owner;
    public string OwnerContacts => string.Join(" · ", new[] { this.Organization.OwnerEmail, this.Organization.OwnerPhone }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    private bool HasFreeWorkspace => this.Organization.IsActive && !this.Organization.IsArchived && !this.Organization.IsDeleted
        && (string.Equals(this.Organization.PrimaryScenarioCode, "detailing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(this.Organization.PrimaryScenarioCode, "service-station", StringComparison.OrdinalIgnoreCase));
    public string Plan => !this.IsCommercialVisible ? string.Empty : string.IsNullOrWhiteSpace(this.Organization.ActiveSubscriptionName)
        ? this.HasFreeWorkspace ? "Free" : AdminLocalization.Instance["core.organizations.noActivePlan"]
        : this.Organization.ActiveSubscriptionName;
    public string PlanEnd => !this.IsCommercialVisible ? string.Empty : this.Organization.ActiveSubscriptionEndDate?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
        ?? (this.HasFreeWorkspace ? AdminLocalization.Instance["core.organizations.noExpiry"] : "—");
    public string Status => this.Organization.IsDeleted ? AdminLocalization.Instance["core.organizations.deletedStatus"]
        : this.Organization.IsArchived ? AdminLocalization.Instance["core.organizations.archivedStatus"]
        : this.Organization.IsActive ? AdminLocalization.Instance["core.organizations.activeStatus"]
        : AdminLocalization.Instance["core.organizations.inactiveStatus"];
    public string CreatedAt => UserDisplayFormatting.Date(this.Organization.CreatedAt);
    public int MembersCount => this.Organization.MembersCount;
    public int ClientsCount => this.Organization.ClientsCount;
    public int VehiclesCount => this.Organization.VehiclesCount;
    public int OrdersCount => this.Organization.WorkOrdersCount;
}
