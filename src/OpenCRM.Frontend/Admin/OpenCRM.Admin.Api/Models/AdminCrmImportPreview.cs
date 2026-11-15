namespace OpenCRM.Admin.Api.Models;

public sealed record AdminCrmImportPreview(Guid Id, string FileName, bool AlreadyImported,
    IReadOnlyList<AdminCrmImportRow> Rows, AdminCrmImportSummary Summary);
