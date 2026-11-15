using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Загружает записи обслуживания с блокировкой для изменений. </summary>
internal sealed class MaintenanceOrderLoader(
    IMaintenanceRepository maintenanceRepository,
    OpenCRMContext context)
{
    /// <summary> Блокирует и возвращает запись обслуживания с проверкой доступа. </summary>
    internal async Task<MaintenanceEntity> LockMaintenanceAsync(string userId, Guid organizationId, Guid maintenanceId)
    {
        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new InvalidOperationException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenance.Id} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        if (maintenance.IsDeleted)
        {
            throw new InvalidOperationException("Обслуживание не найдено.");
        }

        return maintenance;
    }
}
