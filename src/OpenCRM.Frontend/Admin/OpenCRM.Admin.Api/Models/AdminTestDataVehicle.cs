namespace OpenCRM.Admin.Api.Models;

public sealed class AdminTestDataVehicle
{
    public Guid Id { get; init; }
    public int Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string BrandName { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string? Name { get; init; }
    public int? YearIssue { get; init; }
    public bool IsArchived { get; init; }
    public int MaintenanceCount { get; init; }
    public int DefectCount { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
}
