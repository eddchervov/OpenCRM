using OpenCRM.Services.Interfaces.Emails.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Emails;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис отписки пользователей от email-рассылок. </summary>
public sealed class EmailUnsubscribeService(
    OpenCRMContext context,
    IConfiguration configuration) : IEmailUnsubscribeService
{
    /// <summary> Выполняет отписку пользователя по токену. </summary>
    public async Task<EmailUnsubscribeResult> UnsubscribeAsync(
        string userId,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!this.IsValidRequest(userId, email, token))
        {
            return EmailUnsubscribeResult.InvalidLink;
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await context.ApplicationUsers
            .FirstOrDefaultAsync(x => x.Id == userId && x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user == null)
        {
            return EmailUnsubscribeResult.SubscriptionNotFound;
        }

        if (user.AdvertisingConsentAccepted)
        {
            user.AdvertisingConsentAccepted = false;
            user.AdvertisingConsentAcceptedAt = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        return EmailUnsubscribeResult.Unsubscribed;
    }

    /// <summary> Проверяет корректность запроса на отписку по токену. </summary>
    private bool IsValidRequest(string userId, string email, string token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var secret = configuration["EmailPreferences:UnsubscribeTokenSecret"];
        return !string.IsNullOrWhiteSpace(secret)
            && EmailUnsubscribeToken.Validate(userId, email, token, secret);
    }
}
