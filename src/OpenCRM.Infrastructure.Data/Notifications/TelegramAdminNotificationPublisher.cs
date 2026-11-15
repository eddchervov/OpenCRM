using System.Globalization;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Data.Notifications;

/// <summary> Публикатор административных уведомлений через Telegram-исходящие. </summary>
public static class TelegramAdminNotificationPublisher
{
    /// <summary> Добавляет уведомление о регистрации пользователя. </summary>
    public static void AddRegistration(OpenCRMContext context, ApplicationUser user)
    {
        var message = $"Новая регистрация\nEmail: {user.Email}\nТелефон: {user.PhoneNumber ?? "не указан"}"
            + $"\nID: {user.Id}";

        Add(context, $"registration:{user.Id}", message);
    }

    /// <summary> Добавляет уведомление об удалении пользователя. </summary>
    public static void AddDeletion(OpenCRMContext context, ApplicationUser user)
    {
        var message = $"Пользователь удалил аккаунт\nEmail: {user.OriginalEmail}\nID: {user.Id}";

        Add(context, $"deletion:{user.Id}", message);
    }

    /// <summary> Добавляет уведомление о покупке подписки. </summary>
    public static void AddPurchase(
        OpenCRMContext context,
        string userId,
        string? email,
        string planName,
        decimal amount,
        string paymentId,
        Guid? organizationId = null)
    {
        var scope = organizationId.HasValue ? $"Организация: {organizationId}" : "Личная подписка";
        var message = $"Оплачена подписка\n{scope}\nТариф: {planName}"
            + $"\nСумма: {amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))} ₽"
            + $"\nEmail: {email}\nID пользователя: {userId}\nID платежа: {paymentId}";

        Add(context, $"subscription-paid:{paymentId}", message);
    }

    /// <summary> Добавляет исходящее уведомление с ключом и текстом. </summary>
    private static void Add(OpenCRMContext context, string key, string message)
    {
        context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox
        {
            NotificationKey = key,
            Body = message
        });
    }
}
