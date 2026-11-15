using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Независимо восстанавливает доставку команд, входящие события и оплаченный доступ. </summary>
internal sealed class SubscriptionPaymentWorker(
    IServiceScopeFactory scopes,
    IOptions<PaymentsServiceOptions> options,
    ILogger<SubscriptionPaymentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        this.RunLoopAsync<SubscriptionPaymentOutboxDispatcher>((service, token) => service.DispatchAsync(token), "outbox", stoppingToken),
        this.RunLoopAsync<SubscriptionPaymentEventProcessor>((service, token) => service.ReplayPendingAsync(token), "inbox", stoppingToken),
        this.RunLoopAsync<SubscriptionPaymentReconciler>((service, token) => service.ReconcileDueAsync(token), "reconciliation", stoppingToken),
        this.RunLoopAsync<SubscriptionPaymentAdminWorker>((service, token) => service.ProcessDueAsync(token), "admin_actions", stoppingToken),
        this.RunLoopAsync<SubscriptionPurchaseApplicator>((service, token) => service.ApplyPendingAsync(token), "activation", stoppingToken));

    private async Task RunLoopAsync<T>(Func<T, CancellationToken, Task> action, string stage, CancellationToken cancellationToken) where T : notnull
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await this.RunAsync(action, stage, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), cancellationToken);
        }
    }

    private async Task RunAsync<T>(Func<T, CancellationToken, Task> action, string stage, CancellationToken cancellationToken) where T : notnull
    {
        try
        {
            using var scope = scopes.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<T>(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SubscriptionPurchaseDiagnostics.Failure(logger, stage, null, exception);
        }
    }
}
