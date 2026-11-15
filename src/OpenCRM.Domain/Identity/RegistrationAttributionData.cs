namespace OpenCRM.Domain.Identity;

public sealed class RegistrationAttributionData
{
    public string? FirstUtmSource { get; init; }
    public string? FirstUtmMedium { get; init; }
    public string? FirstUtmCampaign { get; init; }
    public string? FirstUtmContent { get; init; }
    public string? FirstUtmTerm { get; init; }
    public DateTime? FirstVisitAt { get; init; }
    public string? LastUtmSource { get; init; }
    public string? LastUtmMedium { get; init; }
    public string? LastUtmCampaign { get; init; }
    public string? LastUtmContent { get; init; }
    public string? LastUtmTerm { get; init; }
    public DateTime? LastVisitAt { get; init; }

    public void ApplyTo(ApplicationUser user)
    {
        user.FirstUtmSource = this.FirstUtmSource;
        user.FirstUtmMedium = this.FirstUtmMedium;
        user.FirstUtmCampaign = this.FirstUtmCampaign;
        user.FirstUtmContent = this.FirstUtmContent;
        user.FirstUtmTerm = this.FirstUtmTerm;
        user.FirstVisitAt = this.FirstVisitAt;
        user.LastUtmSource = this.LastUtmSource;
        user.LastUtmMedium = this.LastUtmMedium;
        user.LastUtmCampaign = this.LastUtmCampaign;
        user.LastUtmContent = this.LastUtmContent;
        user.LastUtmTerm = this.LastUtmTerm;
        user.LastVisitAt = this.LastVisitAt;
    }
}
