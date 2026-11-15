namespace OpenCRM.Services.Interfaces.Emails;

/// <summary>Сохраняет письмо для асинхронной отправки микросервисом уведомлений.</summary>
public interface IEmailNotificationService
{
    /// <summary> Ставит письмо в очередь на асинхронную отправку. </summary>
    Task QueueAsync(
        string email,
        string subject,
        string html,
        string idempotencyKey,
        Guid? campaignRecipientId = null,
        long? subscriptionReminderId = null);

    /// <summary> Ставит шаблонное письмо в очередь на асинхронную отправку. </summary>
    Task QueueTemplateAsync(
        string email,
        string subject,
        string templateKey,
        object data,
        string? idempotencyKey = null,
        long? subscriptionReminderId = null);
}
