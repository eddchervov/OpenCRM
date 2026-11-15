using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Управляет напоминаниями о визите и выпускает наступившие отправки. </summary>
public interface IMaintenanceVisitReminderService
{
    Task<VisitReminderSettingsDto> GetSettingsAsync(string userId, Guid organizationId);

    Task SaveSettingsAsync(string userId, Guid organizationId, VisitReminderSettingsDto settings);

    Task<MaintenanceVisitReminderDto> GetAsync(string userId, Guid organizationId, Guid maintenanceId);

    Task<MaintenanceVisitReminderDto> SetEnabledAsync(string userId, Guid organizationId, Guid maintenanceId, bool enabled, uint version);

    Task SendDueAsync(CancellationToken cancellationToken);
}
