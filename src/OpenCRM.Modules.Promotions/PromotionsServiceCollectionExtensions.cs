using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Promotions;

namespace OpenCRM.Modules.Promotions;

/// <summary> Регистрация сервисов модуля промокодов. </summary>
public static class PromotionsServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы модуля промокодов в контейнер. </summary>
    public static IServiceCollection AddOpenCRMPromotions(this IServiceCollection services)
    {
        services.AddScoped<IPromoCodeService, PromoCodeService>();
        services.AddScoped<IOrganizationPromoCodeService, OrganizationPromoCodeService>();
        services.AddScoped<IPromotionPaymentCoordinator, PromotionPaymentCoordinator>();

        return services;
    }
}
