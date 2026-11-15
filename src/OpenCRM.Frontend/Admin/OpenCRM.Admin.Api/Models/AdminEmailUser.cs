namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailUser
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool EmailConfirmed { get; init; }
    public bool AdvertisingConsentAccepted { get; init; }
}
