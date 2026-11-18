namespace OpenCRM.Admin.Api.Models;

/// <summary> Организация в административной панели. </summary>
public sealed class AdminOrganization
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? LegalName { get; init; }
    public string? Inn { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string PrimaryScenarioCode { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = "RUB";
    public string ScenarioName { get; init; } = string.Empty;
    public string DocumentStyleCode { get; init; } = string.Empty;
    public string OwnerUserId { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string? OwnerEmail { get; init; }
    public string? OwnerPhone { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsArchived { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ArchiveDate { get; init; }
    public DateTime? DeletionDate { get; init; }
    public int MembersCount { get; init; }
    public int AdminsCount { get; init; }
    public int PendingInvitationsCount { get; init; }
    public int ClientsCount { get; init; }
    public int VehiclesCount { get; init; }
    public int MaintenancesCount { get; init; }
    public int WorkOrdersCount { get; init; }
    public int CompletedWorkOrdersCount { get; init; }
    public decimal WorkOrdersTotal { get; init; }
    public decimal PaymentsTotal { get; init; }
    public int OpenDefectsCount { get; init; }
    public int SchedulerTasksCount { get; init; }
    public int OverdueTasksCount { get; init; }
    public int StockItemsCount { get; init; }
    public int LowStockItemsCount { get; init; }
    public decimal InventoryValue { get; init; }
    public IReadOnlyList<AdminOrganizationCurrencyTotal> OtherCurrencyTotals { get; init; } = [];
    public string? ActiveSubscriptionName { get; init; }
    public DateTime? ActiveSubscriptionEndDate { get; init; }
    public DateTime? LastAuditAt { get; init; }
    public string? LastAuditTitle { get; init; }
    public IReadOnlyList<AdminOrganizationMember> Members { get; init; } = [];
    public IReadOnlyList<AdminOrganizationInvitation> PendingInvitations { get; init; } = [];
    public IReadOnlyList<AdminOrganizationAudit> RecentAudit { get; init; } = [];
}
