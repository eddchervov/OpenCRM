using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Imports.Crm;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary> Подключает подготовку и фоновый перенос данных организации. </summary>
public static class ImportsServiceCollectionExtensions
{
    public static IServiceCollection AddOpenCRMImports(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationImportService, OrganizationImportService>();
        services.AddScoped<ICrmImportService, CrmImportService>();
        services.AddScoped<IOrganizationImportBatchService>(provider =>
            (IOrganizationImportBatchService)provider.GetRequiredService<IOrganizationImportService>());

        return services;
    }
}
