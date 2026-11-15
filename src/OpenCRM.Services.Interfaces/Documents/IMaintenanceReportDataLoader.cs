using OpenCRM.Services.Interfaces.Documents.Dtos;
namespace OpenCRM.Services.Interfaces.Documents;

/// <summary>Загружает обслуживание и связанную технику для отчётов.</summary>
public interface IMaintenanceReportDataLoader
{
    /// <summary> Загружает данные отчёта по обслуживанию. </summary>
    Task<MaintenanceReportData?> LoadAsync(string userId, Guid? organizationId, Guid maintenanceId);
}
