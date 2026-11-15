namespace OpenCRM.Services.Interfaces.Subscriptions;

/// <summary> Контракт сервиса напоминаний об окончании подписки. </summary>
public interface ISubscriptionExpiryReminderService
{
    /// <summary> Отправляет напоминания по наступлении срока. </summary>
    Task SendDueRemindersAsync();
}
