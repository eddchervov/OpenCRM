using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Services.Interfaces.Common;

namespace OpenCRM.Infrastructure.Common;

/// <summary> Методы регистрации инфраструктурных сервисов. </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary> Регистрирует инфраструктурные сервисы приложения в контейнере. </summary>
    public static IServiceCollection AddOpenCRMInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();

        return services;
    }
}
