using Microsoft.AspNetCore.Identity;

namespace OpenCRM.Domain.Identity;

public class ApplicationUser : IdentityUser
{
    public DateTime DatetimeCreation { get; set; }

    public string? PhoneFiltered { get; set; }

    public string? SMSCode { get; set; }

    public string? CallCode { get; set; }

    public DateTime LastDateOfEntry { get; set; }
    public int CountVisits { get; set; }

    public DateTime LastLoginDate { get; set; }
    public int CountLogins { get; set; }

    /// <summary> Последнее посещение с подтверждённой сессией и сведения о клиенте. </summary>
    public DateTime? LastAccessAt { get; set; }
    public string? LastAccessIpAddress { get; set; }
    public string? LastAccessUserAgent { get; set; }
    public string? LastAccessLanguage { get; set; }
    public string? LastAccessClientBrands { get; set; }
    public string? LastAccessPlatform { get; set; }
    public bool? LastAccessIsMobile { get; set; }
    public string? LastAccessAuthenticationScheme { get; set; }

    /// <summary> Способ последней успешной авторизации. </summary>
    public string? LastLoginMethod { get; set; }

    public bool PersonalDataProcessingConsentAccepted { get; set; }
    public DateTime? PersonalDataProcessingConsentAcceptedAt { get; set; }
    public bool AdvertisingConsentAccepted { get; set; }
    public DateTime? AdvertisingConsentAcceptedAt { get; set; }

    public string? FirstUtmSource { get; set; }
    public string? FirstUtmMedium { get; set; }
    public string? FirstUtmCampaign { get; set; }
    public string? FirstUtmContent { get; set; }
    public string? FirstUtmTerm { get; set; }
    public DateTime? FirstVisitAt { get; set; }
    public string? LastUtmSource { get; set; }
    public string? LastUtmMedium { get; set; }
    public string? LastUtmCampaign { get; set; }
    public string? LastUtmContent { get; set; }
    public string? LastUtmTerm { get; set; }
    public DateTime? LastVisitAt { get; set; }


    public List<Vehicle> Vehicles { get; set; } = [];

    public virtual UserSubscription? CurrentSubscription { get; set; }

    public bool HasActiveSubscription => CurrentSubscription?.EndDate >= DateTime.UtcNow;

    public List<SubscriptionPayment> SubscriptionPayments { get; set; } = [];
    public List<UserFeatureGrant> FeatureGrants { get; set; } = [];
    public List<Organization> OwnedOrganizations { get; set; } = [];
    public List<OrganizationMember> OrganizationMemberships { get; set; } = [];



    // Мягкое удаление
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? OriginalEmail { get; set; }
    public bool CanUseTestSubscriptions { get; set; }

}


