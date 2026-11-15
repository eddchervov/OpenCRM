using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Inventory.Commands;
using OpenCRM.Modules.Inventory.Contracts;

namespace OpenCRM.Modules.Inventory;

/// <summary> Регистрирует сервисы модуля инвентаризации. </summary>
public static class InventoryServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы инвентаризации в контейнер зависимостей. </summary>
    public static IServiceCollection AddOpenCRMInventory(this IServiceCollection services)
    {
        services.AddScoped<InventoryCommandTransaction>();
        services.AddScoped<InventoryStockRepository>();
        services.AddScoped<OperationMaterialAssignmentLoader>();
        services.AddScoped<IStockCommandService, StockCommandService>();
        services.AddScoped<IOperationMaterialCommandService, OperationMaterialCommandService>();
        services.AddScoped<IInventoryQueryService, MaterialInventoryQueryService>();
        services.AddScoped<IOperationMaterialQueryService, OperationMaterialQueryService>();
        services.AddScoped<IMaintenanceConsumptionService, MaintenanceConsumptionCommandService>();

        return services;
    }
}
