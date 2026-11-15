namespace OpenCRM.Modules.Clients.Contracts.Dtos;

public sealed record OrganizationRepeatVisitDto
{
    public Guid Id { get; init; }
    public Guid OrganizationClientId { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public Guid VehicleId { get; init; }
    public string VehicleName { get; init; } = string.Empty;
    public Guid SourceMaintenanceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateOnly SuggestedVisitDate { get; init; }
    public string? ResponsibleUserId { get; init; }
    public string? ResponsibleName { get; init; }
    public DateTime? ContactByUtc { get; init; }
    public DateTime? ContactCompletedAtUtc { get; init; }
    public bool IsActive { get; init; }
    public Guid? BookedMaintenanceId { get; init; }
    public long Version { get; init; }
    public bool CanEdit { get; init; }
    public bool CanManageContact { get; init; }
    public bool CanBook { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
}

public sealed record OrganizationRepeatVisitPageDto
{
    public IReadOnlyList<OrganizationRepeatVisitDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
}

public sealed record OrganizationRepeatVisitOptionsDto
{
    public IReadOnlyList<OrganizationRepeatVisitResponsibleDto> Responsibles { get; init; } = [];
    public string TimeZoneId { get; init; } = "UTC";
    public bool CanCreate { get; init; }
}

public sealed record OrganizationRepeatVisitResponsibleDto(string Id, string Name);

public sealed record OrganizationRepeatVisitQueryDto
{
    public Guid? ClientId { get; init; }
    public Guid? SourceMaintenanceId { get; init; }
    public bool ActionsOnly { get; init; }
    public DateOnly? Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public sealed record OrganizationRepeatVisitSaveDto
{
    public Guid Id { get; init; }
    public Guid SourceMaintenanceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateOnly SuggestedVisitDate { get; init; }
    public string? ResponsibleUserId { get; init; }
    public DateTime? ContactByUtc { get; init; }
    public long Version { get; init; }
}

public sealed record OrganizationRepeatVisitVersionDto(long Version);
public sealed record OrganizationRepeatVisitRescheduleDto(long Version, DateTime ContactByUtc);
public sealed record OrganizationRepeatVisitStateDto(long Version, bool IsActive);

public sealed record OrganizationRepeatVisitBookingContext(
    Guid Id,
    Guid ClientId,
    Guid VehicleId,
    string ServiceName,
    DateOnly SuggestedVisitDate,
    long Version,
    bool IsActive,
    Guid? BookedMaintenanceId);
