using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Данные административного дашборда. </summary>
public interface IAdminDashboardService
{
    /// <summary> Возвращает сводку дашборда за период. </summary>
    Task<AdminDashboardDto> GetAsync(int periodDays, bool canViewBilling);
}
