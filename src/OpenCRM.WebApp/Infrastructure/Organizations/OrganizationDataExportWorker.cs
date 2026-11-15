using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;

namespace OpenCRM.WebApp.Infrastructure.Organizations;

/// <summary> Выполняет сохранённые задания экспорта без привязки к соединению браузера. </summary>
public sealed class OrganizationDataExportWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrganizationDataExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
                var now = DateTime.UtcNow;
                var hasWork = await db.Set<OrganizationDataExportJob>().AnyAsync(job =>
                    job.State == "queued" || job.State == "running" || job.CleanupAfterUtc <= now, stoppingToken);
                if (hasWork)
                {
                    var exports = scope.ServiceProvider.GetRequiredService<IOrganizationDataExportService>();
                    await exports.ProcessPendingAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(hasWork ? 3 : 15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось обработать очередь выгрузки данных.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
