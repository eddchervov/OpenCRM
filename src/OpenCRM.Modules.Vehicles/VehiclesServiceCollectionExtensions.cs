using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Photos;
using OpenCRM.Modules.Vehicles.Defects;
using OpenCRM.Services.Interfaces.Defects;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Регистрация сервисов модуля техники. </summary>
public static class VehiclesServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы модуля техники в контейнер. </summary>
    public static IServiceCollection AddOpenCRMVehicles(this IServiceCollection services)
    {
        services.AddTransient<IVehicleDefectService, VehicleDefectService>();
        services.AddScoped<IVehicleQueryService, VehicleQueryService>();
        services.AddScoped<IVehicleMaintenanceLookupService, VehicleMaintenanceLookupService>();
        services.AddScoped<IQuickBookingVehicleLookupService, QuickBookingVehicleLookupService>();
        services.AddScoped<IVehicleRegistrationPlatePolicy, VehicleRegistrationPlatePolicy>();
        services.AddScoped<IVehicleOptionQueryService, VehicleOptionQueryService>();
        services.AddScoped<IVehicleClientSummaryQueryService, VehicleClientSummaryQueryService>();
        services.AddScoped<IVehicleCommandService, VehicleCommandService>();
        services.AddScoped<IVehicleGalleryService, VehicleGalleryService>();
        services.AddScoped<IVehiclePhotoAlbumService, VehiclePhotoAlbumService>();
        services.AddScoped<IVehicleCounterQueryService, VehicleCounterQueryService>();
        services.AddScoped<IVehicleCounterCommandService, VehicleCounterCommandService>();
        services.AddScoped<VehicleLimitQueryService>();
        services.AddScoped<IVehicleLimitQueryService>(provider => provider.GetRequiredService<VehicleLimitQueryService>());
        services.AddScoped<VehicleHistoryWriter>();
        services.AddScoped<IVehicleDocumentHistoryService>(provider => provider.GetRequiredService<VehicleHistoryWriter>());

        return services;
    }
}
