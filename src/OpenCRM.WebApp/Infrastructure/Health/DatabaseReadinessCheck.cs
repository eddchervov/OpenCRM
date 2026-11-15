using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.WebApp.Infrastructure.Health;

/// <summary> Проверка готовности приложения через доступность базы данных. </summary>
public sealed class DatabaseReadinessCheck(OpenCRMContext context) : IHealthCheck
{
    /// <summary> Проверяет доступность базы данных для health check. </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            return HealthCheckResult.Unhealthy("Миграции базы данных не применены.");
        }

        await context.Organizations.AsNoTracking().AnyAsync(cancellationToken);

        return HealthCheckResult.Healthy();
    }
}
