using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Interfaces.Brands;
using OpenCRM.Domain.Interfaces.Catalog;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Domain.Interfaces.SmsSendingHistories;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data.Repositories;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Методы регистрации репозиториев. </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary> Регистрирует репозитории приложения в контейнере. </summary>
    public static IServiceCollection AddOpenCRMRepositories(this IServiceCollection services)
    {
        services.AddTransient<ISmsSendingHistoryRepository, SmsSendingHistoryRepository>();
        services.AddTransient<IMaintenanceRepository, MaintenanceRepository>();
        services.AddTransient<IVehicleRepository, VehicleRepository>();
        services.AddTransient<IVehicleCatalogSuggestionRepository, VehicleCatalogSuggestionRepository>();
        services.AddTransient<IBrandRepository, BrandRepository>();
        services.AddTransient<IModelRepository, ModelRepository>();
        services.AddScoped<IVehiclePhotoRepository, VehiclePhotoRepository>();
        services.AddScoped<IMaintenancePhotoRepository, MaintenancePhotoRepository>();
        services.AddScoped<IGenericMediaRepository, GenericMediaRepository>();

        return services;
    }
}
