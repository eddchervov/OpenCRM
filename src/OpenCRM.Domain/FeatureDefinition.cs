using System.Collections.Generic;

namespace OpenCRM.Domain;

public class FeatureDefinition
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string ScopeCode { get; set; } = FeatureCatalogCodes.Scopes.Both;
    public string AudienceCode { get; set; } = FeatureCatalogCodes.Audiences.User;
    public FeatureValueType ValueType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }

    public List<SubscriptionPlanFeature> PlanFeatures { get; set; } = [];
    public List<UserFeatureGrant> UserFeatureGrants { get; set; } = [];
}

