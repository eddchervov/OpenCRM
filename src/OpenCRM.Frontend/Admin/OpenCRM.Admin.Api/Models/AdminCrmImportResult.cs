namespace OpenCRM.Admin.Api.Models;

public sealed record AdminCrmImportResult(int CreatedClients, int CreatedVehicles, int SkippedDuplicates,
    int Errors, Guid ReportId);
