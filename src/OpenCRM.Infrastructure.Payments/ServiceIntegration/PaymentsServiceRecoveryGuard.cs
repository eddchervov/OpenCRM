using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Не допускает незаметного отключения восстановления принятых финансовых покупок. </summary>
internal sealed class PaymentsServiceRecoveryGuard(
    IServiceScopeFactory scopes,
    IOptions<PaymentsServiceOptions> options,
    ILogger<PaymentsServiceRecoveryGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var hasPurchases = await context.SubscriptionPurchases.AsNoTracking().AnyAsync(cancellationToken);
        if (!hasPurchases)
        {
            return;
        }

        logger.LogCritical("Нельзя отключить платёжный транспорт: имеются покупки МС, требующие наблюдения за оплатами и возвратами. Для остановки продаж используйте AcceptNewPurchases=false при Enabled=true.");
        throw new InvalidOperationException("Платёжный сервис отключён при существующих покупках МС. Включите восстановление; новые продажи можно остановить настройкой AcceptNewPurchases.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
