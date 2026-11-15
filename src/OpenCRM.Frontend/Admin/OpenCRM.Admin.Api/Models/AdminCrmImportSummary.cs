namespace OpenCRM.Admin.Api.Models;

public sealed record AdminCrmImportSummary(int Clients, int Vehicles, int Create, int Duplicate, int Error);
