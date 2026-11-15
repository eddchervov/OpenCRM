using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Defaults;
using OpenCRM.Modules.Catalog.Brands;
using OpenCRM.Modules.Catalog.Models;
using OpenCRM.Services.Interfaces.Brands;
using OpenCRM.Services.Interfaces.Models;

namespace OpenCRM.Modules.Catalog;

/// <summary> Расширение регистрации сервисов модуля каталога. </summary>
public static class CatalogServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы каталога в контейнере зависимостей. </summary>
    public static IServiceCollection AddOpenCRMCatalog(this IServiceCollection services)
    {
        services.AddTransient<IBrandService, BrandService>();
        services.AddTransient<IModelService, ModelService>();
        services.AddScoped<CatalogDefaultsService>();
        services.AddScoped<CatalogUsageReader>();
        services.AddScoped<CatalogTransaction>();
        services.AddScoped<ICatalogQueryService, CatalogQueryService>();
        services.AddScoped<ICatalogCommandService, CatalogCommandService>();
        services.AddScoped<ICatalogInitializationService, CatalogInitializationService>();
        services.AddScoped<ICatalogSetupService, CatalogSetupService>();
        services.AddScoped<ICatalogMaterialLookup, CatalogMaterialLookup>();

        return services;
    }
}
