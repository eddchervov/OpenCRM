using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.Loading;

/// <summary> Загрузчик строк работ и услуг обслуживания. </summary>
public static class MaintenanceDetailsLoader
{
    /// <summary> Перезагружает строки обслуживания из базы данных. </summary>
    public static async Task ReloadAsync(OpenCRMContext context, Maintenance maintenance)
    {
        foreach (var detail in maintenance.Details.ToArray())
        {
            context.Entry(detail).State = EntityState.Detached;
        }

        maintenance.Details.Clear();
        context.Entry(maintenance).Collection(item => item.Details).IsLoaded = false;
        await context.MaintenanceDetails
            .Where(detail => detail.MaintenanceId == maintenance.Id)
            .OrderBy(detail => detail.Position)
            .LoadAsync();
        context.Entry(maintenance).Collection(item => item.Details).IsLoaded = true;
    }
}
