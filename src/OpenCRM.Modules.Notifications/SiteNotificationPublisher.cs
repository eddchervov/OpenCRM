using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Notifications;

/// <summary> Публикация site-уведомлений по шаблонам. </summary>
public static class SiteNotificationPublisher
{
    /// <summary> Добавляет уведомления после регистрации пользователя. </summary>
    public static async Task AddRegistrationNotificationsAsync(OpenCRMContext context, ApplicationUser user)
    {
        await AddAsync(
            context,
            SiteNotificationTypes.Welcome,
            user.Id,
            SiteNotificationAudiences.All,
            $"welcome:{user.Id}",
            new Dictionary<string, string>());

        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
        {
            var invitations = await context.OrganizationInvitations.AsNoTracking()
                .Where(invitation => invitation.NormalizedEmail == user.NormalizedEmail &&
                    invitation.StatusCode == OrganizationInvitationStatusCodes.Pending &&
                    !invitation.Organization.IsDeleted &&
                    !invitation.Organization.IsArchived)
                .Select(invitation => new
                {
                    invitation.Id,
                    invitation.Token,
                    OrganizationName = invitation.Organization.Name
                })
                .ToListAsync();

            foreach (var invitation in invitations)
            {
                await AddAsync(
                    context,
                    SiteNotificationTypes.OrganizationInvitation,
                    user.Id,
                    SiteNotificationAudiences.All,
                    $"invitation:{invitation.Id}:{invitation.Token}",
                    new Dictionary<string, string> { ["organization"] = invitation.OrganizationName });
            }
        }

        await context.SaveChangesAsync();
    }


    /// <summary> Добавляет site-уведомление получателю с подстановкой значений. </summary>
    public static async Task AddAsync(
        OpenCRMContext context,
        string typeCode,
        string recipientUserId,
        int audienceMask,
        string eventKey,
        IReadOnlyDictionary<string, string> values)
    {
        if (context.SiteNotifications.Local.Any(notification => notification.EventKey == eventKey) ||
            await context.SiteNotifications.AnyAsync(notification => notification.EventKey == eventKey))
        {
            return;
        }

        var template = await context.SiteNotifications.AsNoTracking()
            .Where(notification => notification.IsTemplate && !notification.IsDeleted && notification.TypeCode == typeCode)
            .OrderByDescending(notification => notification.CreatedAt)
            .FirstOrDefaultAsync();
        var (defaultTitle, defaultBody) = GetDefaultText(typeCode);
        var title = Render(template?.Title ?? defaultTitle, values);
        var body = Render(template?.Body ?? defaultBody, values);

        context.SiteNotifications.Add(new SiteNotification
        {
            TypeCode = typeCode,
            RecipientUserId = recipientUserId,
            AudienceMask = audienceMask,
            EventKey = eventKey,
            Title = title.Length <= 200 ? title : title[..200],
            Body = body.Length <= 4000 ? body : body[..4000]
        });
    }

    /// <summary> Подставляет значения в текст шаблона уведомления. </summary>
    private static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, value) in values)
        {
            text = text.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary> Возвращает заголовок и текст по умолчанию для типа уведомления. </summary>
    public static (string Title, string Body) GetDefaultText(string typeCode) => typeCode switch
    {
        SiteNotificationTypes.OrganizationInvitation =>
            ("Приглашение в организацию",
                "Вас пригласили в организацию «{organization}». Откройте профиль, чтобы принять приглашение."),
        SiteNotificationTypes.InvitationAccepted =>
            ("Приглашение принято",
                "Пользователь {email} принял приглашение в организацию «{organization}»."),
        SiteNotificationTypes.OrganizationSubscriptionExpiring =>
            ("Срок тарифа скоро закончится",
                "Тариф «{plan}» организации «{organization}» закончится через {days} дн. Продлите его, чтобы сохранить полный доступ."),
        SiteNotificationTypes.OrganizationSubscriptionExpired =>
            ("Тариф организации закончился",
                "Тариф организации «{organization}» закончился. Рабочее пространство теперь доступно только для чтения."),
        SiteNotificationTypes.Welcome =>
            ("Добро пожаловать",
                "Добро пожаловать! Добавьте свою технику или создайте организацию, чтобы начать работу."),
        _ => throw new ArgumentOutOfRangeException(nameof(typeCode))
    };
}
