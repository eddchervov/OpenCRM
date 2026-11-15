using Microsoft.Extensions.Options;
using Payments.Service.Configuration;

namespace Payments.Service.Processing;

public sealed class PaymentWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            this.RunLaneAsync(
                (processor, token) => processor.ProcessDueOperationsAsync(token),
                "операции",
                stoppingToken),
            this.RunLaneAsync(
                (processor, token) => processor.ProcessDueReconciliationsAsync(token),
                "сверки",
                stoppingToken),
            this.RunLaneAsync(
                (processor, token) => processor.ProcessDueWebhooksAsync(token),
                "уведомления",
                stoppingToken));
    }

    private async Task RunLaneAsync(
        Func<PaymentProcessor, CancellationToken, Task> processLaneAsync,
        string laneName,
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.Equals(options.Value.Mode, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
                    await processLaneAsync(processor, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError("Не удалось обработать очередь платежей {LaneName}; тип ошибки {ErrorType}.",
                        laneName, exception.GetType().Name);
                }
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}
