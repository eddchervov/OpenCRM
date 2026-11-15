using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.WebApp.Infrastructure.Imports;

/// <summary> Продолжает сохранённые задания импорта после перезапуска приложения. </summary>
public sealed class OrganizationImportWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrganizationImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var imports = scope.ServiceProvider.GetRequiredService<IOrganizationImportBatchService>();
                await imports.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось обработать очередь переноса данных.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
