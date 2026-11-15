using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning;

/// <summary> Регистрация сервисов модуля планирования. </summary>
public static class PlanningServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы модуля планирования в контейнер. </summary>
    public static IServiceCollection AddOpenCRMPlanning(this IServiceCollection services)
    {
        services.AddScoped<SchedulerAccess>();
        services.AddScoped<SchedulerTransaction>();
        services.AddScoped<SchedulerRecurrenceEngine>();
        services.AddScoped<ISchedulerTaskCommandService, SchedulerTaskCommandService>();
        services.AddScoped<ISchedulerQueryService, SchedulerQueryService>();
        services.AddScoped<ISchedulerRecurrenceService, SchedulerRecurrenceService>();
        services.AddScoped<ISchedulerWorkflowCommandService, SchedulerWorkflowCommandService>();
        services.AddScoped<IMaintenanceSchedulerSyncService, MaintenanceSchedulerSyncService>();

        return services;
    }
}
