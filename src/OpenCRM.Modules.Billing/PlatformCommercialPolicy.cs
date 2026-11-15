using Microsoft.Extensions.Configuration;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing;

/// <summary> Политика коммерческого режима, задаваемая настройкой PlatformPolicy:Commercial:Enabled. </summary>
internal sealed class PlatformCommercialPolicy(IConfiguration configuration) : IPlatformCommercialPolicy
{
    /// <summary> Коммерческие тарифы, квоты и промокоды включены. </summary>
    public bool CommercialEnabled { get; } = configuration.GetValue("PlatformPolicy:Commercial:Enabled", false);
}
