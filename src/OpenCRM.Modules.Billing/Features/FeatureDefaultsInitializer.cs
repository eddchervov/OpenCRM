using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Features;

/// <summary> Инициализатор значений возможностей по умолчанию. </summary>
internal sealed class FeatureDefaultsInitializer(OpenCRMContext context, IConfiguration configuration)
    : IFeatureDefaultsInitializer
{
    /// <summary> Гарантирует наличие значений возможностей по умолчанию. </summary>
    public async Task EnsureDefaultsAsync()
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(context);

        // Тарифы, квоты и промокоды — подключаемая коммерческая часть:
        // базовая поставка их не создаёт.
        if (configuration.GetValue("PlatformPolicy:Commercial:Enabled", false))
        {
            await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        }
    }
}
