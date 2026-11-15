using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Обрабатывает внутреннюю команду доставки уже сохранённого уведомления.</summary>
public sealed class NotificationDispatchConsumer(IServiceScopeFactory scopeFactory)
    : IConsumer<NotificationDispatchRequested>
{
    /// <summary>Отделяет внешний вызов провайдера от транзакции входящего сообщения.</summary>
    public async Task Consume(ConsumeContext<NotificationDispatchRequested> context)
    {
        // Отдельная область использует bus outbox: результат сохраняется вместе с состоянием,
        // а транзакция inbox не удерживается во время сетевого вызова SMTP или SMS.ru.
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        await dispatcher.DispatchAsync(context.Message.NotificationId, context.CancellationToken);
    }
}
