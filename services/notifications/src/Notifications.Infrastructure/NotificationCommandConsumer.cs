using MassTransit;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Принимает внешние команды из очереди конкретного приложения.</summary>
public sealed class NotificationCommandConsumer(
    NotificationApplicationService notificationService,
    TemplateApplicationService templateService,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    IOptions<NotificationMessagingOptions> messagingOptions)
    : IConsumer<SendNotification>, IConsumer<GetNotificationStatus>, IConsumer<UpsertNotificationTemplate>
{
    /// <summary>Регистрирует уведомление и подтверждает его состояние через transactional outbox.</summary>
    public async Task Consume(ConsumeContext<SendNotification> context)
    {
        var applicationId = messagingOptions.Value.GetApplicationId(context.ReceiveContext.InputAddress);
        try
        {
            await notificationService.EnqueueAsync(applicationId, context.Message.Request, context.CancellationToken);
        }
        catch (ArgumentException)
        {
            await this.RejectAsync(context, applicationId);
        }
    }

    /// <summary>Возвращает состояние по ключу исходного задания.</summary>
    public async Task Consume(ConsumeContext<GetNotificationStatus> context)
    {
        var applicationId = messagingOptions.Value.GetApplicationId(context.ReceiveContext.InputAddress);
        try
        {
            await notificationService.ReportStatusAsync(
                applicationId, context.Message.IdempotencyKey, context.CancellationToken);
        }
        catch (ArgumentException)
        {
            await this.RejectAsync(context, applicationId);
        }
    }

    /// <summary>Сохраняет шаблон и подтверждение в транзакции обработки команды.</summary>
    public async Task Consume(ConsumeContext<UpsertNotificationTemplate> context)
    {
        var applicationId = messagingOptions.Value.GetApplicationId(context.ReceiveContext.InputAddress);
        try
        {
            await templateService.UpsertAsync(
                applicationId, context.Message.Key, context.Message.Template, context.CancellationToken);
        }
        catch (ArgumentException)
        {
            await this.RejectAsync(context, applicationId);
        }
    }

    /// <summary> Отклоняет команду с указанием приложения. </summary>
    private async Task RejectAsync<T>(ConsumeContext<T> context, string applicationId) where T : class
    {
        await publisher.SendToApplicationAsync(
            applicationId,
            new NotificationCommandRejected(
                Guid.NewGuid(), applicationId, context.MessageId, typeof(T).Name, "invalid-command"),
            context.CancellationToken);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);
    }
}
