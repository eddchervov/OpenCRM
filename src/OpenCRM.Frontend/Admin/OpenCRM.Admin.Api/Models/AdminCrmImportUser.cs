namespace OpenCRM.Admin.Api.Models;

public sealed record AdminCrmImportUser(string Id, string? Email, string? Phone)
{
    public string Label => this.Email ?? this.Phone ?? this.Id;
}
