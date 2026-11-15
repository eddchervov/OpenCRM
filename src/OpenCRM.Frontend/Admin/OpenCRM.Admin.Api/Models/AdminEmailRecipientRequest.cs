namespace OpenCRM.Admin.Api.Models;

public class AdminEmailRecipientRequest
{
    public string TargetMode { get; init; } = "user";
    public string? Email { get; init; }
    public string? UserId { get; init; }
    public string? OrganizationTypeCode { get; init; }
    public bool OnlyConfirmedEmail { get; init; } = true;
    public bool OnlyAdvertisingConsent { get; init; }
}
