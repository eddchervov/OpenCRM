using OpenCRM.Modules.Maintenance.Commands;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Work;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Maintenance.Photos;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Регистрирует сервисы модуля обслуживания. </summary>
public static class MaintenanceServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы обслуживания в контейнер зависимостей. </summary>
    public static IServiceCollection AddOpenCRMMaintenance(this IServiceCollection services)
    {
        services.AddScoped<MaintenanceTransaction>();
        services.AddScoped<IMaintenanceVisitReminderService, OpenCRM.Modules.Maintenance.Reminders.MaintenanceVisitReminderService>();
        services.AddScoped<OpenCRM.Modules.Maintenance.Contracts.Imports.IImportBusinessRecordProcessor,
            OpenCRM.Modules.Maintenance.Imports.ImportBusinessRecordProcessor>();
        services.AddScoped<IMaintenancePhotoService, MaintenancePhotoService>();
        services.AddScoped<IMaintenanceCreateService, MaintenanceCreateService>();
        services.AddScoped<IMaintenanceUpdateService, MaintenanceUpdateService>();
        services.AddScoped<IMaintenancePaymentService, MaintenancePaymentService>();
        services.AddScoped<IMaintenanceIntakeService, MaintenanceIntakeService>();
        services.AddScoped<IMaintenanceIntakeDamageService, MaintenanceIntakeDamageService>();
        services.AddScoped<IMaintenanceDocumentService, MaintenanceDocumentService>();
        services.AddScoped<IMaintenanceAssignmentService, MaintenanceAssignmentService>();
        services.AddScoped<IMaintenanceDeleteService, MaintenanceDeleteService>();
        services.AddScoped<IMaintenanceSchedulingService, MaintenanceSchedulingService>();
        services.AddScoped<IMaintenanceWorkflowService, MaintenanceWorkflowService>();
        services.AddScoped<MaintenanceOrderLoader>();
        services.AddScoped<MaintenanceAccess>();
        services.AddScoped<MaintenancePlanAccess>();
        services.AddScoped<MaintenanceEmployeeResolver>();
        services.AddScoped<MaintenanceWorkGuard>();
        services.AddScoped<MaintenanceSlotGuard>();
        services.AddScoped<MaintenanceDetailRules>();
        services.AddScoped<MaintenanceWorkflowRules>();
        services.AddScoped<MaintenanceHistoryWriter>();
        services.AddScoped<MaintenanceDefectLinks>();
        services.AddScoped<MaintenanceNotificationOutbox>();
        services.AddScoped<MaintenanceBuyerResolver>();
        services.AddScoped<MaintenanceConsumption>();
        services.AddScoped<IMaintenanceQueryService, MaintenanceQueryService>();
        services.AddScoped<IMaintenanceClientSummaryQueryService, MaintenanceClientSummaryQueryService>();
        services.AddScoped<IMaintenanceSlotQueryService, MaintenanceSlotQueryService>();
        services.AddScoped<IMaintenanceWorkQueryService, MaintenanceWorkQueryService>();
        services.AddScoped<IMaintenanceWorkExecutionService, MaintenanceWorkExecutionService>();
        services.AddScoped<IMaintenanceWorkReportService, MaintenanceWorkReportService>();
        services.AddScoped<IMaintenanceWorkTimeService, MaintenanceWorkTimeService>();

        return services;
    }
}
